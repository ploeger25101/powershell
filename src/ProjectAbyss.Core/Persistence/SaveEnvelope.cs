using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Core.Persistence
{
    /// <summary>Versioned, checksummed wrapper around every save file.</summary>
    public sealed class SaveEnvelope
    {
        public const string FormatId = "ProjectAbyss";

        public string Format { get; set; } = FormatId;
        public string Kind { get; set; } = "";
        public int SchemaVersion { get; set; }
        public string GameVersion { get; set; } = "";
        public long Sequence { get; set; }
        public DateTime SavedUtc { get; set; }
        public string PayloadSha256 { get; set; } = "";
        public string Payload { get; set; } = "";
    }

    public enum LoadStatus
    {
        Ok,
        Missing,
        RecoveredFromBackup,
        Corrupt,
        WrongKind,
        UnsupportedVersion,
    }

    public sealed class LoadResult<T> where T : class
    {
        public LoadStatus Status { get; init; }
        public T? Value { get; init; }
        public long Sequence { get; init; }
        public string? Error { get; init; }
    }

    public static class EnvelopeIO
    {
        public static string GameVersion { get; set; } = typeof(EnvelopeIO).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        public static void Save<T>(string path, string kind, int schemaVersion, long sequence, T value)
        {
            string payload = JsonSerializer.Serialize(value, GameData.JsonOptions);
            var envelope = new SaveEnvelope
            {
                Kind = kind,
                SchemaVersion = schemaVersion,
                GameVersion = GameVersion,
                Sequence = sequence,
                SavedUtc = DateTime.UtcNow,
                PayloadSha256 = Hashing.Sha256Hex(payload),
                Payload = payload,
            };
            AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope, GameData.JsonOptions));
        }

        /// <summary>Load and verify; on corruption fall back to the <c>.bak</c> copy written by <see cref="AtomicFile"/>.</summary>
        public static LoadResult<T> Load<T>(string path, string kind, int maxSchemaVersion) where T : class
        {
            var primary = TryLoad<T>(path, kind, maxSchemaVersion);
            if (primary.Status is LoadStatus.Ok or LoadStatus.UnsupportedVersion or LoadStatus.WrongKind)
                return primary;
            var backup = TryLoad<T>(path + ".bak", kind, maxSchemaVersion);
            if (backup.Status == LoadStatus.Ok)
                return new LoadResult<T> { Status = LoadStatus.RecoveredFromBackup, Value = backup.Value, Sequence = backup.Sequence, Error = primary.Error };
            return primary;
        }

        private static LoadResult<T> TryLoad<T>(string path, string kind, int maxSchemaVersion) where T : class
        {
            if (!File.Exists(path))
                return new LoadResult<T> { Status = LoadStatus.Missing };
            try
            {
                var env = JsonSerializer.Deserialize<SaveEnvelope>(File.ReadAllBytes(path), GameData.JsonOptions);
                if (env == null || env.Format != SaveEnvelope.FormatId)
                    return new LoadResult<T> { Status = LoadStatus.Corrupt, Error = "not a Project Abyss save" };
                if (env.Kind != kind)
                    return new LoadResult<T> { Status = LoadStatus.WrongKind, Error = $"expected {kind}, found {env.Kind}" };
                if (env.SchemaVersion > maxSchemaVersion || env.SchemaVersion < 1)
                    return new LoadResult<T> { Status = LoadStatus.UnsupportedVersion, Error = $"schema {env.SchemaVersion} not supported (max {maxSchemaVersion})" };
                if (Hashing.Sha256Hex(env.Payload) != env.PayloadSha256)
                    return new LoadResult<T> { Status = LoadStatus.Corrupt, Error = "checksum mismatch" };
                var value = JsonSerializer.Deserialize<T>(env.Payload, GameData.JsonOptions);
                return value == null
                    ? new LoadResult<T> { Status = LoadStatus.Corrupt, Error = "empty payload" }
                    : new LoadResult<T> { Status = LoadStatus.Ok, Value = value, Sequence = env.Sequence };
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or InvalidOperationException)
            {
                return new LoadResult<T> { Status = LoadStatus.Corrupt, Error = ex.Message };
            }
        }

        internal static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    }
}
