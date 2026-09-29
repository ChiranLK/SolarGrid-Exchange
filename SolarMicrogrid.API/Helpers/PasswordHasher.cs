/*
 * PasswordHasher.cs
 * -----------------------------------------------------------------------------
 * File        : PasswordHasher.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : BCrypt password hashing (work factor 12) for registration, staff
 *               creation and the initial Backoffice bootstrap, and verification
 *               at login. Plain-text passwords are never stored or returned.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Helpers
{
    public static class PasswordHasher
    {
        private const int WorkFactor = 12;

        public static string Hash(string password)
        {
            // Produce a salted BCrypt hash; the salt is embedded in the returned string.
            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        public static bool Verify(string password, string storedHash)
        {
            // Compare a login attempt with the stored hash; a corrupt hash counts as a failed match.
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }
    }
}
