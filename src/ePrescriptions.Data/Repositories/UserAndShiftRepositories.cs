using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Core.Security;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class UserRepository
    {
        private readonly DatabaseContext _dbContext;

        public UserRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public User? Authenticate(string username, string password)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, FullName, PasswordHash, Role, IsActive, CreatedAt FROM Users WHERE Username = @u AND IsActive = 1 LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", username.Trim());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                string storedHash = reader["PasswordHash"].ToString() ?? string.Empty;
                if (PasswordHasher.VerifyPassword(password, storedHash))
                {
                    return new User
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        Username = reader["Username"].ToString() ?? string.Empty,
                        FullName = reader["FullName"].ToString() ?? string.Empty,
                        PasswordHash = storedHash,
                        Role = (UserRole)Convert.ToInt32(reader["Role"]),
                        IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                        CreatedAt = DateTime.Parse(reader["CreatedAt"].ToString() ?? DateTime.Now.ToString("s"))
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// كل المستخدمين عدا صاحب الصيدلية (Role = Owner)، فلا يظهر في أي شاشة إدارة موظفين أو صلاحيات.
        /// </summary>
        public List<User> GetAll(bool includeOwner = false)
        {
            var list = new List<User>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = includeOwner
                ? "SELECT Id, Username, FullName, Role, IsActive, CreatedAt FROM Users ORDER BY FullName ASC;"
                : "SELECT Id, Username, FullName, Role, IsActive, CreatedAt FROM Users WHERE Role <> 99 ORDER BY FullName ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new User
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Username = reader["Username"].ToString() ?? string.Empty,
                    FullName = reader["FullName"].ToString() ?? string.Empty,
                    Role = (UserRole)Convert.ToInt32(reader["Role"]),
                    IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                    CreatedAt = DateTime.Parse(reader["CreatedAt"].ToString() ?? DateTime.Now.ToString("s"))
                });
            }
            return list;
        }

        /// <summary>
        /// إنشاء أو تعديل حساب موظف (مدير إدارة / صيدلي / كاشير). يرفض صراحة أي محاولة لإنشاء أو تعديل
        /// حساب بدور صاحب الصيدلية (Owner) من هذه الدالة؛ حساب صاحب الصيدلية لا يُدار من شاشة الموظفين إطلاقاً.
        /// </summary>
        public int SaveStaffUser(int id, string username, string fullName, string? plainPassword, UserRole role, bool isActive)
        {
            if (role == UserRole.Owner)
                throw new InvalidOperationException("لا يمكن إنشاء أو تعديل حساب صاحب الصيدلية من شاشة إدارة الموظفين.");

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            if (id == 0)
            {
                string hash = PasswordHasher.HashPassword(plainPassword ?? throw new ArgumentException("كلمة المرور مطلوبة للمستخدم الجديد."));
                cmd.CommandText = @"
                    INSERT INTO Users (Username, FullName, PasswordHash, Role, IsActive, CreatedAt)
                    VALUES (@u, @f, @h, @r, @a, @c);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@c", DateTime.Now.ToString("s"));
            }
            else
            {
                // التأكد من أن الحساب المستهدف ليس حساب صاحب الصيدلية قبل التعديل
                using (var guardCmd = conn.CreateCommand())
                {
                    guardCmd.CommandText = "SELECT Role FROM Users WHERE Id = @id;";
                    guardCmd.Parameters.AddWithValue("@id", id);
                    object? existingRole = guardCmd.ExecuteScalar();
                    if (existingRole != null && Convert.ToInt32(existingRole) == 99)
                        throw new InvalidOperationException("لا يمكن تعديل حساب صاحب الصيدلية من شاشة إدارة الموظفين.");
                }

                if (!string.IsNullOrWhiteSpace(plainPassword))
                {
                    string hash = PasswordHasher.HashPassword(plainPassword);
                    cmd.CommandText = @"
                        UPDATE Users SET Username=@u, FullName=@f, PasswordHash=@h, Role=@r, IsActive=@a
                        WHERE Id=@id AND Role <> 99;";
                    cmd.Parameters.AddWithValue("@h", hash);
                }
                else
                {
                    cmd.CommandText = @"
                        UPDATE Users SET Username=@u, FullName=@f, Role=@r, IsActive=@a
                        WHERE Id=@id AND Role <> 99;";
                }
                cmd.Parameters.AddWithValue("@id", id);
            }

            cmd.Parameters.AddWithValue("@u", username.Trim());
            cmd.Parameters.AddWithValue("@f", fullName.Trim());
            cmd.Parameters.AddWithValue("@r", (int)role);
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);

            return id == 0 ? Convert.ToInt32(cmd.ExecuteScalar()) : id;
        }

        /// <summary>
        /// يغيّر مستخدم كلمة مرور حسابه هو فقط (بعد التحقق من كلمة المرور الحالية).
        /// تُستخدم هذه الدالة لتغيير كلمة مرور صاحب الصيدلية الافتراضية بعد أول دخول.
        /// </summary>
        public bool ChangeOwnPassword(int userId, string currentPassword, string newPassword, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                errorMessage = "كلمة المرور الجديدة يجب ألا تقل عن 8 أحرف.";
                return false;
            }

            using var conn = _dbContext.CreateConnection();
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT PasswordHash FROM Users WHERE Id = @id;";
            checkCmd.Parameters.AddWithValue("@id", userId);
            object? storedHashObj = checkCmd.ExecuteScalar();

            if (storedHashObj == null)
            {
                errorMessage = "تعذر العثور على الحساب.";
                return false;
            }

            string storedHash = storedHashObj.ToString() ?? string.Empty;
            if (!PasswordHasher.VerifyPassword(currentPassword, storedHash))
            {
                errorMessage = "كلمة المرور الحالية غير صحيحة.";
                return false;
            }

            string newHash = PasswordHasher.HashPassword(newPassword);
            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE Users SET PasswordHash = @h WHERE Id = @id;";
            updateCmd.Parameters.AddWithValue("@h", newHash);
            updateCmd.Parameters.AddWithValue("@id", userId);
            updateCmd.ExecuteNonQuery();

            return true;
        }
    }

    public class ShiftRepository
    {
        private readonly DatabaseContext _dbContext;

        public ShiftRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Shift? GetCurrentOpenShift(int userId)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, UserId, UserName, StartTime, EndTime, OpeningBalancePiastres,
                       TotalSalesCashPiastres, TotalSalesNetworkPiastres, TotalSalesOnAccountPiastres,
                       TotalReturnsCashPiastres, ExpectedCashPiastres, ActualCashPiastres, DifferencePiastres,
                       Status, Notes
                FROM Shifts
                WHERE UserId = @uId AND Status = 1
                ORDER BY Id DESC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@uId", userId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return MapShift(reader);
            }

            return null;
        }

        public int OpenShift(int userId, string userName, long openingBalancePiastres)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Shifts (UserId, UserName, StartTime, OpeningBalancePiastres, ExpectedCashPiastres, Status)
                VALUES (@uId, @uName, @start, @openBal, @openBal, 1);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@uId", userId);
            cmd.Parameters.AddWithValue("@uName", userName);
            cmd.Parameters.AddWithValue("@start", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@openBal", openingBalancePiastres);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void CloseShift(int shiftId, long actualCashPiastres, string? notes)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            // حساب الفارق بين الفعلي والمتوقع: Difference = Actual - Expected
            cmd.CommandText = @"
                UPDATE Shifts
                SET EndTime = @end,
                    ActualCashPiastres = @actual,
                    DifferencePiastres = @actual - ExpectedCashPiastres,
                    Status = 2,
                    Notes = @notes
                WHERE Id = @sId;";

            cmd.Parameters.AddWithValue("@end", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@actual", actualCashPiastres);
            cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sId", shiftId);

            cmd.ExecuteNonQuery();
        }

        private static Shift MapShift(SQLiteDataReader reader)
        {
            return new Shift
            {
                Id = Convert.ToInt32(reader["Id"]),
                UserId = Convert.ToInt32(reader["UserId"]),
                UserName = reader["UserName"].ToString() ?? string.Empty,
                StartTime = DateTime.Parse(reader["StartTime"].ToString() ?? DateTime.Now.ToString("s")),
                EndTime = reader["EndTime"] is DBNull ? null : DateTime.Parse(reader["EndTime"].ToString()!),
                OpeningBalancePiastres = Convert.ToInt64(reader["OpeningBalancePiastres"]),
                TotalSalesCashPiastres = Convert.ToInt64(reader["TotalSalesCashPiastres"]),
                TotalSalesNetworkPiastres = Convert.ToInt64(reader["TotalSalesNetworkPiastres"]),
                TotalSalesOnAccountPiastres = Convert.ToInt64(reader["TotalSalesOnAccountPiastres"]),
                TotalReturnsCashPiastres = Convert.ToInt64(reader["TotalReturnsCashPiastres"]),
                ExpectedCashPiastres = Convert.ToInt64(reader["ExpectedCashPiastres"]),
                ActualCashPiastres = reader["ActualCashPiastres"] is DBNull ? null : Convert.ToInt64(reader["ActualCashPiastres"]),
                DifferencePiastres = reader["DifferencePiastres"] is DBNull ? null : Convert.ToInt64(reader["DifferencePiastres"]),
                Status = (ShiftStatus)Convert.ToInt32(reader["Status"]),
                Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString()
            };
        }
    }
}
