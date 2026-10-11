using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class ReportRepository
    {
        private readonly DatabaseContext _dbContext;

        public ReportRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// تقرير فواتير المبيعات التفصيلي مع فلترة التاريخ وطريقة السداد
        /// </summary>
        public List<SalesReportRow> GetSalesReport(DateTime fromDate, DateTime toDate, PaymentMethod? paymentMethod = null)
        {
            var list = new List<SalesReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            string methodFilter = paymentMethod.HasValue ? "AND inv.PaymentMethod = @pMethod" : "";

            cmd.CommandText = $@"
                SELECT inv.Id, inv.InvoiceNumber, inv.Date, inv.CustomerName, inv.PaymentMethod,
                       inv.SubtotalPiastres, inv.DiscountPiastres, inv.VatPiastres, inv.TotalPiastres,
                       IFNULL(SUM(it.SmallestUnitsCount * (i.CostPriceBoxPiastres / MAX(1, i.StripsPerBox * i.PiecesPerStrip))), 0) AS EstimatedCostPiastres
                FROM Invoices inv
                LEFT JOIN InvoiceItems it ON inv.Id = it.InvoiceId
                LEFT JOIN Items i ON it.ItemId = i.Id
                WHERE inv.Type = 1
                  AND IFNULL(inv.Status, 1) != 2
                  AND date(inv.Date) >= date(@from)
                  AND date(inv.Date) <= date(@to)
                  {methodFilter}
                GROUP BY inv.Id
                ORDER BY datetime(inv.Date) DESC;";

            cmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd"));
            if (paymentMethod.HasValue)
            {
                cmd.Parameters.AddWithValue("@pMethod", (int)paymentMethod.Value);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SalesReportRow
                {
                    InvoiceId = Convert.ToInt32(reader["Id"]),
                    InvoiceNumber = reader["InvoiceNumber"].ToString() ?? string.Empty,
                    Date = DateTime.Parse(reader["Date"].ToString() ?? DateTime.Now.ToString("s")),
                    CustomerName = reader["CustomerName"].ToString() ?? string.Empty,
                    PaymentMethod = (PaymentMethod)Convert.ToInt32(reader["PaymentMethod"]),
                    SubtotalPiastres = Convert.ToInt64(reader["SubtotalPiastres"]),
                    DiscountPiastres = Convert.ToInt64(reader["DiscountPiastres"]),
                    VatPiastres = Convert.ToInt64(reader["VatPiastres"]),
                    TotalPiastres = Convert.ToInt64(reader["TotalPiastres"]),
                    EstimatedCostPiastres = Convert.ToInt64(reader["EstimatedCostPiastres"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير ملخص المبيعات المالي للفترة
        /// </summary>
        public SalesPeriodSummary GetSalesPeriodSummary(DateTime fromDate, DateTime toDate)
        {
            var summary = new SalesPeriodSummary();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT 
                    COUNT(inv.Id) AS InvoicesCount,
                    IFNULL(SUM(inv.TotalPiastres), 0) AS TotalSales,
                    IFNULL(SUM(inv.VatPiastres), 0) AS TotalVat,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 1 THEN inv.TotalPiastres ELSE 0 END), 0) AS CashSales,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 2 THEN inv.TotalPiastres ELSE 0 END), 0) AS NetworkSales,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 3 THEN inv.TotalPiastres ELSE 0 END), 0) AS OnAccountSales,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 4 THEN inv.TotalPiastres ELSE 0 END), 0) AS InsuranceSales,
                    (
                        SELECT IFNULL(SUM(it.SmallestUnitsCount * (i.CostPriceBoxPiastres / MAX(1, i.StripsPerBox * i.PiecesPerStrip))), 0)
                        FROM InvoiceItems it
                        JOIN Invoices inv2 ON it.InvoiceId = inv2.Id
                        JOIN Items i ON it.ItemId = i.Id
                        WHERE inv2.Type = 1
                          AND IFNULL(inv2.Status, 1) != 2
                          AND date(inv2.Date) >= date(@from)
                          AND date(inv2.Date) <= date(@to)
                    ) AS TotalCost
                FROM Invoices inv
                WHERE inv.Type = 1
                  AND IFNULL(inv.Status, 1) != 2
                  AND date(inv.Date) >= date(@from)
                  AND date(inv.Date) <= date(@to);";

            cmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                summary.TotalInvoicesCount = Convert.ToInt32(reader["InvoicesCount"]);
                summary.TotalSalesPiastres = Convert.ToInt64(reader["TotalSales"]);
                summary.TotalVatPiastres = Convert.ToInt64(reader["TotalVat"]);
                summary.CashSalesPiastres = Convert.ToInt64(reader["CashSales"]);
                summary.NetworkSalesPiastres = Convert.ToInt64(reader["NetworkSales"]);
                summary.OnAccountSalesPiastres = Convert.ToInt64(reader["OnAccountSales"]);
                summary.InsuranceSalesPiastres = Convert.ToInt64(reader["InsuranceSales"]);
                summary.TotalCostPiastres = Convert.ToInt64(reader["TotalCost"]);
            }

            return summary;
        }

        /// <summary>
        /// تقرير حركة الأصناف والأكثر مبيعاً
        /// </summary>
        public List<ItemMovementReportRow> GetItemMovementReport(DateTime fromDate, DateTime toDate)
        {
            var list = new List<ItemMovementReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT i.Id, i.Code, i.Barcode, i.NameAr, i.TotalStockSmallestUnits,
                       IFNULL(SUM(it.SmallestUnitsCount), 0) AS SoldUnits,
                       IFNULL(SUM(it.TotalPiastres), 0) AS TotalRevenue,
                       IFNULL(SUM(it.SmallestUnitsCount * (i.CostPriceBoxPiastres / MAX(1, i.StripsPerBox * i.PiecesPerStrip))), 0) AS TotalCost
                FROM Items i
                LEFT JOIN InvoiceItems it ON i.Id = it.ItemId
                LEFT JOIN Invoices inv ON it.InvoiceId = inv.Id 
                     AND inv.Type = 1
                     AND IFNULL(inv.Status, 1) != 2
                     AND date(inv.Date) >= date(@from)
                     AND date(inv.Date) <= date(@to)
                WHERE i.IsActive = 1
                GROUP BY i.Id
                ORDER BY SoldUnits DESC, TotalRevenue DESC;";

            cmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ItemMovementReportRow
                {
                    ItemId = Convert.ToInt32(reader["Id"]),
                    ItemCode = reader["Code"].ToString() ?? string.Empty,
                    Barcode = reader["Barcode"].ToString() ?? string.Empty,
                    NameAr = reader["NameAr"].ToString() ?? string.Empty,
                    CurrentStockSmallestUnits = Convert.ToInt32(reader["TotalStockSmallestUnits"]),
                    TotalSoldSmallestUnits = Convert.ToInt32(reader["SoldUnits"]),
                    TotalRevenuePiastres = Convert.ToInt64(reader["TotalRevenue"]),
                    TotalCostPiastres = Convert.ToInt64(reader["TotalCost"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير النواقص وحد إعادة الطلب
        /// </summary>
        public List<LowStockReportRow> GetLowStockReport()
        {
            var list = new List<LowStockReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT Id, Code, Barcode, NameAr, TotalStockSmallestUnits,
                       MinStockAlertSmallestUnits, RetailPriceBoxPiastres
                FROM Items
                WHERE IsActive = 1 AND TotalStockSmallestUnits <= MinStockAlertSmallestUnits
                ORDER BY TotalStockSmallestUnits ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new LowStockReportRow
                {
                    ItemId = Convert.ToInt32(reader["Id"]),
                    ItemCode = reader["Code"].ToString() ?? string.Empty,
                    Barcode = reader["Barcode"].ToString() ?? string.Empty,
                    NameAr = reader["NameAr"].ToString() ?? string.Empty,
                    CurrentStockSmallestUnits = Convert.ToInt32(reader["TotalStockSmallestUnits"]),
                    MinStockAlertSmallestUnits = Convert.ToInt32(reader["MinStockAlertSmallestUnits"]),
                    RetailPriceBoxPiastres = Convert.ToInt64(reader["RetailPriceBoxPiastres"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير الأدوية قريبة الصلاحية والمنتهية
        /// </summary>
        public List<ExpiryReportRow> GetNearExpiryReport(int days = 90)
        {
            var list = new List<ExpiryReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            string thresholdDate = DateTime.Today.AddDays(days).ToString("yyyy-MM-dd");

            cmd.CommandText = @"
                SELECT b.Id, b.ItemId, i.Code, i.NameAr, b.BatchNumber, b.ExpiryDate,
                       b.RemainingSmallestUnits, b.CostPricePerSmallestUnitPiastres
                FROM ItemBatches b
                JOIN Items i ON b.ItemId = i.Id
                WHERE b.RemainingSmallestUnits > 0
                  AND date(b.ExpiryDate) <= date(@thresh)
                ORDER BY date(b.ExpiryDate) ASC;";

            cmd.Parameters.AddWithValue("@thresh", thresholdDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ExpiryReportRow
                {
                    ItemId = Convert.ToInt32(reader["ItemId"]),
                    ItemCode = reader["Code"].ToString() ?? string.Empty,
                    NameAr = reader["NameAr"].ToString() ?? string.Empty,
                    BatchNumber = reader["BatchNumber"].ToString() ?? string.Empty,
                    ExpiryDate = DateTime.Parse(reader["ExpiryDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd")),
                    RemainingSmallestUnits = Convert.ToInt32(reader["RemainingSmallestUnits"]),
                    CostPricePerSmallestUnitPiastres = Convert.ToInt64(reader["CostPricePerSmallestUnitPiastres"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير تقييم المخزون المالي
        /// </summary>
        public InventoryValuationSummary GetInventoryValuation()
        {
            var summary = new InventoryValuationSummary();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT 
                    (SELECT COUNT(*) FROM Items WHERE IsActive = 1) AS ItemsCount,
                    (SELECT COUNT(*) FROM ItemBatches WHERE RemainingSmallestUnits > 0) AS BatchesCount,
                    (SELECT IFNULL(SUM(TotalStockSmallestUnits), 0) FROM Items WHERE IsActive = 1) AS TotalUnits,
                    (
                        SELECT IFNULL(SUM(RemainingSmallestUnits * CostPricePerSmallestUnitPiastres), 0)
                        FROM ItemBatches
                        WHERE RemainingSmallestUnits > 0
                    ) AS TotalCostValuation,
                    (
                        SELECT IFNULL(SUM(TotalStockSmallestUnits * (RetailPriceBoxPiastres / MAX(1, StripsPerBox * PiecesPerStrip))), 0)
                        FROM Items
                        WHERE IsActive = 1
                    ) AS TotalRetailValuation;";

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                summary.TotalItemsCount = Convert.ToInt32(reader["ItemsCount"]);
                summary.TotalBatchesCount = Convert.ToInt32(reader["BatchesCount"]);
                summary.TotalSmallestUnitsCount = Convert.ToInt32(reader["TotalUnits"]);
                summary.TotalValuationCostPiastres = Convert.ToInt64(reader["TotalCostValuation"]);
                summary.TotalValuationRetailPiastres = Convert.ToInt64(reader["TotalRetailValuation"]);
            }

            return summary;
        }
    }
}
