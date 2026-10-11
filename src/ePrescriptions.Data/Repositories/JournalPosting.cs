using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    /// <summary>أسماء الحسابات المستخدمة في القيود التلقائية</summary>
    public static class Accounts
    {
        public const string Cash = "الصندوق / الخزينة";
        public const string Network = "البنك / حساب الشبكة";
        public const string InsuranceReceivable = "مستحقات شركات التأمين";
        public const string Customers = "العملاء (ذمم مدينة)";
        public const string Suppliers = "الموردون (ذمم دائنة)";
        public const string Inventory = "المخزون";
        public const string SalesRevenue = "إيرادات المبيعات";
        public const string SalesReturns = "مردودات المبيعات";
        public const string VatPayable = "ضريبة القيمة المضافة";
    }

    /// <summary>
    /// توليد قيود اليومية داخل نفس الـ Transaction التي تحفظ المستند. جدول JournalEntries كل صف فيه طرف مدين واحد
    /// وطرف دائن واحد، فالقيد متعدد الأطراف بيتفكّك لصفوف (مجموع المدين = مجموع الدائن دائماً).
    /// </summary>
    public static class JournalPosting
    {
        public class Leg
        {
            public string Account { get; }
            public long Amount { get; }
            public Leg(string account, long amount) { Account = account; Amount = amount; }
        }

        public class Line
        {
            public string Debit = string.Empty;
            public string Credit = string.Empty;
            public long Amount;
        }

        /// <summary>يوزّع أطراف المدين على أطراف الدائن بالتتابع (المجموعان لازم يتساويا)</summary>
        public static List<Line> Allocate(IList<Leg> debits, IList<Leg> credits)
        {
            long d = 0, c = 0;
            foreach (var x in debits) d += x.Amount;
            foreach (var x in credits) c += x.Amount;
            if (d != c) throw new InvalidOperationException($"القيد غير متوازن: المدين {d} والدائن {c}");

            var lines = new List<Line>();
            int i = 0, j = 0;
            long di = debits.Count > 0 ? debits[0].Amount : 0, cj = credits.Count > 0 ? credits[0].Amount : 0;
            while (i < debits.Count && j < credits.Count)
            {
                if (di <= 0) { i++; if (i < debits.Count) di = debits[i].Amount; continue; }
                if (cj <= 0) { j++; if (j < credits.Count) cj = credits[j].Amount; continue; }
                long take = Math.Min(di, cj);
                lines.Add(new Line { Debit = debits[i].Account, Credit = credits[j].Account, Amount = take });
                di -= take; cj -= take;
            }
            return lines;
        }

        public static void Post(SQLiteConnection conn, SQLiteTransaction trans, string baseNumber, DateTime date,
            string description, IList<Leg> debits, IList<Leg> credits)
        {
            var lines = Allocate(debits, credits);
            int n = 0;
            foreach (var l in lines)
            {
                n++;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO JournalEntries (EntryNumber, Date, Description, DebitAccount, CreditAccount, AmountPiastres, CreatedAt)
                    VALUES (@num, @date, @desc, @deb, @crd, @amt, datetime('now'));";
                cmd.Parameters.AddWithValue("@num", $"JV-{baseNumber}-{n:D2}");
                cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@deb", l.Debit);
                cmd.Parameters.AddWithValue("@crd", l.Credit);
                cmd.Parameters.AddWithValue("@amt", l.Amount);
                cmd.ExecuteNonQuery();
            }
        }

        private static string PaymentAccount(PaymentMethod m) => m switch
        {
            PaymentMethod.Network => Accounts.Network,
            PaymentMethod.Insurance => Accounts.InsuranceReceivable,
            _ => Accounts.Cash
        };

        /// <summary>
        /// بيع: مدين نقدية/شبكة/تأمين (المدفوع) + عملاء (المتبقي الآجل)، دائن إيرادات (الصافي بدون الضريبة) + ضريبة.
        /// مرتجع: العكس (مردودات المبيعات + الضريبة مدين، نقدية/عملاء دائن).
        /// </summary>
        public static void PostSaleInvoice(SQLiteConnection conn, SQLiteTransaction trans, Invoice inv)
        {
            long total = inv.TotalPiastres;
            if (total <= 0) return;
            long vat = Math.Max(0, Math.Min(inv.VatPiastres, total));
            long net = total - vat;
            long receivable = Math.Max(0, Math.Min(inv.RemainingPiastres, total));
            if (inv.PaymentMethod == PaymentMethod.Insurance) receivable = 0;
            long paid = total - receivable;

            var settle = new List<Leg>();
            if (paid > 0) settle.Add(new Leg(PaymentAccount(inv.PaymentMethod), paid));
            if (receivable > 0) settle.Add(new Leg(Accounts.Customers, receivable));

            var revenue = new List<Leg>();
            if (net > 0) revenue.Add(new Leg(inv.Type == InvoiceType.Return ? Accounts.SalesReturns : Accounts.SalesRevenue, net));
            if (vat > 0) revenue.Add(new Leg(Accounts.VatPayable, vat));

            if (inv.Type == InvoiceType.Return)
                Post(conn, trans, inv.InvoiceNumber, inv.Date, $"مرتجع مبيعات رقم {inv.InvoiceNumber}", revenue, settle);
            else
                Post(conn, trans, inv.InvoiceNumber, inv.Date, $"فاتورة مبيعات رقم {inv.InvoiceNumber}", settle, revenue);
        }

        /// <summary>مشتريات: مدين مخزون (الإجمالي شامل الضريبة)، دائن نقدية (المدفوع) + موردين (المتبقي)</summary>
        public static void PostPurchaseInvoice(SQLiteConnection conn, SQLiteTransaction trans, PurchaseInvoice inv)
        {
            long total = inv.TotalPiastres;
            if (total <= 0) return;
            long payable = Math.Max(0, Math.Min(inv.RemainingPiastres, total));
            long paid = total - payable;

            var credits = new List<Leg>();
            if (paid > 0) credits.Add(new Leg(Accounts.Cash, paid));
            if (payable > 0) credits.Add(new Leg(Accounts.Suppliers, payable));

            Post(conn, trans, "PUR-" + inv.InvoiceNumber, inv.InvoiceDate, $"فاتورة مشتريات رقم {inv.InvoiceNumber} - {inv.SupplierName}",
                new List<Leg> { new Leg(Accounts.Inventory, total) }, credits);
        }
    }
}
