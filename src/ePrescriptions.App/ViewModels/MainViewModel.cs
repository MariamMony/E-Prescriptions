using System;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;
using ePrescriptions.App.Views;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly INavigationService _navigationService;
        private readonly IUserSessionService _sessionService;
        private readonly IServiceProvider _serviceProvider;
        private readonly DispatcherTimer _clockTimer;

        private ViewModelBase? _currentView;
        public ViewModelBase? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private string _liveDateTimeString = string.Empty;
        public string LiveDateTimeString
        {
            get => _liveDateTimeString;
            set => SetProperty(ref _liveDateTimeString, value);
        }

        private string _currentUserDisplay = "ماجد القبانى (مدير النظام)";
        public string CurrentUserDisplay
        {
            get => _currentUserDisplay;
            set => SetProperty(ref _currentUserDisplay, value);
        }

        private string _shiftStatusDisplay = "الوردية رقم 1 [مفتوحة]";
        public string ShiftStatusDisplay
        {
            get => _shiftStatusDisplay;
            set => SetProperty(ref _shiftStatusDisplay, value);
        }

        // أوامر التنقل للشريط الجانبي
        public IRelayCommand NavigateSalesCommand { get; }
        public IRelayCommand NavigateInventoryCommand { get; }
        public IRelayCommand NavigatePurchasesCommand { get; }
        public IRelayCommand NavigateSupplierReturnsCommand { get; }
        public IRelayCommand NavigateCustomersCommand { get; }
        public IRelayCommand NavigateFinanceCommand { get; }
        public IRelayCommand NavigateStaffCommand { get; }
        public IRelayCommand NavigateZakatCommand { get; }
        public IRelayCommand NavigateReportsCommand { get; }
        public IRelayCommand NavigateSettingsCommand { get; }
        public IRelayCommand NavigateHelpCommand { get; }
        public IRelayCommand NavigateOwnerLogCommand { get; }
        public IRelayCommand NavigateStaffAuditLogCommand { get; }

        /// <summary>
        /// true فقط لحساب صاحب الصيدلية؛ تتحكم في ظهور زر "سجل نشاطي" (المخفي عن الجميع) في الشريط الجانبي.
        /// لا تُستخدم كآلية حماية وحيدة: شاشة OwnerLogViewModel نفسها لا تُستدعى إلا عبر هذا الزر.
        /// </summary>
        public bool IsOwner => _sessionService.CurrentUser?.Role == ePrescriptions.Domain.Enums.UserRole.Owner;

        /// <summary>
        /// true للمدير وصاحب الصيدلية؛ تتحكم في ظهور زر "سجل نشاط الموظفين" (لا يحتوي على تعديلات Owner).
        /// </summary>
        public bool IsAdminOrOwner =>
            _sessionService.CurrentUser?.Role == ePrescriptions.Domain.Enums.UserRole.Admin || IsOwner;

        // ===== صلاحيات التنقل حسب الدور =====
        // Cashier: المبيعات فقط. Pharmacist: المبيعات + المخزون + المشتريات + العملاء + التقارير.
        // Admin/Owner: كل شيء (سجل صاحب الصيدلية مقصور على Owner وسجل الموظفين على Admin/Owner، كما هو أعلاه).
        private ePrescriptions.Domain.Enums.UserRole? CurrentRole => _sessionService.CurrentUser?.Role;

        public bool CanAccessInventory => CurrentRole is ePrescriptions.Domain.Enums.UserRole.Pharmacist or ePrescriptions.Domain.Enums.UserRole.Admin or ePrescriptions.Domain.Enums.UserRole.Owner;
        public bool CanAccessPurchases => CanAccessInventory;
        public bool CanAccessCustomers => CanAccessInventory;
        public bool CanAccessReports => CanAccessInventory;
        public bool CanAccessFinance => IsAdminOrOwner;
        public bool CanAccessStaff => IsAdminOrOwner;
        public bool CanAccessZakat => IsAdminOrOwner;
        public bool CanAccessSettings => IsAdminOrOwner;

        // أوامر الأمان والجلسة
        public IRelayCommand LockScreenCommand { get; }
        public IRelayCommand LogoutCommand { get; }

        public MainViewModel(
            INavigationService navigationService,
            IUserSessionService sessionService,
            IServiceProvider serviceProvider)
        {
            _navigationService = navigationService;
            _sessionService = sessionService;
            _serviceProvider = serviceProvider;

            _navigationService.CurrentViewModelChanged += vm => CurrentView = vm;
            _sessionService.SessionChanged += UpdateSessionDisplay;

            // تحديث شريط التاريخ والوقت كل ثانية باستخدام UmAlQura والأرقام اللاتينية
            UpdateLiveClock();
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) => UpdateLiveClock();
            _clockTimer.Start();

            // كل أمر تنقل محروس بـ CanExecute حسب الدور (دفاع إضافي بجانب إخفاء الزر في XAML،
            // فحتى لو ظهر الزر بطريقة ما، الأمر نفسه يرفض التنفيذ).
            NavigateSalesCommand = new RelayCommand(() => _navigationService.NavigateTo<SalesViewModel>());
            NavigateInventoryCommand = new RelayCommand(() => _navigationService.NavigateTo<InventoryViewModel>(), () => CanAccessInventory);
            NavigatePurchasesCommand = new RelayCommand(() => _navigationService.NavigateTo<PurchasesViewModel>(), () => CanAccessPurchases);
            NavigateSupplierReturnsCommand = new RelayCommand(() => _navigationService.NavigateTo<SupplierReturnsViewModel>(), () => CanAccessPurchases);
            NavigateCustomersCommand = new RelayCommand(() => _navigationService.NavigateTo<CustomersViewModel>(), () => CanAccessCustomers);
            NavigateFinanceCommand = new RelayCommand(() => _navigationService.NavigateTo<FinanceViewModel>(), () => CanAccessFinance);
            NavigateStaffCommand = new RelayCommand(() => _navigationService.NavigateTo<StaffViewModel>(), () => CanAccessStaff);
            NavigateZakatCommand = new RelayCommand(() => _navigationService.NavigateTo<ZakatViewModel>(), () => CanAccessZakat);
            NavigateReportsCommand = new RelayCommand(() => _navigationService.NavigateTo<ReportsViewModel>(), () => CanAccessReports);
            NavigateSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo<SettingsViewModel>(), () => CanAccessSettings);
            NavigateHelpCommand = new RelayCommand(() => _navigationService.NavigateTo<HelpViewModel>());
            NavigateOwnerLogCommand = new RelayCommand(() => _navigationService.NavigateTo<OwnerLogViewModel>(), () => IsOwner);
            NavigateStaffAuditLogCommand = new RelayCommand(() => _navigationService.NavigateTo<StaffAuditLogViewModel>(), () => IsAdminOrOwner);

            LockScreenCommand = new RelayCommand(ExecuteLockScreen);
            LogoutCommand = new RelayCommand(ExecuteLogout);

            UpdateSessionDisplay();
        }

        private void UpdateLiveClock()
        {
            LiveDateTimeString = DateHelper.FormatFullBanner(DateTime.Now);
        }

        private void UpdateSessionDisplay()
        {
            OnPropertyChanged(nameof(IsOwner));
            OnPropertyChanged(nameof(IsAdminOrOwner));
            OnPropertyChanged(nameof(CanAccessInventory));
            OnPropertyChanged(nameof(CanAccessPurchases));
            OnPropertyChanged(nameof(CanAccessCustomers));
            OnPropertyChanged(nameof(CanAccessReports));
            OnPropertyChanged(nameof(CanAccessFinance));
            OnPropertyChanged(nameof(CanAccessStaff));
            OnPropertyChanged(nameof(CanAccessZakat));
            OnPropertyChanged(nameof(CanAccessSettings));

            // إعادة تقييم CanExecute لكل أوامر التنقل بعد تغيّر الجلسة (دخول/خروج/قفل)
            NavigateInventoryCommand.NotifyCanExecuteChanged();
            NavigatePurchasesCommand.NotifyCanExecuteChanged();
            NavigateSupplierReturnsCommand.NotifyCanExecuteChanged();
            NavigateCustomersCommand.NotifyCanExecuteChanged();
            NavigateReportsCommand.NotifyCanExecuteChanged();
            NavigateFinanceCommand.NotifyCanExecuteChanged();
            NavigateStaffCommand.NotifyCanExecuteChanged();
            NavigateZakatCommand.NotifyCanExecuteChanged();
            NavigateSettingsCommand.NotifyCanExecuteChanged();
            NavigateOwnerLogCommand.NotifyCanExecuteChanged();
            NavigateStaffAuditLogCommand.NotifyCanExecuteChanged();

            if (_sessionService.CurrentUser != null)
            {
                CurrentUserDisplay = $"{_sessionService.CurrentUser.FullName} ({_sessionService.CurrentUser.Role})";
            }
            else
            {
                CurrentUserDisplay = string.Empty;
            }

            if (_sessionService.CurrentShift != null)
            {
                ShiftStatusDisplay = $"الوردية رقم {_sessionService.CurrentShift.Id} [مفتوحة]";
            }
            else
            {
                ShiftStatusDisplay = "لا توجد وردية مفتوحة";
            }
        }

        private void ExecuteLockScreen()
        {
            _sessionService.Lock();
            var loginVm = (LoginViewModel)_serviceProvider.GetService(typeof(LoginViewModel))!;
            loginVm.SetMode(lockMode: true);

            var loginWin = new LoginWindow(loginVm)
            {
                Owner = Application.Current.MainWindow
            };
            loginWin.ShowDialog();
        }

        private void ExecuteLogout()
        {
            _sessionService.Logout();
            var loginVm = (LoginViewModel)_serviceProvider.GetService(typeof(LoginViewModel))!;
            loginVm.SetMode(lockMode: false);

            var loginWin = new LoginWindow(loginVm)
            {
                Owner = Application.Current.MainWindow
            };
            if (loginWin.ShowDialog() != true)
            {
                Application.Current.Shutdown();
            }
        }
    }
}
