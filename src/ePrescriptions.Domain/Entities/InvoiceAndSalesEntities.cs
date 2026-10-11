using System;
using System.Collections.Generic;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Entities
{
    public class Invoice
    {
        public int Id { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public InvoiceType Type { get; set; } = InvoiceType.Sales;
        public InvoiceStatus Status { get; set; } = InvoiceStatus.Active;
        public DateTime Date { get; set; } = DateTime.Now;

        // العميل
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; } = "عميل نقدي";
        public string CustomerPhone { get; set; } = string.Empty;
        public PriceCategory PriceCategory { get; set; } = PriceCategory.Retail;
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        // الوصفة الطبية
        public string? PrescriptionNumber { get; set; }
        public string? DoctorName { get; set; }

        // المستخدم والوردية
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int ShiftId { get; set; }

        // القيم المالية بالوحدة الصغرى (القرش)
        public long SubtotalPiastres { get; set; }
        public long DiscountPiastres { get; set; }
        public long VatPiastres { get; set; }
        public long TotalPiastres { get; set; }
        public long PaidPiastres { get; set; }
        public long RemainingPiastres { get; set; }

        public string? Notes { get; set; }

        /// <summary>
        /// في فاتورة المرتجع: معرّف فاتورة البيع الأصلية (إلزامي لمرتجع المبيعات)
        /// </summary>
        public int? OriginalInvoiceId { get; set; }

        /// <summary>
        /// موافقة مدير (Admin/Owner) على تجاوز حد الائتمان لهذه الفاتورة. لا تُخزَّن؛ تُضبط من الواجهة بعد التأكيد.
        /// </summary>
        public bool CreditLimitOverridden { get; set; }

        /// <summary>
        /// نص أو رمز الاستجابة السريعة (QR) للفاتورة عند تفعيله
        /// </summary>
        public string? QrCodeText { get; set; }

        public List<InvoiceItem> Items { get; set; } = new();
    }

    public class InvoiceItem
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;

        // بيانات التشغيلة المخصومة (FEFO)
        public int? BatchId { get; set; }

        /// <summary>
        /// في بند المرتجع: معرّف بند البيع الأصلي الذي يُرجَع منه
        /// </summary>
        public int? OriginalInvoiceItemId { get; set; }

        /// <summary>رقم الوصفة واسم الطبيب لهذا البند (إلزاميان للأصناف التي تتطلب وصفة)</summary>
        public string? PrescriptionNumber { get; set; }
        public string? DoctorName { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime? ExpiryDate { get; set; }

        public UnitLevel UnitLevel { get; set; } = UnitLevel.Box;
        public int QuantitySold { get; set; } = 1;
        public int SmallestUnitsCount { get; set; } = 1; // إجمالي ما سيُخصم من المخزون

        public long UnitPricePiastres { get; set; }
        public long DiscountPiastres { get; set; }
        public int VatRatePercent { get; set; } = 14;
        public int VatPercent { get => VatRatePercent; set => VatRatePercent = value; }
        public long VatPiastres { get; set; }
        public long TotalPiastres { get; set; }
    }

    public class SuspendedInvoice
    {
        public int Id { get; set; }
        public string ReferenceCode { get; set; } = string.Empty;
        public DateTime SuspendedAt { get; set; } = DateTime.Now;
        public string CustomerName { get; set; } = string.Empty;
        public string JsonData { get; set; } = string.Empty;

        public long TotalPiastres { get; set; }
    }

    public class PharmacySettings
    {
        public int Id { get; set; } = 1;
        public string PharmacyNameAr { get; set; } = "صيدلية ماجد القبانى";
        public string PharmacyNameEn { get; set; } = "Maged Al-Qabbani Pharmacy";
        
        /// <summary>
        /// رقم التسجيل الضريبي المصري (أرقام فقط، قابل للضبط، افتراضياً 9 أرقام)
        /// </summary>
        public string TaxNumber { get; set; } = "123456789";

        /// <summary>
        /// طول رقم التسجيل الضريبي المعتمد (افتراضي 9 أرقام)
        /// </summary>
        public int TaxNumberLength { get; set; } = 9;

        public string CommercialRegister { get; set; } = "1010998877";
        public string LicenseNumber { get; set; } = "PH-99201";
        public string PharmacistInCharge { get; set; } = "ماجد القبانى";
        public string ResponsiblePharmacist
        {
            get => PharmacistInCharge;
            set => PharmacistInCharge = value;
        }
        public string Phone { get; set; } = "0112345678";
        public string Address { get; set; } = "جمهورية مصر العربية - القاهرة";
        
        /// <summary>
        /// نسبة ضريبة القيمة المضافة الافتراضية بالصيدلية (افتراضياً 14%)
        /// </summary>
        public int DefaultVatPercent { get; set; } = 14;

        /// <summary>
        /// تفعيل أو تعطيل رمز QR في الفاتورة (معطل افتراضياً)
        /// </summary>
        public bool EnableQrCode { get; set; } = false;

        // الطابعات
        public string ReceiptPrinterName { get; set; } = string.Empty;
        public string A4PrinterName { get; set; } = string.Empty;
        public string LabelPrinterName { get; set; } = string.Empty;
        public int ReceiptPaperWidthMm { get; set; } = 80;
        public int LabelWidthMm { get; set; } = 50;
        public int LabelHeightMm { get; set; } = 25;

        // درج النقود والباركود
        public bool EnableCashDrawerOnPrint { get; set; } = true;
        public string CashDrawerEscPosBytes { get; set; } = "27,112,0,25,250"; // ESC p 0 25 250
        public int BarcodeScanThresholdMs { get; set; } = 45; // عتبة التفريق بين القارئ والكتابة اليدوية
    }
}
