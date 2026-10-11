using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.Services
{
    public interface IExcelExportService
    {
        string ExportSalesReport(List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName);
        string ExportItemMovementReport(List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName);
        string ExportLowStockReport(List<LowStockReportRow> lowStock, string pharmacyName);
        string ExportExpiryReport(List<ExpiryReportRow> expiries, string pharmacyName);
        string ExportInventoryValuationReport(InventoryValuationSummary valuation, string pharmacyName);
    }

    public class ExcelExportService : IExcelExportService
    {
        private static string GetExportDirectory()
        {
            return ePrescriptions.Core.Helpers.AppPaths.EnsureDirectory(ePrescriptions.Core.Helpers.AppPaths.ExportsDirectory);
        }

        public string ExportSalesReport(List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName)
        {
            string fileName = $"تقرير_المبيعات_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("تقرير المبيعات");
            ws.RightToLeft = true;

            // 1) العنوان والترويسة
            ws.Cell(1, 1).Value = $"{pharmacyName} - تقرير المبيعات والأرباح التفصيلي";
            ws.Range(1, 1, 1, 8).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 15;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(30, 27, 75); // Dark Indigo
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"الفترة من: {from:yyyy/MM/dd}  إلى: {to:yyyy/MM/dd}  |  تاريخ التصدير: {DateTime.Now:yyyy/MM/dd HH:mm}";
            ws.Range(2, 1, 2, 8).Merge();
            ws.Cell(2, 1).Style.Font.FontSize = 11;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromArgb(71, 85, 105);
            ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // 2) ترويسة الأعمدة
            string[] headers = { "رقم الفاتورة", "التاريخ والوقت", "العميل", "طريقة الدفع", "المجموع قبل الضريبة", "ضريبة القيمة المضافة", "الإجمالي النهائي", "الربح التقديري" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(79, 70, 229);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // 3) صفوف البيانات
            int row = 5;
            foreach (var item in sales)
            {
                ws.Cell(row, 1).Value = item.InvoiceNumber;
                ws.Cell(row, 2).Value = item.Date.ToString("yyyy/MM/dd HH:mm");
                ws.Cell(row, 3).Value = item.CustomerName;
                ws.Cell(row, 4).Value = item.PaymentMethodName;

                ws.Cell(row, 5).Value = item.SubtotalPiastres / 100.0;
                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 6).Value = item.VatPounds;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 7).Value = item.TotalPounds;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 8).Value = item.ProfitPounds;
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                for (int col = 1; col <= 8; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }

                row++;
            }

            // 4) سطر الإجمالي
            ws.Cell(row, 1).Value = $"الإجمالي ({sales.Count} فاتورة)";
            ws.Range(row, 1, row, 4).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, 5).FormulaA1 = $"SUM(E5:E{row - 1})";
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(row, 6).FormulaA1 = $"SUM(F5:F{row - 1})";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(row, 7).FormulaA1 = $"SUM(G5:G{row - 1})";
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(row, 8).FormulaA1 = $"SUM(H5:H{row - 1})";
            ws.Cell(row, 8).Style.Font.Bold = true;
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            var totalRange = ws.Range(row, 1, row, 8);
            totalRange.Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249);
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;

            // 5) ملخص طرق الدفع في الأسفل
            int summaryRow = row + 3;
            ws.Cell(summaryRow, 1).Value = "ملخص المبيعات وطرق الدفع للفترة:";
            ws.Cell(summaryRow, 1).Style.Font.Bold = true;

            ws.Cell(summaryRow + 1, 1).Value = "المبيعات النقدية:";
            ws.Cell(summaryRow + 1, 2).Value = summary.CashSalesPounds;
            ws.Cell(summaryRow + 1, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(summaryRow + 2, 1).Value = "مبيعات الشبكة / البطاقات:";
            ws.Cell(summaryRow + 2, 2).Value = summary.NetworkSalesPounds;
            ws.Cell(summaryRow + 2, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(summaryRow + 3, 1).Value = "المبيعات الآجلة:";
            ws.Cell(summaryRow + 3, 2).Value = summary.OnAccountSalesPounds;
            ws.Cell(summaryRow + 3, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(summaryRow + 4, 1).Value = "صافي أرباح المبيعات:";
            ws.Cell(summaryRow + 4, 2).Value = summary.NetProfitPounds;
            ws.Cell(summaryRow + 4, 2).Style.Font.Bold = true;
            ws.Cell(summaryRow + 4, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportItemMovementReport(List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName)
        {
            string fileName = $"تقرير_حركة_الأصناف_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("حركة ومبيعات الأصناف");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - تقرير حركة ومبيعات الأصناف (الأكثر طلباً وركوداً)";
            ws.Range(1, 1, 1, 7).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(15, 23, 42);
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            string[] headers = { "كود الصنف", "الباركود", "اسم الدواء / المستحضر", "الكمية المباعة (وحدة)", "المخزون الحالي", "إجمالي الإيراد", "صافي الربح" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(59, 130, 246);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = 4;
            foreach (var item in movements)
            {
                ws.Cell(row, 1).Value = item.ItemCode;
                ws.Cell(row, 2).Value = item.Barcode;
                ws.Cell(row, 3).Value = item.NameAr;
                ws.Cell(row, 4).Value = item.TotalSoldSmallestUnits;
                ws.Cell(row, 5).Value = item.CurrentStockSmallestUnits;

                ws.Cell(row, 6).Value = item.TotalRevenuePounds;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 7).Value = item.TotalProfitPounds;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                for (int col = 1; col <= 7; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportLowStockReport(List<LowStockReportRow> lowStock, string pharmacyName)
        {
            string fileName = $"تقرير_النواقص_وإعادة_الطلب_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("النواقص وإعادة الطلب");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - قائمة النواقص وحد إعادة الطلب المقترح";
            ws.Range(1, 1, 1, 7).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(185, 28, 28); // Red
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            string[] headers = { "كود الصنف", "الباركود", "اسم الدواء", "الرصيد الفعلي الحالي", "حد الطلب الأدنى", "مقدار العجز", "الكمية المقترحة للطلب" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(239, 68, 68);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = 4;
            foreach (var item in lowStock)
            {
                ws.Cell(row, 1).Value = item.ItemCode;
                ws.Cell(row, 2).Value = item.Barcode;
                ws.Cell(row, 3).Value = item.NameAr;
                ws.Cell(row, 4).Value = item.CurrentStockSmallestUnits;
                ws.Cell(row, 5).Value = item.MinStockAlertSmallestUnits;
                ws.Cell(row, 6).Value = item.DeficitUnits;
                ws.Cell(row, 7).Value = item.SuggestedOrderUnits;

                for (int col = 1; col <= 7; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportExpiryReport(List<ExpiryReportRow> expiries, string pharmacyName)
        {
            string fileName = $"تقرير_صلاحية_الأدوية_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("صلاحيات الأدوية");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - تقرير الأدوية قريبة الصلاحية والمنتهية (FEFO)";
            ws.Range(1, 1, 1, 7).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(217, 119, 6); // Amber
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            string[] headers = { "كود الصنف", "اسم الدواء", "رقم التشغيلة (Batch)", "تاريخ الانتهاء", "الأيام المتبقية", "الكمية المتبقية", "إجمالي قيمة التكلفة" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 158, 11);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = 4;
            foreach (var item in expiries)
            {
                ws.Cell(row, 1).Value = item.ItemCode;
                ws.Cell(row, 2).Value = item.NameAr;
                ws.Cell(row, 3).Value = item.BatchNumber;
                ws.Cell(row, 4).Value = item.ExpiryDate.ToString("yyyy/MM/dd");
                ws.Cell(row, 5).Value = item.DaysRemaining;
                ws.Cell(row, 6).Value = item.RemainingSmallestUnits;
                ws.Cell(row, 7).Value = item.TotalCostPounds;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                if (item.IsExpired)
                {
                    ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromArgb(254, 226, 226); // Light red
                }

                for (int col = 1; col <= 7; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportInventoryValuationReport(InventoryValuationSummary valuation, string pharmacyName)
        {
            string fileName = $"تقرير_تقييم_المخزون_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("تقييم المخزون المالي");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - التقييم المالي الشامل للمخزون";
            ws.Range(1, 1, 1, 3).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(16, 185, 129); // Emerald
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(3, 1).Value = "البند الإحصائي";
            ws.Cell(3, 2).Value = "القيمة";
            ws.Range(3, 1, 3, 2).Style.Font.Bold = true;
            ws.Range(3, 1, 3, 2).Style.Fill.BackgroundColor = XLColor.FromArgb(5, 150, 105);
            ws.Range(3, 1, 3, 2).Style.Font.FontColor = XLColor.White;

            string[] labels = {
                "إجمالي عدد الأصناف المعرفة:",
                "إجمالي عدد التشغيلات (Batches) النشطة:",
                "إجمالي عدد الوحدات الصغرى المتوفرة:",
                "إجمالي قيمة المخزون بسعر الشراء (التكلفة):",
                "إجمالي قيمة المخزون بسعر البيع (التجزئة):",
                "صافي الأرباح المتوقعة عند البيع الكامل:"
            };

            object[] values = {
                valuation.TotalItemsCount,
                valuation.TotalBatchesCount,
                valuation.TotalSmallestUnitsCount,
                valuation.TotalValuationCostPounds,
                valuation.TotalValuationRetailPounds,
                valuation.ExpectedProfitPounds
            };

            for (int i = 0; i < labels.Length; i++)
            {
                int r = 4 + i;
                ws.Cell(r, 1).Value = labels[i];
                ws.Cell(r, 2).Value = Convert.ToDouble(values[i]);
                if (i >= 3)
                {
                    ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";
                    ws.Cell(r, 2).Style.Font.Bold = true;
                }
                ws.Cell(r, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(r, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }
    }
}
