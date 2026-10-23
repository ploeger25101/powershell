using System;
using System.IO;

namespace ProjectAbyss.Core.Tests
{
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abyss-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
