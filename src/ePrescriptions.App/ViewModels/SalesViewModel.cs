using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class SalesViewModel : ViewModelBase
    {
        private readonly ItemRepository _itemRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly CustomerRepository _customerRepository;
        private readonly SettingsRepository _settingsRepository;
        private readonly Services.IPdfReportService? _pdfReportService;
        private readonly Services.IUserSessionService? _sessionService;

        // المستخدم والوردية الحاليان من جلسة الدخول (القيم الافتراضية للتوافق عند غياب الجلسة)
        private int CurrentUserId => _sessionService?.CurrentUser?.Id ?? 0;
        private string CurrentUserName => _sessionService?.CurrentUser?.FullName ?? string.Empty;

        /// <summary>رقم الوردية المفتوحة الحالية، أو 0 لو مفيش وردية مفتوحة (البيع مرفوض ساعتها)</summary>
        private int CurrentShiftId =>
            _sessionService?.CurrentShift != null && _sessionService.CurrentShift.Status == ShiftStatus.Open
                ? _sessionService.CurrentShift.Id
                : 0;
        public bool HasOpenShift => CurrentShiftId > 0;
        public bool NoOpenShift => !HasOpenShift;

        private readonly ShiftRepository? _shiftRepository;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;

        private decimal _openingBalancePounds;
        /// <summary>رصيد الافتتاح (بالجنيه) عند فتح وردية من شاشة البيع</summary>
        public decimal OpeningBalancePounds
        {
            get => _openingBalancePounds;
            set => SetProperty(ref _openingBalancePounds, value < 0 ? 0 : value);
        }

        public IRelayCommand OpenShiftCommand { get; }

        /// <summary>
        /// تأكيد تجاوز حد الائتمان (للمدير فقط). الافتراضي نافذة سؤال، ويمكن استبداله في الاختبارات.
        /// </summary>
        public Func<string, bool> ConfirmCreditLimitOverride { get; set; } = message =>
            System.Windows.MessageBox.Show(
                message + "\n\nتوافق على تجاوز الحد وتسجيل الفاتورة؟",
                "تجاوز حد الائتمان",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No,
                System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign) == System.Windows.MessageBoxResult.Yes;

        // رأس الفاتورة
        private string _invoiceNumber = string.Empty;
        public string InvoiceNumber
        {
            get => _invoiceNumber;
            set => SetProperty(ref _invoiceNumber, value);
        }

        private InvoiceType _currentInvoiceType = InvoiceType.Sales;
        public InvoiceType CurrentInvoiceType
        {
            get => _currentInvoiceType;
            set
            {
                if (SetProperty(ref _currentInvoiceType, value))
                {
                    OnPropertyChanged(nameof(IsReturnMode));
                    OnPropertyChanged(nameof(InvoiceTypeTitle));
                }
            }
        }

        public bool IsReturnMode => CurrentInvoiceType == InvoiceType.Return;

        // ===== مرتجع المبيعات: مرتبط بفاتورة بيع أصلية =====
        private ReturnableInvoice? _returnSource;

        private string _originalInvoiceNumber = string.Empty;
        public string OriginalInvoiceNumber
        {
            get => _originalInvoiceNumber;
            set => SetProperty(ref _originalInvoiceNumber, value);
        }

        public IRelayCommand LoadOriginalInvoiceCommand { get; }
        public string InvoiceTypeTitle => IsReturnMode ? "فاتورة مرتجع مبيعات" : "فاتورة مبيعات جديدة";

        // بيانات العميل والوصفة
        private string _customerName = "عميل نقدي";
        public string CustomerName
        {
            get => _customerName;
            set => SetProperty(ref _customerName, value);
        }

        private string _customerPhone = string.Empty;
        public string CustomerPhone
        {
            get => _customerPhone;
            set => SetProperty(ref _customerPhone, value);
        }

        private PriceCategory _selectedPriceCategory = PriceCategory.Retail;
        public PriceCategory SelectedPriceCategory
        {
            get => _selectedPriceCategory;
            set
            {
                if (SetProperty(ref _selectedPriceCategory, value))
                {
                    // تحديث الأسعار لجميع البنود الحالية
                    foreach (var item in CartItems)
                    {
                        item.UpdatePriceBasedOnUnit();
                    }
                    RecalculateTotals();
                }
            }
        }

        private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;
        public PaymentMethod SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set
            {
                if (SetProperty(ref _selectedPaymentMethod, value))
                {
                    if (value == PaymentMethod.Cash || value == PaymentMethod.Network)
                    {
                        PaidRiyals = TotalNetRiyals;
                    }
                    else if (value == PaymentMethod.OnAccount)
                    {
                        PaidRiyals = 0;
                    }
                    RecalculateTotals();
                }
            }
        }

        private string _prescriptionNumber = string.Empty;
        public string PrescriptionNumber
        {
            get => _prescriptionNumber;
            set => SetProperty(ref _prescriptionNumber, value);
        }

        private string _doctorName = string.Empty;
        public string DoctorName
        {
            get => _doctorName;
            set => SetProperty(ref _doctorName, value);
        }

        // البحث اللحظي والباركود
        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    PerformSearch(value);
                }
            }
        }

        public ObservableCollection<Item> SearchResults { get; } = new();
        public ObservableCollection<Item> TopSellingItems { get; } = new();
        public ObservableCollection<SalesItemRow> CartItems { get; } = new();
        public ObservableCollection<Customer> CustomersList { get; } = new();

        // المجاميع بالقروش
        private long _subtotalPiastres;
        public long SubtotalPiastres
        {
            get => _subtotalPiastres;
            set => SetProperty(ref _subtotalPiastres, value);
        }

        private long _discountPiastres;
        public long DiscountPiastres
        {
            get => _discountPiastres;
            set => SetProperty(ref _discountPiastres, value);
        }

        private long _vatPiastres;
        public long VatPiastres
        {
            get => _vatPiastres;
            set => SetProperty(ref _vatPiastres, value);
        }

        private long _netTotalPiastres;
        public long NetTotalPiastres
        {
            get => _netTotalPiastres;
            set => SetProperty(ref _netTotalPiastres, value);
        }

        private decimal _paidRiyals;
        public decimal PaidRiyals
        {
            get => _paidRiyals;
            set
            {
                if (SetProperty(ref _paidRiyals, value))
                {
                    RecalculateTotals();
                }
            }
        }

        private long _remainingPiastres;
        public long RemainingPiastres
        {
            get => _remainingPiastres;
            set => SetProperty(ref _remainingPiastres, value);
        }

        // خواص العرض بالجنيه المصري (مع الحفاظ على الأسماء للتوافق الكامل)
        public decimal SubtotalRiyals => Money.FromPiastres(SubtotalPiastres).ToPounds();
        public decimal DiscountRiyals => Money.FromPiastres(DiscountPiastres).ToPounds();
        public decimal VatRiyals => Money.FromPiastres(VatPiastres).ToPounds();
        public decimal TotalNetRiyals => Money.FromPiastres(NetTotalPiastres).ToPounds();
        public decimal RemainingRiyals => Money.FromPiastres(RemainingPiastres).ToPounds();

        // مرادفات بالجنيه
        public decimal SubtotalPounds => SubtotalRiyals;
        public decimal VatPounds => VatRiyals;
        public decimal TotalNetPounds => TotalNetRiyals;
        public decimal RemainingPounds => RemainingRiyals;
        public decimal PaidPounds => PaidRiyals;

        // إشعارات الحالة
        private string _statusMessage = "جاهز لاستقبال عمليات البيع";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isStatusSuccess = true;
        public bool IsStatusSuccess
        {
            get => _isStatusSuccess;
            set => SetProperty(ref _isStatusSuccess, value);
        }

        // الأوامر (F1..F10)
        public IRelayCommand NewInvoiceCommand { get; }
        public IRelayCommand SaveInvoiceCommand { get; }
        public IRelayCommand PrintInvoiceCommand { get; }
        public IRelayCommand PrintA4InvoicePdfCommand { get; }
        public IRelayCommand SuspendInvoiceCommand { get; }
        public IRelayCommand ToggleReturnModeCommand { get; }
        public IRelayCommand OpenCashDrawerCommand { get; }
        public IRelayCommand<SalesItemRow> RemoveItemCommand { get; }
        public IRelayCommand<Item> AddItemToCartCommand { get; }
        public IRelayCommand BarcodeEnterCommand { get; }

        public SalesViewModel(
            ItemRepository itemRepository,
            InvoiceRepository invoiceRepository,
            CustomerRepository customerRepository,
            SettingsRepository settingsRepository,
            Services.IPdfReportService? pdfReportService = null,
            Services.IUserSessionService? sessionService = null,
            ShiftRepository? shiftRepository = null,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null)
        {
            _shiftRepository = shiftRepository;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;
            Title = "نقطة البيع - المبيعات";
            _itemRepository = itemRepository;
            _invoiceRepository = invoiceRepository;
            _customerRepository = customerRepository;
            _settingsRepository = settingsRepository;
            _pdfReportService = pdfReportService;

            NewInvoiceCommand = new RelayCommand(StartNewInvoice);
            SaveInvoiceCommand = new RelayCommand(SaveCurrentInvoice);
            PrintInvoiceCommand = new RelayCommand(PrintCurrentInvoice);
            PrintA4InvoicePdfCommand = new RelayCommand(PrintA4InvoicePdf);
            SuspendInvoiceCommand = new RelayCommand(SuspendCurrentInvoice);
            ToggleReturnModeCommand = new RelayCommand(ToggleReturnMode);
            OpenCashDrawerCommand = new RelayCommand(OpenDrawer);
            RemoveItemCommand = new RelayCommand<SalesItemRow>(RemoveItem);
            AddItemToCartCommand = new RelayCommand<Item>(AddItem);
            BarcodeEnterCommand = new RelayCommand(HandleBarcodeEnter);
            LoadOriginalInvoiceCommand = new RelayCommand(LoadOriginalInvoice);
            OpenShiftCommand = new RelayCommand(OpenShiftFromSales);

            LoadInitialData();
            StartNewInvoice();
        }

        private void LoadInitialData()
        {
            try
            {
                var topItems = _itemRepository.GetTopSellingItems(8);
                TopSellingItems.Clear();
                foreach (var itm in topItems) TopSellingItems.Add(itm);

                var customers = _customerRepository.GetAll();
                CustomersList.Clear();
                foreach (var c in customers) CustomersList.Add(c);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.LoadInitialData");
            }
        }

        public void StartNewInvoice()
        {
            InvoiceNumber = _invoiceRepository.GenerateNextInvoiceNumber(CurrentInvoiceType);
            _returnSource = null;
            OriginalInvoiceNumber = string.Empty;
            CustomerName = "عميل نقدي";
            CustomerPhone = string.Empty;
            PrescriptionNumber = string.Empty;
            DoctorName = string.Empty;
            SelectedPaymentMethod = PaymentMethod.Cash;
            SelectedPriceCategory = PriceCategory.Retail;
            CartItems.Clear();
            SearchQuery = string.Empty;
            SearchResults.Clear();
            PaidRiyals = 0;
            RecalculateTotals();
            SetStatus("تم تجهيز فاتورة جديدة جاهزة للبيع", true);
        }

        private void PerformSearch(string query)
        {
            SearchResults.Clear();
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return;

            try
            {
                var list = _itemRepository.Search(query, 15);
                foreach (var itm in list) SearchResults.Add(itm);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.PerformSearch");
            }
        }

        public void HandleBarcodeEnter()
        {
            string code = SearchQuery.Trim();
            if (string.IsNullOrEmpty(code)) return;

            if (IsReturnMode)
            {
                SetStatus("المرتجع بيتعمل من فاتورة بيع أصلية: اكتب رقم الفاتورة واضغط تحميل.", false);
                return;
            }

            // ملصق تشغيلة مطبوع على العلبة: يحدد الصنف والتشغيلة (ومنها تاريخ الصلاحية)
            if (BatchLabelCode.TryDecode(code, out int batchId))
            {
                var found = _itemRepository.GetBatchWithItem(batchId);
                if (found != null)
                {
                    AddItemFromBatchLabel(found.Value.Item, found.Value.Batch);
                    SearchQuery = string.Empty;
                    SearchResults.Clear();
                    return;
                }
            }

            var item = _itemRepository.GetByBarcodeOrCode(code);
            if (item != null)
            {
                AddItem(item);
                SearchQuery = string.Empty;
                SearchResults.Clear();
            }
            else
            {
                SetStatus($"لم يتم العثور على صنف بالباركود: {code}", false);
            }
        }

        private int GetDefaultVat()
        {
            try { return _settingsRepository.GetSettings().DefaultVatPercent; }
            catch { return 14; }
        }

        public void AddItem(Item? item)
        {
            if (item == null) return;

            if (IsReturnMode)
            {
                SetStatus("المرتجع بيتعمل من فاتورة بيع أصلية: اكتب رقم الفاتورة واضغط تحميل.", false);
                return;
            }

            int defaultVat = GetDefaultVat();

            // التحقق مما إذا كان الصنف مضافاً مسبقاً بنفس الوحدة (بنود التشغيلة المحددة بالملصق لا تُدمج هنا)
            var existing = CartItems.FirstOrDefault(x => x.ItemId == item.Id && x.BatchId == null && x.SelectedUnitLevel == UnitLevel.Box);
            if (existing != null)
            {
                existing.Quantity++;
                SetStatus($"تمت إضافة: {item.NameAr}", true);
            }
            else
            {
                var row = new SalesItemRow(item, SelectedPriceCategory, defaultVat);
                string warning = BuildStockWarning(item, row);
                row.WarningText = warning;
                row.PropertyChanged += (s, e) => RecalculateTotals();
                CartItems.Add(row);

                if (item.IsPrescriptionRequired)
                    warning = ("يتطلب وصفة طبية (رقم الوصفة واسم الطبيب إلزاميان)" + (warning.Length > 0 ? " | " + warning : string.Empty));
                row.WarningText = warning;

                if (warning.Length > 0)
                {
                    SetStatus($"{item.NameAr}: {warning}", false);
                    PlayWarningSound();
                }
                else
                {
                    SetStatus($"تمت إضافة: {item.NameAr}", true);
                }
            }

            RecalculateTotals();
        }

        /// <summary>
        /// بيع بقراءة ملصق التشغيلة: التشغيلة المنتهية ممنوعة، وأي تشغيلة أقدم على الرف تظهر كتنبيه فوري.
        /// </summary>
        public void AddItemFromBatchLabel(Item item, ItemBatch batch)
        {
            if (ExpiryPolicy.IsExpired(batch.ExpiryDate))
            {
                SetStatus($"⛔ {item.NameAr}: التشغيلة {batch.BatchNumber} منتهية ({batch.ExpiryDate:yyyy-MM-dd}) — مينفعش تتباع، اسحبها من الرف.", false);
                PlayWarningSound();
                return;
            }

            var warnings = new System.Collections.Generic.List<string>();

            if (batch.RemainingSmallestUnits <= 0)
                warnings.Add("رصيد التشغيلة صفر بالنظام (راجع الجرد)");

            var older = _itemRepository.GetOlderSellableBatches(item.Id, batch.ExpiryDate, batch.Id);
            if (older.Count > 0)
            {
                var o = older[0];
                string qty = UnitBreakdownHelper.FormatStockDetailed(o.RemainingSmallestUnits, item.StripsPerBox, item.PiecesPerStrip);
                warnings.Add($"في تشغيلة أقدم على الرف: {o.BatchNumber} بتنتهي {o.ExpiryDate:yyyy-MM-dd} (الكمية {qty}) — الأفضل تبيعها الأول");
            }

            if (ExpiryPolicy.IsNearExpiry(batch.ExpiryDate))
                warnings.Add($"قريبة الانتهاء ({batch.ExpiryDate:yyyy-MM-dd})");

            string warning = string.Join(" | ", warnings);

            var existing = CartItems.FirstOrDefault(x => x.ItemId == item.Id && x.BatchId == batch.Id && x.SelectedUnitLevel == UnitLevel.Box);
            if (existing != null)
            {
                existing.Quantity++;
            }
            else
            {
                var row = new SalesItemRow(item, SelectedPriceCategory, GetDefaultVat())
                {
                    BatchId = batch.Id,
                    BatchNumber = batch.BatchNumber,
                    ExpiryDate = batch.ExpiryDate
                };
                row.WarningText = warning;
                row.PropertyChanged += (s, e) => RecalculateTotals();
                CartItems.Add(row);
            }

            RecalculateTotals();

            if (warning.Length > 0)
            {
                SetStatus($"{item.NameAr} (صلاحية {batch.ExpiryDate:yyyy-MM-dd}): {warning}", false);
                PlayWarningSound();
            }
            else
            {
                SetStatus($"تمت إضافة: {item.NameAr} — تشغيلة {batch.BatchNumber} صلاحية {batch.ExpiryDate:yyyy-MM-dd}", true);
            }
        }

        /// <summary>
        /// تنبيهات المخزون عند الإضافة العادية (بالباركود التجاري/البحث): منتهي على الرف، لا توجد تشغيلة سارية، قرب الانتهاء
        /// </summary>
        private string BuildStockWarning(Item item, SalesItemRow row)
        {
            var warnings = new System.Collections.Generic.List<string>();
            try
            {
                var expired = _itemRepository.GetExpiredBatchesWithStock(item.Id);
                if (expired.Count > 0)
                    warnings.Add($"فيه تشغيلة منتهية على الرف ({string.Join("، ", expired.Select(b => b.BatchNumber))}) — اسحبها ومتبعهاش");

                if (item.Batches == null || item.Batches.Count == 0)
                    warnings.Add("مفيش تشغيلة سارية — هتتسجل بالسالب لحد ما تدخل فاتورة شراء");
                else if (ExpiryPolicy.IsNearExpiry(item.Batches[0].ExpiryDate))
                    warnings.Add($"أقرب تشغيلة قريبة الانتهاء ({item.Batches[0].ExpiryDate:yyyy-MM-dd})");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.BuildStockWarning");
            }
            return string.Join(" | ", warnings);
        }

        /// <summary>
        /// فتح وردية للمستخدم الحالي من شاشة البيع نفسها برصيد افتتاح (الكاشير مالوش صلاحية شاشة الموظفين)
        /// </summary>
        public void OpenShiftFromSales()
        {
            var user = _sessionService?.CurrentUser;
            if (user == null || _shiftRepository == null)
            {
                SetStatus("سجّل الدخول الأول عشان تفتح وردية.", false);
                return;
            }

            if (HasOpenShift)
            {
                SetStatus("فيه وردية مفتوحة بالفعل.", true);
                return;
            }

            var existing = _shiftRepository.GetCurrentOpenShift(user.Id);
            if (existing == null)
            {
                long openingPiastres = Money.FromPounds(OpeningBalancePounds).Piastres;
                int shiftId = _shiftRepository.OpenShift(user.Id, user.FullName, openingPiastres);
                existing = _shiftRepository.GetCurrentOpenShift(user.Id);
                if (existing == null)
                {
                    SetStatus($"تعذر فتح الوردية رقم {shiftId}.", false);
                    return;
                }
            }

            _sessionService!.SetCurrentShift(existing);
            OnPropertyChanged(nameof(HasOpenShift));
            OnPropertyChanged(nameof(NoOpenShift));
            SetStatus($"تم فتح الوردية رقم {existing.Id} برصيد افتتاح {OpeningBalancePounds:N2} ج.م. تقدر تبدأ البيع.", true);
        }

        /// <summary>
        /// تحميل فاتورة البيع الأصلية لبدء المرتجع: الأصناف والأسعار والوحدات من الفاتورة،
        /// والكمية تبدأ بالمتبقي للإرجاع ويمكن تقليلها أو حذف البند.
        /// </summary>
        public void LoadOriginalInvoice()
        {
            if (!IsReturnMode) return;

            var source = _invoiceRepository.GetReturnableInvoice(OriginalInvoiceNumber, out string err);
            if (source == null)
            {
                SetStatus(err, false);
                PlayWarningSound();
                return;
            }

            _returnSource = source;
            CartItems.Clear();

            CustomerName = source.Invoice.CustomerName;
            CustomerPhone = source.Invoice.CustomerPhone;
            PrescriptionNumber = source.Invoice.PrescriptionNumber ?? string.Empty;
            DoctorName = source.Invoice.DoctorName ?? string.Empty;
            SelectedPriceCategory = source.Invoice.PriceCategory;
            SelectedPaymentMethod = source.Invoice.PaymentMethod;

            int defaultVat = GetDefaultVat();
            foreach (var line in source.Lines.Where(l => l.RemainingQuantity > 0))
            {
                var item = _itemRepository.GetById(line.Line.ItemId);
                if (item == null) continue;

                var row = new SalesItemRow(item, source.Invoice.PriceCategory, defaultVat);
                row.ConfigureAsReturnLine(line.Line, line.RemainingQuantity);
                row.PropertyChanged += (s, e) => RecalculateTotals();
                CartItems.Add(row);
            }

            RecalculateTotals();
            SetStatus($"تم تحميل فاتورة {source.Invoice.InvoiceNumber}: عدّل الكميات المرتجعة أو احذف البنود اللي مش راجعة.", true);
        }

        public void RemoveItem(SalesItemRow? row)
        {
            if (row != null && CartItems.Contains(row))
            {
                CartItems.Remove(row);
                RecalculateTotals();
                SetStatus($"تم حذف البند: {row.NameAr}", true);
            }
        }

        public void RecalculateTotals()
        {
            long sub = 0;
            long disc = 0;
            long vat = 0;
            long net = 0;

            foreach (var row in CartItems)
            {
                long gross = row.UnitPricePiastres * row.Quantity;
                sub += gross;
                disc += row.DiscountPiastres;
                vat += row.VatPiastres;
                net += row.TotalPiastres;
            }

            SubtotalPiastres = sub;
            DiscountPiastres = disc;
            VatPiastres = vat;
            NetTotalPiastres = net;

            if (SelectedPaymentMethod == PaymentMethod.Cash || SelectedPaymentMethod == PaymentMethod.Network)
            {
                PaidRiyals = TotalNetRiyals;
            }

            long paidPiastres = Money.FromPounds(PaidRiyals).Piastres;
            RemainingPiastres = Math.Max(0, NetTotalPiastres - paidPiastres);

            OnPropertyChanged(nameof(SubtotalRiyals));
            OnPropertyChanged(nameof(DiscountRiyals));
            OnPropertyChanged(nameof(VatRiyals));
            OnPropertyChanged(nameof(TotalNetRiyals));
            OnPropertyChanged(nameof(RemainingRiyals));

            OnPropertyChanged(nameof(SubtotalPounds));
            OnPropertyChanged(nameof(VatPounds));
            OnPropertyChanged(nameof(TotalNetPounds));
            OnPropertyChanged(nameof(RemainingPounds));
            OnPropertyChanged(nameof(PaidPounds));
        }

        public void SaveCurrentInvoice()
        {
            if (CartItems.Count == 0)
            {
                SetStatus("لا يمكن حفظ فاتورة فارغة! أضف أصنافاً أولاً.", false);
                return;
            }

            if (!IsReturnMode)
            {
                var rxItems = CartItems.Where(r => r.RequiresPrescription).ToList();
                if (rxItems.Count > 0 && (string.IsNullOrWhiteSpace(PrescriptionNumber) || string.IsNullOrWhiteSpace(DoctorName)))
                {
                    SetStatus($"الأصناف دي بتتطلب وصفة طبية: {string.Join("، ", rxItems.Select(r => r.NameAr))} — اكتب رقم الوصفة واسم الطبيب الأول.", false);
                    PlayWarningSound();
                    return;
                }
            }

            if (!HasOpenShift)
            {
                SetStatus("لازم تفتح وردية الأول (من شاشة الموظفين والورديات برصيد افتتاح) قبل أي بيع أو مرتجع.", false);
                PlayWarningSound();
                return;
            }

            if (IsReturnMode && _returnSource == null)
            {
                SetStatus("حمّل فاتورة البيع الأصلية الأول: اكتب رقمها واضغط تحميل.", false);
                return;
            }

            try
            {
                // ربط الفاتورة بعميل مسجل (مطلوب لتحديث رصيد العميل في البيع الآجل)
                string typedName = (CustomerName ?? string.Empty).Trim();
                var matchedCustomer = CustomersList.FirstOrDefault(c => c.Id > 1 &&
                    string.Equals(c.Name.Trim(), typedName, StringComparison.OrdinalIgnoreCase));

                if (SelectedPaymentMethod == PaymentMethod.OnAccount && matchedCustomer == null)
                {
                    SetStatus("البيع الآجل يتطلب اختيار عميل مسجل بنفس الاسم من قائمة العملاء.", false);
                    return;
                }

                var settings = _settingsRepository.GetSettings();
                DateTime now = DateTime.Now;

                // إنشاء رمز QR بسيط إن كان مفعلاً في الإعدادات
                string? qrText = null;
                if (settings.EnableQrCode)
                {
                    qrText = EgyptQrHelper.GenerateSimpleInvoiceQrText(
                        InvoiceNumber,
                        now,
                        TotalNetRiyals,
                        settings.TaxNumber,
                        settings.PharmacyNameAr
                    );
                }

                var invoice = new Invoice
                {
                    InvoiceNumber = InvoiceNumber,
                    Type = CurrentInvoiceType,
                    Date = now,
                    CustomerId = matchedCustomer?.Id,
                    CustomerName = CustomerName,
                    CustomerPhone = CustomerPhone,
                    PriceCategory = SelectedPriceCategory,
                    PaymentMethod = SelectedPaymentMethod,
                    PrescriptionNumber = PrescriptionNumber,
                    DoctorName = DoctorName,
                    UserId = CurrentUserId,
                    UserName = CurrentUserName,
                    ShiftId = CurrentShiftId,
                    SubtotalPiastres = SubtotalPiastres,
                    DiscountPiastres = DiscountPiastres,
                    VatPiastres = VatPiastres,
                    TotalPiastres = NetTotalPiastres,
                    PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                    RemainingPiastres = RemainingPiastres,
                    QrCodeText = qrText,
                    OriginalInvoiceId = IsReturnMode ? _returnSource?.Invoice.Id : null
                };

                foreach (var row in CartItems)
                {
                    invoice.Items.Add(new InvoiceItem
                    {
                        ItemId = row.ItemId,
                        ItemCode = row.Code,
                        ItemNameAr = row.NameAr,
                        BatchId = row.BatchId,
                        PrescriptionNumber = row.RequiresPrescription ? PrescriptionNumber : null,
                        DoctorName = row.RequiresPrescription ? DoctorName : null,
                        OriginalInvoiceItemId = row.OriginalInvoiceItemId,
                        BatchNumber = row.BatchNumber,
                        ExpiryDate = row.ExpiryDate,
                        UnitLevel = row.SelectedUnitLevel,
                        QuantitySold = row.Quantity,
                        SmallestUnitsCount = row.SmallestUnitsToDeduct,
                        UnitPricePiastres = row.UnitPricePiastres,
                        DiscountPiastres = row.DiscountPiastres,
                        VatRatePercent = row.VatRatePercent,
                        VatPiastres = row.VatPiastres,
                        TotalPiastres = row.TotalPiastres
                    });
                }

                bool saved = _invoiceRepository.SaveInvoice(invoice, out string err);

                // تجاوز حد الائتمان: Admin/Owner يقدر يوافق بعد تأكيد صريح، غيرهم بيتم رفض البيع
                if (!saved && err.StartsWith(InvoiceRepository.CreditLimitErrorPrefix, StringComparison.Ordinal))
                {
                    var role = _sessionService?.CurrentUser?.Role;
                    if (role == UserRole.Admin || role == UserRole.Owner)
                    {
                        // رصيد وحد العميل قبل البيع (من القاعدة) لتسجيلهما في سجل التدقيق عند الموافقة
                        var customerBefore = invoice.CustomerId.HasValue ? _customerRepository.GetById(invoice.CustomerId.Value) : null;

                        if (ConfirmCreditLimitOverride(err))
                        {
                            invoice.CreditLimitOverridden = true;
                            saved = _invoiceRepository.SaveInvoice(invoice, out err);
                            if (saved) LogCreditLimitOverride(invoice, customerBefore);
                        }
                    }
                    else
                    {
                        err += " — تجاوز الحد محتاج موافقة مدير النظام.";
                    }
                }

                if (saved)
                {
                    SetStatus($"تم حفظ الفاتورة بنجاح: {invoice.InvoiceNumber}", true);
                    PlayConfirmSound();
                    StartNewInvoice();
                }
                else
                {
                    SetStatus($"فشل حفظ الفاتورة: {err}", false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SaveCurrentInvoice");
                SetStatus($"خطأ غير متوقع: {ex.Message}", false);
            }
        }

        /// <summary>
        /// تسجيل موافقة المدير على تجاوز حد الائتمان في نظام التدقيق: Owner في سجله الخاص، وAdmin في سجل الموظفين.
        /// </summary>
        private void LogCreditLimitOverride(Invoice invoice, Customer? customerBefore)
        {
            try
            {
                var user = _sessionService?.CurrentUser;
                if (user == null) return;

                long limit = customerBefore?.CreditLimitPiastres ?? 0;
                long balanceBefore = customerBefore?.CurrentBalancePiastres ?? 0;
                string name = customerBefore?.Name ?? invoice.CustomerName;

                string M(long piastres) => (piastres / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                string oldValue = $"حد الائتمان {M(limit)} ج.م / الرصيد قبل الفاتورة {M(balanceBefore)} ج.م";
                string newValue = $"فاتورة {invoice.InvoiceNumber} بمبلغ آجل {M(invoice.RemainingPiastres)} ج.م / الرصيد بعدها {M(balanceBefore + invoice.RemainingPiastres)} ج.م / وافق: {user.FullName}";
                string entityId = (invoice.CustomerId ?? 0).ToString();

                if (user.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, "CreditLimitOverride", entityId, name, "تجاوز حد الائتمان", oldValue, newValue);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "CreditLimitOverride", entityId, name, "تجاوز حد الائتمان", oldValue, newValue);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.LogCreditLimitOverride");
            }
        }

        public void PrintCurrentInvoice()
        {
            if (CartItems.Count == 0)
            {
                SetStatus("لا يمكن طباعة فاتورة فارغة!", false);
                return;
            }

            try
            {
                var settings = _settingsRepository.GetSettings();
                string? qrText = null;
                if (settings.EnableQrCode)
                {
                    qrText = EgyptQrHelper.GenerateSimpleInvoiceQrText(
                        InvoiceNumber,
                        DateTime.Now,
                        TotalNetRiyals,
                        settings.TaxNumber,
                        settings.PharmacyNameAr
                    );
                }

                var invoice = new Invoice
                {
                    InvoiceNumber = InvoiceNumber,
                    Date = DateTime.Now,
                    CustomerName = CustomerName,
                    CustomerPhone = CustomerPhone,
                    PaymentMethod = SelectedPaymentMethod,
                    SubtotalPiastres = SubtotalPiastres,
                    DiscountPiastres = DiscountPiastres,
                    VatPiastres = VatPiastres,
                    TotalPiastres = NetTotalPiastres,
                    PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                    RemainingPiastres = RemainingPiastres,
                    QrCodeText = qrText
                };

                foreach (var row in CartItems)
                {
                    invoice.Items.Add(new InvoiceItem
                    {
                        ItemId = row.ItemId,
                        ItemCode = row.Code,
                        ItemNameAr = row.NameAr,
                        BatchNumber = row.BatchNumber,
                        ExpiryDate = row.ExpiryDate,
                        UnitLevel = row.SelectedUnitLevel,
                        QuantitySold = row.Quantity,
                        SmallestUnitsCount = row.SmallestUnitsToDeduct,
                        UnitPricePiastres = row.UnitPricePiastres,
                        DiscountPiastres = row.DiscountPiastres,
                        VatRatePercent = row.VatRatePercent,
                        VatPiastres = row.VatPiastres,
                        TotalPiastres = row.TotalPiastres
                    });
                }

                if (Services.ThermalReceiptPrinter.PrintReceipt(invoice, settings, out string err))
                {
                    SetStatus("تمت طباعة الإيصال الحراري بنجاح (F3)", true);
                }
                else
                {
                    SetStatus($"تعذر طباعة الإيصال: {err}", false);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"خطأ في الطباعة: {ex.Message}", false);
            }
        }

        public void PrintA4InvoicePdf()
        {
            if (CartItems.Count == 0)
            {
                SetStatus("الرجاء إضافة أصناف أولاً قبل طباعة الفاتورة A4", false);
                return;
            }

            if (_pdfReportService == null)
            {
                SetStatus("خدمة طباعة PDF غير مفعلة.", false);
                return;
            }

            try
            {
                var settings = _settingsRepository.GetSettings();
                string? qrText = null;
                if (settings.EnableQrCode)
                {
                    qrText = EgyptQrHelper.GenerateSimpleInvoiceQrText(
                        InvoiceNumber,
                        DateTime.Now,
                        TotalNetRiyals,
                        settings.TaxNumber,
                        settings.PharmacyNameAr
                    );
                }

                var invoice = new Invoice
                {
                    InvoiceNumber = InvoiceNumber,
                    Date = DateTime.Now,
                    CustomerName = CustomerName,
                    CustomerPhone = CustomerPhone,
                    PaymentMethod = SelectedPaymentMethod,
                    SubtotalPiastres = SubtotalPiastres,
                    DiscountPiastres = DiscountPiastres,
                    VatPiastres = VatPiastres,
                    TotalPiastres = NetTotalPiastres,
                    PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                    RemainingPiastres = RemainingPiastres,
                    QrCodeText = qrText
                };

                foreach (var row in CartItems)
                {
                    invoice.Items.Add(new InvoiceItem
                    {
                        ItemId = row.ItemId,
                        ItemCode = row.Code,
                        ItemNameAr = row.NameAr,
                        BatchNumber = row.BatchNumber,
                        ExpiryDate = row.ExpiryDate,
                        UnitLevel = row.SelectedUnitLevel,
                        QuantitySold = row.Quantity,
                        SmallestUnitsCount = row.SmallestUnitsToDeduct,
                        UnitPricePiastres = row.UnitPricePiastres,
                        DiscountPiastres = row.DiscountPiastres,
                        VatRatePercent = row.VatRatePercent,
                        VatPiastres = row.VatPiastres,
                        TotalPiastres = row.TotalPiastres
                    });
                }

                string pdfPath = _pdfReportService.GenerateA4InvoicePdf(invoice, settings);
                SetStatus($"تم توليد فاتورة A4 الضريبية بنجاح: {System.IO.Path.GetFileName(pdfPath)}", true);
                System.Windows.MessageBox.Show(
                    $"تم إنشاء فاتورة المبيعات الضريبية الرسمية A4 بصيغة PDF بنجاح:\n{pdfPath}",
                    "فاتورة ضريبية رسمية (A4)",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                SetStatus($"فشل توليد الفاتورة A4: {ex.Message}", false);
            }
        }

        public void SuspendCurrentInvoice()
        {
            if (CartItems.Count == 0)
            {
                SetStatus("لا توجد فاتورة لتعليقها!", false);
                return;
            }
            SetStatus($"تم تعليق الفاتورة {InvoiceNumber} بنجاح (F8)", true);
            StartNewInvoice();
        }

        public void ToggleReturnMode()
        {
            CurrentInvoiceType = CurrentInvoiceType == InvoiceType.Sales ? InvoiceType.Return : InvoiceType.Sales;
            StartNewInvoice();
        }

        public void OpenDrawer()
        {
            try
            {
                var settings = _settingsRepository.GetSettings();
                if (Services.ThermalReceiptPrinter.OpenCashDrawer(settings, out string err))
                {
                    SetStatus("تم إرسال نبضة فتح درج النقدية (F10)", true);
                }
                else
                {
                    SetStatus($"تعذر فتح الدرج: {err}", false);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"تعذر فتح الدرج: {ex.Message}", false);
            }
        }

        private void SetStatus(string msg, bool success)
        {
            StatusMessage = msg;
            IsStatusSuccess = success;
        }

        private static void PlayWarningSound()
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch { }
        }

        private static void PlayConfirmSound()
        {
            try
            {
                SystemSounds.Beep.Play();
            }
            catch { }
        }
    }
}
