using System;
using System.Text.RegularExpressions;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// أدوات الفاتورة ورمز QR البسيط والتحقق من البيانات الضريبية المصرية.
    /// يدعم توليد نص QR بسيط للفاتورة عند تفعيله من الإعدادات بدون أي اعتماد على الإنترنت أو منظومة خارجية.
    /// </summary>
    public static class EgyptQrHelper
    {
        /// <summary>
        /// توليد نص QR بسيط غير مشفر يحمل رقم الفاتورة والتاريخ والإجمالي والرقم الضريبي
        /// بصيغة نصية مباشرة وواضحة لقارئ الباركود/الكاميرا:
        /// FTR: رقم الفاتورة | DAT: التاريخ | TOT: الإجمالي | TAX: الرقم الضريبي
        /// </summary>
        public static string GenerateSimpleInvoiceQrText(
            string invoiceNumber,
            DateTime invoiceDate,
            decimal totalWithVat,
            string taxRegistrationNumber,
            string sellerName = "")
        {
            string cleanSeller = string.IsNullOrWhiteSpace(sellerName) ? string.Empty : $"{sellerName.Trim()} | ";
            return $"{cleanSeller}فاتورة: {invoiceNumber} | التاريخ: {invoiceDate:yyyy-MM-dd HH:mm} | الإجمالي: {totalWithVat:N2} ج.م | س.ض: {taxRegistrationNumber}";
        }

        /// <summary>
        /// توليد نص QR بسيط للفاتورة بقيمة الإجمالي بالقروش
        /// </summary>
        public static string BuildSimpleQrText(
            string invoiceNumber,
            DateTime invoiceDate,
            long totalPiastres,
            string taxRegistrationNumber,
            string sellerName = "")
        {
            decimal totalPounds = totalPiastres / 100m;
            return GenerateSimpleInvoiceQrText(invoiceNumber, invoiceDate, totalPounds, taxRegistrationNumber, sellerName);
        }

        /// <summary>
        /// التحقق من رقم التسجيل الضريبي المصري: أرقام فقط مع مطابقة الطول المطلوب (افتراضياً 9 أرقام).
        /// </summary>
        public static bool ValidateTaxRegistrationNumber(string? taxNumber, int expectedLength = 9)
        {
            if (string.IsNullOrWhiteSpace(taxNumber))
                return false;

            string clean = taxNumber.Trim();
            if (expectedLength > 0 && clean.Length != expectedLength)
                return false;

            return Regex.IsMatch(clean, @"^\d+$");
        }
    }
}
