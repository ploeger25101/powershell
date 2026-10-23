using System;
using System.IO;

namespace ProjectAbyss.Core.Persistence
{
    /// <summary>
    /// Crash-safe file replacement: write to a temp file, flush to disk, then swap it in. The previous version is kept as
    /// <c>.bak</c> so a file corrupted by the OS or hardware can still be recovered from the last good copy.
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // some file systems do not support Replace; fall through to copy + move
                }
                catch (IOException)
                {
                }
                File.Copy(path, bak, overwrite: true);
            }
            File.Move(tmp, path, overwrite: true);
        }

        public static byte[]? TryReadAllBytes(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

        public static void Delete(string path)
        {
            foreach (string p in new[] { path, path + ".tmp", path + ".bak" })
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
        }
    }
}
