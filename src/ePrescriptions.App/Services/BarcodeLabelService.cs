using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Printing;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.Services
{
    public interface IBarcodeLabelService
    {
        bool PrintBarcodeLabel(Item item, ItemBatch? batch, int labelCount, out string errorMessage);
    }

    public class BarcodeLabelService : IBarcodeLabelService
    {
        private readonly SettingsRepository _settingsRepository;

        public BarcodeLabelService(SettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }

        public bool PrintBarcodeLabel(Item item, ItemBatch? batch, int labelCount, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (labelCount <= 0) labelCount = 1;

            try
            {
                var settings = _settingsRepository.GetSettings();
                string printerName = string.IsNullOrWhiteSpace(settings.LabelPrinterName) || settings.LabelPrinterName.Contains("الافتراضية")
                    ? string.Empty
                    : settings.LabelPrinterName;

                string pharmacyName = string.IsNullOrWhiteSpace(settings.PharmacyNameAr) ? "صيدلية ماجد القبانى" : settings.PharmacyNameAr;
                // ملصق تشغيلة: الباركود يعرّف التشغيلة (ومنها تاريخ الصلاحية)، وغير ذلك باركود الصنف العادي
                bool isBatchLabel = batch != null && batch.Id > 0;
                string barcodeToPrint = isBatchLabel
                    ? BatchLabelCode.Encode(batch!.Id)
                    : (string.IsNullOrWhiteSpace(item.Barcode) ? item.Code : item.Barcode);
                decimal retailPounds = item.RetailPriceBoxPiastres / 100m;
                string priceText = $"{retailPounds:N2} ج.م";
                string expText = batch != null ? $"صلاحية: {batch.ExpiryDate:yyyy/MM/dd}" : "";
                string batchText = batch != null && !string.IsNullOrEmpty(batch.BatchNumber) ? $"تشغيلة: {batch.BatchNumber}" : "";

                // طباعة المستند باستخدام PrintDocument لنظام Windows
                for (int i = 0; i < labelCount; i++)
                {
                    using var printDoc = new PrintDocument();
                    if (!string.IsNullOrEmpty(printerName))
                    {
                        printDoc.PrinterSettings.PrinterName = printerName;
                    }

                    // أبعاد استيكر 50مم × 25مم (تقريباً 200 × 100 بمعدل 100 DPI)
                    int widthHundredths = (int)(settings.LabelWidthMm / 25.4 * 100);
                    int heightHundredths = (int)(settings.LabelHeightMm / 25.4 * 100);
                    printDoc.DefaultPageSettings.PaperSize = new PaperSize("CustomLabel", Math.Max(150, widthHundredths), Math.Max(90, heightHundredths));
                    printDoc.DefaultPageSettings.Margins = new Margins(2, 2, 2, 2);

                    printDoc.PrintPage += (s, e) =>
                    {
                        var g = e.Graphics;
                        if (g == null) return;

                        using var fontTitle = new Font("Tahoma", 7, FontStyle.Bold);
                        using var fontItem = new Font("Tahoma", 8, FontStyle.Bold);
                        using var fontPrice = new Font("Tahoma", 9, FontStyle.Bold);
                        using var fontSub = new Font("Tahoma", 6, FontStyle.Regular);
                        using var brush = new SolidBrush(Color.Black);

                        var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
                        int currentY = 2;
                        int totalWidth = e.PageBounds.Width;

                        // 1) اسم الصيدلية (ورقم التشغيلة على ملصق التشغيلة)
                        string titleLine = isBatchLabel && !string.IsNullOrEmpty(batch!.BatchNumber)
                            ? $"{pharmacyName} | ت: {batch.BatchNumber}"
                            : pharmacyName;
                        g.DrawString(titleLine, fontTitle, brush, new RectangleF(0, currentY, totalWidth, 14), sfCenter);
                        currentY += 14;

                        // 2) اسم الدواء
                        string displayName = item.NameAr.Length > 26 ? item.NameAr.Substring(0, 24) + ".." : item.NameAr;
                        g.DrawString(displayName, fontItem, brush, new RectangleF(0, currentY, totalWidth, 16), sfCenter);
                        currentY += 16;

                        // 3) رسم الباركود Code128
                        try
                        {
                            using var barcodeBmp = Code128BarcodeGenerator.GenerateCode128Bitmap(barcodeToPrint, height: 36, moduleWidth: 1, includeText: true);
                            int bmpX = Math.Max(0, (totalWidth - barcodeBmp.Width) / 2);
                            g.DrawImage(barcodeBmp, bmpX, currentY);
                            currentY += barcodeBmp.Height + 2;
                        }
                        catch
                        {
                            g.DrawString(barcodeToPrint, fontPrice, brush, new RectangleF(0, currentY, totalWidth, 18), sfCenter);
                            currentY += 18;
                        }

                        // 4) السعر وتاريخ الصلاحية
                        string footerText = $"{priceText}  |  {expText}";
                        g.DrawString(footerText, fontPrice, brush, new RectangleF(0, currentY, totalWidth, 18), sfCenter);
                    };

                    printDoc.Print();
                }

                Logger.LogInfo($"تمت طباعة {labelCount} ملصق باركود للصنف {item.Code} بنجاح.", "BarcodeLabelService");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Logger.LogException(ex, "BarcodeLabelService.PrintBarcodeLabel");
                return false;
            }
        }
    }
}
