using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class PurchaseItemRow : ObservableObject
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public int StripsPerBox { get; set; } = 1;
        public int PiecesPerStrip { get; set; } = 10;

        private string _batchNumber = string.Empty;
        public string BatchNumber { get => _batchNumber; set => SetProperty(ref _batchNumber, value); }

        private DateTime _expiryDate = DateTime.Today.AddYears(2);
        public DateTime ExpiryDate { get => _expiryDate; set => SetProperty(ref _expiryDate, value); }

        private UnitLevel _unitLevel = UnitLevel.Box;
        public UnitLevel UnitLevel
        {
            get => _unitLevel;
            set
            {
                if (SetProperty(ref _unitLevel, value))
                {
                    Recalculate();
                }
            }
        }

        private int _quantityPurchased = 1;
        public int QuantityPurchased
        {
            get => _quantityPurchased;
            set
            {
                if (value < 1) value = 1;
                if (SetProperty(ref _quantityPurchased, value))
                {
                    Recalculate();
                }
            }
        }

        private decimal _unitCostRiyals;
        public decimal UnitCostRiyals
        {
            get => _unitCostRiyals;
            set
            {
                if (SetProperty(ref _unitCostRiyals, value))
                {
                    Recalculate();
                }
            }
        }

        public long UnitCostPiastres => Money.FromPounds(UnitCostRiyals).Piastres;

        private long _vatPiastres;
        public long VatPiastres { get => _vatPiastres; private set => SetProperty(ref _vatPiastres, value); }

        private long _totalPiastres;
        public long TotalPiastres { get => _totalPiastres; private set => SetProperty(ref _totalPiastres, value); }

        public int SmallestUnitsCount => UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel, QuantityPurchased, StripsPerBox, PiecesPerStrip);

        public string FormattedTotal => Money.FromPiastres(TotalPiastres).ToFormattedString();
        public string FormattedVat => Money.FromPiastres(VatPiastres).ToFormattedString();

        public PurchaseItemRow(Item item)
        {
            ItemId = item.Id;
            ItemCode = item.Code;
            ItemNameAr = item.NameAr;
            StripsPerBox = item.StripsPerBox;
            PiecesPerStrip = item.PiecesPerStrip;
            BatchNumber = $"BN-{DateTime.Now:yyyyMMdd}";
            ExpiryDate = DateTime.Today.AddYears(2);
            UnitCostRiyals = Money.FromPiastres(item.CostPriceBoxPiastres).ToPounds();
            Recalculate();
        }

        public void Recalculate()
        {
            long taxable = UnitCostPiastres * QuantityPurchased;
            VatPiastres = Money.CalculateVat(taxable, 15).Piastres;
            TotalPiastres = taxable + VatPiastres;
            OnPropertyChanged(nameof(SmallestUnitsCount));
            OnPropertyChanged(nameof(FormattedTotal));
            OnPropertyChanged(nameof(FormattedVat));
        }
    }

    public class PurchasesViewModel : ViewModelBase
    {
        private readonly PurchaseRepository _purchaseRepository;
        private readonly SupplierRepository _supplierRepository;
        private readonly ItemRepository _itemRepository;
        private readonly SettingsRepository? _settingsRepository;
        private readonly Services.IPdfReportService? _pdfReportService;

        // فاتورة الشراء الجديدة
        private string _invoiceNumber = string.Empty;
        public string InvoiceNumber { get => _invoiceNumber; set => SetProperty(ref _invoiceNumber, value); }

        private DateTime _invoiceDate = DateTime.Today;
        public DateTime InvoiceDate { get => _invoiceDate; set => SetProperty(ref _invoiceDate, value); }

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetProperty(ref _selectedSupplier, value);
        }

        private string _invoiceNotes = string.Empty;
        public string InvoiceNotes { get => _invoiceNotes; set => SetProperty(ref _invoiceNotes, value); }

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

        public ObservableCollection<PurchaseItemRow> CartItems { get; } = new();
        public ObservableCollection<Supplier> SuppliersList { get; } = new();
        public ObservableCollection<PurchaseInvoice> InvoicesHistory { get; } = new();

        // بيانات إضافة مورد جديد
        private string _newSupplierName = string.Empty;
        public string NewSupplierName { get => _newSupplierName; set => SetProperty(ref _newSupplierName, value); }

        private string _newSupplierPhone = string.Empty;
        public string NewSupplierPhone { get => _newSupplierPhone; set => SetProperty(ref _newSupplierPhone, value); }

        private string _newSupplierTaxNo = string.Empty;
        public string NewSupplierTaxNo { get => _newSupplierTaxNo; set => SetProperty(ref _newSupplierTaxNo, value); }

        // سند صرف مورد
        private decimal _voucherAmountRiyals;
        public decimal VoucherAmountRiyals { get => _voucherAmountRiyals; set => SetProperty(ref _voucherAmountRiyals, value); }

        private string _voucherNotes = "سداد دفعة للمورد";
        public string VoucherNotes { get => _voucherNotes; set => SetProperty(ref _voucherNotes, value); }

        // مجاميع الفاتورة
        private long _subtotalPiastres;
        public long SubtotalPiastres { get => _subtotalPiastres; set => SetProperty(ref _subtotalPiastres, value); }

        private long _vatPiastres;
        public long VatPiastres { get => _vatPiastres; set => SetProperty(ref _vatPiastres, value); }

        private long _totalPiastres;
        public long TotalPiastres { get => _totalPiastres; set => SetProperty(ref _totalPiastres, value); }

        private long _remainingPiastres;
        public long RemainingPiastres { get => _remainingPiastres; set => SetProperty(ref _remainingPiastres, value); }

        public decimal SubtotalRiyals => Money.FromPiastres(SubtotalPiastres).ToPounds();
        public decimal VatRiyals => Money.FromPiastres(VatPiastres).ToPounds();
        public decimal TotalRiyals => Money.FromPiastres(TotalPiastres).ToPounds();
        public decimal RemainingRiyals => Money.FromPiastres(RemainingPiastres).ToPounds();

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        // الأوامر
        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand NewInvoiceCommand { get; }
        public IRelayCommand SaveInvoiceCommand { get; }
        public IRelayCommand<PurchaseItemRow> RemoveCartItemCommand { get; }
        public IRelayCommand AddSupplierCommand { get; }
        public IRelayCommand PaySupplierVoucherCommand { get; }

        private readonly Data.Repositories.OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly Data.Repositories.StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly Services.IBarcodeLabelService? _barcodeLabelService;

        public PurchasesViewModel(
            PurchaseRepository purchaseRepository,
            SupplierRepository supplierRepository,
            ItemRepository itemRepository,
            SettingsRepository? settingsRepository = null,
            Services.IPdfReportService? pdfReportService = null,
            Data.Repositories.OwnerAuditLogRepository? ownerAuditLogRepository = null,
            Data.Repositories.StaffAuditLogRepository? staffAuditLogRepository = null,
            Services.IUserSessionService? sessionService = null,
            Services.IBarcodeLabelService? barcodeLabelService = null)
        {
            Title = "المشتريات والموردون";
            _barcodeLabelService = barcodeLabelService;
            _purchaseRepository = purchaseRepository;
            _supplierRepository = supplierRepository;
            _itemRepository = itemRepository;
            _settingsRepository = settingsRepository;
            _pdfReportService = pdfReportService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;

            RefreshCommand = new RelayCommand(LoadData);
            NewInvoiceCommand = new RelayCommand(StartNewInvoice);
            SaveInvoiceCommand = new RelayCommand(SaveInvoice);
            RemoveCartItemCommand = new RelayCommand<PurchaseItemRow>(row =>
            {
                if (row != null && CartItems.Contains(row))
                {
                    CartItems.Remove(row);
                    RecalculateTotals();
                }
            });
            AddSupplierCommand = new RelayCommand(AddSupplier);
            PaySupplierVoucherCommand = new RelayCommand(PaySupplierVoucher);

            LoadData();
            StartNewInvoice();
        }

        public void LoadData()
        {
            SuppliersList.Clear();
            var suppliers = _supplierRepository.GetAll();
            foreach (var s in suppliers) SuppliersList.Add(s);

            InvoicesHistory.Clear();
            var history = _purchaseRepository.GetAllPurchases(50);
            foreach (var inv in history) InvoicesHistory.Add(inv);

            StatusMessage = "تم تحديث بيانات المشتريات والموردين.";
        }

        public void StartNewInvoice()
        {
            InvoiceNumber = _purchaseRepository.GenerateNextPurchaseInvoiceNumber();
            InvoiceDate = DateTime.Today;
            CartItems.Clear();
            PaidRiyals = 0;
            InvoiceNotes = string.Empty;

            // إضافة عينة صنفين للبدء
            var items = _itemRepository.GetAllItems();
            if (items.Count > 0)
            {
                AddItemToCart(items[0]);
            }
            RecalculateTotals();
        }

        public void AddItemToCart(Item item)
        {
            var row = new PurchaseItemRow(item);
            row.PropertyChanged += (s, e) => RecalculateTotals();
            CartItems.Add(row);
            RecalculateTotals();
        }

        public void RecalculateTotals()
        {
            long sub = 0;
            long vat = 0;
            long tot = 0;

            foreach (var row in CartItems)
            {
                sub += row.UnitCostPiastres * row.QuantityPurchased;
                vat += row.VatPiastres;
                tot += row.TotalPiastres;
            }

            SubtotalPiastres = sub;
            VatPiastres = vat;
            TotalPiastres = tot;

            long paidPiastres = Money.FromPounds(PaidRiyals).Piastres;
            RemainingPiastres = Math.Max(0, TotalPiastres - paidPiastres);

            OnPropertyChanged(nameof(SubtotalRiyals));
            OnPropertyChanged(nameof(VatRiyals));
            OnPropertyChanged(nameof(TotalRiyals));
            OnPropertyChanged(nameof(RemainingRiyals));
        }

        public void SaveInvoice()
        {
            if (SelectedSupplier == null)
            {
                StatusMessage = "يرجى تحديد المورد أولاً قبل حفظ الفاتورة!";
                return;
            }

            if (CartItems.Count == 0)
            {
                StatusMessage = "لا يمكن حفظ فاتورة مشتريات خالية من الأصناف!";
                return;
            }

            var inv = new PurchaseInvoice
            {
                InvoiceNumber = InvoiceNumber,
                SupplierId = SelectedSupplier.Id,
                SupplierName = SelectedSupplier.Name,
                InvoiceDate = InvoiceDate,
                SubtotalPiastres = SubtotalPiastres,
                VatPiastres = VatPiastres,
                TotalPiastres = TotalPiastres,
                PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                RemainingPiastres = RemainingPiastres,
                Notes = InvoiceNotes
            };

            foreach (var row in CartItems)
            {
                inv.Items.Add(new PurchaseInvoiceItem
                {
                    ItemId = row.ItemId,
                    ItemCode = row.ItemCode,
                    ItemNameAr = row.ItemNameAr,
                    BatchNumber = string.IsNullOrWhiteSpace(row.BatchNumber) ? $"BN-{DateTime.Now:yyyyMMdd}" : row.BatchNumber,
                    ExpiryDate = row.ExpiryDate,
                    UnitLevel = row.UnitLevel,
                    QuantityPurchased = row.QuantityPurchased,
                    SmallestUnitsCount = row.SmallestUnitsCount,
                    UnitCostPiastres = row.UnitCostPiastres,
                    VatPiastres = row.VatPiastres,
                    TotalPiastres = row.TotalPiastres
                });
            }

            if (_purchaseRepository.SavePurchaseInvoice(inv, out string err))
            {
                string saved = $"تم حفظ فاتورة الشراء '{inv.InvoiceNumber}' بنجاح وزيادة أرصدة المخزون.";
                LoadData();
                StartNewInvoice();
                StatusMessage = saved;

                // ملصقات الصلاحية: ملصق لكل علبة بباركود يعرّف التشغيلة
                try
                {
                    var labelLines = new System.Collections.Generic.List<(Item Item, ItemBatch Batch, int Units)>();
                    foreach (var line in inv.Items)
                    {
                        var itm = _itemRepository.GetById(line.ItemId);
                        var batch = line.BatchId > 0 ? _itemRepository.GetBatchById(line.BatchId) : null;
                        if (itm != null && batch != null)
                            labelLines.Add((itm, batch, line.SmallestUnitsCount));
                    }
                    string? printMsg = Services.BatchLabelPrintHelper.OfferAndPrint(_barcodeLabelService, labelLines);
                    if (printMsg != null) StatusMessage = $"{saved} {printMsg}";
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "PurchasesViewModel.PrintBatchLabels");
                }
            }
            else
            {
                StatusMessage = $"فشل حفظ فاتورة المشتريات: {err}";
            }
        }

        public void AddSupplier()
        {
            if (string.IsNullOrWhiteSpace(NewSupplierName))
            {
                StatusMessage = "يرجى كتابة اسم المورد!";
                return;
            }

            var s = new Supplier
            {
                Name = NewSupplierName,
                Phone = NewSupplierPhone,
                TaxNumber = NewSupplierTaxNo,
                BalancePiastres = 0,
                IsActive = true
            };

            _supplierRepository.SaveSupplier(s);
            StatusMessage = $"تمت إضافة المورد '{s.Name}' بنجاح.";
            NewSupplierName = string.Empty;
            NewSupplierPhone = string.Empty;
            NewSupplierTaxNo = string.Empty;
            LoadData();
        }

        public void PaySupplierVoucher()
        {
            if (SelectedSupplier == null)
            {
                StatusMessage = "يرجى اختيار مورد لإصدار سند الصرف له!";
                return;
            }

            if (VoucherAmountRiyals <= 0)
            {
                StatusMessage = "يرجى إدخال مبلغ صحيح لسند الصرف!";
                return;
            }

            long amtPiastres = Money.FromPounds(VoucherAmountRiyals).Piastres;
            string vNum = _purchaseRepository.GenerateNextSupplierVoucherNumber();

            var voucher = new SupplierVoucher
            {
                VoucherNumber = vNum,
                SupplierId = SelectedSupplier.Id,
                SupplierName = SelectedSupplier.Name,
                Date = DateTime.Now,
                AmountPiastres = amtPiastres,
                PaymentMethod = PaymentMethod.Cash,
                Notes = VoucherNotes
            };

            if (_purchaseRepository.AddSupplierVoucher(voucher))
            {
                LogSupplierVoucherAudit(SelectedSupplier, amtPiastres, vNum);
                StatusMessage = $"تم تسجيل سند الصرف رقم {vNum} بمبلغ {VoucherAmountRiyals:N2} ج.م وخصمه من حساب المورد.";

                if (_pdfReportService != null && _settingsRepository != null)
                {
                    try
                    {
                        var settings = _settingsRepository.GetSettings();
                        string pdfPath = _pdfReportService.GenerateVoucherPdf(vNum, DateTime.Now, SelectedSupplier.Name, amtPiastres, "سند صرف لمورد", VoucherNotes, settings);
                        StatusMessage += $" (تم إنشاء السند PDF: {System.IO.Path.GetFileName(pdfPath)})";
                    }
                    catch { }
                }

                VoucherAmountRiyals = 0;
                LoadData();
            }
            else
            {
                StatusMessage = "فشل تسجيل سند الصرف.";
            }
        }
        /// <summary>
        /// تسجيل سند صرف لمورد (تسوية رصيد مورد): لصاحب الصيدلية في سجله الخاص المخفي عن الجميع،
        /// ولأي دور آخر (مدير/صيدلي/كاشير) في سجل الموظفين العام المرئي لـ Admin وOwner فقط.
        /// </summary>
        private void LogSupplierVoucherAudit(Supplier? supplier, long amountPiastres, string voucherNumber)
        {
            var user = _sessionService?.CurrentUser;
            if (user == null || supplier == null) return;

            string field = $"سند صرف رقم {voucherNumber} (تسوية رصيد مورد)";
            string oldVal = supplier.BalancePiastres.ToString();
            string newVal = (supplier.BalancePiastres - amountPiastres).ToString();

            if (user.Role == UserRole.Owner)
            {
                _ownerAuditLogRepository?.LogChange(user.Id, "Supplier", supplier.Id.ToString(), supplier.Name, field, oldVal, newVal);
            }
            else
            {
                _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "Supplier", supplier.Id.ToString(), supplier.Name, field, oldVal, newVal);
            }
        }
    }
}