using System.Security.Cryptography;
using System.Text;

namespace ProjectAbyss.Core.Data
{
    public static class Hashing
    {
        public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

        public static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
