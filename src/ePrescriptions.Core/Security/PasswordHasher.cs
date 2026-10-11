using System;
using System.Security.Cryptography;

namespace ePrescriptions.Core.Security
{
    /// <summary>
    /// تشفير وفحص كلمات المرور باستخدام PBKDF2 مع Salt عشوائي آمن (Rfc2898DeriveBytes).
    /// متوافق تماماً مع .NET Framework 4.8 وWindows 7.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltByteSize = 16;
        private const int HashByteSize = 32;
        private const int IterationCount = 50000;

        /// <summary>
        /// توليد تجزئة لكلمة المرور بصيغة: {iterations}:{salt_base64}:{hash_base64}
        /// </summary>
        public static string HashPassword(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
                throw new ArgumentNullException(nameof(plainPassword));

            byte[] salt = new byte[SaltByteSize];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            using var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, IterationCount);
            byte[] hash = pbkdf2.GetBytes(HashByteSize);

            return $"{IterationCount}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// التحقق من صحة كلمة المرور المدخلة ومقارنتها بالتجزئة المحفوظة
        /// </summary>
        public static bool VerifyPassword(string plainPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(storedHash))
                return false;

            try
            {
                string[] parts = storedHash.Split(':');
                if (parts.Length != 3)
                    return false;

                if (!int.TryParse(parts[0], out int iterations))
                    return false;

                byte[] salt = Convert.FromBase64String(parts[1]);
                byte[] expectedHash = Convert.FromBase64String(parts[2]);

                using var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, iterations);
                byte[] actualHash = pbkdf2.GetBytes(expectedHash.Length);

                return SlowEquals(expectedHash, actualHash);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// مقارنة ذات وقت ثابت (Constant Time) لمنع هجمات التوقيت (Timing Attacks)
        /// </summary>
        private static bool SlowEquals(byte[] a, byte[] b)
        {
            uint diff = (uint)a.Length ^ (uint)b.Length;
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                diff |= (uint)(a[i] ^ b[i]);
            }
            return diff == 0;
        }
    }
}
