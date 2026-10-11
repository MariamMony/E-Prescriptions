using System;
using System.Globalization;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// رمز الباركود المطبوع على علب التشغيلة. الرمز يشير إلى التشغيلة (ومنها تاريخ الصلاحية) وليس إلى الصنف فقط.
    /// الصيغة أرقام فقط (10 خانات): "99" + رقم التشغيلة بثمانية أرقام. أرقام فقط حتى لا يتأثر بلغة لوحة المفاتيح
    /// (قارئ الباركود يكتب كأنه لوحة مفاتيح، والحروف تتغير لو اللغة العربية مفعّلة).
    /// </summary>
    public static class BatchLabelCode
    {
        private const string Prefix = "99";
        private const int NumberDigits = 8;
        public const int CodeLength = 10;

        public static string Encode(int batchId)
        {
            if (batchId <= 0 || batchId > 99999999)
                throw new ArgumentOutOfRangeException(nameof(batchId));
            return Prefix + batchId.ToString(new string('0', NumberDigits), CultureInfo.InvariantCulture);
        }

        public static bool TryDecode(string? code, out int batchId)
        {
            batchId = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;

            string c = code.Trim();
            if (c.Length != CodeLength || !c.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            foreach (char ch in c)
            {
                if (ch < '0' || ch > '9') return false;
            }

            return int.TryParse(c.Substring(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out batchId) && batchId > 0;
        }
    }

    /// <summary>
    /// قواعد الصلاحية المشتركة بين شاشات البيع والمخزون
    /// </summary>
    public static class ExpiryPolicy
    {
        /// <summary>عدد الأيام التي تُعتبر فيها التشغيلة "قريبة الانتهاء" ويظهر تنبيه عند بيعها</summary>
        public const int NearExpiryDays = 90;

        /// <summary>التشغيلة منتهية إذا كان تاريخ انتهائها قبل اليوم (يوم الانتهاء نفسه ما زال صالحاً)</summary>
        public static bool IsExpired(DateTime expiryDate) => expiryDate.Date < DateTime.Today;

        public static bool IsNearExpiry(DateTime expiryDate) =>
            !IsExpired(expiryDate) && expiryDate.Date <= DateTime.Today.AddDays(NearExpiryDays);
    }
}
