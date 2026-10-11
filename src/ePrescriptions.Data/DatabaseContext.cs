using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Security;

namespace ePrescriptions.Data
{
    /// <summary>
    /// مدير قاعدة بيانات SQLite: يتولى المسار في ProgramData، تفعيل WAL والمفاتيح الأجنبية،
    /// وتطبيق الـ Migrations الآمنة عبر PRAGMA user_version.
    /// </summary>
    public class DatabaseContext
    {
        /// <summary>
        /// آخر إصدار للمخطط (عدد ملفات الـ Migrations المطبقة)
        /// </summary>
        public const int LatestSchemaVersion = 12;

        private readonly string _connectionString;
        private readonly bool _isInMemory;

        /// <summary>
        /// المسار الفعلي لملف قاعدة البيانات (ملف مؤقت في وضع :memory:)
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>%ProgramData%\ePrescriptions (مشترك بين كل مستخدمي ويندوز على الجهاز)</summary>
        public static string DefaultDatabaseDirectory => AppPaths.DataDirectory;

        public static string DefaultDatabasePath => AppPaths.DatabasePath;

        public DatabaseContext(string? customDbPath = null)
        {
            if (customDbPath == ":memory:" || customDbPath == "Data Source=:memory:")
            {
                // SQLite تنشئ قاعدة :memory: مستقلة وفارغة لكل اتصال جديد، والمستودعات هنا تفتح اتصالاً لكل عملية،
                // لذلك نستخدم ملفاً مؤقتاً فريداً يعمل كقاعدة "مؤقتة" مشتركة بين كل الاتصالات ويُحذف عند إنهاء العملية.
                _isInMemory = true;
                string tempPath = Path.Combine(Path.GetTempPath(), "e_prescriptions_tmp_" + Guid.NewGuid().ToString("N") + ".db");
                DatabasePath = tempPath;
                _connectionString = $"Data Source={tempPath};Version=3;Pooling=False;";
                AppDomain.CurrentDomain.ProcessExit += (_, __) =>
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* تجاهل */ }
                };
            }
            else
            {
                _isInMemory = false;
                string dbPath = customDbPath ?? DefaultDatabasePath;
                DatabasePath = dbPath;
                string directory = Path.GetDirectoryName(dbPath)!;
                AppPaths.EnsureDirectory(directory);

                _connectionString = $"Data Source={dbPath};Version=3;Pooling=True;Max Pool Size=10;";
            }
        }

        /// <summary>
        /// فتح اتصال SQLite مع تفعيل WAL والمفاتيح الأجنبية
        /// </summary>
        public SQLiteConnection CreateConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            if (!_isInMemory)
            {
                cmd.CommandText += " PRAGMA journal_mode = WAL;";
            }
            cmd.ExecuteNonQuery();

            return conn;
        }

        /// <summary>
        /// فحص وتطبيق التحديثات المرقّمة (Migrations) عبر PRAGMA user_version داخل Transaction
        /// </summary>
        public void InitializeAndMigrate(bool seedData = true)
        {
            using var conn = CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                int currentVersion = GetUserVersion(conn);
                Logger.LogInfo($"فحص إصدار قاعدة البيانات: {currentVersion}", "DatabaseContext");

                if (currentVersion < 1)
                {
                    // تطبيق Migration 001: الهيكل الأساسي
                    string script001 = ReadEmbeddedSql("001_InitialSchema.sql");
                    ExecuteSqlScript(conn, script001);
                    SetUserVersion(conn, 1);
                    Logger.LogInfo("تم تطبيق Migration 001 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 2)
                {
                    // تطبيق Migration 002 (لم يعد يحتوي بيانات: البيانات التجريبية تُحمَّل عبر LoadDemoData عند الطلب)
                    string script002 = ReadEmbeddedSql("002_SeedData.sql");
                    ExecuteSqlScript(conn, script002);
                    SetUserVersion(conn, 2);
                    Logger.LogInfo("تم تطبيق Migration 002 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 3)
                {
                    // تطبيق Migration 003: المشتريات والسندات والجرد
                    string script003 = ReadEmbeddedSql("003_PurchasesAndVouchers.sql");
                    ExecuteSqlScript(conn, script003);
                    SetUserVersion(conn, 3);
                    Logger.LogInfo("تم تطبيق Migration 003 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 4)
                {
                    // تطبيق Migration 004: المصروفات والقيود اليومية والمالية
                    string script004 = ReadEmbeddedSql("004_FinanceAndStaff.sql");
                    ExecuteSqlScript(conn, script004);
                    SetUserVersion(conn, 4);
                    Logger.LogInfo("تم تطبيق Migration 004 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 5)
                {
                    // تطبيق Migration 005: التوطين المصري وضبط نسب الضريبة وبيانات الصيدلية
                    string script005 = ReadEmbeddedSql("005_EgyptLocalization.sql");
                    ExecuteSqlScript(conn, script005);
                    SetUserVersion(conn, 5);
                    Logger.LogInfo("تم تطبيق Migration 005 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 6)
                {
                    // تطبيق Migration 006: جدول سجل نشاط صاحب الصيدلية
                    string script006 = ReadEmbeddedSql("006_OwnerAuditLog.sql");
                    ExecuteSqlScript(conn, script006);
                    SetUserVersion(conn, 6);
                    Logger.LogInfo("تم تطبيق Migration 006 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 7)
                {
                    // تطبيق Migration 007: جدول سجل تدقيق الموظفين (مرئي للمدير وصاحب الصيدلية)
                    string script007 = ReadEmbeddedSql("007_StaffAuditLog.sql");
                    ExecuteSqlScript(conn, script007);
                    SetUserVersion(conn, 7);
                    Logger.LogInfo("تم تطبيق Migration 007 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 8)
                {
                    // تطبيق Migration 008: الرصيد السالب، ربط المرتجع بالفاتورة الأصلية، تتبع التشغيلات، مرتجع/إتلاف المنتهي
                    string script008 = ReadEmbeddedSql("008_ExpiryReturnsAndBatchLabels.sql");
                    ExecuteSqlScript(conn, script008);
                    SetUserVersion(conn, 8);
                    Logger.LogInfo("تم تطبيق Migration 008 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 9)
                {
                    // تطبيق Migration 009: سبب مرتجع/شطب المورد
                    string script009 = ReadEmbeddedSql("009_SupplierReturnReason.sql");
                    ExecuteSqlScript(conn, script009);
                    SetUserVersion(conn, 9);
                    Logger.LogInfo("تم تطبيق Migration 009 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 10)
                {
                    // تطبيق Migration 010: رقم الوصفة واسم الطبيب على بند الفاتورة
                    string script010 = ReadEmbeddedSql("010_ItemPrescriptionDetails.sql");
                    ExecuteSqlScript(conn, script010);
                    SetUserVersion(conn, 10);
                    Logger.LogInfo("تم تطبيق Migration 010 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 11)
                {
                    // تطبيق Migration 011: إضافة عمود Status إلى جدول Invoices (1 = Active, 2 = Cancelled)
                    bool alreadyHasStatus = false;
                    using (var checkCmd = conn.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Invoices') WHERE name = 'Status';";
                        alreadyHasStatus = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0) > 0;
                    }
                    if (!alreadyHasStatus)
                    {
                        string script011 = ReadEmbeddedSql("011_InvoiceStatusAndCancellation.sql");
                        ExecuteSqlScript(conn, script011);
                    }
                    SetUserVersion(conn, 11);
                    Logger.LogInfo("تم تطبيق Migration 011 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 12)
                {
                    // تطبيق Migration 012: جدول عدادات الفواتير لمنع تكرار الترقيم
                    string script012 = ReadEmbeddedSql("012_InvoiceCounters.sql");
                    ExecuteSqlScript(conn, script012);
                    SetUserVersion(conn, 12);
                    Logger.LogInfo("تم تطبيق Migration 012 بنجاح.", "DatabaseContext");
                }

                trans.Commit();
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "InitializeAndMigrate");
                throw;
            }

            if (seedData)
            {
                // مسار الاختبارات/التطوير فقط: بيانات تجريبية + حساب Owner بكلمة مرور افتراضية معروفة.
                // التطبيق الفعلي يستدعي InitializeAndMigrate(seedData: false) ثم يمرّ بمعالج الإعداد الأول.
                LoadDemoData();
                EnsureOwnerAccountExists();
            }
        }

        /// <summary>اسم مستخدم حساب صاحب الصيدلية</summary>
        public const string OwnerUsername = "pharmacy_owner";

        /// <summary>كلمة مرور Owner الافتراضية (مسار الاختبارات فقط، ولا تُنشأ في التثبيت الفعلي)</summary>
        public const string DefaultOwnerPassword = "ChangeMe@Owner1";

        /// <summary>هل يوجد حساب Owner؟ (غيابه = أول تشغيل)</summary>
        public bool OwnerExists()
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = 99;";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0) > 0;
        }

        /// <summary>
        /// إنشاء حساب صاحب الصيدلية بكلمة مرور يختارها المستخدم. يُرفض لو كان موجوداً.
        /// الحساب لا يظهر في شاشات إدارة المستخدمين (UserRepository.GetAll يستثنيه دائماً).
        /// </summary>
        public void CreateOwnerAccount(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("كلمة المرور مطلوبة.", nameof(password));
            if (OwnerExists())
                throw new InvalidOperationException("حساب صاحب الصيدلية موجود بالفعل.");

            using var conn = CreateConnection();
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Users (Username, FullName, PasswordHash, Role, IsActive, CreatedAt)
                VALUES (@u, @f, @h, 99, 1, @c);";
            insertCmd.Parameters.AddWithValue("@u", OwnerUsername);
            insertCmd.Parameters.AddWithValue("@f", "صاحب الصيدلية");
            insertCmd.Parameters.AddWithValue("@h", PasswordHasher.HashPassword(password));
            insertCmd.Parameters.AddWithValue("@c", DateTime.Now.ToString("s"));
            insertCmd.ExecuteNonQuery();
        }

        /// <summary>
        /// سجل أساسي لا غنى عنه: العميل النقدي العام (رقم 1). ليس بيانات تجريبية.
        /// </summary>
        public void EnsureCashCustomer()
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Customers (Id, Name, Phone, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive)
                VALUES (1, 'عميل نقدي', '', 0, 0, 1, 'العميل النقدي العام', 1);";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// تحميل بيانات تجريبية (أصناف/تشغيلات/عملاء/مورد/إعدادات) على قاعدة فاضية من الأصناف فقط.
        /// ترجع false لو فيه أصناف بالفعل (لا تُكرَّر ولا تُدمج مع بيانات حقيقية).
        /// </summary>
        public bool LoadDemoData()
        {
            using var conn = CreateConnection();
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM Items;";
                if (Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0) > 0) return false;
            }

            using var trans = conn.BeginTransaction();
            try
            {
                ExecuteSqlScript(conn, ReadEmbeddedSql("DemoData.sql"));
                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "DatabaseContext.LoadDemoData");
                throw;
            }
        }

        /// <summary>
        /// إنشاء حساب صاحب الصيدلية تلقائياً إن لم يكن موجوداً (مرة واحدة فقط).
        /// الحساب لا يظهر في أي شاشة لإدارة المستخدمين (UserRepository.GetAll يستثنيه دائماً)،
        /// ولا توجد قائمة مستخدمين في شاشة الدخول؛ يُدخل عبر اسم المستخدم وكلمة المرور مباشرة فقط.
        /// بيانات الدخول الافتراضية: pharmacy_owner / ChangeMe@Owner1 — يجب تغييرها فوراً بعد أول دخول.
        /// </summary>
        private void EnsureOwnerAccountExists()
        {
            if (OwnerExists()) return;

            CreateOwnerAccount(DefaultOwnerPassword);
            Logger.LogInfo("تم إنشاء حساب صاحب الصيدلية الافتراضي (مسار الاختبارات فقط).", "DatabaseContext.EnsureOwnerAccountExists");
        }

        public static int GetUserVersion(SQLiteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            object? result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }

        private static void SetUserVersion(SQLiteConnection conn, int version)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {version};";
            cmd.ExecuteNonQuery();
        }

        private static void ExecuteSqlScript(SQLiteConnection conn, string script)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = script;
            cmd.ExecuteNonQuery();
        }

        private static string ReadEmbeddedSql(string filename)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // البحث عن المورد المدمج بغض النظر عن الـ namespace prefix
            string? resourceName = null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(filename, StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (resourceName == null)
            {
                // محاولة قراءة الملف مباشرة من مسار التطبيق إذا لم يكن مدمجاً
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Migrations", filename);
                if (File.Exists(localPath))
                {
                    return File.ReadAllText(localPath);
                }
                throw new FileNotFoundException($"تعذر العثور على ملف السكربت المدمج: {filename}");
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
