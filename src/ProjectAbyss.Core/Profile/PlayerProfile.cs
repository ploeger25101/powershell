using System;
using System.Collections.Generic;

namespace ProjectAbyss.Core.Profile
{
    public sealed class AudioSettings
    {
        public float Master { get; set; } = 1f;
        public float Music { get; set; } = 0.7f;
        public float Effects { get; set; } = 0.8f;
        public float Ambience { get; set; } = 0.6f;
    }

    public sealed class AccessibilitySettings
    {
        public float HudScale { get; set; } = 1f;
        public bool ReducedParticles { get; set; }
        public bool ScreenShake { get; set; } = true;
        public bool ReducedFlashes { get; set; }
        public bool HighContrastTelegraphs { get; set; }
        public bool ShowDamageNumbers { get; set; } = true;
    }

    public sealed class ProfileSettings
    {
        /// <summary>Action → key name (SMAPI SButton names, e.g. "Q", "MouseLeft"). Defaults per design §9.</summary>
        public Dictionary<string, string> KeyBindings { get; set; } = DefaultBindings();
        public AudioSettings Audio { get; set; } = new();
        public AccessibilitySettings Accessibility { get; set; } = new();

        public static Dictionary<string, string> DefaultBindings() => new()
        {
            ["attack"] = "MouseLeft",
            ["special"] = "MouseRight",
            ["ability1"] = "Q",
            ["ability2"] = "E",
            ["ultimate"] = "R",
            ["dash"] = "Space",
            ["potion"] = "F",
            ["skillTree"] = "Tab",
            ["inventory"] = "I",
            ["map"] = "M",
            ["interact"] = "G",
            ["moveUp"] = "W",
            ["moveDown"] = "S",
            ["moveLeft"] = "A",
            ["moveRight"] = "D",
            ["menu"] = "Escape",
        };
    }

    public sealed class ProfileStatistics
    {
        public int RunsStarted { get; set; }
        public int RunsWon { get; set; }
        public int RunsLost { get; set; }
        public long EnemiesKilled { get; set; }
        public int BossesKilled { get; set; }
        public int DeepestLevel { get; set; }
        public double PlaySeconds { get; set; }
    }

    /// <summary>
    /// Permanent, local, per-player progress (design §23/§28). Contains no numeric start bonuses — only options.
    /// No personal data beyond a self-chosen display name.
    /// </summary>
    public sealed class PlayerProfile
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = "Spieler";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Developer profiles can use cheats but never earn achievements or unlocks.</summary>
        public bool IsDeveloperProfile { get; set; }
        public HashSet<string> UnlockedClasses { get; set; } = new();
        public HashSet<string> UnlockedDifficulties { get; set; } = new() { "normal" };
        public HashSet<string> Blueprints { get; set; } = new();
        public HashSet<string> Achievements { get; set; } = new();
        public HashSet<string> Cosmetics { get; set; } = new();
        public HashSet<string> Titles { get; set; } = new();
        public Dictionary<string, int> Bestiary { get; set; } = new();
        public ProfileStatistics Statistics { get; set; } = new();
        public ProfileSettings Settings { get; set; } = new();
        /// <summary>Unlock transaction ids already applied (idempotent unlocks across crashes/reconnects).</summary>
        public HashSet<string> AppliedUnlocks { get; set; } = new();
    }
}
