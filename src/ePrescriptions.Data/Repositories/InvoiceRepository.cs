using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class InvoiceRepository
    {
        private readonly DatabaseContext _dbContext;
        private readonly ItemRepository _itemRepository;

        public InvoiceRepository(DatabaseContext dbContext, ItemRepository itemRepository)
        {
            _dbContext = dbContext;
            _itemRepository = itemRepository;
        }

        public string GenerateNextInvoiceNumber(InvoiceType type, int? year = null, SQLiteConnection? conn = null, SQLiteTransaction? trans = null)
        {
            string prefix = type == InvoiceType.Sales ? "INV" : "RET";
            int targetYear = year ?? DateTime.Now.Year;

            bool ownsConnection = conn == null;
            var connection = conn ?? _dbContext.CreateConnection();
            try
            {
                bool hasTable = false;
                using (var checkCmd = connection.CreateCommand())
                {
                    if (trans != null) checkCmd.Transaction = trans;
                    checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='InvoiceCounters';";
                    hasTable = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0) > 0;
                }

                int nextNumber;
                if (hasTable)
                {
                    // فحص وجود سجل العداد مسبقاً
                    object? existingScalar = null;
                    using (var readCmd = connection.CreateCommand())
                    {
                        if (trans != null) readCmd.Transaction = trans;
                        readCmd.CommandText = "SELECT LastNumber FROM InvoiceCounters WHERE Type = @type AND Year = @year;";
                        readCmd.Parameters.AddWithValue("@type", (int)type);
                        readCmd.Parameters.AddWithValue("@year", targetYear);
                        existingScalar = readCmd.ExecuteScalar();
                    }

                    if (existingScalar != null && existingScalar != DBNull.Value)
                    {
                        int current = Convert.ToInt32(existingScalar);
                        int maxInInvoices = GetMaxSequenceFromInvoices(connection, trans, prefix, targetYear);
                        nextNumber = Math.Max(current, maxInInvoices) + 1;

                        using var updCmd = connection.CreateCommand();
                        if (trans != null) updCmd.Transaction = trans;
                        updCmd.CommandText = @"
                            UPDATE InvoiceCounters 
                            SET LastNumber = @next 
                            WHERE Type = @type AND Year = @year;";
                        updCmd.Parameters.AddWithValue("@type", (int)type);
                        updCmd.Parameters.AddWithValue("@year", targetYear);
                        updCmd.Parameters.AddWithValue("@next", nextNumber);
                        updCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        int maxExisting = GetMaxSequenceFromInvoices(connection, trans, prefix, targetYear);
                        nextNumber = maxExisting + 1;

                        using var insCmd = connection.CreateCommand();
                        if (trans != null) insCmd.Transaction = trans;
                        insCmd.CommandText = @"
                            INSERT INTO InvoiceCounters (Type, Year, LastNumber)
                            VALUES (@type, @year, @lastNum);";
                        insCmd.Parameters.AddWithValue("@type", (int)type);
                        insCmd.Parameters.AddWithValue("@year", targetYear);
                        insCmd.Parameters.AddWithValue("@lastNum", nextNumber);
                        try
                        {
                            insCmd.ExecuteNonQuery();
                        }
                        catch
                        {
                            // في حال حدوث تعارض تزامني أثناء الإدراج نقوم بالتحديث
                            using var updRetry = connection.CreateCommand();
                            if (trans != null) updRetry.Transaction = trans;
                            updRetry.CommandText = @"
                                UPDATE InvoiceCounters 
                                SET LastNumber = LastNumber + 1 
                                WHERE Type = @type AND Year = @year;";
                            updRetry.Parameters.AddWithValue("@type", (int)type);
                            updRetry.Parameters.AddWithValue("@year", targetYear);
                            updRetry.ExecuteNonQuery();

                            using var readAgain = connection.CreateCommand();
                            if (trans != null) readAgain.Transaction = trans;
                            readAgain.CommandText = "SELECT LastNumber FROM InvoiceCounters WHERE Type = @type AND Year = @year;";
                            readAgain.Parameters.AddWithValue("@type", (int)type);
                            readAgain.Parameters.AddWithValue("@year", targetYear);
                            nextNumber = Convert.ToInt32(readAgain.ExecuteScalar() ?? (nextNumber + 1));
                        }
                    }
                }
                else
                {
                    // في حال عدم وجود جدول العدادات
                    int maxExisting = GetMaxSequenceFromInvoices(connection, trans, prefix, targetYear);
                    nextNumber = maxExisting + 1;
                }

                return $"{prefix}-{targetYear}-{nextNumber:D5}";
            }
            finally
            {
                if (ownsConnection)
                {
                    connection.Dispose();
                }
            }
        }

        private static int GetMaxSequenceFromInvoices(SQLiteConnection conn, SQLiteTransaction? trans, string prefix, int year)
        {
            string pattern = $"{prefix}-{year}-%";
            using var cmd = conn.CreateCommand();
            if (trans != null) cmd.Transaction = trans;
            cmd.CommandText = "SELECT InvoiceNumber FROM Invoices WHERE InvoiceNumber LIKE @pattern;";
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
            return maxSeq;
        }

        private static bool TableHasColumn(SQLiteConnection conn, string table, string column)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({table});";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (string.Equals(r["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// حفظ الفاتورة وبنودها وخصم المخزون بنظام FEFO (بدون التشغيلات المنتهية) وتحديث حساب العميل والوردية
        /// داخل Transaction واحدة. مرتجع المبيعات لازم يكون مربوطاً بفاتورة بيع أصلية وبنودها.
        /// </summary>
        public bool SaveInvoice(Invoice invoice, out string errorMessage)
        {
            errorMessage = string.Empty;
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                // لا بيع ولا مرتجع بدون وردية مفتوحة (حتى لو تجاوزت الواجهة هذا الفحص)
                ValidateOpenShift(invoice, conn, trans);

                // البيع الآجل لا يتجاوز حد ائتمان العميل إلا بموافقة مدير
                if (invoice.Type == InvoiceType.Sales)
                    ValidateCreditLimit(invoice, conn, trans);

                if (invoice.Type == InvoiceType.Return)
                    ValidateReturn(invoice, conn, trans);
                else
                    ValidateSale(invoice, conn, trans);

                string qrCol = TableHasColumn(conn, "Invoices", "QrCodeText") ? "QrCodeText" : "ZatcaQrBase64";
                bool hasStatus = TableHasColumn(conn, "Invoices", "Status");
                string statusCol = hasStatus ? ", Status" : "";
                string statusVal = hasStatus ? ", @status" : "";

                // التأكد من صحة رقم الفاتورة وعدم تكراره وتوليد رقم جديد عند الحاجة
                if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
                {
                    invoice.InvoiceNumber = GenerateNextInvoiceNumber(invoice.Type, invoice.Date.Year, conn, trans);
                }
                else
                {
                    using var checkCmd = conn.CreateCommand();
                    checkCmd.Transaction = trans;
                    checkCmd.CommandText = "SELECT COUNT(*) FROM Invoices WHERE InvoiceNumber = @num COLLATE NOCASE;";
                    checkCmd.Parameters.AddWithValue("@num", invoice.InvoiceNumber.Trim());
                    long exists = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0);
                    if (exists > 0)
                    {
                        string prefix = invoice.Type == InvoiceType.Sales ? "INV" : "RET";
                        if (invoice.InvoiceNumber.Trim().StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
                        {
                            invoice.InvoiceNumber = GenerateNextInvoiceNumber(invoice.Type, invoice.Date.Year, conn, trans);
                        }
                        else
                        {
                            errorMessage = $"رقم الفاتورة '{invoice.InvoiceNumber}' مستخدم بالفعل.";
                            trans.Rollback();
                            return false;
                        }
                    }
                }

                // 1) حفظ رأس الفاتورة
                using var insertInvoiceCmd = conn.CreateCommand();
                insertInvoiceCmd.Transaction = trans;
                insertInvoiceCmd.CommandText = $@"
                    INSERT INTO Invoices (
                        InvoiceNumber, Type{statusCol}, Date, CustomerId, CustomerName, CustomerPhone,
                        PriceCategory, PaymentMethod, PrescriptionNumber, DoctorName,
                        UserId, UserName, ShiftId, SubtotalPiastres, DiscountPiastres,
                        VatPiastres, TotalPiastres, PaidPiastres, RemainingPiastres, Notes, {qrCol},
                        OriginalInvoiceId
                    ) VALUES (
                        @num, @type{statusVal}, @date, @cId, @cName, @cPhone,
                        @pCat, @pMethod, @rxNum, @docName,
                        @uId, @uName, @sId, @sub, @disc,
                        @vat, @total, @paid, @rem, @notes, @qr,
                        @origInv
                    );
                    SELECT last_insert_rowid();";

                insertInvoiceCmd.Parameters.AddWithValue("@num", invoice.InvoiceNumber);
                insertInvoiceCmd.Parameters.AddWithValue("@type", (int)invoice.Type);
                if (hasStatus) insertInvoiceCmd.Parameters.AddWithValue("@status", (int)invoice.Status);
                insertInvoiceCmd.Parameters.AddWithValue("@date", invoice.Date.ToString("yyyy-MM-dd HH:mm:ss"));
                insertInvoiceCmd.Parameters.AddWithValue("@cId", (object?)invoice.CustomerId ?? DBNull.Value);
                insertInvoiceCmd.Parameters.AddWithValue("@cName", invoice.CustomerName);
                insertInvoiceCmd.Parameters.AddWithValue("@cPhone", invoice.CustomerPhone ?? string.Empty);
                insertInvoiceCmd.Parameters.AddWithValue("@pCat", (int)invoice.PriceCategory);
                insertInvoiceCmd.Parameters.AddWithValue("@pMethod", (int)invoice.PaymentMethod);
                insertInvoiceCmd.Parameters.AddWithValue("@rxNum", (object?)invoice.PrescriptionNumber ?? DBNull.Value);
                insertInvoiceCmd.Parameters.AddWithValue("@docName", (object?)invoice.DoctorName ?? DBNull.Value);
                insertInvoiceCmd.Parameters.AddWithValue("@uId", invoice.UserId);
                insertInvoiceCmd.Parameters.AddWithValue("@uName", invoice.UserName);
                insertInvoiceCmd.Parameters.AddWithValue("@sId", invoice.ShiftId);
                insertInvoiceCmd.Parameters.AddWithValue("@sub", invoice.SubtotalPiastres);
                insertInvoiceCmd.Parameters.AddWithValue("@disc", invoice.DiscountPiastres);
                insertInvoiceCmd.Parameters.AddWithValue("@vat", invoice.VatPiastres);
                insertInvoiceCmd.Parameters.AddWithValue("@total", invoice.TotalPiastres);
                insertInvoiceCmd.Parameters.AddWithValue("@paid", invoice.PaidPiastres);
                insertInvoiceCmd.Parameters.AddWithValue("@rem", invoice.RemainingPiastres);
                insertInvoiceCmd.Parameters.AddWithValue("@notes", (object?)invoice.Notes ?? DBNull.Value);
                insertInvoiceCmd.Parameters.AddWithValue("@qr", (object?)invoice.QrCodeText ?? DBNull.Value);
                insertInvoiceCmd.Parameters.AddWithValue("@origInv", (object?)invoice.OriginalInvoiceId ?? DBNull.Value);

                int invoiceId = Convert.ToInt32(insertInvoiceCmd.ExecuteScalar());
                invoice.Id = invoiceId;

                // 2) حفظ بنود الفاتورة وتحريك المخزون
                foreach (var item in invoice.Items)
                {
                    item.InvoiceId = invoiceId;
                    List<BatchAllocation> allocations = new();

                    if (invoice.Type == InvoiceType.Sales)
                    {
                        // خصم FEFO (أو من التشغيلة المقروءة من الملصق أولاً) — التشغيلات المنتهية مستبعدة
                        allocations = _itemRepository.DeductStock(item.ItemId, item.SmallestUnitsCount, conn, trans, item.BatchId);

                        var named = allocations.Where(a => a.BatchId.HasValue).ToList();
                        item.BatchId = named.Count > 0 ? named[0].BatchId : null;
                        item.BatchNumber = named.Count > 0
                            ? string.Join(" + ", named.Select(a => a.BatchNumber).Distinct())
                            : "بالسالب";
                        item.ExpiryDate = named.Count > 0 ? named.Min(a => a.ExpiryDate) : null;
                    }
                    else
                    {
                        // مرتجع: إعادة الكمية لنفس تشغيلات بند البيع الأصلي
                        _itemRepository.RestoreForInvoiceItem(item.OriginalInvoiceItemId!.Value, item.ItemId, item.SmallestUnitsCount, item.BatchNumber, conn, trans);
                    }

                    using var insertItemCmd = conn.CreateCommand();
                    insertItemCmd.Transaction = trans;
                    insertItemCmd.CommandText = @"
                        INSERT INTO InvoiceItems (
                            InvoiceId, ItemId, ItemCode, ItemNameAr, BatchId, BatchNumber, ExpiryDate,
                            UnitLevel, QuantitySold, SmallestUnitsCount, UnitPricePiastres,
                            DiscountPiastres, VatRatePercent, VatPiastres, TotalPiastres, OriginalInvoiceItemId,
                            PrescriptionNumber, DoctorName
                        ) VALUES (
                            @invId, @itemId, @code, @name, @batchId, @batch, @exp,
                            @uLevel, @qty, @smUnits, @price,
                            @disc, @vatRate, @vat, @total, @origItem,
                            @rxNum, @rxDoc
                        );
                        SELECT last_insert_rowid();";

                    insertItemCmd.Parameters.AddWithValue("@invId", invoiceId);
                    insertItemCmd.Parameters.AddWithValue("@itemId", item.ItemId);
                    insertItemCmd.Parameters.AddWithValue("@code", item.ItemCode);
                    insertItemCmd.Parameters.AddWithValue("@name", item.ItemNameAr);
                    insertItemCmd.Parameters.AddWithValue("@batchId", invoice.Type == InvoiceType.Sales ? (object?)item.BatchId ?? DBNull.Value : DBNull.Value);
                    insertItemCmd.Parameters.AddWithValue("@batch", item.BatchNumber ?? string.Empty);
                    insertItemCmd.Parameters.AddWithValue("@exp", item.ExpiryDate?.ToString("yyyy-MM-dd") ?? string.Empty);
                    insertItemCmd.Parameters.AddWithValue("@uLevel", (int)item.UnitLevel);
                    insertItemCmd.Parameters.AddWithValue("@qty", item.QuantitySold);
                    insertItemCmd.Parameters.AddWithValue("@smUnits", item.SmallestUnitsCount);
                    insertItemCmd.Parameters.AddWithValue("@price", item.UnitPricePiastres);
                    insertItemCmd.Parameters.AddWithValue("@disc", item.DiscountPiastres);
                    insertItemCmd.Parameters.AddWithValue("@vatRate", item.VatRatePercent);
                    insertItemCmd.Parameters.AddWithValue("@vat", item.VatPiastres);
                    insertItemCmd.Parameters.AddWithValue("@total", item.TotalPiastres);
                    insertItemCmd.Parameters.AddWithValue("@origItem", (object?)item.OriginalInvoiceItemId ?? DBNull.Value);
                    insertItemCmd.Parameters.AddWithValue("@rxNum", (object?)item.PrescriptionNumber ?? DBNull.Value);
                    insertItemCmd.Parameters.AddWithValue("@rxDoc", (object?)item.DoctorName ?? DBNull.Value);
                    item.Id = Convert.ToInt32(insertItemCmd.ExecuteScalar());

                    foreach (var alloc in allocations)
                    {
                        using var allocCmd = conn.CreateCommand();
                        allocCmd.Transaction = trans;
                        allocCmd.CommandText = "INSERT INTO InvoiceItemBatches (InvoiceItemId, BatchId, Units) VALUES (@ii, @b, @u);";
                        allocCmd.Parameters.AddWithValue("@ii", item.Id);
                        allocCmd.Parameters.AddWithValue("@b", (object?)alloc.BatchId ?? DBNull.Value);
                        allocCmd.Parameters.AddWithValue("@u", alloc.Units);
                        allocCmd.ExecuteNonQuery();
                    }
                }

                // 3) تحديث رصيد العميل: البيع الآجل يزيد المديونية، ومرتجع الآجل يخفضها
                if (invoice.CustomerId.HasValue && invoice.CustomerId.Value > 1 && invoice.RemainingPiastres > 0)
                {
                    long delta = invoice.Type == InvoiceType.Return ? -invoice.RemainingPiastres : invoice.RemainingPiastres;

                    using var updateCustomerCmd = conn.CreateCommand();
                    updateCustomerCmd.Transaction = trans;
                    updateCustomerCmd.CommandText = @"
                        UPDATE Customers
                        SET CurrentBalancePiastres = CurrentBalancePiastres + @delta
                        WHERE Id = @cId;";
                    updateCustomerCmd.Parameters.AddWithValue("@delta", delta);
                    updateCustomerCmd.Parameters.AddWithValue("@cId", invoice.CustomerId.Value);
                    updateCustomerCmd.ExecuteNonQuery();
                }

                // 4) تحديث مجاميع الوردية الحالية
                if (invoice.ShiftId > 0)
                {
                    using var updateShiftCmd = conn.CreateCommand();
                    updateShiftCmd.Transaction = trans;

                    if (invoice.Type == InvoiceType.Return)
                    {
                        // المرتجع النقدي يخرج من الدرج ولا يُحسب ضمن المبيعات
                        updateShiftCmd.CommandText = @"
                            UPDATE Shifts
                            SET TotalReturnsCashPiastres = TotalReturnsCashPiastres + (CASE WHEN @pMethod = 1 THEN @amt ELSE 0 END),
                                ExpectedCashPiastres = ExpectedCashPiastres - (CASE WHEN @pMethod = 1 THEN @amt ELSE 0 END)
                            WHERE Id = @sId;";
                    }
                    else
                    {
                        string columnToUpdate = invoice.PaymentMethod switch
                        {
                            PaymentMethod.Cash => "TotalSalesCashPiastres",
                            PaymentMethod.Network => "TotalSalesNetworkPiastres",
                            PaymentMethod.OnAccount => "TotalSalesOnAccountPiastres",
                            _ => "TotalSalesCashPiastres"
                        };

                        updateShiftCmd.CommandText = $@"
                            UPDATE Shifts
                            SET {columnToUpdate} = {columnToUpdate} + @amt,
                                ExpectedCashPiastres = ExpectedCashPiastres + (CASE WHEN @pMethod = 1 THEN @amt ELSE 0 END)
                            WHERE Id = @sId;";
                    }

                    updateShiftCmd.Parameters.AddWithValue("@amt", invoice.TotalPiastres);
                    updateShiftCmd.Parameters.AddWithValue("@pMethod", (int)invoice.PaymentMethod);
                    updateShiftCmd.Parameters.AddWithValue("@sId", invoice.ShiftId);
                    updateShiftCmd.ExecuteNonQuery();
                }

                // قيد اليومية التلقائي داخل نفس الـ Transaction (لو فشل، الفاتورة كلها تتلغي)
                JournalPosting.PostSaleInvoice(conn, trans, invoice);

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                errorMessage = ex.Message;
                if (ex is not InvalidOperationException)
                    Logger.LogException(ex, "SaveInvoice");
                return false;
            }
        }

        /// <summary>بداية رسالة الخطأ عند تجاوز حد الائتمان (تستخدمها الواجهة للتعرف عليه وعرض خيار موافقة المدير)</summary>
        public const string CreditLimitErrorPrefix = "تجاوز حد الائتمان";

        /// <summary>
        /// (رصيد العميل الحالي + المتبقي من الفاتورة) لازم لا يتجاوز CreditLimitPiastres. حد 0 = لا ائتمان.
        /// </summary>
        private static void ValidateCreditLimit(Invoice invoice, SQLiteConnection conn, SQLiteTransaction trans)
        {
            if (!invoice.CustomerId.HasValue || invoice.CustomerId.Value <= 1 || invoice.RemainingPiastres <= 0) return;
            if (invoice.CreditLimitOverridden) return;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT CreditLimitPiastres, CurrentBalancePiastres, Name FROM Customers WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", invoice.CustomerId.Value);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;

            long limit = Convert.ToInt64(r["CreditLimitPiastres"]);
            long balance = Convert.ToInt64(r["CurrentBalancePiastres"]);
            if (balance + invoice.RemainingPiastres > limit)
            {
                throw new InvalidOperationException(
                    $"{CreditLimitErrorPrefix}: رصيد العميل {r["Name"]} الحالي {balance / 100m:N2} + قيمة الفاتورة الآجلة {invoice.RemainingPiastres / 100m:N2} أكبر من الحد المسموح {limit / 100m:N2} ج.م.");
            }
        }

        /// <summary>الفاتورة لازم تتبع وردية موجودة ومفتوحة (Status = 1)</summary>
        private static void ValidateOpenShift(Invoice invoice, SQLiteConnection conn, SQLiteTransaction trans)
        {
            if (invoice.ShiftId <= 0)
                throw new InvalidOperationException("لا توجد وردية مفتوحة. افتح وردية أولاً (برصيد افتتاح) قبل البيع.");

            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT Status FROM Shifts WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", invoice.ShiftId);
            object? status = cmd.ExecuteScalar();
            if (status == null || status == DBNull.Value || Convert.ToInt32(status) != 1)
                throw new InvalidOperationException("الوردية الحالية مغلقة أو غير موجودة. افتح وردية جديدة قبل البيع.");
        }

        /// <summary>
        /// منع بيع تشغيلة منتهية الصلاحية حتى لو تجاوزت الواجهة هذا الفحص
        /// </summary>
        private static void ValidateSale(Invoice invoice, SQLiteConnection conn, SQLiteTransaction trans)
        {
            foreach (var item in invoice.Items)
            {
                if (item.SmallestUnitsCount <= 0)
                    throw new InvalidOperationException($"الكمية غير صحيحة للصنف {item.ItemNameAr}.");

                ValidatePrescription(invoice, item, conn, trans);

                if (!item.BatchId.HasValue) continue;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = "SELECT ItemId, BatchNumber, ExpiryDate FROM ItemBatches WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", item.BatchId.Value);
                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    throw new InvalidOperationException($"التشغيلة المختارة للصنف {item.ItemNameAr} غير موجودة.");

                if (Convert.ToInt32(r["ItemId"]) != item.ItemId)
                    throw new InvalidOperationException($"التشغيلة المختارة لا تخص الصنف {item.ItemNameAr}.");

                DateTime expiry = DateTime.Parse(r["ExpiryDate"]?.ToString() ?? DateTime.Today.ToString("yyyy-MM-dd"));
                if (ExpiryPolicy.IsExpired(expiry))
                    throw new InvalidOperationException(
                        $"التشغيلة {r["BatchNumber"]} للصنف {item.ItemNameAr} منتهية الصلاحية ({expiry:yyyy-MM-dd}) ولا يمكن بيعها.");
            }
        }

        /// <summary>
        /// الصنف الذي يتطلب وصفة لا يُباع بدون رقم وصفة واسم طبيب (على البند أو على رأس الفاتورة)
        /// </summary>
        private static void ValidatePrescription(Invoice invoice, InvoiceItem item, SQLiteConnection conn, SQLiteTransaction trans)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT IsPrescriptionRequired FROM Items WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", item.ItemId);
            object? v = cmd.ExecuteScalar();
            if (v == null || v == DBNull.Value || Convert.ToInt32(v) == 0) return;

            string rx = !string.IsNullOrWhiteSpace(item.PrescriptionNumber) ? item.PrescriptionNumber! : invoice.PrescriptionNumber ?? string.Empty;
            string doctor = !string.IsNullOrWhiteSpace(item.DoctorName) ? item.DoctorName! : invoice.DoctorName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rx) || string.IsNullOrWhiteSpace(doctor))
                throw new InvalidOperationException($"الصنف {item.ItemNameAr} يتطلب وصفة طبية: أدخل رقم الوصفة واسم الطبيب قبل البيع.");

            item.PrescriptionNumber = rx.Trim();
            item.DoctorName = doctor.Trim();
        }

        /// <summary>
        /// قواعد مرتجع المبيعات: فاتورة أصلية إلزامية، والصنف والوحدة والسعر مطابقة لبند الأصل،
        /// والكمية لا تتجاوز المباع مخصوماً منه ما سبق إرجاعه.
        /// </summary>
        private static void ValidateReturn(Invoice invoice, SQLiteConnection conn, SQLiteTransaction trans)
        {
            if (!invoice.OriginalInvoiceId.HasValue)
                throw new InvalidOperationException("مرتجع المبيعات لازم يكون مرتبط بفاتورة بيع أصلية.");

            using (var typeCmd = conn.CreateCommand())
            {
                typeCmd.Transaction = trans;
                typeCmd.CommandText = "SELECT Type FROM Invoices WHERE Id = @id;";
                typeCmd.Parameters.AddWithValue("@id", invoice.OriginalInvoiceId.Value);
                object? t = typeCmd.ExecuteScalar();
                if (t == null || t == DBNull.Value)
                    throw new InvalidOperationException("فاتورة البيع الأصلية غير موجودة.");
                if (Convert.ToInt32(t) != (int)InvoiceType.Sales)
                    throw new InvalidOperationException("الفاتورة الأصلية يجب أن تكون فاتورة بيع وليست مرتجع.");
            }

            if (invoice.Items.Count == 0)
                throw new InvalidOperationException("لا توجد بنود في المرتجع.");

            var requestedUnits = new Dictionary<int, int>();
            var requestedQty = new Dictionary<int, int>();

            foreach (var item in invoice.Items)
            {
                if (!item.OriginalInvoiceItemId.HasValue)
                    throw new InvalidOperationException($"البند {item.ItemNameAr} غير مرتبط ببند في فاتورة البيع الأصلية.");
                if (item.QuantitySold <= 0 || item.SmallestUnitsCount <= 0)
                    throw new InvalidOperationException($"كمية المرتجع غير صحيحة للصنف {item.ItemNameAr}.");

                int origId = item.OriginalInvoiceItemId.Value;
                requestedUnits[origId] = (requestedUnits.TryGetValue(origId, out int u) ? u : 0) + item.SmallestUnitsCount;
                requestedQty[origId] = (requestedQty.TryGetValue(origId, out int q) ? q : 0) + item.QuantitySold;

                int origInvoiceId, origItemId, origLevel, origQty, origUnits;
                long origPrice, origDiscount;
                using (var lineCmd = conn.CreateCommand())
                {
                    lineCmd.Transaction = trans;
                    lineCmd.CommandText = @"
                        SELECT InvoiceId, ItemId, UnitLevel, QuantitySold, SmallestUnitsCount, UnitPricePiastres, DiscountPiastres
                        FROM InvoiceItems WHERE Id = @id;";
                    lineCmd.Parameters.AddWithValue("@id", origId);
                    using var r = lineCmd.ExecuteReader();
                    if (!r.Read())
                        throw new InvalidOperationException($"بند البيع الأصلي للصنف {item.ItemNameAr} غير موجود.");
                    origInvoiceId = Convert.ToInt32(r["InvoiceId"]);
                    origItemId = Convert.ToInt32(r["ItemId"]);
                    origLevel = Convert.ToInt32(r["UnitLevel"]);
                    origQty = Convert.ToInt32(r["QuantitySold"]);
                    origUnits = Convert.ToInt32(r["SmallestUnitsCount"]);
                    origPrice = Convert.ToInt64(r["UnitPricePiastres"]);
                    origDiscount = Convert.ToInt64(r["DiscountPiastres"]);
                }

                if (origInvoiceId != invoice.OriginalInvoiceId.Value)
                    throw new InvalidOperationException($"البند {item.ItemNameAr} لا يتبع فاتورة البيع الأصلية المحددة.");
                if (origItemId != item.ItemId)
                    throw new InvalidOperationException($"الصنف في المرتجع لا يطابق الصنف في فاتورة البيع ({item.ItemNameAr}).");
                if (origLevel != (int)item.UnitLevel)
                    throw new InvalidOperationException($"وحدة المرتجع ({item.ItemNameAr}) لازم تكون نفس وحدة البيع الأصلية.");
                if (origPrice != item.UnitPricePiastres)
                    throw new InvalidOperationException($"سعر المرتجع للصنف {item.ItemNameAr} لازم يطابق سعر البيع الأصلي.");

                int returnedUnits;
                using (var retCmd = conn.CreateCommand())
                {
                    retCmd.Transaction = trans;
                    retCmd.CommandText = "SELECT IFNULL(SUM(SmallestUnitsCount), 0) FROM InvoiceItems WHERE OriginalInvoiceItemId = @id;";
                    retCmd.Parameters.AddWithValue("@id", origId);
                    returnedUnits = Convert.ToInt32(retCmd.ExecuteScalar() ?? 0);
                }

                int available = origUnits - returnedUnits;
                if (requestedUnits[origId] > available)
                    throw new InvalidOperationException(
                        $"كمية مرتجع الصنف {item.ItemNameAr} أكبر من المتاح للإرجاع (المباع {origUnits}، سبق إرجاع {returnedUnits}، المتاح {Math.Max(0, available)} من أصغر وحدة).");

                // الخصم المرتجع لا يتجاوز حصة الكمية المرتجعة من خصم البيع الأصلي
                if (origQty > 0)
                {
                    long maxDiscount = (origDiscount * requestedQty[origId] + origQty - 1) / origQty;
                    if (item.DiscountPiastres > maxDiscount)
                        throw new InvalidOperationException($"خصم المرتجع للصنف {item.ItemNameAr} أكبر من الخصم المطبق في البيع الأصلي.");
                }
            }
        }

        /// <summary>
        /// البحث عن فاتورة بيع برقمها لبدء مرتجع منها، مع الكمية المتبقية القابلة للإرجاع من كل بند
        /// </summary>
        public ReturnableInvoice? GetReturnableInvoice(string invoiceNumber, out string errorMessage)
        {
            errorMessage = string.Empty;
            string number = (invoiceNumber ?? string.Empty).Trim();
            if (number.Length == 0)
            {
                errorMessage = "اكتب رقم فاتورة البيع الأصلية.";
                return null;
            }

            using var conn = _dbContext.CreateConnection();

            var inv = new Invoice();
            using (var cmd = conn.CreateCommand())
            {
                bool hasStatus = TableHasColumn(conn, "Invoices", "Status");
                string statusSelect = hasStatus ? "Status, " : "";
                cmd.CommandText = $@"
                    SELECT Id, InvoiceNumber, Type, {statusSelect}Date, CustomerId, CustomerName, CustomerPhone,
                           PriceCategory, PaymentMethod, PrescriptionNumber, DoctorName, UserId, UserName, ShiftId,
                           SubtotalPiastres, DiscountPiastres, VatPiastres, TotalPiastres, PaidPiastres, RemainingPiastres
                    FROM Invoices WHERE InvoiceNumber = @n COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("@n", number);
                using var r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    errorMessage = $"مفيش فاتورة بالرقم {number}.";
                    return null;
                }

                inv.Id = Convert.ToInt32(r["Id"]);
                inv.InvoiceNumber = r["InvoiceNumber"].ToString() ?? string.Empty;
                inv.Type = (InvoiceType)Convert.ToInt32(r["Type"]);
                if (hasStatus)
                {
                    inv.Status = (InvoiceStatus)Convert.ToInt32(r["Status"]);
                }
                inv.Date = DateTime.Parse(r["Date"].ToString() ?? DateTime.Now.ToString("s"));
                inv.CustomerId = r["CustomerId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["CustomerId"]);
                inv.CustomerName = r["CustomerName"].ToString() ?? string.Empty;
                inv.CustomerPhone = r["CustomerPhone"].ToString() ?? string.Empty;
                inv.PriceCategory = (PriceCategory)Convert.ToInt32(r["PriceCategory"]);
                inv.PaymentMethod = (PaymentMethod)Convert.ToInt32(r["PaymentMethod"]);
                inv.PrescriptionNumber = r["PrescriptionNumber"] == DBNull.Value ? null : r["PrescriptionNumber"].ToString();
                inv.DoctorName = r["DoctorName"] == DBNull.Value ? null : r["DoctorName"].ToString();
                inv.UserId = Convert.ToInt32(r["UserId"]);
                inv.UserName = r["UserName"].ToString() ?? string.Empty;
                inv.ShiftId = Convert.ToInt32(r["ShiftId"]);
                inv.SubtotalPiastres = Convert.ToInt64(r["SubtotalPiastres"]);
                inv.DiscountPiastres = Convert.ToInt64(r["DiscountPiastres"]);
                inv.VatPiastres = Convert.ToInt64(r["VatPiastres"]);
                inv.TotalPiastres = Convert.ToInt64(r["TotalPiastres"]);
                inv.PaidPiastres = Convert.ToInt64(r["PaidPiastres"]);
                inv.RemainingPiastres = Convert.ToInt64(r["RemainingPiastres"]);
            }

            if (inv.Status == InvoiceStatus.Cancelled)
            {
                errorMessage = "الفاتورة دي ملغاة، لا يمكن عمل مرتجع منها.";
                return null;
            }

            if (inv.Type != InvoiceType.Sales)
            {
                errorMessage = "الفاتورة دي مرتجع، المرتجع بيتعمل من فاتورة بيع.";
                return null;
            }

            var result = new ReturnableInvoice { Invoice = inv };
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT i.Id, i.ItemId, i.ItemCode, i.ItemNameAr, i.BatchId, i.BatchNumber, i.ExpiryDate,
                           i.UnitLevel, i.QuantitySold, i.SmallestUnitsCount, i.UnitPricePiastres,
                           i.DiscountPiastres, i.VatRatePercent, i.VatPiastres, i.TotalPiastres,
                           IFNULL((SELECT SUM(r.QuantitySold) FROM InvoiceItems r WHERE r.OriginalInvoiceItemId = i.Id), 0) AS ReturnedQty
                    FROM InvoiceItems i
                    WHERE i.InvoiceId = @id
                    ORDER BY i.Id ASC;";
                cmd.Parameters.AddWithValue("@id", inv.Id);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string exp = r["ExpiryDate"]?.ToString() ?? string.Empty;
                    var line = new InvoiceItem
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        InvoiceId = inv.Id,
                        ItemId = Convert.ToInt32(r["ItemId"]),
                        ItemCode = r["ItemCode"].ToString() ?? string.Empty,
                        ItemNameAr = r["ItemNameAr"].ToString() ?? string.Empty,
                        BatchId = r["BatchId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["BatchId"]),
                        BatchNumber = r["BatchNumber"].ToString() ?? string.Empty,
                        ExpiryDate = string.IsNullOrWhiteSpace(exp) ? (DateTime?)null : DateTime.Parse(exp),
                        UnitLevel = (UnitLevel)Convert.ToInt32(r["UnitLevel"]),
                        QuantitySold = Convert.ToInt32(r["QuantitySold"]),
                        SmallestUnitsCount = Convert.ToInt32(r["SmallestUnitsCount"]),
                        UnitPricePiastres = Convert.ToInt64(r["UnitPricePiastres"]),
                        DiscountPiastres = Convert.ToInt64(r["DiscountPiastres"]),
                        VatRatePercent = Convert.ToInt32(r["VatRatePercent"]),
                        VatPiastres = Convert.ToInt64(r["VatPiastres"]),
                        TotalPiastres = Convert.ToInt64(r["TotalPiastres"])
                    };
                    result.Lines.Add(new ReturnableLine
                    {
                        Line = line,
                        ReturnedQuantity = Convert.ToInt32(r["ReturnedQty"])
                    });
                }
            }

            if (result.Lines.All(l => l.RemainingQuantity <= 0))
            {
                errorMessage = "كل أصناف الفاتورة دي اترجعت بالفعل.";
                return null;
            }

            return result;
        }

        private Invoice? LoadInvoice(SQLiteConnection conn, string whereClause, (string Param, object Value) param)
        {
            var inv = new Invoice();
            bool hasStatus = TableHasColumn(conn, "Invoices", "Status");
            bool hasOrig = TableHasColumn(conn, "Invoices", "OriginalInvoiceId");
            string qrCol = TableHasColumn(conn, "Invoices", "QrCodeText") ? "QrCodeText" : "ZatcaQrBase64";
            string statusCol = hasStatus ? ", Status" : "";
            string origCol = hasOrig ? ", OriginalInvoiceId" : "";

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT Id, InvoiceNumber, Type{statusCol}, Date, CustomerId, CustomerName, CustomerPhone,
                           PriceCategory, PaymentMethod, PrescriptionNumber, DoctorName, UserId, UserName, ShiftId,
                           SubtotalPiastres, DiscountPiastres, VatPiastres, TotalPiastres, PaidPiastres, RemainingPiastres,
                           Notes{origCol}, {qrCol}
                    FROM Invoices WHERE {whereClause};";
                cmd.Parameters.AddWithValue(param.Param, param.Value);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                inv.Id = Convert.ToInt32(r["Id"]);
                inv.InvoiceNumber = r["InvoiceNumber"].ToString() ?? string.Empty;
                inv.Type = (InvoiceType)Convert.ToInt32(r["Type"]);
                inv.Status = hasStatus && r["Status"] != DBNull.Value ? (InvoiceStatus)Convert.ToInt32(r["Status"]) : InvoiceStatus.Active;
                inv.Date = DateTime.Parse(r["Date"].ToString() ?? DateTime.Now.ToString("s"));
                inv.CustomerId = r["CustomerId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["CustomerId"]);
                inv.CustomerName = r["CustomerName"].ToString() ?? string.Empty;
                inv.CustomerPhone = r["CustomerPhone"].ToString() ?? string.Empty;
                inv.PriceCategory = (PriceCategory)Convert.ToInt32(r["PriceCategory"]);
                inv.PaymentMethod = (PaymentMethod)Convert.ToInt32(r["PaymentMethod"]);
                inv.PrescriptionNumber = r["PrescriptionNumber"] == DBNull.Value ? null : r["PrescriptionNumber"].ToString();
                inv.DoctorName = r["DoctorName"] == DBNull.Value ? null : r["DoctorName"].ToString();
                inv.UserId = Convert.ToInt32(r["UserId"]);
                inv.UserName = r["UserName"].ToString() ?? string.Empty;
                inv.ShiftId = Convert.ToInt32(r["ShiftId"]);
                inv.SubtotalPiastres = Convert.ToInt64(r["SubtotalPiastres"]);
                inv.DiscountPiastres = Convert.ToInt64(r["DiscountPiastres"]);
                inv.VatPiastres = Convert.ToInt64(r["VatPiastres"]);
                inv.TotalPiastres = Convert.ToInt64(r["TotalPiastres"]);
                inv.PaidPiastres = Convert.ToInt64(r["PaidPiastres"]);
                inv.RemainingPiastres = Convert.ToInt64(r["RemainingPiastres"]);
                inv.Notes = r["Notes"] == DBNull.Value ? null : r["Notes"].ToString();
                if (hasOrig) inv.OriginalInvoiceId = r["OriginalInvoiceId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["OriginalInvoiceId"]);
                inv.QrCodeText = r[qrCol] == DBNull.Value ? null : r[qrCol].ToString();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, InvoiceId, ItemId, ItemCode, ItemNameAr, BatchId, BatchNumber, ExpiryDate,
                           UnitLevel, QuantitySold, SmallestUnitsCount, UnitPricePiastres, DiscountPiastres,
                           VatRatePercent, VatPiastres, TotalPiastres, OriginalInvoiceItemId, PrescriptionNumber, DoctorName
                    FROM InvoiceItems
                    WHERE InvoiceId = @id
                    ORDER BY Id ASC;";
                cmd.Parameters.AddWithValue("@id", inv.Id);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string exp = r["ExpiryDate"]?.ToString() ?? string.Empty;
                    inv.Items.Add(new InvoiceItem
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        InvoiceId = inv.Id,
                        ItemId = Convert.ToInt32(r["ItemId"]),
                        ItemCode = r["ItemCode"].ToString() ?? string.Empty,
                        ItemNameAr = r["ItemNameAr"].ToString() ?? string.Empty,
                        BatchId = r["BatchId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["BatchId"]),
                        BatchNumber = r["BatchNumber"].ToString() ?? string.Empty,
                        ExpiryDate = string.IsNullOrWhiteSpace(exp) ? (DateTime?)null : DateTime.Parse(exp),
                        UnitLevel = (UnitLevel)Convert.ToInt32(r["UnitLevel"]),
                        QuantitySold = Convert.ToInt32(r["QuantitySold"]),
                        SmallestUnitsCount = Convert.ToInt32(r["SmallestUnitsCount"]),
                        UnitPricePiastres = Convert.ToInt64(r["UnitPricePiastres"]),
                        DiscountPiastres = Convert.ToInt64(r["DiscountPiastres"]),
                        VatRatePercent = Convert.ToInt32(r["VatRatePercent"]),
                        VatPiastres = Convert.ToInt64(r["VatPiastres"]),
                        TotalPiastres = Convert.ToInt64(r["TotalPiastres"]),
                        OriginalInvoiceItemId = r["OriginalInvoiceItemId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["OriginalInvoiceItemId"]),
                        PrescriptionNumber = r["PrescriptionNumber"] == DBNull.Value ? null : r["PrescriptionNumber"].ToString(),
                        DoctorName = r["DoctorName"] == DBNull.Value ? null : r["DoctorName"].ToString()
                    });
                }
            }

            return inv;
        }

        public Invoice? GetInvoiceById(int id)
        {
            using var conn = _dbContext.CreateConnection();
            return LoadInvoice(conn, "Id = @val", ("@val", id));
        }

        public Invoice? GetInvoiceByNumber(string invoiceNumber)
        {
            using var conn = _dbContext.CreateConnection();
            return LoadInvoice(conn, "InvoiceNumber = @val COLLATE NOCASE", ("@val", (invoiceNumber ?? string.Empty).Trim()));
        }

        /// <summary>
        /// إلغاء فاتورة مبيعات: يرجّع المخزون بنظام تتبع التشغيلات، يعكس رصيد العميل الآجل ومجاميع الوردية،
        /// يعكس القيد المحاسبي، ويُسجَّل في سجل التدقيق (Admin في سجل الموظفين، Owner في سجله الخاص).
        /// </summary>
        public bool CancelInvoice(
            int invoiceId,
            int cancellingUserId,
            string cancellingUserName,
            UserRole cancellingUserRole,
            out string errorMessage,
            OwnerAuditLogRepository? ownerAuditRepo = null,
            StaffAuditLogRepository? staffAuditRepo = null)
        {
            errorMessage = string.Empty;

            if (cancellingUserRole != UserRole.Admin && cancellingUserRole != UserRole.Owner)
            {
                errorMessage = "إلغاء الفاتورة يتطلب صلاحية مدير النظام (Admin) أو صاحب الصيدلية (Owner).";
                return false;
            }

            var invoice = GetInvoiceById(invoiceId);
            if (invoice == null)
            {
                errorMessage = "الفاتورة غير موجودة.";
                return false;
            }

            if (invoice.Status == InvoiceStatus.Cancelled)
            {
                errorMessage = "الفاتورة ملغاة بالفعل.";
                return false;
            }

            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                // التحقق من عدم وجود مرتجعات سارية على هذه الفاتورة
                using (var checkReturnsCmd = conn.CreateCommand())
                {
                    checkReturnsCmd.Transaction = trans;
                    checkReturnsCmd.CommandText = "SELECT COUNT(*) FROM Invoices WHERE OriginalInvoiceId = @id AND Status != @can;";
                    checkReturnsCmd.Parameters.AddWithValue("@id", invoice.Id);
                    checkReturnsCmd.Parameters.AddWithValue("@can", (int)InvoiceStatus.Cancelled);
                    long returnsCount = Convert.ToInt64(checkReturnsCmd.ExecuteScalar() ?? 0);
                    if (returnsCount > 0)
                    {
                        throw new InvalidOperationException("لا يمكن إلغاء الفاتورة لوجود مرتجعات مسجلة عليها.");
                    }
                }

                // 1) تحديث حالة الفاتورة إلى ملغاة
                using (var updateStatusCmd = conn.CreateCommand())
                {
                    updateStatusCmd.Transaction = trans;
                    updateStatusCmd.CommandText = "UPDATE Invoices SET Status = @can WHERE Id = @id;";
                    updateStatusCmd.Parameters.AddWithValue("@can", (int)InvoiceStatus.Cancelled);
                    updateStatusCmd.Parameters.AddWithValue("@id", invoice.Id);
                    updateStatusCmd.ExecuteNonQuery();
                }

                // 2) إرجاع المخزون (لفاتورة البيع)
                if (invoice.Type == InvoiceType.Sales)
                {
                    foreach (var item in invoice.Items)
                    {
                        _itemRepository.RestoreForInvoiceItem(item.Id, item.ItemId, item.SmallestUnitsCount, item.BatchNumber, conn, trans);
                    }
                }

                // 3) عكس رصيد العميل الآجل
                if (invoice.CustomerId.HasValue && invoice.CustomerId.Value > 1 && invoice.RemainingPiastres > 0)
                {
                    long delta = invoice.Type == InvoiceType.Return ? invoice.RemainingPiastres : -invoice.RemainingPiastres;
                    using var updateCustomerCmd = conn.CreateCommand();
                    updateCustomerCmd.Transaction = trans;
                    updateCustomerCmd.CommandText = @"
                        UPDATE Customers
                        SET CurrentBalancePiastres = CurrentBalancePiastres + @delta
                        WHERE Id = @cId;";
                    updateCustomerCmd.Parameters.AddWithValue("@delta", delta);
                    updateCustomerCmd.Parameters.AddWithValue("@cId", invoice.CustomerId.Value);
                    updateCustomerCmd.ExecuteNonQuery();
                }

                // 4) عكس مجاميع الوردية
                if (invoice.ShiftId > 0)
                {
                    using var updateShiftCmd = conn.CreateCommand();
                    updateShiftCmd.Transaction = trans;
                    if (invoice.Type == InvoiceType.Return)
                    {
                        updateShiftCmd.CommandText = @"
                            UPDATE Shifts
                            SET TotalReturnsCashPiastres = MAX(0, TotalReturnsCashPiastres - (CASE WHEN @pMethod = 1 THEN @amt ELSE 0 END)),
                                ExpectedCashPiastres = ExpectedCashPiastres + (CASE WHEN @pMethod = 1 THEN @amt ELSE 0 END)
                            WHERE Id = @sId;";
                    }
                    else
                    {
                        string columnToUpdate = invoice.PaymentMethod switch
                        {
                            PaymentMethod.Cash => "TotalSalesCashPiastres",
                            PaymentMethod.Network => "TotalSalesNetworkPiastres",
                            PaymentMethod.OnAccount => "TotalSalesOnAccountPiastres",
                            _ => "TotalSalesCashPiastres"
                        };

                        updateShiftCmd.CommandText = $@"
                            UPDATE Shifts
                            SET {columnToUpdate} = MAX(0, {columnToUpdate} - @amt),
                                ExpectedCashPiastres = ExpectedCashPiastres - (CASE WHEN @pMethod = 1 THEN @amt ELSE 0 END)
                            WHERE Id = @sId;";
                    }

                    updateShiftCmd.Parameters.AddWithValue("@amt", invoice.TotalPiastres);
                    updateShiftCmd.Parameters.AddWithValue("@pMethod", (int)invoice.PaymentMethod);
                    updateShiftCmd.Parameters.AddWithValue("@sId", invoice.ShiftId);
                    updateShiftCmd.ExecuteNonQuery();
                }

                // 5) عكس القيود المحاسبية
                JournalPosting.PostInvoiceCancellation(conn, trans, invoice);

                trans.Commit();

                // 6) تسجيل العملية في سجل التدقيق
                string entityId = invoice.Id.ToString();
                string entityLabel = invoice.InvoiceNumber;
                string oldValue = "نشطة (Active)";
                string newValue = $"ملغاة (بواسطة: {cancellingUserName})";

                if (cancellingUserRole == UserRole.Owner)
                {
                    ownerAuditRepo?.LogChange(cancellingUserId, "Invoices", entityId, entityLabel, "Status", oldValue, newValue);
                }
                else
                {
                    staffAuditRepo?.LogChange(cancellingUserId, cancellingUserName, cancellingUserRole.ToString(),
                        "Invoices", entityId, entityLabel, "Status", oldValue, newValue);
                }

                invoice.Status = InvoiceStatus.Cancelled;
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                errorMessage = ex.Message;
                if (ex is not InvalidOperationException)
                    Logger.LogException(ex, "CancelInvoice");
                return false;
            }
        }

        public bool CancelInvoice(
            string invoiceNumber,
            int cancellingUserId,
            string cancellingUserName,
            UserRole cancellingUserRole,
            out string errorMessage,
            OwnerAuditLogRepository? ownerAuditRepo = null,
            StaffAuditLogRepository? staffAuditRepo = null)
        {
            var inv = GetInvoiceByNumber(invoiceNumber);
            if (inv == null)
            {
                errorMessage = $"لا توجد فاتورة بالرقم {invoiceNumber}.";
                return false;
            }
            return CancelInvoice(inv.Id, cancellingUserId, cancellingUserName, cancellingUserRole, out errorMessage, ownerAuditRepo, staffAuditRepo);
        }

        public void SaveSuspendedInvoice(SuspendedInvoice suspended)
        {
            using var conn = _dbContext.CreateConnection();
            bool usePiastres = TableHasColumn(conn, "SuspendedInvoices", "TotalPiastres");
            string totCol = usePiastres ? "TotalPiastres" : "TotalPiastres";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                INSERT OR REPLACE INTO SuspendedInvoices (ReferenceCode, SuspendedAt, CustomerName, JsonData, {totCol})
                VALUES (@ref, @date, @cust, @json, @tot);";
            cmd.Parameters.AddWithValue("@ref", suspended.ReferenceCode);
            cmd.Parameters.AddWithValue("@date", suspended.SuspendedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@cust", suspended.CustomerName);
            cmd.Parameters.AddWithValue("@json", suspended.JsonData);
            cmd.Parameters.AddWithValue("@tot", suspended.TotalPiastres);
            cmd.ExecuteNonQuery();
        }

        public List<SuspendedInvoice> GetSuspendedInvoices()
        {
            var list = new List<SuspendedInvoice>();
            using var conn = _dbContext.CreateConnection();
            bool usePiastres = TableHasColumn(conn, "SuspendedInvoices", "TotalPiastres");
            string totCol = usePiastres ? "TotalPiastres" : "TotalPiastres";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT Id, ReferenceCode, SuspendedAt, CustomerName, JsonData, {totCol} FROM SuspendedInvoices ORDER BY SuspendedAt DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SuspendedInvoice
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    ReferenceCode = reader["ReferenceCode"].ToString() ?? string.Empty,
                    SuspendedAt = DateTime.Parse(reader["SuspendedAt"].ToString() ?? DateTime.Now.ToString("s")),
                    CustomerName = reader["CustomerName"].ToString() ?? string.Empty,
                    JsonData = reader["JsonData"].ToString() ?? string.Empty,
                    TotalPiastres = Convert.ToInt64(reader[totCol])
                });
            }
            return list;
        }

        public void DeleteSuspendedInvoice(int id)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM SuspendedInvoices WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }
}
