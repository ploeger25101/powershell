namespace ProjectAbyss.Smapi
{
    public sealed class ModConfig
    {
        /// <summary>Show the Project Abyss main menu instead of the Stardew title screen at startup.</summary>
        public bool ShowAbyssOnStartup { get; set; } = true;

        /// <summary>Enables the developer menu. Uses a separate profile and run folder that never earn unlocks.</summary>
        public bool DeveloperMode { get; set; }
    }
}
