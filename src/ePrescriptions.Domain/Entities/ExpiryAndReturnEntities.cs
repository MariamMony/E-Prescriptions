using System;
using System.Collections.Generic;

namespace ePrescriptions.Domain.Entities
{
    /// <summary>
    /// كمية خُصمت من تشغيلة معينة لبند فاتورة. BatchId = null تعني كمية بيعت بالسالب (لا توجد تشغيلة تغطيها).
    /// </summary>
    public class BatchAllocation
    {
        public int? BatchId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime? ExpiryDate { get; set; }
        public int Units { get; set; }
    }

    /// <summary>
    /// بند من فاتورة بيع أصلية مع الكمية التي لا تزال قابلة للإرجاع منه
    /// </summary>
    public class ReturnableLine
    {
        public InvoiceItem Line { get; set; } = new();
        public int ReturnedQuantity { get; set; }
        public int RemainingQuantity => Math.Max(0, Line.QuantitySold - ReturnedQuantity);
    }

    public class ReturnableInvoice
    {
        public Invoice Invoice { get; set; } = new();
        public List<ReturnableLine> Lines { get; set; } = new();
    }

    public enum SupplierReturnKind
    {
        ReturnToSupplier = 1,   // مرتجع للشركة بخصم نسبة
        Destroy = 2             // إتلاف (لا استرداد)
    }

    public class SupplierReturn
    {
        public int Id { get; set; }
        public string ReturnNumber { get; set; } = string.Empty;
        public SupplierReturnKind Kind { get; set; } = SupplierReturnKind.ReturnToSupplier;
        public int? SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;

        public long TotalCostPiastres { get; set; }
        public long CreditPiastres { get; set; }
        public long LossPiastres { get; set; }

        /// <summary>سبب المرتجع/الشطب: انتهاء صلاحية، قرب انتهاء، تلف، ...</summary>
        public string Reason { get; set; } = string.Empty;

        public string? Notes { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;

        public List<SupplierReturnItem> Items { get; set; } = new();
    }

    public class SupplierReturnItem
    {
        public int Id { get; set; }
        public int ReturnId { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public int BatchId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int SmallestUnitsCount { get; set; }
        public long CostPerUnitPiastres { get; set; }
        public int DeductionPercent { get; set; }
        public long CostTotalPiastres { get; set; }
        public long CreditPiastres { get; set; }
    }
}
