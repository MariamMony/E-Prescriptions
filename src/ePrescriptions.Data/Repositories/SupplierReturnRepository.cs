using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Data.Repositories
{
    /// <summary>
    /// مرتجع الأدوية المنتهية أو القريبة من الانتهاء للشركة (بخصم نسبة) أو إتلافها.
    /// الحفظ ذري: خصم الكمية من التشغيلة ومن رصيد الصنف + تسجيل المستند + تخفيض رصيد المورد بقيمة الاسترداد.
    /// </summary>
    public class SupplierReturnRepository
    {
        private readonly DatabaseContext _dbContext;

        public SupplierReturnRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>قيمة الاسترداد بعد خصم النسبة (تقريب Half-Up بالقروش)</summary>
        public static long CalculateCredit(long costTotalPiastres, int deductionPercent)
        {
            if (costTotalPiastres <= 0) return 0;
            int pct = Math.Max(0, Math.Min(100, deductionPercent));
            return (costTotalPiastres * (100 - pct) + 50) / 100;
        }

        public bool Save(SupplierReturn ret, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (ret.Items.Count == 0)
            {
                errorMessage = "أضف تشغيلة واحدة على الأقل.";
                return false;
            }
            if (ret.Kind == SupplierReturnKind.ReturnToSupplier && (!ret.SupplierId.HasValue || ret.SupplierId.Value <= 0))
            {
                errorMessage = "اختار الشركة (المورد) اللي هترجعلها الأدوية.";
                return false;
            }

            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                long totalCost = 0;
                long totalCredit = 0;

                // فحص وحساب كل بند من بيانات التشغيلة الفعلية (لا نثق في أرقام الواجهة)
                foreach (var line in ret.Items)
                {
                    if (line.SmallestUnitsCount <= 0)
                        throw new InvalidOperationException($"كمية غير صحيحة للصنف {line.ItemNameAr}.");

                    using var bCmd = conn.CreateCommand();
                    bCmd.Transaction = trans;
                    bCmd.CommandText = @"
                        SELECT b.ItemId, b.BatchNumber, b.ExpiryDate, b.RemainingSmallestUnits, b.CostPricePerSmallestUnitPiastres,
                               i.Code, i.NameAr
                        FROM ItemBatches b JOIN Items i ON i.Id = b.ItemId
                        WHERE b.Id = @id;";
                    bCmd.Parameters.AddWithValue("@id", line.BatchId);
                    using var r = bCmd.ExecuteReader();
                    if (!r.Read())
                        throw new InvalidOperationException($"التشغيلة المختارة للصنف {line.ItemNameAr} غير موجودة.");

                    int remaining = Convert.ToInt32(r["RemainingSmallestUnits"]);
                    if (line.SmallestUnitsCount > remaining)
                        throw new InvalidOperationException(
                            $"الكمية المطلوب إرجاعها من {r["NameAr"]} (تشغيلة {r["BatchNumber"]}) أكبر من المتاح بالتشغيلة ({remaining}).");

                    line.ItemId = Convert.ToInt32(r["ItemId"]);
                    line.ItemCode = r["Code"].ToString() ?? string.Empty;
                    line.ItemNameAr = r["NameAr"].ToString() ?? string.Empty;
                    line.BatchNumber = r["BatchNumber"].ToString() ?? string.Empty;
                    line.ExpiryDate = DateTime.Parse(r["ExpiryDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd"));
                    line.CostPerUnitPiastres = Convert.ToInt64(r["CostPricePerSmallestUnitPiastres"]);

                    line.DeductionPercent = ret.Kind == SupplierReturnKind.Destroy ? 100 : Math.Max(0, Math.Min(100, line.DeductionPercent));
                    line.CostTotalPiastres = line.CostPerUnitPiastres * line.SmallestUnitsCount;
                    line.CreditPiastres = CalculateCredit(line.CostTotalPiastres, line.DeductionPercent);

                    totalCost += line.CostTotalPiastres;
                    totalCredit += line.CreditPiastres;
                }

                ret.TotalCostPiastres = totalCost;
                ret.CreditPiastres = totalCredit;
                ret.LossPiastres = totalCost - totalCredit;

                // رقم المستند داخل نفس الـ Transaction لتفادي التكرار
                using (var numCmd = conn.CreateCommand())
                {
                    numCmd.Transaction = trans;
                    numCmd.CommandText = "SELECT IFNULL(MAX(Id), 0) + 1 FROM SupplierReturns;";
                    long next = Convert.ToInt64(numCmd.ExecuteScalar() ?? 1);
                    string prefix = ret.Kind == SupplierReturnKind.Destroy ? "DST" : "SRT";
                    ret.ReturnNumber = $"{prefix}-{DateTime.Now.Year}-{next:D5}";
                }

                string supplierName = ret.SupplierName ?? string.Empty;
                if (ret.SupplierId.HasValue && ret.SupplierId.Value > 0 && string.IsNullOrWhiteSpace(supplierName))
                {
                    using var sCmd = conn.CreateCommand();
                    sCmd.Transaction = trans;
                    sCmd.CommandText = "SELECT Name FROM Suppliers WHERE Id = @id;";
                    sCmd.Parameters.AddWithValue("@id", ret.SupplierId.Value);
                    supplierName = sCmd.ExecuteScalar()?.ToString() ?? string.Empty;
                }
                ret.SupplierName = supplierName;

                using (var hCmd = conn.CreateCommand())
                {
                    hCmd.Transaction = trans;
                    hCmd.CommandText = @"
                        INSERT INTO SupplierReturns (
                            ReturnNumber, Kind, SupplierId, SupplierName, Date,
                            TotalCostPiastres, CreditPiastres, LossPiastres, Reason, Notes, UserId, UserName, CreatedAt
                        ) VALUES (
                            @num, @kind, @sId, @sName, @date,
                            @cost, @credit, @loss, @reason, @notes, @uId, @uName, @created
                        );
                        SELECT last_insert_rowid();";
                    hCmd.Parameters.AddWithValue("@num", ret.ReturnNumber);
                    hCmd.Parameters.AddWithValue("@kind", (int)ret.Kind);
                    hCmd.Parameters.AddWithValue("@sId", ret.Kind == SupplierReturnKind.ReturnToSupplier && ret.SupplierId.HasValue ? (object)ret.SupplierId.Value : DBNull.Value);
                    hCmd.Parameters.AddWithValue("@sName", ret.SupplierName);
                    hCmd.Parameters.AddWithValue("@date", ret.Date.ToString("yyyy-MM-dd HH:mm:ss"));
                    hCmd.Parameters.AddWithValue("@cost", ret.TotalCostPiastres);
                    hCmd.Parameters.AddWithValue("@credit", ret.CreditPiastres);
                    hCmd.Parameters.AddWithValue("@loss", ret.LossPiastres);
                    hCmd.Parameters.AddWithValue("@reason", ret.Reason ?? string.Empty);
                    hCmd.Parameters.AddWithValue("@notes", (object?)ret.Notes ?? DBNull.Value);
                    hCmd.Parameters.AddWithValue("@uId", ret.UserId);
                    hCmd.Parameters.AddWithValue("@uName", ret.UserName ?? string.Empty);
                    hCmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("s"));
                    ret.Id = Convert.ToInt32(hCmd.ExecuteScalar());
                }

                foreach (var line in ret.Items)
                {
                    line.ReturnId = ret.Id;

                    using var iCmd = conn.CreateCommand();
                    iCmd.Transaction = trans;
                    iCmd.CommandText = @"
                        INSERT INTO SupplierReturnItems (
                            ReturnId, ItemId, ItemCode, ItemNameAr, BatchId, BatchNumber, ExpiryDate,
                            SmallestUnitsCount, CostPerUnitPiastres, DeductionPercent, CostTotalPiastres, CreditPiastres
                        ) VALUES (
                            @rId, @itemId, @code, @name, @bId, @batch, @exp,
                            @units, @cpu, @pct, @cost, @credit
                        );";
                    iCmd.Parameters.AddWithValue("@rId", ret.Id);
                    iCmd.Parameters.AddWithValue("@itemId", line.ItemId);
                    iCmd.Parameters.AddWithValue("@code", line.ItemCode);
                    iCmd.Parameters.AddWithValue("@name", line.ItemNameAr);
                    iCmd.Parameters.AddWithValue("@bId", line.BatchId);
                    iCmd.Parameters.AddWithValue("@batch", line.BatchNumber);
                    iCmd.Parameters.AddWithValue("@exp", line.ExpiryDate.ToString("yyyy-MM-dd"));
                    iCmd.Parameters.AddWithValue("@units", line.SmallestUnitsCount);
                    iCmd.Parameters.AddWithValue("@cpu", line.CostPerUnitPiastres);
                    iCmd.Parameters.AddWithValue("@pct", line.DeductionPercent);
                    iCmd.Parameters.AddWithValue("@cost", line.CostTotalPiastres);
                    iCmd.Parameters.AddWithValue("@credit", line.CreditPiastres);
                    iCmd.ExecuteNonQuery();

                    // خصم الكمية من التشغيلة ومن إجمالي رصيد الصنف
                    using var bUpd = conn.CreateCommand();
                    bUpd.Transaction = trans;
                    bUpd.CommandText = "UPDATE ItemBatches SET RemainingSmallestUnits = RemainingSmallestUnits - @u WHERE Id = @id;";
                    bUpd.Parameters.AddWithValue("@u", line.SmallestUnitsCount);
                    bUpd.Parameters.AddWithValue("@id", line.BatchId);
                    bUpd.ExecuteNonQuery();

                    using var tUpd = conn.CreateCommand();
                    tUpd.Transaction = trans;
                    tUpd.CommandText = "UPDATE Items SET TotalStockSmallestUnits = TotalStockSmallestUnits - @u WHERE Id = @id;";
                    tUpd.Parameters.AddWithValue("@u", line.SmallestUnitsCount);
                    tUpd.Parameters.AddWithValue("@id", line.ItemId);
                    tUpd.ExecuteNonQuery();
                }

                // قيمة الاسترداد تخفض ما نحن مدينون به للمورد (قد يصبح رصيده بالسالب = له عندنا مستحقات تُسوّى لاحقاً)
                if (ret.Kind == SupplierReturnKind.ReturnToSupplier && ret.SupplierId.HasValue && ret.CreditPiastres > 0)
                {
                    using var supCmd = conn.CreateCommand();
                    supCmd.Transaction = trans;
                    supCmd.CommandText = "UPDATE Suppliers SET BalancePiastres = BalancePiastres - @c WHERE Id = @id;";
                    supCmd.Parameters.AddWithValue("@c", ret.CreditPiastres);
                    supCmd.Parameters.AddWithValue("@id", ret.SupplierId.Value);
                    supCmd.ExecuteNonQuery();
                }

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                errorMessage = ex.Message;
                if (ex is not InvalidOperationException)
                    Logger.LogException(ex, "SupplierReturnRepository.Save");
                return false;
            }
        }

        /// <summary>آخر المرتجعات/الإتلافات (للعرض في الشاشة)</summary>
        public List<SupplierReturn> GetRecent(int limit = 50)
        {
            var list = new List<SupplierReturn>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ReturnNumber, Kind, SupplierId, SupplierName, Date,
                       TotalCostPiastres, CreditPiastres, LossPiastres, Reason, Notes, UserId, UserName
                FROM SupplierReturns ORDER BY Id DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SupplierReturn
                {
                    Id = Convert.ToInt32(r["Id"]),
                    ReturnNumber = r["ReturnNumber"].ToString() ?? string.Empty,
                    Kind = (SupplierReturnKind)Convert.ToInt32(r["Kind"]),
                    SupplierId = r["SupplierId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["SupplierId"]),
                    SupplierName = r["SupplierName"].ToString() ?? string.Empty,
                    Date = DateTime.Parse(r["Date"].ToString() ?? DateTime.Now.ToString("s")),
                    TotalCostPiastres = Convert.ToInt64(r["TotalCostPiastres"]),
                    CreditPiastres = Convert.ToInt64(r["CreditPiastres"]),
                    LossPiastres = Convert.ToInt64(r["LossPiastres"]),
                    Reason = r["Reason"].ToString() ?? string.Empty,
                    Notes = r["Notes"] == DBNull.Value ? null : r["Notes"].ToString(),
                    UserId = Convert.ToInt32(r["UserId"]),
                    UserName = r["UserName"].ToString() ?? string.Empty
                });
            }
            return list;
        }
    }
}
