using System;
using System.IO;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Persistence;

namespace ProjectAbyss.Core.Profile
{
    public enum UnlockKind
    {
        Class,
        Difficulty,
        Blueprint,
        Achievement,
        Cosmetic,
        Title,
    }

    /// <summary>Loads and saves the local profile. Every unlock is written immediately (atomic) so a later crash or failed run cannot lose it.</summary>
    public sealed class ProfileStore
    {
        public const string Kind = "profile";
        private readonly string path;
        private long sequence;

        public ProfileStore(string directory)
        {
            this.Directory = directory;
            this.path = Path.Combine(directory, "profile.json");
        }

        public string Directory { get; }
        public PlayerProfile Profile { get; private set; } = new();
        public LoadStatus LastLoadStatus { get; private set; }

        public PlayerProfile LoadOrCreate(GameData data, string defaultName)
        {
            var result = EnvelopeIO.Load<PlayerProfile>(this.path, Kind, PlayerProfile.CurrentSchemaVersion);
            this.LastLoadStatus = result.Status;
            if (result.Value != null)
            {
                this.Profile = result.Value;
                this.sequence = result.Sequence;
            }
            else if (result.Status == LoadStatus.Missing)
            {
                this.Profile = new PlayerProfile { DisplayName = defaultName };
                this.Save();
            }
            else
            {
                // never silently overwrite a profile we could not read; keep it aside for manual recovery
                string aside = this.path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                if (File.Exists(this.path))
                    File.Copy(this.path, aside, overwrite: false);
                this.Profile = new PlayerProfile { DisplayName = defaultName };
                this.Save();
            }
            foreach (var c in data.Classes.Values.Where(c => c.UnlockedByDefault))
                this.Profile.UnlockedClasses.Add(c.Id);
            return this.Profile;
        }

        public void Save() => EnvelopeIO.Save(this.path, Kind, PlayerProfile.CurrentSchemaVersion, ++this.sequence, this.Profile);

        /// <summary>Apply an unlock exactly once. Returns false for developer profiles and for already applied unlock ids.</summary>
        public bool Unlock(string unlockTxId, UnlockKind kind, string id)
        {
            var p = this.Profile;
            if (p.IsDeveloperProfile || p.AppliedUnlocks.Contains(unlockTxId))
                return false;
            var set = kind switch
            {
                UnlockKind.Class => p.UnlockedClasses,
                UnlockKind.Difficulty => p.UnlockedDifficulties,
                UnlockKind.Blueprint => p.Blueprints,
                UnlockKind.Achievement => p.Achievements,
                UnlockKind.Cosmetic => p.Cosmetics,
                _ => p.Titles,
            };
            set.Add(id);
            p.AppliedUnlocks.Add(unlockTxId);
            this.Save();
            return true;
        }

        /// <summary>DSGVO-oriented export: a plain JSON copy of everything stored about the player.</summary>
        public string ExportJson() => System.Text.Json.JsonSerializer.Serialize(this.Profile, GameData.JsonOptions);

        /// <summary>Delete all local profile data, including backups and the identity key.</summary>
        public void DeleteAll()
        {
            AtomicFile.Delete(this.path);
            string key = Path.Combine(this.Directory, "identity.key");
            if (File.Exists(key))
                File.Delete(key);
            this.Profile = new PlayerProfile();
        }
    }
}
