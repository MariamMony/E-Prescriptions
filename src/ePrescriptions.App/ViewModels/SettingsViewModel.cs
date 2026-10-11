using System;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Printing;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly IBackupService _backupService;
        private readonly Data.Repositories.OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly Data.Repositories.StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly IUserSessionService? _sessionService;

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // بيانات المنشأة
        private string _pharmacyNameAr = "صيدلية ماجد القبانى";
        public string PharmacyNameAr
        {
            get => _pharmacyNameAr;
            set => SetProperty(ref _pharmacyNameAr, value);
        }

        private string _pharmacyNameEn = "Maged Al-Qabbani Pharmacy";
        public string PharmacyNameEn
        {
            get => _pharmacyNameEn;
            set => SetProperty(ref _pharmacyNameEn, value);
        }

        private string _taxNumber = "123456789";
        public string TaxNumber
        {
            get => _taxNumber;
            set => SetProperty(ref _taxNumber, value);
        }

        private string _commercialRegister = "1010998877";
        public string CommercialRegister
        {
            get => _commercialRegister;
            set => SetProperty(ref _commercialRegister, value);
        }

        private string _licenseNumber = "PH-99201";
        public string LicenseNumber
        {
            get => _licenseNumber;
            set => SetProperty(ref _licenseNumber, value);
        }

        private string _phone = "0112345678";
        public string Phone
        {
            get => _phone;
            set => SetProperty(ref _phone, value);
        }

        private string _address = "جمهورية مصر العربية - القاهرة";
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private int _defaultVatPercent = 14;
        public int DefaultVatPercent
        {
            get => _defaultVatPercent;
            set => SetProperty(ref _defaultVatPercent, value);
        }

        private string _currencyDisplay = "الجنيه المصري (ج.م / EGP)";
        public string CurrencyDisplay
        {
            get => _currencyDisplay;
            set => SetProperty(ref _currencyDisplay, value);
        }

        // إعدادات الطابعات
        public ObservableCollection<string> InstalledPrinters { get; } = new();

        private string _receiptPrinterName = string.Empty;
        public string ReceiptPrinterName
        {
            get => _receiptPrinterName;
            set => SetProperty(ref _receiptPrinterName, value);
        }

        private string _a4PrinterName = string.Empty;
        public string A4PrinterName
        {
            get => _a4PrinterName;
            set => SetProperty(ref _a4PrinterName, value);
        }

        private string _labelPrinterName = string.Empty;
        public string LabelPrinterName
        {
            get => _labelPrinterName;
            set => SetProperty(ref _labelPrinterName, value);
        }

        private int _receiptPaperWidthMm = 80;
        public int ReceiptPaperWidthMm
        {
            get => _receiptPaperWidthMm;
            set => SetProperty(ref _receiptPaperWidthMm, value);
        }

        private int _labelWidthMm = 50;
        public int LabelWidthMm
        {
            get => _labelWidthMm;
            set => SetProperty(ref _labelWidthMm, value);
        }

        private int _labelHeightMm = 25;
        public int LabelHeightMm
        {
            get => _labelHeightMm;
            set => SetProperty(ref _labelHeightMm, value);
        }

        // درج النقود والباركود
        private bool _enableCashDrawerOnPrint = true;
        public bool EnableCashDrawerOnPrint
        {
            get => _enableCashDrawerOnPrint;
            set => SetProperty(ref _enableCashDrawerOnPrint, value);
        }

        private string _cashDrawerEscPosBytes = "27,112,0,25,250";
        public string CashDrawerEscPosBytes
        {
            get => _cashDrawerEscPosBytes;
            set => SetProperty(ref _cashDrawerEscPosBytes, value);
        }

        private int _barcodeScanThresholdMs = 45;
        public int BarcodeScanThresholdMs
        {
            get => _barcodeScanThresholdMs;
            set => SetProperty(ref _barcodeScanThresholdMs, value);
        }

        // النسخ الاحتياطي
        private string _backupFolder = string.Empty;
        public string BackupFolder
        {
            get => _backupFolder;
            set => SetProperty(ref _backupFolder, value);
        }

        private string _externalBackupFolder = string.Empty;
        /// <summary>مجلد/قرص خارجي منفصل (USB) لنسخة ثانية؛ فاضي = معطّل</summary>
        public string ExternalBackupFolder
        {
            get => _externalBackupFolder;
            set => SetProperty(ref _externalBackupFolder, value);
        }

        private int _backupRetentionCount = BackupSettings.DefaultRetentionCount;
        public int BackupRetentionCount
        {
            get => _backupRetentionCount;
            set => SetProperty(ref _backupRetentionCount, value);
        }

        private bool _autoBackupAfterShift = true;
        public bool AutoBackupAfterShift
        {
            get => _autoBackupAfterShift;
            set => SetProperty(ref _autoBackupAfterShift, value);
        }

        private string _lastBackupInfo = "لم يتم إنشاء نسخة في هذه الجلسة.";
        public string LastBackupInfo
        {
            get => _lastBackupInfo;
            set => SetProperty(ref _lastBackupInfo, value);
        }

        // الأوامر
        public IRelayCommand SaveSettingsCommand { get; }
        public IRelayCommand TestReceiptPrinterCommand { get; }
        public IRelayCommand TestCashDrawerCommand { get; }
        public IRelayCommand CreateBackupCommand { get; }
        public IRelayCommand RestoreBackupCommand { get; }
        public IRelayCommand SaveBackupSettingsCommand { get; }

        public SettingsViewModel(
            SettingsRepository settingsRepository,
            IBackupService backupService,
            Data.Repositories.OwnerAuditLogRepository? ownerAuditLogRepository = null,
            Data.Repositories.StaffAuditLogRepository? staffAuditLogRepository = null,
            IUserSessionService? sessionService = null)
        {
            _settingsRepository = settingsRepository;
            _backupService = backupService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;

            Title = "إعدادات النظام والملحقات";
            var backupSettings = BackupSettingsStore.Load();
            BackupFolder = string.IsNullOrWhiteSpace(backupSettings.PrimaryFolder) ? _backupService.GetDefaultBackupFolder() : backupSettings.PrimaryFolder;
            ExternalBackupFolder = backupSettings.ExternalFolder;
            BackupRetentionCount = backupSettings.EffectiveRetention;
            AutoBackupAfterShift = backupSettings.AutoBackupAfterShift;

            SaveSettingsCommand = new RelayCommand(SaveSettings);
            TestReceiptPrinterCommand = new RelayCommand(TestReceiptPrinter);
            TestCashDrawerCommand = new RelayCommand(TestCashDrawer);
            CreateBackupCommand = new RelayCommand(CreateBackup);
            RestoreBackupCommand = new RelayCommand(RestoreBackup);
            SaveBackupSettingsCommand = new RelayCommand(() => SaveBackupSettings());

            LoadInstalledPrinters();
            LoadCurrentSettings();
        }

        private void LoadInstalledPrinters()
        {
            InstalledPrinters.Clear();
            InstalledPrinters.Add("(الطابعة الافتراضية للويندوز)");
            try
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    InstalledPrinters.Add(printer);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "LoadInstalledPrinters");
            }
        }

        private void LoadCurrentSettings()
        {
            try
            {
                var s = _settingsRepository.GetSettings();
                PharmacyNameAr = string.IsNullOrWhiteSpace(s.PharmacyNameAr) ? "صيدلية ماجد القبانى" : s.PharmacyNameAr;
                PharmacyNameEn = s.PharmacyNameEn;
                TaxNumber = s.TaxNumber;
                CommercialRegister = s.CommercialRegister;
                LicenseNumber = s.LicenseNumber;
                Phone = s.Phone;
                Address = s.Address;
                DefaultVatPercent = s.DefaultVatPercent;

                ReceiptPrinterName = s.ReceiptPrinterName;
                A4PrinterName = s.A4PrinterName;
                LabelPrinterName = s.LabelPrinterName;
                ReceiptPaperWidthMm = s.ReceiptPaperWidthMm;
                LabelWidthMm = s.LabelWidthMm;
                LabelHeightMm = s.LabelHeightMm;

                EnableCashDrawerOnPrint = s.EnableCashDrawerOnPrint;
                CashDrawerEscPosBytes = s.CashDrawerEscPosBytes;
                BarcodeScanThresholdMs = s.BarcodeScanThresholdMs;

                StatusMessage = "تم تحميل إعدادات النظام الحالية بنجاح.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"تعذر تحميل الإعدادات: {ex.Message}";
            }
        }

        private void SaveSettings()
        {
            try
            {
                // نبدأ من الإعدادات المحفوظة حتى لا تُفقد قيم غير معروضة في الشاشة (QR، طول الرقم الضريبي، الصيدلي المسؤول)
                var previous = _settingsRepository.GetSettings();
                var s = _settingsRepository.GetSettings();
                s.Id = 1;
                s.PharmacyNameAr = PharmacyNameAr.Trim();
                s.PharmacyNameEn = PharmacyNameEn.Trim();
                s.TaxNumber = TaxNumber.Trim();
                s.CommercialRegister = CommercialRegister.Trim();
                s.LicenseNumber = LicenseNumber.Trim();
                s.Phone = Phone.Trim();
                s.Address = Address.Trim();
                s.DefaultVatPercent = DefaultVatPercent;
                s.ReceiptPrinterName = ReceiptPrinterName ?? string.Empty;
                s.A4PrinterName = A4PrinterName ?? string.Empty;
                s.LabelPrinterName = LabelPrinterName ?? string.Empty;
                s.ReceiptPaperWidthMm = ReceiptPaperWidthMm;
                s.LabelWidthMm = LabelWidthMm;
                s.LabelHeightMm = LabelHeightMm;
                s.EnableCashDrawerOnPrint = EnableCashDrawerOnPrint;
                s.CashDrawerEscPosBytes = CashDrawerEscPosBytes.Trim();
                s.BarcodeScanThresholdMs = BarcodeScanThresholdMs;

                LogSettingsAuditIfChanged(previous, s);

                _settingsRepository.SaveSettings(s);
                StatusMessage = "تم حفظ جميع إعدادات النظام بنجاح.";
                MessageBox.Show(
                    "تم حفظ الإعدادات بنجاح في قاعدة البيانات المحلية.",
                    "تأكيد الحفظ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ أثناء حفظ الإعدادات: {ex.Message}";
                MessageBox.Show(
                    $"فشل حفظ الإعدادات:\n{ex.Message}",
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// يسجّل أي حقل حسّاس تغيّر في إعدادات الصيدلية: لصاحب الصيدلية في سجله الخاص المخفي عن الجميع،
        /// ولأي دور آخر في سجل الموظفين العام المرئي لـ Admin وOwner فقط.
        /// </summary>
        private void LogSettingsAuditIfChanged(PharmacySettings previous, PharmacySettings updated)
        {
            var user = _sessionService?.CurrentUser;
            if (user == null) return;

            const string entityType = "PharmacySettings";
            const string entityId = "1";
            const string label = "إعدادات الصيدلية";

            void Log(string field, string? oldVal, string? newVal)
            {
                if (string.Equals(oldVal, newVal, StringComparison.Ordinal)) return;

                if (user.Role == ePrescriptions.Domain.Enums.UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, entityType, entityId, label, field, oldVal, newVal);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), entityType, entityId, label, field, oldVal, newVal);
            }

            Log("PharmacyNameAr", previous.PharmacyNameAr, updated.PharmacyNameAr);
            Log("TaxNumber", previous.TaxNumber, updated.TaxNumber);
            Log("DefaultVatPercent", previous.DefaultVatPercent.ToString(), updated.DefaultVatPercent.ToString());
            Log("Phone", previous.Phone, updated.Phone);
            Log("Address", previous.Address, updated.Address);
            Log("EnableCashDrawerOnPrint", previous.EnableCashDrawerOnPrint.ToString(), updated.EnableCashDrawerOnPrint.ToString());
        }

        private void TestReceiptPrinter()
        {
            try
            {
                string prn = string.IsNullOrWhiteSpace(ReceiptPrinterName) || ReceiptPrinterName.Contains("الافتراضية")
                    ? string.Empty
                    : ReceiptPrinterName;

                if (ThermalReceiptPrinter.PrintTestTicket(prn, PharmacyNameAr, out string printError))
                {
                    StatusMessage = "تم إرسال تذكرة اختبارية إلى الطابعة الحرارية.";
                }
                else
                {
                    StatusMessage = $"فشل اختبار الطابعة: {printError}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ في فحص الطابعة: {ex.Message}";
                MessageBox.Show(
                    $"تعذر إرسال أمر الطباعة:\n{ex.Message}",
                    "تنبيه الطابعة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void TestCashDrawer()
        {
            try
            {
                string prn = string.IsNullOrWhiteSpace(ReceiptPrinterName) || ReceiptPrinterName.Contains("الافتراضية")
                    ? string.Empty
                    : ReceiptPrinterName;

                if (ThermalReceiptPrinter.OpenCashDrawer(prn, CashDrawerEscPosBytes, out string drawerError))
                {
                    StatusMessage = "تم إرسال نبضة فتح درج النقود بنجاح.";
                }
                else
                {
                    StatusMessage = $"فشل فتح درج النقود: {drawerError}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"فشل فتح درج النقود: {ex.Message}";
                MessageBox.Show(
                    $"تعذر فتح درج النقود:\n{ex.Message}",
                    "درج النقود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private BackupSettings CurrentBackupSettings() => new BackupSettings
        {
            PrimaryFolder = BackupFolder?.Trim() ?? string.Empty,
            ExternalFolder = ExternalBackupFolder?.Trim() ?? string.Empty,
            RetentionCount = BackupRetentionCount,
            AutoBackupAfterShift = AutoBackupAfterShift
        };

        /// <summary>حفظ إعدادات النسخ في ملف مستقل (backup_settings.ini) مش في قاعدة البيانات</summary>
        private bool SaveBackupSettings()
        {
            try
            {
                var settings = CurrentBackupSettings();
                BackupSettingsStore.Save(settings);
                BackupRetentionCount = settings.EffectiveRetention;
                StatusMessage = "تم حفظ إعدادات النسخ الاحتياطي.";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"تعذر حفظ إعدادات النسخ الاحتياطي: {ex.Message}";
                return false;
            }
        }

        private void CreateBackup()
        {
            SaveBackupSettings();
            var result = _backupService.RunConfiguredBackup(CurrentBackupSettings());
            if (result.Success)
            {
                LastBackupInfo = $"آخر نسخة: {Path.GetFileName(result.PrimaryPath)} في {DateTime.Now:HH:mm:ss}";
                StatusMessage = result.Message;
                string details = $"تم أخذ نسخة احتياطية كاملة وموثوقة لقاعدة البيانات:\n{result.PrimaryPath}";
                if (!string.IsNullOrEmpty(result.ExternalPath)) details += $"\n\nونسخة على القرص الخارجي:\n{result.ExternalPath}";
                if (result.ExternalFailed) details += $"\n\n⚠ {result.Message}";
                MessageBox.Show(
                    details,
                    "النسخ الاحتياطي",
                    MessageBoxButton.OK,
                    result.ExternalFailed ? MessageBoxImage.Warning : MessageBoxImage.Information
                );
            }
            else
            {
                StatusMessage = result.Message;
                MessageBox.Show(
                    $"تعذر إنشاء النسخة الاحتياطية:\n{result.Message}",
                    "خطأ النسخ الاحتياطي",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void RestoreBackup()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختر ملف النسخة الاحتياطية لاستعادتها",
                Filter = "ملفات قواعد بيانات SQLite (*.db;*.bak)|*.db;*.bak|جميع الملفات (*.*)|*.*",
                InitialDirectory = BackupFolder
            };

            if (dialog.ShowDialog() == true)
            {
                var confirm = MessageBox.Show(
                    $"تحذير هـام جداً:\nسيتم استبدال قاعدة البيانات الحالية بالكامل بالملف:\n{dialog.FileName}\n\nهل ترغب بالاستمرار وتطبيق الاستعادة؟",
                    "تأكيد استعادة قاعدة البيانات",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (confirm == MessageBoxResult.Yes)
                {
                    if (_backupService.RestoreBackup(dialog.FileName, out string error))
                    {
                        StatusMessage = "تمت استعادة قاعدة البيانات بنجاح.";
                        MessageBox.Show(
                            "تمت استعادة قاعدة البيانات بنجاح وتأكيد سلامة الجداول، وتم تطبيق أي تحديثات ناقصة على هيكل القاعدة.\nأعد تشغيل البرنامج لتحميل البيانات المستعادة في كل الشاشات.",
                            "تمت الاستعادة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    else
                    {
                        StatusMessage = $"فشلت الاستعادة: {error}";
                        MessageBox.Show(
                            $"تعذر استعادة قاعدة البيانات:\n{error}",
                            "خطأ في الاستعادة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
            }
        }
    }
}
