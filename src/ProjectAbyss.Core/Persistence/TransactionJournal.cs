using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Run;

namespace ProjectAbyss.Core.Persistence
{
    public sealed class JournalEntry
    {
        public long Seq { get; set; }
        public string Sha { get; set; } = "";
        public RunTransaction Tx { get; set; } = new();
    }

    /// <summary>
    /// Append-only, fsync'd log of critical run transactions (JSON lines with per-line checksums).
    /// A torn final line from a crash mid-write is detected and discarded: that transaction was never confirmed.
    /// </summary>
    public sealed class TransactionJournal
    {
        private readonly string path;
        public long LastSequence { get; private set; }

        public TransactionJournal(string path)
        {
            this.path = path;
            var entries = this.ReadAll(out bool damaged);
            foreach (var e in entries)
                this.LastSequence = Math.Max(this.LastSequence, e.Seq);
            if (damaged)
            {
                // remove the torn tail now, otherwise new appends would land behind an unreadable line
                this.RepairedOnOpen = true;
                this.Rewrite(entries);
            }
        }

        public bool RepairedOnOpen { get; }

        private void Rewrite(List<JournalEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
                sb.Append(JsonSerializer.Serialize(e, Compact)).Append('\n');
            AtomicFile.WriteAllBytes(this.path, Encoding.UTF8.GetBytes(sb.ToString()));
        }

        public long Append(RunTransaction tx)
        {
            long seq = this.LastSequence + 1;
            string txJson = JsonSerializer.Serialize(tx, Compact);
            var entry = new JournalEntry { Seq = seq, Sha = Hashing.Sha256Hex(seq + "|" + txJson), Tx = tx };
            byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, Compact) + "\n");
            string? dir = Path.GetDirectoryName(Path.GetFullPath(this.path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            using (var fs = new FileStream(this.path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
            {
                fs.Write(line, 0, line.Length);
                fs.Flush(flushToDisk: true);
            }
            this.LastSequence = seq;
            return seq;
        }

        /// <summary>Read all intact entries in order. <paramref name="discardedTail"/> is true if a damaged tail was found.</summary>
        public List<JournalEntry> ReadAll(out bool discardedTail)
        {
            discardedTail = false;
            var result = new List<JournalEntry>();
            if (!File.Exists(this.path))
                return result;
            long expected = -1;
            foreach (string line in File.ReadAllLines(this.path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                JournalEntry? e;
                try
                {
                    e = JsonSerializer.Deserialize<JournalEntry>(line, Compact);
                }
                catch (JsonException)
                {
                    discardedTail = true;
                    break;
                }
                if (e == null || Hashing.Sha256Hex(e.Seq + "|" + JsonSerializer.Serialize(e.Tx, Compact)) != e.Sha || (expected >= 0 && e.Seq != expected))
                {
                    discardedTail = true;
                    break;
                }
                expected = e.Seq + 1;
                result.Add(e);
            }
            return result;
        }

        /// <summary>Rewrite the journal keeping only entries newer than <paramref name="upToSequence"/> (after a checkpoint).</summary>
        public void TruncateThrough(long upToSequence)
        {
            this.Rewrite(this.ReadAll(out _).FindAll(e => e.Seq > upToSequence));
        }

        public void Delete() => AtomicFile.Delete(this.path);

        private static readonly JsonSerializerOptions Compact = CreateCompact();

        private static JsonSerializerOptions CreateCompact()
        {
            var o = GameData.CreateJsonOptions();
            o.WriteIndented = false;
            return o;
        }
    }
}
