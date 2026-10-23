using System.Collections.Generic;

namespace ProjectAbyss.Client.Rendering
{
    // Mirrors assets/sprites.json written by tools/assetgen (and by the final art pipeline).

    public sealed class AnimationMeta
    {
        public int Row { get; set; }
        public int Frames { get; set; } = 1;
        public int Fps { get; set; } = 8;
        public bool Loop { get; set; } = true;
    }

    public sealed class PlayerSheetMeta
    {
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public int FramesPerRowPerDirection { get; set; }
        public Dictionary<string, AnimationMeta> Animations { get; set; } = new();
    }

    public sealed class WeaponSheetMeta
    {
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int GripX { get; set; }
        public int GripY { get; set; }
        public List<string> Order { get; set; } = new();
    }

    public sealed class CreatureSheetMeta
    {
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public int WalkFrames { get; set; } = 4;
        public int AttackFrames { get; set; } = 2;
        public bool FaceRight { get; set; } = true;
    }

    public sealed class TileSheetMeta
    {
        public int TileSize { get; set; } = 16;
        public List<string> Order { get; set; } = new();
    }

    public sealed class IconSheetMeta
    {
        public int Size { get; set; } = 48;
        public List<string> Order { get; set; } = new();
    }

    public sealed class SpriteMeta
    {
        public Dictionary<string, PlayerSheetMeta> Players { get; set; } = new();
        public WeaponSheetMeta Weapons { get; set; } = new();
        public Dictionary<string, CreatureSheetMeta> Enemies { get; set; } = new();
        public Dictionary<string, CreatureSheetMeta> Bosses { get; set; } = new();
        public Dictionary<string, TileSheetMeta> Tiles { get; set; } = new();
        public IconSheetMeta Icons { get; set; } = new();
    }
}
