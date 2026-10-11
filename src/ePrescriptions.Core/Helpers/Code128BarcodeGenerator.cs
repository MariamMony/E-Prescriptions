using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// مولد باركود Code 128 (مجموعة B) بلغة C# نقية وبدون أي مكتبات خارجية.
    /// يرسم الخطوط والفراغات بدقة مع حساب خانة التحقق (Modulo 103 Checksum).
    /// </summary>
    public static class Code128BarcodeGenerator
    {
        // أنماط الخطوط والفراغات لكل رمز من 0 إلى 106 (كل رمز 11 وحدة، ورمز التوقف 13 وحدة)
        private static readonly string[] Code128Patterns = new string[]
        {
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", // 0-9
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132", // 10-19
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", // 20-29
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", // 30-39
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331", // 40-49
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", // 50-59
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214", // 60-69
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111", // 70-79
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", // 80-89
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141", // 90-99
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112" // 100-106 (104=Start B, 106=Stop)
        };

        private const int StartCodeB = 104;
        private const int StopCode = 106;

        /// <summary>
        /// توليد صورة الباركود بصيغة Bitmap بدقة محددة مع نص الصنف وأرقام الباركود أسفله
        /// </summary>
        public static Bitmap GenerateCode128Bitmap(string text, int height = 80, int moduleWidth = 2, bool includeText = true)
        {
            if (string.IsNullOrEmpty(text)) text = "000000";

            var codes = new List<int> { StartCodeB };
            int checksum = StartCodeB;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int codeVal = c - 32; // Code Set B mapping
                if (codeVal < 0 || codeVal > 95) codeVal = 0;

                codes.Add(codeVal);
                checksum += (codeVal * (i + 1));
            }

            int checkDigit = checksum % 103;
            codes.Add(checkDigit);
            codes.Add(StopCode);

            // تحويل الأكواد إلى سلسلة خطوط (Bar/Space)
            string patternString = "";
            foreach (int c in codes)
            {
                patternString += Code128Patterns[c];
            }

            int totalUnits = 0;
            foreach (char ch in patternString)
            {
                totalUnits += (ch - '0');
            }

            int quietZone = 15; // هامش أمان أبيض جانبي
            int totalWidth = (totalUnits * moduleWidth) + (quietZone * 2);
            int textHeight = includeText ? 20 : 0;
            int totalHeight = height + textHeight;

            var bmp = new Bitmap(totalWidth, totalHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                int currentX = quietZone;
                bool isBar = true;

                foreach (char ch in patternString)
                {
                    int width = (ch - '0') * moduleWidth;
                    if (isBar)
                    {
                        g.FillRectangle(Brushes.Black, currentX, 5, width, height - 10);
                    }
                    currentX += width;
                    isBar = !isBar;
                }

                if (includeText)
                {
                    using var font = new Font("Arial", 9, FontStyle.Bold);
                    using var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    var textRect = new RectangleF(0, height - 5, totalWidth, textHeight);
                    g.DrawString(text, font, Brushes.Black, textRect, sf);
                }
            }

            return bmp;
        }
    }
}
