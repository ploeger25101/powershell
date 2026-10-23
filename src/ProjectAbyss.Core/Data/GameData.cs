using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectAbyss.Core.Data
{
    /// <summary>All gameplay definitions, loaded once from JSON and validated.</summary>
    public sealed class GameData
    {
        public BalanceConfig Balance { get; private set; } = new();
        public IReadOnlyDictionary<string, ClassDef> Classes { get; private set; } = new Dictionary<string, ClassDef>();
        public IReadOnlyDictionary<string, WeaponTypeDef> WeaponTypes { get; private set; } = new Dictionary<string, WeaponTypeDef>();
        public IReadOnlyDictionary<string, AbilityDef> Abilities { get; private set; } = new Dictionary<string, AbilityDef>();
        public IReadOnlyDictionary<string, EnemyDef> Enemies { get; private set; } = new Dictionary<string, EnemyDef>();
        public IReadOnlyDictionary<string, BossDef> Bosses { get; private set; } = new Dictionary<string, BossDef>();
        public IReadOnlyDictionary<string, ArenaTemplateDef> Arenas { get; private set; } = new Dictionary<string, ArenaTemplateDef>();
        public IReadOnlyDictionary<string, ItemDef> Items { get; private set; } = new Dictionary<string, ItemDef>();
        public IReadOnlyList<AffixDef> Affixes { get; private set; } = Array.Empty<AffixDef>();

        /// <summary>SHA-256 over all data files; compared in the multiplayer handshake so all peers simulate the same rules.</summary>
        public string ContentHash { get; private set; } = "";

        public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        public static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                WriteIndented = true,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }

        public static GameData LoadFromDirectory(string directory)
        {
            var files = new[] { "balance.json", "classes.json", "weapons.json", "abilities.json", "enemies.json", "bosses.json", "arenas.json", "items.json" };
            var texts = new Dictionary<string, string>();
            foreach (string file in files)
            {
                string path = Path.Combine(directory, file);
                if (!File.Exists(path))
                    throw new GameDataException($"Missing data file '{path}'.");
                texts[file] = File.ReadAllText(path);
            }
            return LoadFromJson(texts);
        }

        public static GameData LoadFromJson(IReadOnlyDictionary<string, string> files)
        {
            T Read<T>(string name)
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(files[name], JsonOptions) ?? throw new GameDataException($"'{name}' is empty.");
                }
                catch (JsonException ex)
                {
                    throw new GameDataException($"'{name}' is invalid JSON: {ex.Message}", ex);
                }
            }

            var data = new GameData
            {
                Balance = Read<BalanceConfig>("balance.json"),
                Classes = ToMap(Read<List<ClassDef>>("classes.json"), c => c.Id, "class"),
                WeaponTypes = ToMap(Read<List<WeaponTypeDef>>("weapons.json"), w => w.Id, "weapon type"),
                Abilities = ToMap(Read<List<AbilityDef>>("abilities.json"), a => a.Id, "ability"),
                Enemies = ToMap(Read<List<EnemyDef>>("enemies.json"), e => e.Id, "enemy"),
                Bosses = ToMap(Read<List<BossDef>>("bosses.json"), b => b.Id, "boss"),
                Arenas = ToMap(Read<List<ArenaTemplateDef>>("arenas.json"), a => a.Id, "arena"),
            };
            var itemFile = Read<ItemFile>("items.json");
            data.Items = ToMap(itemFile.Items, i => i.Id, "item");
            data.Affixes = itemFile.Affixes;
            data.ContentHash = Hashing.Sha256Hex(string.Join("\n", files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\n" + p.Value)));

            var errors = GameDataValidator.Validate(data);
            if (errors.Count > 0)
                throw new GameDataException("Game data validation failed:\n - " + string.Join("\n - ", errors));
            return data;
        }

        private static IReadOnlyDictionary<string, T> ToMap<T>(List<T> list, Func<T, string> key, string kind)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T entry in list)
            {
                string id = key(entry);
                if (string.IsNullOrWhiteSpace(id))
                    throw new GameDataException($"A {kind} definition has no id.");
                if (!map.TryAdd(id, entry))
                    throw new GameDataException($"Duplicate {kind} id '{id}'.");
            }
            return map;
        }

        private sealed class ItemFile
        {
            public List<ItemDef> Items { get; set; } = new();
            public List<AffixDef> Affixes { get; set; } = new();
        }
    }

    public sealed class GameDataException : Exception
    {
        public GameDataException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
