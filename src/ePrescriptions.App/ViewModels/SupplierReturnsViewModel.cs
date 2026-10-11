using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>تشغيلة منتهية أو قريبة الانتهاء يمكن إضافتها لمستند المرتجع/الإتلاف</summary>
    public class ExpiryCandidateRow
    {
        public Item Item { get; }
        public ItemBatch Batch { get; }

        public ExpiryCandidateRow(Item item, ItemBatch batch)
        {
            Item = item;
            Batch = batch;
        }

        public string ItemName => Item.NameAr;
        public string BatchNumber => Batch.BatchNumber;
        public string ExpiryText => Batch.ExpiryDate.ToString("yyyy-MM-dd");
        public int DaysLeft => (int)(Batch.ExpiryDate.Date - DateTime.Today).TotalDays;
        public bool IsExpired => DaysLeft < 0;
        public string StatusText => IsExpired ? $"منتهي منذ {-DaysLeft} يوم" : $"ينتهي خلال {DaysLeft} يوم";
        public string RemainingText => UnitBreakdownHelper.FormatStockDetailed(Batch.RemainingSmallestUnits, Item.StripsPerBox, Item.PiecesPerStrip);
        public string CostValueText => Money.FromPiastres(Batch.CostPricePerSmallestUnitPiastres * Batch.RemainingSmallestUnits).ToFormattedString();
    }

    /// <summary>بند في مستند المرتجع/الإتلاف: تشغيلة + كمية + نسبة خصم الشركة</summary>
    public class SupplierReturnLineRow : ObservableObject
    {
        public Item Item { get; }
        public ItemBatch Batch { get; }

        public string ItemName => Item.NameAr;
        public string BatchNumber => Batch.BatchNumber;
        public string ExpiryText => Batch.ExpiryDate.ToString("yyyy-MM-dd");
        public string AvailableText => UnitBreakdownHelper.FormatStockDetailed(Batch.RemainingSmallestUnits, Item.StripsPerBox, Item.PiecesPerStrip);

        private UnitLevel _unitLevel = UnitLevel.Box;
        public UnitLevel UnitLevel
        {
            get => _unitLevel;
            set { if (SetProperty(ref _unitLevel, value)) Recalculate(); }
        }

        private int _quantity = 1;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (value < 1) value = 1;
                if (SetProperty(ref _quantity, value)) Recalculate();
            }
        }

        private int _deductionPercent;
        public int DeductionPercent
        {
            get => _deductionPercent;
            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (SetProperty(ref _deductionPercent, value)) Recalculate();
            }
        }

        public int SmallestUnits => UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel, Quantity, Item.StripsPerBox, Item.PiecesPerStrip);
        public bool ExceedsAvailable => SmallestUnits > Batch.RemainingSmallestUnits;
        public long CostTotalPiastres => Batch.CostPricePerSmallestUnitPiastres * SmallestUnits;
        public long CreditPiastres => SupplierReturnRepository.CalculateCredit(CostTotalPiastres, DeductionPercent);
        public string CostText => Money.FromPiastres(CostTotalPiastres).ToFormattedString();
        public string CreditText => Money.FromPiastres(CreditPiastres).ToFormattedString();
        public string ErrorText => ExceedsAvailable ? "أكبر من المتاح بالتشغيلة" : string.Empty;

        public SupplierReturnLineRow(Item item, ItemBatch batch, int deductionPercent)
        {
            Item = item;
            Batch = batch;
            _deductionPercent = Math.Max(0, Math.Min(100, deductionPercent));

            // الافتراضي: كل المتاح بالتشغيلة، بالعلبة إن كان يقبل القسمة على عدد وحدات العلبة وإلا بأصغر وحدة
            int perBox = Math.Max(1, item.SmallestUnitsPerBox);
            if (batch.RemainingSmallestUnits >= perBox && batch.RemainingSmallestUnits % perBox == 0)
            {
                _unitLevel = UnitLevel.Box;
                _quantity = batch.RemainingSmallestUnits / perBox;
            }
            else
            {
                _unitLevel = UnitLevel.Piece;
                _quantity = Math.Max(1, batch.RemainingSmallestUnits);
            }
        }

        public void Recalculate()
        {
            OnPropertyChanged(nameof(SmallestUnits));
            OnPropertyChanged(nameof(ExceedsAvailable));
            OnPropertyChanged(nameof(CostTotalPiastres));
            OnPropertyChanged(nameof(CreditPiastres));
            OnPropertyChanged(nameof(CostText));
            OnPropertyChanged(nameof(CreditText));
            OnPropertyChanged(nameof(ErrorText));
        }
    }

    public class SupplierReturnHistoryRow
    {
        public SupplierReturnHistoryRow(SupplierReturn r)
        {
            ReturnNumber = r.ReturnNumber;
            DateText = r.Date.ToString("yyyy-MM-dd HH:mm");
            KindText = r.Kind == SupplierReturnKind.Destroy ? "إتلاف" : "مرتجع للشركة";
            ReasonText = r.Reason;
            PartyText = r.Kind == SupplierReturnKind.Destroy ? "-" : r.SupplierName;
            CostText = Money.FromPiastres(r.TotalCostPiastres).ToFormattedString();
            CreditText = Money.FromPiastres(r.CreditPiastres).ToFormattedString();
            LossText = Money.FromPiastres(r.LossPiastres).ToFormattedString();
            UserName = r.UserName;
        }

        public string ReturnNumber { get; }
        public string DateText { get; }
        public string KindText { get; }
        public string ReasonText { get; }
        public string PartyText { get; }
        public string CostText { get; }
        public string CreditText { get; }
        public string LossText { get; }
        public string UserName { get; }
    }

    /// <summary>
    /// شاشة مرتجع الأدوية المنتهية/القريبة من الانتهاء للشركة (بنسبة خصم) أو إتلافها.
    /// الحفظ يخصم الكمية من التشغيلة ومن رصيد الصنف، ويخفض رصيد المورد بقيمة الاسترداد.
    /// </summary>
    public class SupplierReturnsViewModel : ViewModelBase
    {
        private readonly ItemRepository _itemRepository;
        private readonly SupplierRepository _supplierRepository;
        private readonly SupplierReturnRepository _returnRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;

        public ObservableCollection<ExpiryCandidateRow> Candidates { get; } = new();
        public ObservableCollection<SupplierReturnLineRow> Lines { get; } = new();
        public ObservableCollection<Supplier> Suppliers { get; } = new();
        public ObservableCollection<SupplierReturnHistoryRow> History { get; } = new();

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetProperty(ref _selectedSupplier, value);
        }

        private int _withinDays = 90;
        /// <summary>عرض التشغيلات التي تنتهي خلال هذا العدد من الأيام (والمنتهية بالفعل)</summary>
        public int WithinDays
        {
            get => _withinDays;
            set => SetProperty(ref _withinDays, Math.Max(0, value));
        }

        private bool _isDestroy;
        /// <summary>إتلاف بدون مورد ولا استرداد (الخسارة = كامل التكلفة)</summary>
        public bool IsDestroy
        {
            get => _isDestroy;
            set
            {
                if (SetProperty(ref _isDestroy, value))
                {
                    OnPropertyChanged(nameof(IsReturnToSupplier));
                    foreach (var l in Lines) l.DeductionPercent = value ? 100 : DeductionPercent;
                    RecalculateTotals();
                }
            }
        }

        public bool IsReturnToSupplier => !IsDestroy;

        private int _deductionPercent = 10;
        /// <summary>نسبة الخصم التي تخصمها الشركة من قيمة المرتجع (تُطبق على كل البنود ويمكن تعديلها لكل بند)</summary>
        public int DeductionPercent
        {
            get => _deductionPercent;
            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (SetProperty(ref _deductionPercent, value))
                {
                    foreach (var l in Lines) l.DeductionPercent = value;
                    RecalculateTotals();
                }
            }
        }

        public string[] Reasons { get; } = { "انتهاء صلاحية", "قرب انتهاء الصلاحية", "تلف", "أخرى" };

        private string _reason = "انتهاء صلاحية";
        /// <summary>سبب المرتجع/الشطب (يُسجَّل في المستند)</summary>
        public string Reason
        {
            get => _reason;
            set => SetProperty(ref _reason, value);
        }

        private string _notes = string.Empty;
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        private string _statusMessage = "اختار التشغيلات المنتهية أو القريبة من الانتهاء وأضفها للمستند.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private long _totalCostPiastres;
        private long _creditPiastres;
        public string TotalCostText => Money.FromPiastres(_totalCostPiastres).ToFormattedString();
        public string CreditText => Money.FromPiastres(IsDestroy ? 0 : _creditPiastres).ToFormattedString();
        public string LossText => Money.FromPiastres(IsDestroy ? _totalCostPiastres : _totalCostPiastres - _creditPiastres).ToFormattedString();

        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand<ExpiryCandidateRow> AddToReturnCommand { get; }
        public IRelayCommand AddAllExpiredCommand { get; }
        public IRelayCommand<SupplierReturnLineRow> RemoveLineCommand { get; }
        public IRelayCommand SaveCommand { get; }

        public SupplierReturnsViewModel(
            ItemRepository itemRepository,
            SupplierRepository supplierRepository,
            SupplierReturnRepository returnRepository,
            Services.IUserSessionService? sessionService = null,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null)
        {
            Title = "مرتجع / إتلاف المنتهي";
            _itemRepository = itemRepository;
            _supplierRepository = supplierRepository;
            _returnRepository = returnRepository;
            _sessionService = sessionService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;

            RefreshCommand = new RelayCommand(LoadData);
            AddToReturnCommand = new RelayCommand<ExpiryCandidateRow>(AddToReturn);
            AddAllExpiredCommand = new RelayCommand(AddAllExpired);
            RemoveLineCommand = new RelayCommand<SupplierReturnLineRow>(RemoveLine);
            SaveCommand = new RelayCommand(Save);

            LoadData();
        }

        public override void OnNavigatedTo() => LoadData();

        public void LoadData()
        {
            try
            {
                Suppliers.Clear();
                foreach (var s in _supplierRepository.GetAll()) Suppliers.Add(s);
                if (SelectedSupplier != null)
                    SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == SelectedSupplier.Id);

                LoadCandidates();

                History.Clear();
                foreach (var r in _returnRepository.GetRecent(30)) History.Add(new SupplierReturnHistoryRow(r));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SupplierReturnsViewModel.LoadData");
                StatusMessage = $"تعذر تحميل البيانات: {ex.Message}";
            }
        }

        private void LoadCandidates()
        {
            Candidates.Clear();
            var inDocument = Lines.Select(l => l.Batch.Id).ToHashSet();
            foreach (var (item, batch) in _itemRepository.GetNearExpiryBatches(WithinDays))
            {
                if (inDocument.Contains(batch.Id)) continue;
                Candidates.Add(new ExpiryCandidateRow(item, batch));
            }
        }

        public void AddToReturn(ExpiryCandidateRow? row)
        {
            if (row == null) return;
            if (Lines.Any(l => l.Batch.Id == row.Batch.Id)) return;

            var line = new SupplierReturnLineRow(row.Item, row.Batch, IsDestroy ? 100 : DeductionPercent);
            line.PropertyChanged += (s, e) => RecalculateTotals();
            Lines.Add(line);
            Candidates.Remove(row);
            RecalculateTotals();
            StatusMessage = $"تمت إضافة {row.ItemName} (تشغيلة {row.BatchNumber}) للمستند.";
        }

        public void AddAllExpired()
        {
            foreach (var row in Candidates.Where(c => c.IsExpired).ToList())
                AddToReturn(row);
        }

        public void RemoveLine(SupplierReturnLineRow? line)
        {
            if (line == null || !Lines.Contains(line)) return;
            Lines.Remove(line);
            LoadCandidates();
            RecalculateTotals();
        }

        private void RecalculateTotals()
        {
            _totalCostPiastres = Lines.Sum(l => l.CostTotalPiastres);
            _creditPiastres = Lines.Sum(l => l.CreditPiastres);
            OnPropertyChanged(nameof(TotalCostText));
            OnPropertyChanged(nameof(CreditText));
            OnPropertyChanged(nameof(LossText));
        }

        public void Save()
        {
            if (Lines.Count == 0)
            {
                StatusMessage = "أضف تشغيلة واحدة على الأقل للمستند.";
                return;
            }

            if (!IsDestroy && SelectedSupplier == null)
            {
                StatusMessage = "اختار الشركة (المورد) اللي هترجعلها الأدوية.";
                return;
            }

            var tooMuch = Lines.FirstOrDefault(l => l.ExceedsAvailable);
            if (tooMuch != null)
            {
                StatusMessage = $"الكمية المطلوبة من {tooMuch.ItemName} (تشغيلة {tooMuch.BatchNumber}) أكبر من المتاح بالتشغيلة.";
                return;
            }

            int totalUnits = Lines.Sum(l => l.SmallestUnits);
            string what = IsDestroy ? "إتلاف" : $"مرتجع للشركة {SelectedSupplier!.Name} بخصم {DeductionPercent}%";
            var answer = MessageBox.Show(
                $"تأكيد {what}\nعدد البنود: {Lines.Count}\nالتكلفة: {TotalCostText}\nالاسترداد: {CreditText}\nالخسارة: {LossText}\n\nالكميات هتتخصم من المخزون ومش هينفع تتراجع. تكمّل؟",
                "تأكيد المستند",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer != MessageBoxResult.Yes) return;

            var user = _sessionService?.CurrentUser;
            var doc = new SupplierReturn
            {
                Kind = IsDestroy ? SupplierReturnKind.Destroy : SupplierReturnKind.ReturnToSupplier,
                SupplierId = IsDestroy ? null : SelectedSupplier?.Id,
                SupplierName = IsDestroy ? string.Empty : SelectedSupplier?.Name ?? string.Empty,
                Reason = Reason ?? string.Empty,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                UserId = user?.Id ?? 0,
                UserName = user?.FullName ?? string.Empty
            };

            foreach (var l in Lines)
            {
                doc.Items.Add(new SupplierReturnItem
                {
                    BatchId = l.Batch.Id,
                    SmallestUnitsCount = l.SmallestUnits,
                    DeductionPercent = IsDestroy ? 100 : l.DeductionPercent
                });
            }

            if (!_returnRepository.Save(doc, out string err))
            {
                StatusMessage = $"فشل حفظ المستند: {err}";
                return;
            }

            LogAudit(doc, totalUnits);

            StatusMessage = IsDestroy
                ? $"تم إتلاف الأصناف وحفظ المستند {doc.ReturnNumber}. الخسارة: {Money.FromPiastres(doc.LossPiastres).ToFormattedString()}"
                : $"تم حفظ مستند المرتجع {doc.ReturnNumber}. الاسترداد: {Money.FromPiastres(doc.CreditPiastres).ToFormattedString()} (اتخصم من رصيد المورد)، الخسارة: {Money.FromPiastres(doc.LossPiastres).ToFormattedString()}";

            Lines.Clear();
            Notes = string.Empty;
            RecalculateTotals();
            LoadData();
        }

        private void LogAudit(SupplierReturn doc, int totalUnits)
        {
            try
            {
                var user = _sessionService?.CurrentUser;
                if (user == null) return;

                string field = doc.Kind == SupplierReturnKind.Destroy ? "إتلاف أدوية منتهية" : "مرتجع أدوية للشركة";
                string newVal = $"{doc.Items.Count} بند / {totalUnits} وحدة / تكلفة {doc.TotalCostPiastres} / استرداد {doc.CreditPiastres}";

                if (user.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, "SupplierReturn", doc.Id.ToString(), doc.ReturnNumber, field, "-", newVal);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "SupplierReturn", doc.Id.ToString(), doc.ReturnNumber, field, "-", newVal);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SupplierReturnsViewModel.LogAudit");
            }
        }
    }
}
