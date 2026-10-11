using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.Services
{
    public interface IPdfReportService
    {
        string GenerateA4InvoicePdf(Invoice invoice, PharmacySettings settings);
        string GenerateCustomerStatementPdf(Customer customer, List<StatementRow> statement, PharmacySettings settings);
        string GenerateSupplierStatementPdf(Supplier supplier, List<StatementRow> statement, PharmacySettings settings);
        string GenerateVoucherPdf(string voucherNumber, DateTime date, string partyName, long amountPiastres, string voucherType, string notes, PharmacySettings settings);
    }

    public class PdfReportService : IPdfReportService
    {
        private static string GetPdfExportDirectory()
        {
            string pdfDir = Path.Combine(ePrescriptions.Core.Helpers.AppPaths.ExportsDirectory, "pdf");
            ePrescriptions.Core.Helpers.AppPaths.EnsureDirectory(pdfDir);
            return pdfDir;
        }

        public string GenerateA4InvoicePdf(Invoice invoice, PharmacySettings settings)
        {
            string fileName = $"فاتورة_{invoice.InvoiceNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = Path.Combine(GetPdfExportDirectory(), fileName);

            using var doc = new PdfDocument();
            doc.Info.Title = $"فاتورة مبيعات ضريبية - {invoice.InvoiceNumber}";
            doc.Info.Author = settings.PharmacyNameAr;

            var page = doc.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);

            var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
            var fontSubTitle = new XFont("Arial", 11, XFontStyle.Bold);
            var fontRegular = new XFont("Arial", 9, XFontStyle.Regular);
            var fontBold = new XFont("Arial", 9, XFontStyle.Bold);
            var fontSmall = new XFont("Arial", 8, XFontStyle.Regular);

            double margin = 36; // 0.5 inch
            double pageWidth = page.Width.Point;
            double contentWidth = pageWidth - (margin * 2);
            double y = margin;

            // 1) الترويسة الرئيسية
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(30, 27, 75)), margin, y, contentWidth, 54);
            gfx.DrawString(settings.PharmacyNameAr, fontTitle, XBrushes.White, new XRect(margin, y + 10, contentWidth, 22), XStringFormats.TopCenter);
            gfx.DrawString("فاتورة ضريبية (Tax Invoice)", fontSubTitle, XBrushes.LightSteelBlue, new XRect(margin, y + 32, contentWidth, 18), XStringFormats.TopCenter);
            y += 64;

            // 2) بيانات الصيدلية والعميل (صندوقان جانبيان)
            double boxWidth = (contentWidth - 12) / 2;

            // صندوق بيانات الصيدلية (البائع)
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(203, 213, 225), 1), new XSolidBrush(XColor.FromArgb(248, 250, 252)), margin, y, boxWidth, 88, 6, 6);
            gfx.DrawString("بيانات البائع (الصيدلية):", fontBold, XBrushes.DarkSlateBlue, margin + 10, y + 8);
            gfx.DrawString($"اسم الصيدلية: {settings.PharmacyNameAr}", fontRegular, XBrushes.Black, margin + 10, y + 24);
            gfx.DrawString($"رقم التسجيل الضريبي: {settings.TaxNumber}", fontBold, XBrushes.Black, margin + 10, y + 38);
            gfx.DrawString($"السجل التجاري: {settings.CommercialRegister} | الترخيص: {settings.LicenseNumber}", fontRegular, XBrushes.Black, margin + 10, y + 52);
            gfx.DrawString($"الصيدلي المسؤول: {settings.ResponsiblePharmacist}", fontRegular, XBrushes.Black, margin + 10, y + 66);
            gfx.DrawString($"العنوان: {settings.Address}", fontRegular, XBrushes.Black, margin + 10, y + 80);

            // صندوق بيانات الفاتورة والمشتري (العميل)
            double rightBoxX = margin + boxWidth + 12;
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(203, 213, 225), 1), new XSolidBrush(XColor.FromArgb(248, 250, 252)), rightBoxX, y, boxWidth, 88, 6, 6);
            gfx.DrawString("بيانات الفاتورة والمشتري:", fontBold, XBrushes.DarkSlateBlue, rightBoxX + 10, y + 8);
            gfx.DrawString($"رقم الفاتورة: {invoice.InvoiceNumber}", fontBold, XBrushes.Black, rightBoxX + 10, y + 24);
            gfx.DrawString($"التاريخ: {invoice.Date:yyyy/MM/dd HH:mm}", fontRegular, XBrushes.Black, rightBoxX + 10, y + 38);
            gfx.DrawString($"اسم المشتري / العميل: {invoice.CustomerName}", fontBold, XBrushes.Black, rightBoxX + 10, y + 52);
            string customerPhoneDisplay = string.IsNullOrWhiteSpace(invoice.CustomerPhone) ? "-" : invoice.CustomerPhone;
            gfx.DrawString($"الهاتف: {customerPhoneDisplay} | طريقة الدفع: {GetPaymentMethodName(invoice.PaymentMethod)}", fontRegular, XBrushes.Black, rightBoxX + 10, y + 66);
            string docAndRx = string.IsNullOrWhiteSpace(invoice.DoctorName) ? "" : $"الطبيب: {invoice.DoctorName}";
            if (!string.IsNullOrWhiteSpace(invoice.PrescriptionNumber)) docAndRx += $" | Rx: {invoice.PrescriptionNumber}";
            if (!string.IsNullOrWhiteSpace(docAndRx))
                gfx.DrawString(docAndRx, fontSmall, XBrushes.DarkSlateGray, rightBoxX + 10, y + 80);

            y += 100;

            // 3) جدول بنود الفاتورة
            double colItem = 175;
            double colBatch = 75;
            double colQty = 40;
            double colPrice = 60;
            double colDisc = 50;
            double colVatRate = 50;
            double colVat = 55;
            double colTotal = contentWidth - (colItem + colBatch + colQty + colPrice + colDisc + colVatRate + colVat);

            // رأس الجدول
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(79, 70, 229)), margin, y, contentWidth, 24);
            double curX = margin;
            DrawTableCell(gfx, "اسم الصنف / الدواء", fontBold, XBrushes.White, curX, y, colItem, 24, true); curX += colItem;
            DrawTableCell(gfx, "التشغيلة", fontBold, XBrushes.White, curX, y, colBatch, 24, true); curX += colBatch;
            DrawTableCell(gfx, "الكمية", fontBold, XBrushes.White, curX, y, colQty, 24, true); curX += colQty;
            DrawTableCell(gfx, "سعر الوحدة", fontBold, XBrushes.White, curX, y, colPrice, 24, true); curX += colPrice;
            DrawTableCell(gfx, "الخصم", fontBold, XBrushes.White, curX, y, colDisc, 24, true); curX += colDisc;
            DrawTableCell(gfx, "نسبة الضريبة", fontBold, XBrushes.White, curX, y, colVatRate, 24, true); curX += colVatRate;
            DrawTableCell(gfx, "قيمة الضريبة", fontBold, XBrushes.White, curX, y, colVat, 24, true); curX += colVat;
            DrawTableCell(gfx, "الإجمالي (ج.م)", fontBold, XBrushes.White, curX, y, colTotal, 24, true);
            y += 24;

            // أسطر البنود
            bool alt = false;
            foreach (var itm in invoice.Items)
            {
                var rowBrush = alt ? new XSolidBrush(XColor.FromArgb(248, 250, 252)) : XBrushes.White;
                gfx.DrawRectangle(rowBrush, margin, y, contentWidth, 22);

                curX = margin;
                DrawTableCell(gfx, itm.ItemNameAr, fontRegular, XBrushes.Black, curX, y, colItem, 22, false); curX += colItem;
                DrawTableCell(gfx, itm.BatchNumber, fontSmall, XBrushes.DarkSlateGray, curX, y, colBatch, 22, true); curX += colBatch;
                DrawTableCell(gfx, itm.QuantitySold.ToString(), fontBold, XBrushes.Black, curX, y, colQty, 22, true); curX += colQty;
                DrawTableCell(gfx, (itm.UnitPricePiastres / 100m).ToString("N2"), fontRegular, XBrushes.Black, curX, y, colPrice, 22, true); curX += colPrice;
                DrawTableCell(gfx, (itm.DiscountPiastres / 100m).ToString("N2"), fontRegular, XBrushes.DarkRed, curX, y, colDisc, 22, true); curX += colDisc;
                DrawTableCell(gfx, $"{itm.VatPercent}%", fontRegular, XBrushes.Black, curX, y, colVatRate, 22, true); curX += colVatRate;
                DrawTableCell(gfx, (itm.VatPiastres / 100m).ToString("N2"), fontRegular, XBrushes.Black, curX, y, colVat, 22, true); curX += colVat;
                DrawTableCell(gfx, (itm.TotalPiastres / 100m).ToString("N2"), fontBold, XBrushes.Black, curX, y, colTotal, 22, true);

                gfx.DrawRectangle(new XPen(XColor.FromArgb(226, 232, 240), 0.5), margin, y, contentWidth, 22);
                y += 22;
                alt = !alt;
            }

            y += 14;

            // 4) ملخص الحساب وصورة الـ QR Code (إن فُعّل)
            double summaryWidth = 240;
            double summaryX = margin + contentWidth - summaryWidth;

            // رسم QR Code إذا كان مفعلاً بالصيدلية
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
                    using var qrBmp = qr.GetGraphic(4);
                    using var ms = new MemoryStream();
                    qrBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);

                    using var ximg = XImage.FromStream(ms);
                    gfx.DrawImage(ximg, margin + 20, y, 90, 90);
                    gfx.DrawString("رمز الاستجابة السريعة المبسط", fontSmall, XBrushes.Gray, new XRect(margin, y + 94, 130, 16), XStringFormats.TopCenter);
                }
                catch { }
            }

            // صندوق الإجماليات المالية
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(203, 213, 225), 1), new XSolidBrush(XColor.FromArgb(241, 245, 249)), summaryX, y, summaryWidth, 105, 6, 6);
            DrawSummaryLine(gfx, "المجموع قبل الضريبة:", (invoice.SubtotalPiastres / 100m).ToString("N2") + " ج.م", fontRegular, summaryX, y + 10, summaryWidth);
            DrawSummaryLine(gfx, "إجمالي الخصم:", (invoice.DiscountPiastres / 100m).ToString("N2") + " ج.م", fontRegular, summaryX, y + 26, summaryWidth);
            DrawSummaryLine(gfx, "ضريبة القيمة المضافة:", (invoice.VatPiastres / 100m).ToString("N2") + " ج.م", fontRegular, summaryX, y + 42, summaryWidth);
            gfx.DrawLine(new XPen(XColor.FromArgb(203, 213, 225), 1), summaryX + 10, y + 60, summaryX + summaryWidth - 10, y + 60);
            DrawSummaryLine(gfx, "الإجمالي النهائي:", (invoice.TotalPiastres / 100m).ToString("N2") + " ج.م", fontTitle, summaryX, y + 66, summaryWidth, true);
            DrawSummaryLine(gfx, "المبلغ المدفوع:", (invoice.PaidPiastres / 100m).ToString("N2") + " ج.م", fontBold, summaryX, y + 88, summaryWidth);

            y += 120;

            // 5) التذييل والتوقيع
            gfx.DrawLine(new XPen(XColor.FromArgb(226, 232, 240), 1), margin, y, margin + contentWidth, y);
            y += 10;
            gfx.DrawString("شكراً لتعاملكم مع صيدلية ماجد القبانى  |  الأدوية المباعة لا تستبدل بعد 14 يوماً من تاريخ الفاتورة حسب التعليمات الطبية.", fontSmall, XBrushes.DarkSlateGray, new XRect(margin, y, contentWidth, 20), XStringFormats.TopCenter);

            doc.Save(filePath);
            return filePath;
        }

        public string GenerateCustomerStatementPdf(Customer customer, List<StatementRow> statement, PharmacySettings settings)
        {
            string fileName = $"كشف_حساب_عميل_{customer.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = Path.Combine(GetPdfExportDirectory(), fileName);

            using var doc = new PdfDocument();
            doc.Info.Title = $"كشف حساب عميل - {customer.Name}";
            var page = doc.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);

            var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
            var fontSub = new XFont("Arial", 11, XFontStyle.Bold);
            var fontRegular = new XFont("Arial", 9, XFontStyle.Regular);
            var fontBold = new XFont("Arial", 9, XFontStyle.Bold);
            var fontSmall = new XFont("Arial", 8, XFontStyle.Regular);

            double margin = 36;
            double contentWidth = page.Width.Point - (margin * 2);
            double y = margin;

            // الترويسة
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(15, 23, 42)), margin, y, contentWidth, 48);
            gfx.DrawString(settings.PharmacyNameAr, fontTitle, XBrushes.White, new XRect(margin, y + 8, contentWidth, 20), XStringFormats.TopCenter);
            gfx.DrawString("كشف حساب تفصيلي للعميل (Customer Statement)", fontSub, XBrushes.LightSteelBlue, new XRect(margin, y + 28, contentWidth, 18), XStringFormats.TopCenter);
            y += 58;

            // معلومات العميل
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(203, 213, 225), 1), new XSolidBrush(XColor.FromArgb(248, 250, 252)), margin, y, contentWidth, 54, 6, 6);
            gfx.DrawString($"اسم العميل: {customer.Name}", fontBold, XBrushes.Black, margin + 12, y + 10);
            gfx.DrawString($"رقم الهاتف: {customer.Phone}", fontRegular, XBrushes.Black, margin + 12, y + 28);
            gfx.DrawString($"الحد الائتماني: {(customer.CreditLimitPiastres / 100m):N2} ج.م", fontRegular, XBrushes.Black, margin + 220, y + 10);
            gfx.DrawString($"الرصيد الحالي المستحق: {(customer.CurrentBalancePiastres / 100m):N2} ج.م", fontBold, XBrushes.DarkRed, margin + 220, y + 28);
            gfx.DrawString($"تاريخ التقرير: {DateTime.Now:yyyy/MM/dd HH:mm}", fontRegular, XBrushes.DarkSlateGray, margin + 400, y + 10);
            y += 66;

            // جدول الحركات
            double colDate = 90;
            double colRef = 95;
            double colDesc = 140;
            double colDebit = 65;
            double colCredit = 65;
            double colBal = contentWidth - (colDate + colRef + colDesc + colDebit + colCredit);

            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(51, 65, 85)), margin, y, contentWidth, 24);
            double curX = margin;
            DrawTableCell(gfx, "التاريخ", fontBold, XBrushes.White, curX, y, colDate, 24, true); curX += colDate;
            DrawTableCell(gfx, "رقم المرجع", fontBold, XBrushes.White, curX, y, colRef, 24, true); curX += colRef;
            DrawTableCell(gfx, "البيان / الحركة", fontBold, XBrushes.White, curX, y, colDesc, 24, false); curX += colDesc;
            DrawTableCell(gfx, "مدين (ج.م)", fontBold, XBrushes.White, curX, y, colDebit, 24, true); curX += colDebit;
            DrawTableCell(gfx, "دائن (ج.م)", fontBold, XBrushes.White, curX, y, colCredit, 24, true); curX += colCredit;
            DrawTableCell(gfx, "الرصيد التراكمي", fontBold, XBrushes.White, curX, y, colBal, 24, true);
            y += 24;

            bool alt = false;
            foreach (var r in statement)
            {
                var bg = alt ? new XSolidBrush(XColor.FromArgb(248, 250, 252)) : XBrushes.White;
                gfx.DrawRectangle(bg, margin, y, contentWidth, 20);

                curX = margin;
                DrawTableCell(gfx, r.Date.ToString("yyyy/MM/dd"), fontRegular, XBrushes.Black, curX, y, colDate, 20, true); curX += colDate;
                DrawTableCell(gfx, r.ReferenceNumber, fontSmall, XBrushes.DarkSlateGray, curX, y, colRef, 20, true); curX += colRef;
                DrawTableCell(gfx, r.Description, fontRegular, XBrushes.Black, curX, y, colDesc, 20, false); curX += colDesc;
                DrawTableCell(gfx, (r.DebitPiastres / 100m).ToString("N2"), fontRegular, XBrushes.DarkRed, curX, y, colDebit, 20, true); curX += colDebit;
                DrawTableCell(gfx, (r.CreditPiastres / 100m).ToString("N2"), fontRegular, XBrushes.DarkGreen, curX, y, colCredit, 20, true); curX += colCredit;
                DrawTableCell(gfx, (r.RunningBalancePiastres / 100m).ToString("N2") + " ج.م", fontBold, XBrushes.Black, curX, y, colBal, 20, true);

                gfx.DrawRectangle(new XPen(XColor.FromArgb(226, 232, 240), 0.5), margin, y, contentWidth, 20);
                y += 20;
                alt = !alt;
            }

            y += 20;
            gfx.DrawString("ختم وتوقيع الحسابات:", fontBold, XBrushes.DarkSlateBlue, margin + 40, y);
            gfx.DrawString("توقيع العميل بالمصادقة:", fontBold, XBrushes.DarkSlateBlue, margin + 350, y);

            doc.Save(filePath);
            return filePath;
        }

        public string GenerateSupplierStatementPdf(Supplier supplier, List<StatementRow> statement, PharmacySettings settings)
        {
            string fileName = $"كشف_حساب_مورد_{supplier.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = Path.Combine(GetPdfExportDirectory(), fileName);

            using var doc = new PdfDocument();
            doc.Info.Title = $"كشف حساب مورد - {supplier.Name}";
            var page = doc.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);

            var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
            var fontSub = new XFont("Arial", 11, XFontStyle.Bold);
            var fontRegular = new XFont("Arial", 9, XFontStyle.Regular);
            var fontBold = new XFont("Arial", 9, XFontStyle.Bold);

            double margin = 36;
            double contentWidth = page.Width.Point - (margin * 2);
            double y = margin;

            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(4, 120, 87)), margin, y, contentWidth, 48);
            gfx.DrawString(settings.PharmacyNameAr, fontTitle, XBrushes.White, new XRect(margin, y + 8, contentWidth, 20), XStringFormats.TopCenter);
            gfx.DrawString("كشف حساب مورد أدوية (Supplier Statement)", fontSub, XBrushes.LightSteelBlue, new XRect(margin, y + 28, contentWidth, 18), XStringFormats.TopCenter);
            y += 58;

            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(203, 213, 225), 1), new XSolidBrush(XColor.FromArgb(248, 250, 252)), margin, y, contentWidth, 54, 6, 6);
            gfx.DrawString($"اسم المورد / الشركة: {supplier.Name}", fontBold, XBrushes.Black, margin + 12, y + 10);
            gfx.DrawString($"الهاتف: {supplier.Phone}", fontRegular, XBrushes.Black, margin + 12, y + 28);
            gfx.DrawString($"الرصيد المستحق للمورد: {(supplier.BalancePiastres / 100m):N2} ج.م", fontBold, XBrushes.DarkRed, margin + 260, y + 10);
            gfx.DrawString($"تاريخ الطباعة: {DateTime.Now:yyyy/MM/dd}", fontRegular, XBrushes.Black, margin + 260, y + 28);
            y += 66;

            double colDate = 90;
            double colRef = 95;
            double colDesc = 140;
            double colDebit = 65;
            double colCredit = 65;
            double colBal = contentWidth - (colDate + colRef + colDesc + colDebit + colCredit);

            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(6, 78, 59)), margin, y, contentWidth, 24);
            double curX = margin;
            DrawTableCell(gfx, "التاريخ", fontBold, XBrushes.White, curX, y, colDate, 24, true); curX += colDate;
            DrawTableCell(gfx, "رقم المرجع", fontBold, XBrushes.White, curX, y, colRef, 24, true); curX += colRef;
            DrawTableCell(gfx, "البيان", fontBold, XBrushes.White, curX, y, colDesc, 24, false); curX += colDesc;
            DrawTableCell(gfx, "سندات صرف", fontBold, XBrushes.White, curX, y, colDebit, 24, true); curX += colDebit;
            DrawTableCell(gfx, "فواتير شراء", fontBold, XBrushes.White, curX, y, colCredit, 24, true); curX += colCredit;
            DrawTableCell(gfx, "الرصيد المستحق", fontBold, XBrushes.White, curX, y, colBal, 24, true);
            y += 24;

            bool alt = false;
            foreach (var r in statement)
            {
                var bg = alt ? new XSolidBrush(XColor.FromArgb(248, 250, 252)) : XBrushes.White;
                gfx.DrawRectangle(bg, margin, y, contentWidth, 20);

                curX = margin;
                DrawTableCell(gfx, r.Date.ToString("yyyy/MM/dd"), fontRegular, XBrushes.Black, curX, y, colDate, 20, true); curX += colDate;
                DrawTableCell(gfx, r.ReferenceNumber, fontRegular, XBrushes.DarkSlateGray, curX, y, colRef, 20, true); curX += colRef;
                DrawTableCell(gfx, r.Description, fontRegular, XBrushes.Black, curX, y, colDesc, 20, false); curX += colDesc;
                DrawTableCell(gfx, (r.DebitPiastres / 100m).ToString("N2"), fontRegular, XBrushes.DarkGreen, curX, y, colDebit, 20, true); curX += colDebit;
                DrawTableCell(gfx, (r.CreditPiastres / 100m).ToString("N2"), fontRegular, XBrushes.DarkRed, curX, y, colCredit, 20, true); curX += colCredit;
                DrawTableCell(gfx, (r.RunningBalancePiastres / 100m).ToString("N2") + " ج.م", fontBold, XBrushes.Black, curX, y, colBal, 20, true);

                gfx.DrawRectangle(new XPen(XColor.FromArgb(226, 232, 240), 0.5), margin, y, contentWidth, 20);
                y += 20;
                alt = !alt;
            }

            doc.Save(filePath);
            return filePath;
        }

        public string GenerateVoucherPdf(string voucherNumber, DateTime date, string partyName, long amountPiastres, string voucherType, string notes, PharmacySettings settings)
        {
            string fileName = $"سند_{voucherNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = Path.Combine(GetPdfExportDirectory(), fileName);

            using var doc = new PdfDocument();
            doc.Info.Title = $"{voucherType} - {voucherNumber}";
            var page = doc.AddPage();
            page.Size = PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);

            var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
            var fontSub = new XFont("Arial", 12, XFontStyle.Bold);
            var fontRegular = new XFont("Arial", 10, XFontStyle.Regular);
            var fontBold = new XFont("Arial", 10, XFontStyle.Bold);

            double margin = 36;
            double contentWidth = page.Width.Point - (margin * 2);
            double y = margin;

            // إطار خارجي أنيق
            gfx.DrawRoundedRectangle(new XPen(XColor.FromArgb(30, 41, 59), 1.5), margin, y, contentWidth, 260, 10, 10);

            // ترويسة السند
            gfx.DrawString(settings.PharmacyNameAr, fontTitle, XBrushes.Black, new XRect(margin, y + 16, contentWidth, 24), XStringFormats.TopCenter);
            gfx.DrawString(voucherType, fontSub, XBrushes.Indigo, new XRect(margin, y + 42, contentWidth, 20), XStringFormats.TopCenter);
            gfx.DrawLine(new XPen(XColor.FromArgb(203, 213, 225), 1), margin + 20, y + 68, margin + contentWidth - 20, y + 68);

            // محتوى السند
            double innerY = y + 80;
            gfx.DrawString($"رقم السند: {voucherNumber}", fontBold, XBrushes.Black, margin + 24, innerY);
            gfx.DrawString($"التاريخ: {date:yyyy/MM/dd HH:mm}", fontRegular, XBrushes.Black, margin + contentWidth - 180, innerY);
            innerY += 26;

            decimal amountPounds = amountPiastres / 100m;
            gfx.DrawString($"المبلغ رقماً: {amountPounds:N2} جنيه مصري (ج.م)", fontBold, XBrushes.DarkRed, margin + 24, innerY);
            innerY += 26;

            string actionVerb = voucherType.Contains("قبض") ? "استلمنا من السيد / السادة:" : "صرفنا إلى السيد / السادة:";
            gfx.DrawString($"{actionVerb}  {partyName}", fontRegular, XBrushes.Black, margin + 24, innerY);
            innerY += 26;

            if (!string.IsNullOrEmpty(notes))
            {
                gfx.DrawString($"وذلك عن / البيان: {notes}", fontRegular, XBrushes.DarkSlateGray, margin + 24, innerY);
                innerY += 26;
            }

            innerY += 20;
            gfx.DrawString("المستلم / الدافع: .............................", fontRegular, XBrushes.Black, margin + 30, innerY);
            gfx.DrawString("أمين الصندوق / المحاسب: .............................", fontRegular, XBrushes.Black, margin + contentWidth - 250, innerY);

            doc.Save(filePath);
            return filePath;
        }

        private static void DrawTableCell(XGraphics gfx, string text, XFont font, XBrush brush, double x, double y, double width, double height, bool center)
        {
            var format = center ? XStringFormats.Center : XStringFormats.CenterLeft;
            double pad = center ? 0 : 6;
            gfx.DrawString(text, font, brush, new XRect(x + pad, y, width - (pad * 2), height), format);
        }

        private static void DrawSummaryLine(XGraphics gfx, string label, string val, XFont font, double x, double y, double width, bool highlight = false)
        {
            var brush = highlight ? XBrushes.Indigo : XBrushes.Black;
            gfx.DrawString(label, font, brush, x + 10, y);
            gfx.DrawString(val, font, brush, new XRect(x, y - 4, width - 12, 16), XStringFormats.TopRight);
        }

        private static string GetPaymentMethodName(PaymentMethod method) => method switch
        {
            PaymentMethod.Cash => "نقدي",
            PaymentMethod.Network => "شبكة / فيزا",
            PaymentMethod.OnAccount => "آجل (على الحساب)",
            PaymentMethod.Insurance => "تأمين طبي",
            _ => "نقدي"
        };
    }
}
