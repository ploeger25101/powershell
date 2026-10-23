using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData;

namespace ProjectAbyss.Smapi
{
    /// <summary>
    /// Registers Project Abyss' own audio files as Stardew audio cues by editing <c>Data/AudioChanges</c> (Stardew 1.6).
    /// Every .wav/.ogg file under assets/audio/sfx and assets/audio/music becomes a cue named after the file.
    /// </summary>
    internal static class AudioRegistry
    {
        private static readonly HashSet<string> Registered = new();

        public static bool IsRegistered(string cueId) => Registered.Contains(cueId);

        public static void Scan(string modDirectory)
        {
            Registered.Clear();
            foreach (string sub in new[] { "sfx", "music" })
            {
                string dir = Path.Combine(modDirectory, "assets", "audio", sub);
                if (!Directory.Exists(dir))
                    continue;
                foreach (string file in Directory.GetFiles(dir).Where(f => f.EndsWith(".wav") || f.EndsWith(".ogg")))
                    Registered.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        public static void OnAssetRequested(AssetRequestedEventArgs e, string modDirectory)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/AudioChanges"))
                return;
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, AudioCueData>().Data;
                foreach (string sub in new[] { "sfx", "music" })
                {
                    string dir = Path.Combine(modDirectory, "assets", "audio", sub);
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (string file in Directory.GetFiles(dir).Where(f => f.EndsWith(".wav") || f.EndsWith(".ogg")))
                    {
                        string id = Path.GetFileNameWithoutExtension(file);
                        data[id] = new AudioCueData
                        {
                            Id = id,
                            FilePaths = new List<string> { file },
                            Category = sub == "music" ? "Music" : "Sound",
                            StreamedVorbis = sub == "music" && file.EndsWith(".ogg"),
                            Looped = sub == "music",
                        };
                    }
                }
            });
        }
    }
}
