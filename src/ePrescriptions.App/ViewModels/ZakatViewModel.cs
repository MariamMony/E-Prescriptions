using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    public class ZakatViewModel : ViewModelBase
    {
        private readonly FinanceRepository _financeRepository;

        // خيارات الحساب
        private bool _evaluateAtCost = true;
        public bool EvaluateAtCost
        {
            get => _evaluateAtCost;
            set
            {
                if (SetProperty(ref _evaluateAtCost, value))
                {
                    RecalculateZakat();
                }
            }
        }

        private bool _isHijriYear = true;
        public bool IsHijriYear
        {
            get => _isHijriYear;
            set
            {
                if (SetProperty(ref _isHijriYear, value))
                {
                    RecalculateZakat();
                }
            }
        }

        // سعر جرام الذهب عيار 24 بالجنيه المصري (افتراضي 3800.00 ج.م)
        private decimal _goldGramPricePounds = 3800m;
        public decimal GoldGramPricePounds
        {
            get => _goldGramPricePounds;
            set
            {
                if (SetProperty(ref _goldGramPricePounds, value))
                {
                    RecalculateZakat();
                }
            }
        }

        // نتائج الحساب الشرعي
        private ZakatCalculation _zakatResult = new();
        public ZakatCalculation ZakatResult
        {
            get => _zakatResult;
            set => SetProperty(ref _zakatResult, value);
        }

        public string FormattedStockValue => Money.FromPiastres(ZakatResult.StockValuePiastres).ToFormattedString();
        public string FormattedCashAndBank => Money.FromPiastres(ZakatResult.CashAndBankPiastres).ToFormattedString();
        public string FormattedReceivables => Money.FromPiastres(ZakatResult.GoodReceivablesPiastres).ToFormattedString();
        public string FormattedPayables => Money.FromPiastres(ZakatResult.DuePayablesPiastres).ToFormattedString();
        public string FormattedZakatBase => Money.FromPiastres(ZakatResult.ZakatBasePiastres).ToFormattedString();
        public string FormattedNisab => Money.FromPiastres(ZakatResult.NisabPiastres).ToFormattedString();
        public string FormattedZakatDue => Money.FromPiastres(ZakatResult.ZakatAmountPiastres).ToFormattedString();

        public bool IsZakatDue => ZakatResult.IsZakatDue;
        public string ZakatStatusText => IsZakatDue 
            ? "بلغ وعاء الزكاة النصاب الشرعي وتجب الزكاة." 
            : "لم يبلغ وعاء الزكاة النصاب الشرعي (أقل من قيمة 85 جم ذهب).";

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public IRelayCommand RecalculateCommand { get; }

        public ZakatViewModel(FinanceRepository financeRepository)
        {
            Title = "حاسبة الزكاة الشرعية للصيدلية";
            _financeRepository = financeRepository;

            RecalculateCommand = new RelayCommand(RecalculateZakat);

            RecalculateZakat();
        }

        public void RecalculateZakat()
        {
            long goldGramPiastres = Money.FromPounds(GoldGramPricePounds).Piastres;
            ZakatResult = _financeRepository.CalculateZakat(EvaluateAtCost, goldGramPiastres, IsHijriYear);

            OnPropertyChanged(nameof(FormattedStockValue));
            OnPropertyChanged(nameof(FormattedCashAndBank));
            OnPropertyChanged(nameof(FormattedReceivables));
            OnPropertyChanged(nameof(FormattedPayables));
            OnPropertyChanged(nameof(FormattedZakatBase));
            OnPropertyChanged(nameof(FormattedNisab));
            OnPropertyChanged(nameof(FormattedZakatDue));
            OnPropertyChanged(nameof(IsZakatDue));
            OnPropertyChanged(nameof(ZakatStatusText));

            StatusMessage = "تم احتساب وعاء ونصاب الزكاة الشرعية وفق أحدث أرصدة الصيدلية.";
        }
    }
}
