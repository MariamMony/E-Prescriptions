using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Printing;
using ePrescriptions.Domain.Entities;
using QRCoder;

namespace ePrescriptions.App.Services
{
    /// <summary>
    /// محرك طباعة الإيصالات الحرارية (58 مم / 80 مم) المتوافق مع طابعات إيصالات نقاط البيع في مصر
    /// يدعم الاتجاه العربي من اليمين لليسار، رمز الاستجابة السريع البسيط (اختياري حسب الإعدادات)، والعملة بالجنيه المصري (ج.م).
    /// </summary>
    public static class ThermalReceiptPrinter
    {
        public static bool PrintReceipt(Invoice invoice, PharmacySettings settings, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using var printDoc = new PrintDocument();
                string printerName = string.IsNullOrWhiteSpace(settings.ReceiptPrinterName)
                    ? printDoc.PrinterSettings.PrinterName
                    : settings.ReceiptPrinterName;

                printDoc.PrinterSettings.PrinterName = printerName;
                if (!printDoc.PrinterSettings.IsValid)
                {
                    errorMessage = $"طابعة الإيصالات المحددة ({printerName}) غير متوفرة أو غير متصلة.";
                    return false;
                }

                int paperWidthHundredths = (int)Math.Round(settings.ReceiptPaperWidthMm * 100.0 / 25.4);
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", paperWidthHundredths, 2000);
                printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 20);

                printDoc.PrintPage += (sender, e) =>
                {
                    if (e.Graphics == null) return;
                    var g = e.Graphics;

                    float printWidth = e.MarginBounds.Width > 0 ? e.MarginBounds.Width : (paperWidthHundredths - 20);
                    float y = 10f;

                    using var fontTitle = new Font("Arial", 11, FontStyle.Bold);
                    using var fontSubTitle = new Font("Arial", 8.5f, FontStyle.Regular);
                    using var fontRegular = new Font("Arial", 8f, FontStyle.Regular);
                    using var fontBold = new Font("Arial", 8f, FontStyle.Bold);
                    using var fontLargeBold = new Font("Arial", 12f, FontStyle.Bold);

                    using var sfCenter = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.DirectionRightToLeft
                    };

                    using var sfRtl = new StringFormat
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.DirectionRightToLeft
                    };

                    using var sfLtr = new StringFormat
                    {
                        Alignment = StringAlignment.Far,
                        LineAlignment = StringAlignment.Center
                    };

                    // 1) اسم الصيدلية والترويسة
                    g.DrawString(settings.PharmacyNameAr, fontTitle, Brushes.Black, new RectangleF(0, y, printWidth, 20), sfCenter);
                    y += 20;

                    if (!string.IsNullOrWhiteSpace(settings.PharmacyNameEn))
                    {
                        g.DrawString(settings.PharmacyNameEn, fontSubTitle, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.ResponsiblePharmacist))
                    {
                        g.DrawString($"الصيدلي المسؤول: {settings.ResponsiblePharmacist}", fontSubTitle, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.Address))
                    {
                        g.DrawString(settings.Address, fontSubTitle, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.TaxNumber))
                    {
                        g.DrawString($"رقم التسجيل الضريبي: {settings.TaxNumber}", fontSubTitle, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.CommercialRegister))
                    {
                        g.DrawString($"س.ت: {settings.CommercialRegister}", fontSubTitle, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    y += 4;
                    DrawDottedLine(g, y, printWidth);
                    y += 8;

                    // 2) بيانات الفاتورة
                    string invoiceTypeStr = invoice.Type == Domain.Enums.InvoiceType.Return ? "إشعار دائن (مرتجع مبيعات)" : "فاتورة مبيعات نقدية";
                    g.DrawString(invoiceTypeStr, fontBold, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                    y += 18;

                    g.DrawString($"رقم الفاتورة: {invoice.InvoiceNumber}", fontBold, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfRtl);
                    y += 16;

                    g.DrawString($"التاريخ: {invoice.Date:yyyy/MM/dd HH:mm}", fontRegular, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfRtl);
                    y += 16;

                    if (!string.IsNullOrWhiteSpace(invoice.CustomerName) && invoice.CustomerName != "عميل نقدي")
                    {
                        g.DrawString($"العميل: {invoice.CustomerName}", fontRegular, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfRtl);
                        y += 16;
                    }

                    y += 4;
                    DrawDottedLine(g, y, printWidth);
                    y += 8;

                    // 3) جدول الأصناف
                    g.DrawString("الصنف", fontBold, Brushes.Black, new RectangleF(0, y, printWidth * 0.45f, 16), sfRtl);
                    g.DrawString("الكمية", fontBold, Brushes.Black, new RectangleF(printWidth * 0.45f, y, printWidth * 0.20f, 16), sfCenter);
                    g.DrawString("السعر (ج.م)", fontBold, Brushes.Black, new RectangleF(printWidth * 0.65f, y, printWidth * 0.35f, 16), sfLtr);
                    y += 18;

                    foreach (var item in invoice.Items)
                    {
                        g.DrawString(item.ItemNameAr, fontBold, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfRtl);
                        y += 16;

                        string qtyDetail = $"{item.QuantitySold} x {(item.UnitPricePiastres / 100.0):N2}";
                        g.DrawString(qtyDetail, fontRegular, Brushes.DarkSlateGray, new RectangleF(0, y, printWidth * 0.60f, 14), sfRtl);
                        g.DrawString($"{(item.TotalPiastres / 100.0):N2} ج.م", fontBold, Brushes.Black, new RectangleF(printWidth * 0.60f, y, printWidth * 0.40f, 14), sfLtr);
                        y += 16;
                    }

                    y += 4;
                    DrawDottedLine(g, y, printWidth);
                    y += 8;

                    // 4) المجاميع المالية
                    g.DrawString($"المجموع قبل الضريبة: {(invoice.SubtotalPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfRtl);
                    y += 16;

                    if (invoice.DiscountPiastres > 0)
                    {
                        g.DrawString($"إجمالي الخصم: {(invoice.DiscountPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfRtl);
                        y += 16;
                    }

                    g.DrawString($"ضريبة القيمة المضافة: {(invoice.VatPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfRtl);
                    y += 18;

                    // الإجمالي النهائي
                    g.DrawString($"صافي المستحق: {(invoice.TotalPiastres / 100.0):N2} ج.م", fontLargeBold, Brushes.Black, new RectangleF(0, y, printWidth, 24), sfCenter);
                    y += 24;

                    g.DrawString($"المدفوع: {(invoice.PaidPiastres / 100.0):N2} ج.م   |   المتبقي: {(invoice.RemainingPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                    y += 20;

                    // 5) رمز الاستجابة السريع البسيط (إن فُعّل من الإعدادات)
                    if (settings.EnableQrCode)
                    {
                        try
                        {
                            string qrText = !string.IsNullOrEmpty(invoice.QrCodeText)
                                ? invoice.QrCodeText
                                : EgyptQrHelper.BuildSimpleQrText(invoice.InvoiceNumber, invoice.Date, invoice.TotalPiastres, settings.TaxNumber);

                            using var qrGen = new QRCodeGenerator();
                            using var qrData = qrGen.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.M);
                            using var qr = new QRCode(qrData);
                            using var qrBmp = qr.GetGraphic(3);

                            float qrSize = 90f;
                            float qrX = (printWidth - qrSize) / 2f;
                            g.DrawImage(qrBmp, qrX, y, qrSize, qrSize);
                            y += qrSize + 6;
                        }
                        catch { }
                    }

                    // تذييل الإيصال
                    g.DrawString("شكراً لزيارتكم - نتمنى لكم دوام الصحة والعافية", fontSubTitle, Brushes.Black, new RectangleF(0, y, printWidth, 16), sfCenter);
                    y += 18;
                    g.DrawString("نظام إدارة الصيدليات e_prescriptions", fontRegular, Brushes.Gray, new RectangleF(0, y, printWidth, 14), sfCenter);

                    e.HasMorePages = false;
                };

                printDoc.Print();

                // فتح درج النقدية إذا كان مفعلاً وكانت الفاتورة نقدية
                if (settings.EnableCashDrawerOnPrint && invoice.PaymentMethod == Domain.Enums.PaymentMethod.Cash)
                {
                    OpenCashDrawer(settings, out _);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"حدث خطأ أثناء الطباعة: {ex.Message}";
                return false;
            }
        }

        public static bool OpenCashDrawer(PharmacySettings settings, out string errorMessage)
        {
            return OpenCashDrawer(settings.ReceiptPrinterName, settings.CashDrawerEscPosBytes, out errorMessage);
        }

        public static bool OpenCashDrawer(string printerName, string escPosBytesStr, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(printerName))
                {
                    using var defaultDoc = new PrintDocument();
                    printerName = defaultDoc.PrinterSettings.PrinterName;
                }
                byte[] pulseBytes = ParseEscPosBytes(escPosBytesStr);
                return RawPrinterHelper.SendBytesToPrinter(printerName, pulseBytes, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static bool PrintTestTicket(string printerName, string pharmacyName, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using var printDoc = new PrintDocument();
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    printDoc.PrinterSettings.PrinterName = printerName;
                }
                if (!printDoc.PrinterSettings.IsValid)
                {
                    errorMessage = $"الطابعة المحددة ({printerName}) غير متوفرة.";
                    return false;
                }

                printDoc.PrintPage += (s, e) =>
                {
                    if (e.Graphics == null) return;
                    using var f = new Font("Arial", 10, FontStyle.Bold);
                    e.Graphics.DrawString($"اختبار نجاح الاتصال بطابعة الكاشير\n{pharmacyName}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}", f, Brushes.Black, new PointF(10, 10));
                };

                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void DrawDottedLine(Graphics g, float y, float width)
        {
            using var pen = new Pen(Color.Black, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawLine(pen, 0, y, width, y);
        }

        private static byte[] ParseEscPosBytes(string csvBytes)
        {
            if (string.IsNullOrWhiteSpace(csvBytes)) return new byte[] { 27, 112, 0, 25, 250 };
            var parts = csvBytes.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<byte>();
            foreach (var part in parts)
            {
                if (byte.TryParse(part.Trim(), out byte b)) list.Add(b);
            }
            return list.Count > 0 ? list.ToArray() : new byte[] { 27, 112, 0, 25, 250 };
        }
    }
}
