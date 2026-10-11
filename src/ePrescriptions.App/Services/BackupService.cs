using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;

namespace ePrescriptions.App.Services
{
    /// <summary>نتيجة نسخة احتياطية مضبوطة (أساسي + خارجي اختياري)</summary>
    public class BackupRunResult
    {
        public bool Success { get; set; }
        public string PrimaryPath { get; set; } = string.Empty;
        public string ExternalPath { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>true لو النسخة الأساسية نجحت لكن النسخة على القرص الخارجي فشلت</summary>
        public bool ExternalFailed { get; set; }
    }

    public interface IBackupService
    {
        string GetDefaultBackupFolder();
        bool CreateBackup(string? customFolder, out string backupFilePath, out string errorMessage);
        bool RestoreBackup(string backupFilePath, out string errorMessage);
        bool ValidateBackupFile(string backupFilePath, out string errorMessage);

        /// <summary>نسخة بحسب الإعدادات: المجلد الأساسي + المجلد الخارجي (لو محدد) + حذف الأقدم من كل منهما</summary>
        BackupRunResult RunConfiguredBackup(BackupSettings settings);

        /// <summary>تُستدعى بعد إغلاق الوردية: لا تفعل شيئاً لو النسخ التلقائي معطّل. لا ترمي استثناءات أبداً.</summary>
        BackupRunResult? RunAfterShiftBackup(BackupSettings settings);

        /// <summary>حذف أقدم النسخ (بنمط e_prescriptions_backup_*.db فقط) مع إبقاء عدد محدد من الأحدث. يرجع عدد المحذوف.</summary>
        int PruneOldBackups(string folder, int keepCount);
    }

    public class BackupService : IBackupService
    {
        public const string BackupFilePrefix = "e_prescriptions_backup_";
        public const string BackupFilePattern = BackupFilePrefix + "*.db";

        private readonly DatabaseContext _dbContext;

        public BackupService(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public string GetDefaultBackupFolder()
        {
            return ePrescriptions.Core.Helpers.AppPaths.EnsureDirectory(ePrescriptions.Core.Helpers.AppPaths.BackupsDirectory);
        }

        public bool CreateBackup(string? customFolder, out string backupFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;
            backupFilePath = string.Empty;

            try
            {
                string folder = string.IsNullOrWhiteSpace(customFolder) ? GetDefaultBackupFolder() : customFolder!;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                backupFilePath = NextBackupPath(folder);
                VacuumInto(_dbContext, backupFilePath);

                Logger.LogInfo($"تم إنشاء نسخة احتياطية بنجاح في: {backupFilePath}", "BackupService.CreateBackup");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Logger.LogException(ex, "BackupService.CreateBackup");
                return false;
            }
        }

        /// <summary>اسم ملف نسخة جديد غير مستخدم (ثواني + رقم تسلسلي لو اتأخدت أكتر من نسخة في نفس الثانية)</summary>
        private static string NextBackupPath(string folder)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(folder, $"{BackupFilePrefix}{stamp}.db");
            int n = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, $"{BackupFilePrefix}{stamp}_{n:D2}.db");
                n++;
            }
            return path;
        }

        /// <summary>
        /// نسخة متّسقة من القاعدة وهي مفتوحة عبر VACUUM INTO (بتشمل محتوى ملف WAL، وبتكتب ملف واحد مستقل).
        /// بيتكتب لملف مؤقت وبعدين يتسمّى، عشان نسخة ناقصة ما تظهرش أبداً باسم نسخة صالحة.
        /// </summary>
        private static void VacuumInto(DatabaseContext db, string destination)
        {
            string tmp = destination + ".tmp";
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);

                using (var conn = db.CreateConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "VACUUM INTO '" + tmp.Replace("'", "''") + "';";
                    cmd.ExecuteNonQuery();
                }

                // فحص سلامة النسخة الناتجة قبل اعتمادها
                if (!IntegrityOk(tmp, out string why))
                {
                    throw new InvalidOperationException("فشل فحص سلامة النسخة الناتجة: " + why);
                }

                File.Move(tmp, destination);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* تجاهل */ }
            }
        }

        private static bool IntegrityOk(string path, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                var connStr = $"Data Source={path};Version=3;Read Only=True;Pooling=False;";
                using var conn = new SQLiteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                var result = cmd.ExecuteScalar()?.ToString();
                if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)) return true;
                errorMessage = result ?? "غير معروف";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public BackupRunResult RunConfiguredBackup(BackupSettings settings)
        {
            var result = new BackupRunResult();
            int keep = settings.EffectiveRetention;

            string primaryFolder = string.IsNullOrWhiteSpace(settings.PrimaryFolder) ? GetDefaultBackupFolder() : settings.PrimaryFolder;
            if (!CreateBackup(primaryFolder, out string primaryPath, out string error))
            {
                result.Success = false;
                result.Message = "فشل إنشاء النسخة الاحتياطية: " + error;
                return result;
            }
            result.PrimaryPath = primaryPath;
            result.Success = true;
            PruneSafe(primaryFolder, keep);

            if (!string.IsNullOrWhiteSpace(settings.ExternalFolder))
            {
                string extFolder = settings.ExternalFolder;
                // لو الخارجي هو نفس مجلد البرنامج الأساسي ما يبقاش نسخة "منفصلة": نرفض بوضوح
                if (SameFolder(extFolder, primaryFolder))
                {
                    result.ExternalFailed = true;
                    result.Message = "تم أخذ النسخة الأساسية، لكن المجلد الخارجي هو نفس مجلد النسخ الأساسي. اختر قرصاً أو USB منفصلاً.";
                    return result;
                }
                // نسخ ملف النسخة الجاهزة (مش VACUUM تاني) لتكون النسختان متطابقتين
                try
                {
                    Directory.CreateDirectory(extFolder);
                    string extPath = Path.Combine(extFolder, Path.GetFileName(primaryPath));
                    string extTmp = extPath + ".tmp";
                    File.Copy(primaryPath, extTmp, overwrite: true);
                    if (File.Exists(extPath)) File.Delete(extPath);
                    File.Move(extTmp, extPath);
                    result.ExternalPath = extPath;
                    PruneSafe(extFolder, keep);
                }
                catch (Exception ex)
                {
                    result.ExternalFailed = true;
                    result.Message = "تم أخذ النسخة الأساسية، لكن فشل النسخ على القرص الخارجي: " + ex.Message;
                    Logger.LogException(ex, "BackupService.RunConfiguredBackup.External");
                    return result;
                }
            }

            result.Message = string.IsNullOrEmpty(result.ExternalPath)
                ? "تم أخذ نسخة احتياطية: " + Path.GetFileName(primaryPath)
                : "تم أخذ نسخة احتياطية في المجلد الأساسي وعلى القرص الخارجي: " + Path.GetFileName(primaryPath);
            return result;
        }

        public BackupRunResult? RunAfterShiftBackup(BackupSettings settings)
        {
            if (!settings.AutoBackupAfterShift) return null;
            try
            {
                return RunConfiguredBackup(settings);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupService.RunAfterShiftBackup");
                return new BackupRunResult { Success = false, Message = "فشل النسخ الاحتياطي التلقائي: " + ex.Message };
            }
        }

        private void PruneSafe(string folder, int keep)
        {
            try { PruneOldBackups(folder, keep); }
            catch (Exception ex) { Logger.LogException(ex, "BackupService.PruneOldBackups"); }
        }

        private static bool SameFolder(string a, string b)
        {
            try
            {
                string na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public int PruneOldBackups(string folder, int keepCount)
        {
            if (keepCount < BackupSettings.MinRetentionCount) keepCount = BackupSettings.MinRetentionCount;
            if (!Directory.Exists(folder)) return 0;

            // الاسم يحتوي التاريخ والوقت، فالترتيب بالاسم = الترتيب الزمني (وأأمن من تواريخ الملفات بعد النسخ على USB)
            var files = Directory.GetFiles(folder, BackupFilePattern)
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int deleted = 0;
            foreach (var old in files.Skip(keepCount))
            {
                try { File.Delete(old); deleted++; }
                catch (Exception ex) { Logger.LogException(ex, "BackupService.PruneOldBackups"); }
            }
            return deleted;
        }

        public bool ValidateBackupFile(string backupFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!File.Exists(backupFilePath))
            {
                errorMessage = "ملف النسخة الاحتياطية غير موجود.";
                return false;
            }

            if (IntegrityOk(backupFilePath, out string why)) return true;
            errorMessage = $"فشل فحص سلامة قاعدة البيانات أو الملف غير صالح: {why}";
            return false;
        }

        private static int ReadSchemaVersion(string path)
        {
            var connStr = $"Data Source={path};Version=3;Read Only=True;Pooling=False;";
            using var conn = new SQLiteConnection(connStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        public bool RestoreBackup(string backupFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!ValidateBackupFile(backupFilePath, out errorMessage))
            {
                return false;
            }

            // نسخة أحدث من البرنامج الحالي: ما نقدرش نفهمها، فلا نستبدل أي شيء
            int backupVersion;
            try { backupVersion = ReadSchemaVersion(backupFilePath); }
            catch (Exception ex) { errorMessage = "تعذر قراءة إصدار النسخة: " + ex.Message; return false; }

            if (backupVersion > DatabaseContext.LatestSchemaVersion)
            {
                errorMessage = $"النسخة الاحتياطية من إصدار أحدث للبرنامج (إصدار القاعدة {backupVersion} وإصدار هذا البرنامج {DatabaseContext.LatestSchemaVersion}). حدّث البرنامج أولاً.";
                return false;
            }

            string dbPath = _dbContext.DatabasePath;
            string emergencyBackup = dbPath + ".pre_restore.bak";
            bool replaced = false;

            try
            {
                // أخذ نسخة وقائية متّسقة من الحالي قبل الاستبدال (VACUUM INTO يشمل WAL)، وبديلها نسخ الملف
                try
                {
                    if (File.Exists(emergencyBackup)) File.Delete(emergencyBackup);
                    VacuumInto(_dbContext, emergencyBackup + ".db");
                    File.Move(emergencyBackup + ".db", emergencyBackup);
                }
                catch
                {
                    SQLiteConnection.ClearAllPools();
                    if (File.Exists(dbPath)) File.Copy(dbPath, emergencyBackup, overwrite: true);
                }

                // إغلاق أي اتصالات معلقة، وحذف ملفات WAL/SHM القديمة قبل وضع الملف الجديد (عشان ما تتطبقش على القاعدة المستعادة)
                SQLiteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                DeleteIfExists(dbPath + "-wal");
                DeleteIfExists(dbPath + "-shm");

                File.Copy(backupFilePath, dbPath, overwrite: true);
                replaced = true;

                // إعادة تشغيل الاتصال بالقاعدة وتطبيق أي migrations ناقصة لو النسخة أقدم
                SQLiteConnection.ClearAllPools();
                _dbContext.InitializeAndMigrate(seedData: false);

                int finalVersion = ReadSchemaVersion(dbPath);
                if (finalVersion != DatabaseContext.LatestSchemaVersion)
                {
                    throw new InvalidOperationException($"إصدار القاعدة بعد الاستعادة {finalVersion} بدل {DatabaseContext.LatestSchemaVersion}.");
                }

                Logger.LogInfo($"تم استعادة قاعدة البيانات بنجاح من: {backupFilePath} (إصدار النسخة {backupVersion})", "BackupService.RestoreBackup");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Logger.LogException(ex, "BackupService.RestoreBackup");

                // فشل بعد الاستبدال: رجّع القاعدة الأصلية من النسخة الوقائية
                if (replaced && File.Exists(emergencyBackup))
                {
                    try
                    {
                        SQLiteConnection.ClearAllPools();
                        DeleteIfExists(dbPath + "-wal");
                        DeleteIfExists(dbPath + "-shm");
                        File.Copy(emergencyBackup, dbPath, overwrite: true);
                        errorMessage += " (تم الرجوع للقاعدة السابقة تلقائياً)";
                    }
                    catch (Exception ex2)
                    {
                        Logger.LogException(ex2, "BackupService.RestoreBackup.Rollback");
                        errorMessage += " (تعذر الرجوع التلقائي؛ النسخة الوقائية: " + emergencyBackup + ")";
                    }
                }
                return false;
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
