using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace ePrescriptions.Data.Repositories
{
    public class OwnerAuditLogEntry
    {
        public int Id { get; set; }
        public int OwnerUserId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string EntityLabel { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime ChangedAt { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>
    /// سجل تعديلات صاحب الصيدلية. يُستعلم منه فقط عبر شاشة مقصورة على حساب صاحب الصيدلية؛
    /// لا تستدعِ هذه الفئة أي شاشة أو مستودع آخر لعرض البيانات لغير صاحب الصيدلية.
    /// </summary>
    public class OwnerAuditLogRepository
    {
        private readonly DatabaseContext _dbContext;

        public OwnerAuditLogRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// تسجيل تعديل حقل واحد. يُستدعى فقط عندما يكون منفّذ العملية هو صاحب الصيدلية (Role = Owner)؛
        /// لا يُستدعى أبداً لتعديلات المدير أو الموظفين العاديين.
        /// </summary>
        public void LogChange(int ownerUserId, string entityType, string entityId, string entityLabel, string fieldName, string? oldValue, string? newValue, string? note = null)
        {
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO OwnerAuditLog (OwnerUserId, EntityType, EntityId, EntityLabel, FieldName, OldValue, NewValue, ChangedAt, Note)
                VALUES (@uid, @etype, @eid, @elabel, @field, @old, @new, @at, @note);";
            cmd.Parameters.AddWithValue("@uid", ownerUserId);
            cmd.Parameters.AddWithValue("@etype", entityType);
            cmd.Parameters.AddWithValue("@eid", entityId);
            cmd.Parameters.AddWithValue("@elabel", entityLabel);
            cmd.Parameters.AddWithValue("@field", fieldName);
            cmd.Parameters.AddWithValue("@old", (object?)oldValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@new", (object?)newValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// أحدث التعديلات (الأحدث أولاً). الاستدعاء مقصور، على مستوى واجهة المستخدم، على شاشة صاحب الصيدلية فقط.
        /// </summary>
        public List<OwnerAuditLogEntry> GetRecent(int limit = 200)
        {
            var list = new List<OwnerAuditLogEntry>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM OwnerAuditLog ORDER BY Id DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new OwnerAuditLogEntry
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    OwnerUserId = Convert.ToInt32(reader["OwnerUserId"]),
                    EntityType = reader["EntityType"]?.ToString() ?? string.Empty,
                    EntityId = reader["EntityId"]?.ToString() ?? string.Empty,
                    EntityLabel = reader["EntityLabel"]?.ToString() ?? string.Empty,
                    FieldName = reader["FieldName"]?.ToString() ?? string.Empty,
                    OldValue = reader["OldValue"] is DBNull ? null : reader["OldValue"].ToString(),
                    NewValue = reader["NewValue"] is DBNull ? null : reader["NewValue"].ToString(),
                    ChangedAt = DateTime.Parse(reader["ChangedAt"]?.ToString() ?? DateTime.Now.ToString("s")),
                    Note = reader["Note"] is DBNull ? null : reader["Note"].ToString()
                });
            }
            return list;
        }
    }
}
