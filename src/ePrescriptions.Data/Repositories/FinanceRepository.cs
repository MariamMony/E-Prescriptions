using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Data.Repositories
{
    public class FinanceRepository
    {
        private readonly DatabaseContext _dbContext;

        public FinanceRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public string GenerateNextExpenseNumber(int? year = null)
        {
            int targetYear = year ?? DateTime.Now.Year;
            string prefix = "EXP";
            string pattern = $"{prefix}-{targetYear}-%";
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ExpenseNumber FROM Expenses WHERE ExpenseNumber LIKE @pattern;";
            cmd.Parameters.AddWithValue("@pattern", pattern);
            int maxSeq = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string num = r[0]?.ToString() ?? "";
                var parts = num.Split('-');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int seq))
                {
                    if (seq > maxSeq) maxSeq = seq;
                }
            }
            return $"{prefix}-{targetYear}-{(maxSeq + 1):D5}";
        }

        public string GenerateNextJournalEntryNumber(int? year = null)
        {
            int targetYear = year ?? DateTime.Now.Year;
            string prefix = "JV";
            string pattern = $"{prefix}-{targetYear}-%";
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EntryNumber FROM JournalEntries WHERE EntryNumber LIKE @pattern;";
            cmd.Parameters.AddWithValue("@pattern", pattern);
            int maxSeq = 0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string num = r[0]?.ToString() ?? "";
                var parts = num.Split('-');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int seq))
                {
                    if (seq > maxSeq) maxSeq = seq;
                }
            }
            return $"{prefix}-{targetYear}-{(maxSeq + 1):D5}";
        }

        public void AddExpense(Expense expense)
        {
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO Expenses (ExpenseNumber, Category, AmountPiastres, Date, PaidTo, Notes, CreatedAt)
                    VALUES (@num, @cat, @amt, @date, @paidTo, @notes, datetime('now'));";
                cmd.Parameters.AddWithValue("@num", expense.ExpenseNumber);
                cmd.Parameters.AddWithValue("@cat", expense.Category);
                cmd.Parameters.AddWithValue("@amt", expense.AmountPiastres);
                cmd.Parameters.AddWithValue("@date", expense.Date.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@paidTo", expense.PaidTo);
                cmd.Parameters.AddWithValue("@notes", (object?)expense.Notes ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                // تسجيل قيد يومي تلقائي: من حـ/ المصروفات إلى حـ/ الصندوق
                using var cmdJv = conn.CreateCommand();
                cmdJv.Transaction = trans;
                cmdJv.CommandText = @"
                    INSERT INTO JournalEntries (EntryNumber, Date, Description, DebitAccount, CreditAccount, AmountPiastres, CreatedAt)
                    VALUES (@jNum, @date, @desc, @deb, @crd, @amt, datetime('now'));";
                cmdJv.Parameters.AddWithValue("@jNum", $"JV-{expense.ExpenseNumber}");
                cmdJv.Parameters.AddWithValue("@date", expense.Date.ToString("yyyy-MM-dd HH:mm:ss"));
                cmdJv.Parameters.AddWithValue("@desc", $"إثبات مصروف: {expense.Category} - {expense.PaidTo}");
                cmdJv.Parameters.AddWithValue("@deb", $"مصروفات: {expense.Category}");
                cmdJv.Parameters.AddWithValue("@crd", "الصندوق / الخزينة");
                cmdJv.Parameters.AddWithValue("@amt", expense.AmountPiastres);
                cmdJv.ExecuteNonQuery();

                trans.Commit();
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "AddExpense");
                throw;
            }
        }

        public List<Expense> GetExpenses(DateTime from, DateTime to)
        {
            var list = new List<Expense>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ExpenseNumber, Category, AmountPiastres, Date, PaidTo, Notes, CreatedAt
                FROM Expenses
                WHERE date(Date) >= date(@from) AND date(Date) <= date(@to)
                ORDER BY Date DESC;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Expense
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    ExpenseNumber = reader["ExpenseNumber"].ToString() ?? string.Empty,
                    Category = reader["Category"].ToString() ?? string.Empty,
                    AmountPiastres = Convert.ToInt64(reader["AmountPiastres"]),
                    Date = DateTime.Parse(reader["Date"].ToString() ?? DateTime.Now.ToString("s")),
                    PaidTo = reader["PaidTo"].ToString() ?? string.Empty,
                    Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString()
                });
            }
            return list;
        }

        public List<JournalEntry> GetJournalEntries(DateTime from, DateTime to)
        {
            var list = new List<JournalEntry>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, EntryNumber, Date, Description, DebitAccount, CreditAccount, AmountPiastres, CreatedAt
                FROM JournalEntries
                WHERE date(Date) >= date(@from) AND date(Date) <= date(@to)
                ORDER BY Date DESC;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new JournalEntry
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    EntryNumber = reader["EntryNumber"].ToString() ?? string.Empty,
                    Date = DateTime.Parse(reader["Date"].ToString() ?? DateTime.Now.ToString("s")),
                    Description = reader["Description"].ToString() ?? string.Empty,
                    DebitAccount = reader["DebitAccount"].ToString() ?? string.Empty,
                    CreditAccount = reader["CreditAccount"].ToString() ?? string.Empty,
                    AmountPiastres = Convert.ToInt64(reader["AmountPiastres"])
                });
            }
            return list;
        }

        public VatReturnStatement GetVatReturnReport(DateTime from, DateTime to)
        {
            var vatReport = new VatReturnStatement
            {
                PeriodStart = from,
                PeriodEnd = to
            };

            using var conn = _dbContext.CreateConnection();

            // 1) مبيعات الفترة ومخرجات الضريبة
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(SubtotalPiastres), 0) AS TotalSales,
                           COALESCE(SUM(VatPiastres), 0) AS TotalSalesVat
                    FROM Invoices
                    WHERE Type = 1 AND date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    vatReport.TaxableSalesPiastres = Convert.ToInt64(reader["TotalSales"]);
                    vatReport.OutputVatPiastres = Convert.ToInt64(reader["TotalSalesVat"]);
                }
            }

            // 2) مشتريات الفترة ومدخلات الضريبة
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(SubtotalPiastres), 0) AS TotalPurchases,
                           COALESCE(SUM(VatPiastres), 0) AS TotalPurchasesVat
                    FROM PurchaseInvoices
                    WHERE date(InvoiceDate) >= date(@from) AND date(InvoiceDate) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    vatReport.TaxablePurchasesPiastres = Convert.ToInt64(reader["TotalPurchases"]);
                    vatReport.InputVatPiastres = Convert.ToInt64(reader["TotalPurchasesVat"]);
                }
            }

            return vatReport;
        }

        public CashFlowSummary GetCashFlowSummary(DateTime from, DateTime to)
        {
            var summary = new CashFlowSummary
            {
                PeriodStart = from,
                PeriodEnd = to
            };

            using var conn = _dbContext.CreateConnection();

            // المقبوضات: مبيعات نقدية
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(TotalPiastres), 0) FROM Invoices
                    WHERE PaymentMethod = 1 AND date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
                summary.CashSalesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // المقبوضات: سندات قبض عملاء
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(AmountPiastres), 0) FROM CustomerVouchers
                    WHERE date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
                summary.CustomerReceiptsPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // المدفوعات: سندات صرف موردين
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(AmountPiastres), 0) FROM SupplierVouchers
                    WHERE date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
                summary.SupplierPaymentsPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // المدفوعات: مصروفات تشغيلية
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(AmountPiastres), 0) FROM Expenses
                    WHERE date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
                summary.OperatingExpensesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            return summary;
        }

        public ZakatCalculation CalculateZakat(bool evaluateAtCost, long goldGramPricePiastres, bool isHijri)
        {
            var zakat = new ZakatCalculation
            {
                EvaluateAtCost = evaluateAtCost,
                GoldGramPricePiastres = goldGramPricePiastres,
                IsHijriYear = isHijri
            };

            using var conn = _dbContext.CreateConnection();

            // 1) تقييم عروض التجارة (المخزون)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT TotalStockSmallestUnits, StripsPerBox, PiecesPerStrip,
                           RetailPriceBoxPiastres, CostPriceBoxPiastres
                    FROM Items WHERE IsActive = 1;";

                using var reader = cmd.ExecuteReader();
                long totalStockValue = 0;
                while (reader.Read())
                {
                    int totalUnits = Convert.ToInt32(reader["TotalStockSmallestUnits"]);
                    int strips = Convert.ToInt32(reader["StripsPerBox"]);
                    int pieces = Convert.ToInt32(reader["PiecesPerStrip"]);
                    long boxPrice = evaluateAtCost 
                        ? Convert.ToInt64(reader["CostPriceBoxPiastres"]) 
                        : Convert.ToInt64(reader["RetailPriceBoxPiastres"]);

                    int unitsPerBox = Math.Max(1, strips * pieces);
                    if (totalUnits > 0 && boxPrice > 0)
                    {
                        totalStockValue += (totalUnits * boxPrice) / unitsPerBox;
                    }
                }
                zakat.StockValuePiastres = totalStockValue;
            }

            // 2) النقدية الحاضرة (من رصيد الصندوق بالورديات)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(ExpectedCashPiastres), 0) FROM Shifts WHERE Status = 1;";
                zakat.CashAndBankPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // 3) الديون المرجوة على العملاء
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(CurrentBalancePiastres), 0) FROM Customers WHERE IsActive = 1;";
                zakat.GoodReceivablesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // 4) الالتزامات الحالة للموردين
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(BalancePiastres), 0) FROM Suppliers WHERE IsActive = 1;";
                zakat.DuePayablesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            return zakat;
        }
    }
}
