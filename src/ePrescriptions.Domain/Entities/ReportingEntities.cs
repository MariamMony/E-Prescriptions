using System;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Entities
{
    /// <summary>
    /// بند تقرير المبيعات التفصيلي
    /// </summary>
    public class SalesReportRow
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public PaymentMethod PaymentMethod { get; set; }
        public string PaymentMethodName => PaymentMethod switch
        {
            PaymentMethod.Cash => "نقدي",
            PaymentMethod.Network => "شبكة / فيزا",
            PaymentMethod.OnAccount => "آجل",
            PaymentMethod.Insurance => "تأمين",
            _ => "نقدي"
        };
        public long SubtotalPiastres { get; set; }
        public long DiscountPiastres { get; set; }
        public long VatPiastres { get; set; }
        public long TotalPiastres { get; set; }
        public long EstimatedCostPiastres { get; set; }
        public long ProfitPiastres => TotalPiastres - EstimatedCostPiastres;

        public decimal SubtotalPounds => SubtotalPiastres / 100m;
        public decimal TotalPounds => TotalPiastres / 100m;
        public decimal VatPounds => VatPiastres / 100m;
        public decimal ProfitPounds => ProfitPiastres / 100m;
    }

    /// <summary>
    /// بند تقرير حركة ومبيعات الأصناف (الأكثر مبيعاً وركوداً)
    /// </summary>
    public class ItemMovementReportRow
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public int TotalSoldSmallestUnits { get; set; }
        public int CurrentStockSmallestUnits { get; set; }

        public long TotalRevenuePiastres { get; set; }
        public long TotalCostPiastres { get; set; }
        public long TotalProfitPiastres => TotalRevenuePiastres - TotalCostPiastres;

        public decimal TotalRevenuePounds => TotalRevenuePiastres / 100m;
        public decimal TotalProfitPounds => TotalProfitPiastres / 100m;
    }

    /// <summary>
    /// بند تقرير النواقص وحد إعادة الطلب
    /// </summary>
    public class LowStockReportRow
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public int CurrentStockSmallestUnits { get; set; }
        public int MinStockAlertSmallestUnits { get; set; }
        public int DeficitUnits => Math.Max(0, MinStockAlertSmallestUnits - CurrentStockSmallestUnits);
        public int SuggestedOrderUnits => DeficitUnits + (MinStockAlertSmallestUnits * 2);

        public long RetailPriceBoxPiastres { get; set; }

        public decimal RetailPriceBoxPounds => RetailPriceBoxPiastres / 100m;
    }

    /// <summary>
    /// بند تقرير الأدوية قريبة الصلاحية والمنتهية
    /// </summary>
    public class ExpiryReportRow
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int RemainingSmallestUnits { get; set; }

        public long CostPricePerSmallestUnitPiastres { get; set; }
        public long TotalCostPiastres => RemainingSmallestUnits * CostPricePerSmallestUnitPiastres;

        public int DaysRemaining => (int)(ExpiryDate.Date - DateTime.Today).TotalDays;
        public bool IsExpired => DaysRemaining <= 0;
        public string StatusDisplay => IsExpired ? "منتهي الصلاحية" : $"متبقي {DaysRemaining} يوم";
        public decimal TotalCostPounds => TotalCostPiastres / 100m;
    }

    /// <summary>
    /// ملخص تقييم المخزون المالي
    /// </summary>
    public class InventoryValuationSummary
    {
        public int TotalItemsCount { get; set; }
        public int TotalBatchesCount { get; set; }
        public int TotalSmallestUnitsCount { get; set; }

        public long TotalValuationCostPiastres { get; set; }
        public long TotalValuationRetailPiastres { get; set; }
        public long ExpectedProfitPiastres => TotalValuationRetailPiastres - TotalValuationCostPiastres;

        public decimal TotalValuationCostPounds => TotalValuationCostPiastres / 100m;
        public decimal TotalValuationRetailPounds => TotalValuationRetailPiastres / 100m;
        public decimal ExpectedProfitPounds => ExpectedProfitPiastres / 100m;
    }

    /// <summary>
    /// ملخص المبيعات المالي للفترة
    /// </summary>
    public class SalesPeriodSummary
    {
        public int TotalInvoicesCount { get; set; }

        public long TotalSalesPiastres { get; set; }
        public long TotalCostPiastres { get; set; }
        public long TotalVatPiastres { get; set; }
        public long NetProfitPiastres => TotalSalesPiastres - TotalCostPiastres;

        public double ProfitMarginPercent => TotalSalesPiastres > 0 ? (double)NetProfitPiastres / TotalSalesPiastres * 100 : 0;

        public long CashSalesPiastres { get; set; }
        public long NetworkSalesPiastres { get; set; }
        public long OnAccountSalesPiastres { get; set; }
        public long InsuranceSalesPiastres { get; set; }

        public decimal TotalSalesPounds => TotalSalesPiastres / 100m;
        public decimal TotalCostPounds => TotalCostPiastres / 100m;
        public decimal TotalVatPounds => TotalVatPiastres / 100m;
        public decimal NetProfitPounds => NetProfitPiastres / 100m;
        public decimal CashSalesPounds => CashSalesPiastres / 100m;
        public decimal NetworkSalesPounds => NetworkSalesPiastres / 100m;
        public decimal OnAccountSalesPounds => OnAccountSalesPiastres / 100m;
        public decimal InsuranceSalesPounds => InsuranceSalesPiastres / 100m;
    }

    /// <summary>
    /// سطر في كشف حساب (عميل / مورد): مدين ودائن ورصيد تراكمي بالقروش
    /// </summary>
    public class StatementRow
    {
        public DateTime Date { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long DebitPiastres { get; set; }
        public long CreditPiastres { get; set; }
        public long RunningBalancePiastres { get; set; }

        public decimal DebitPounds => DebitPiastres / 100m;
        public decimal CreditPounds => CreditPiastres / 100m;
        public decimal RunningBalancePounds => RunningBalancePiastres / 100m;
    }
}
