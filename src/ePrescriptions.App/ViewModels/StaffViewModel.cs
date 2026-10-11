using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Security;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class StaffViewModel : ViewModelBase
    {
        private readonly UserRepository _userRepository;
        private readonly ShiftRepository _shiftRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly Services.IBackupService? _backupService;
        private readonly Func<BackupSettings> _backupSettingsProvider;

        // قائمة الموظفين
        public ObservableCollection<User> UsersList { get; } = new();

        // الوردية الحالية
        private Shift? _currentShift;
        public Shift? CurrentShift
        {
            get => _currentShift;
            set
            {
                if (SetProperty(ref _currentShift, value))
                {
                    OnPropertyChanged(nameof(HasOpenShift));
                    OnPropertyChanged(nameof(FormattedOpeningBalance));
                    OnPropertyChanged(nameof(FormattedExpectedCash));
                    OnPropertyChanged(nameof(FormattedCashSales));
                    OnPropertyChanged(nameof(FormattedNetworkSales));
                }
            }
        }

        public bool HasOpenShift => CurrentShift != null && CurrentShift.Status == ShiftStatus.Open;

        public string FormattedOpeningBalance => CurrentShift != null ? Money.FromPiastres(CurrentShift.OpeningBalancePiastres).ToFormattedString() : "-";
        public string FormattedExpectedCash => CurrentShift != null ? Money.FromPiastres(CurrentShift.ExpectedCashPiastres).ToFormattedString() : "-";
        public string FormattedCashSales => CurrentShift != null ? Money.FromPiastres(CurrentShift.TotalSalesCashPiastres).ToFormattedString() : "-";
        public string FormattedNetworkSales => CurrentShift != null ? Money.FromPiastres(CurrentShift.TotalSalesNetworkPiastres).ToFormattedString() : "-";

        // حقول إغلاق الوردية
        private decimal _actualClosingCashPounds;
        public decimal ActualClosingCashPounds
        {
            get => _actualClosingCashPounds;
            set
            {
                if (SetProperty(ref _actualClosingCashPounds, value))
                {
                    CalculateShiftDifference();
                }
            }
        }

        private decimal _shiftDifferencePounds;
        public decimal ShiftDifferencePounds
        {
            get => _shiftDifferencePounds;
            set => SetProperty(ref _shiftDifferencePounds, value);
        }

        private string _closingNotes = string.Empty;
        public string ClosingNotes
        {
            get => _closingNotes;
            set => SetProperty(ref _closingNotes, value);
        }

        // حقول فتح وردية جديدة
        private decimal _newOpeningBalancePounds = 200;
        public decimal NewOpeningBalancePounds
        {
            get => _newOpeningBalancePounds;
            set => SetProperty(ref _newOpeningBalancePounds, value);
        }

        // حقول إضافة موظف جديد
        private string _newUsername = string.Empty;
        public string NewUsername { get => _newUsername; set => SetProperty(ref _newUsername, value); }

        private string _newFullName = string.Empty;
        public string NewFullName { get => _newFullName; set => SetProperty(ref _newFullName, value); }

        private string _newPassword = string.Empty;
        public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }

        private UserRole _newRole = UserRole.Pharmacist;
        public UserRole NewRole { get => _newRole; set => SetProperty(ref _newRole, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        // الأوامر
        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand OpenShiftCommand { get; }
        public IRelayCommand CloseShiftCommand { get; }
        public IRelayCommand AddUserCommand { get; }

        public StaffViewModel(UserRepository userRepository, ShiftRepository shiftRepository, Services.IUserSessionService? sessionService = null,
            Services.IBackupService? backupService = null, Func<BackupSettings>? backupSettingsProvider = null)
        {
            _backupService = backupService;
            _backupSettingsProvider = backupSettingsProvider ?? (() => BackupSettingsStore.Load());
            Title = "الموظفون وإدارة الورديات والصندوق";
            _userRepository = userRepository;
            _shiftRepository = shiftRepository;
            _sessionService = sessionService;

            RefreshCommand = new RelayCommand(LoadData);
            OpenShiftCommand = new RelayCommand(OpenShift);
            CloseShiftCommand = new RelayCommand(CloseShift);
            AddUserCommand = new RelayCommand(AddUser);

            LoadData();
        }

        public void LoadData()
        {
            // تحميل المستخدمين
            UsersList.Clear();
            var users = _userRepository.GetAll();
            foreach (var u in users) UsersList.Add(u);

            // جلب الوردية المفتوحة للمستخدم المسجّل دخوله حالياً
            int currentUserId = _sessionService?.CurrentUser?.Id ?? 0;
            CurrentShift = currentUserId > 0 ? _shiftRepository.GetCurrentOpenShift(currentUserId) : null;
            _sessionService?.SetCurrentShift(CurrentShift);
            if (CurrentShift != null)
            {
                ActualClosingCashPounds = Money.FromPiastres(CurrentShift.ExpectedCashPiastres).ToPounds();
                CalculateShiftDifference();
            }

            StatusMessage = "تم تحديث بيانات الورديات والموظفين.";
        }

        private void CalculateShiftDifference()
        {
            if (CurrentShift == null) return;
            decimal expected = Money.FromPiastres(CurrentShift.ExpectedCashPiastres).ToPounds();
            ShiftDifferencePounds = ActualClosingCashPounds - expected;
        }

        public void OpenShift()
        {
            if (HasOpenShift)
            {
                StatusMessage = "توجد وردية مفتوحة بالفعل حالياً!";
                return;
            }

            var currentUser = _sessionService?.CurrentUser;
            if (currentUser == null)
            {
                StatusMessage = "سجّل الدخول الأول عشان تفتح وردية.";
                return;
            }

            long openPiastres = Money.FromPounds(NewOpeningBalancePounds).Piastres;
            int sId = _shiftRepository.OpenShift(currentUser.Id, currentUser.FullName, openPiastres);
            StatusMessage = $"تم فتح الوردية رقم {sId} برصيد افتتاحي {NewOpeningBalancePounds:N2} ج.م بنجاح.";
            LoadData();
        }

        public void CloseShift()
        {
            if (CurrentShift == null)
            {
                StatusMessage = "لا توجد وردية مفتوحة لإغلاقها!";
                return;
            }

            long actualPiastres = Money.FromPounds(ActualClosingCashPounds).Piastres;
            _shiftRepository.CloseShift(CurrentShift.Id, actualPiastres, ClosingNotes);

            string diffText = ShiftDifferencePounds == 0
                ? "الصندوق مطابق تماماً بدون عجز أو زيادة."
                : ShiftDifferencePounds > 0 
                    ? $"يوجد زيادة بالصندوق قدرها +{ShiftDifferencePounds:N2} ج.م"
                    : $"يوجد عجز بالصندوق قدره {ShiftDifferencePounds:N2} ج.م";

            int closedShiftId = CurrentShift.Id;
            LoadData();
            StatusMessage = $"تم إغلاق الوردية رقم {closedShiftId} بنجاح. {diffText}";

            // خطوة 13: نسخة احتياطية تلقائية بعد إغلاق الوردية (فشلها ما بيلغيش الإغلاق، بس بيتنبّه له المستخدم)
            string backupNote = RunAutoBackupAfterShift();
            if (backupNote.Length > 0) StatusMessage += " " + backupNote;
        }

        /// <summary>طريقة تنبيه المستخدم لفشل النسخ التلقائي (الافتراضي نافذة تحذير؛ الاختبارات بتستبدلها)</summary>
        public Action<string> BackupWarningNotifier { get; set; } = msg =>
            System.Windows.MessageBox.Show(msg, "النسخ الاحتياطي التلقائي",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

        /// <summary>يرجع نص للعرض للمستخدم (فاضي لو النسخ التلقائي معطّل أو الخدمة غير متاحة). لا يرمي استثناءات.</summary>
        public string RunAutoBackupAfterShift()
        {
            if (_backupService == null) return string.Empty;
            try
            {
                var result = _backupService.RunAfterShiftBackup(_backupSettingsProvider());
                if (result == null) return string.Empty;
                if (result.Success && !result.ExternalFailed) return "تم أخذ نسخة احتياطية تلقائية.";

                string warn = "⚠ " + result.Message;
                BackupWarningNotifier(warn);
                return warn;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.RunAutoBackupAfterShift");
                return "⚠ تعذر أخذ النسخة الاحتياطية التلقائية: " + ex.Message;
            }
        }

        public void AddUser()
        {
            if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewFullName) || string.IsNullOrWhiteSpace(NewPassword))
            {
                StatusMessage = "يرجى تعبئة كافة حقول الموظف بما فيها كلمة المرور!";
                return;
            }

            try
            {
                // NewRole مقصورة في هذه الشاشة على Admin/Pharmacist/Cashier فقط؛
                // SaveStaffUser نفسها ترفض أي محاولة لإنشاء حساب بدور Owner كحماية إضافية.
                _userRepository.SaveStaffUser(0, NewUsername, NewFullName, NewPassword, NewRole, true);
                StatusMessage = $"تم حفظ حساب الموظف '{NewFullName}' بنجاح.";
                NewUsername = string.Empty;
                NewFullName = string.Empty;
                NewPassword = string.Empty;
                LoadData();
            }
            catch (Exception ex)
            {
                StatusMessage = $"تعذر حفظ الموظف: {ex.Message}";
            }
        }
    }
}
