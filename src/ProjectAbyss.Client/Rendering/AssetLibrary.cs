using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Client.Rendering
{
    /// <summary>Loads textures and atlas metadata once; missing optional textures fall back to a visible magenta marker.</summary>
    public sealed class AssetLibrary
    {
        private readonly IAbyssPlatform platform;
        private readonly Dictionary<string, Texture2D> textures = new();

        public AssetLibrary(IAbyssPlatform platform)
        {
            this.platform = platform;
            string metaPath = Path.Combine(platform.ModDirectory, "assets", "sprites.json");
            this.Meta = JsonSerializer.Deserialize<SpriteMeta>(File.ReadAllText(metaPath), GameData.JsonOptions) ?? new SpriteMeta();
            this.Pixel = platform.CreateTexture(1, 1, new[] { Color.White });
            this.Circle = CreateCircle(platform, 64);
            this.Missing = platform.CreateTexture(2, 2, new[] { Color.Magenta, Color.Black, Color.Black, Color.Magenta });
        }

        public SpriteMeta Meta { get; }
        public Texture2D Pixel { get; }
        public Texture2D Circle { get; }
        public Texture2D Missing { get; }

        public Texture2D Get(string relativePath)
        {
            if (this.textures.TryGetValue(relativePath, out var t))
                return t;
            t = this.platform.TryLoadTexture(relativePath) ?? this.Missing;
            this.textures[relativePath] = t;
            return t;
        }

        private static Texture2D CreateCircle(IAbyssPlatform platform, int size)
        {
            var px = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    px[y * size + x] = dx * dx + dy * dy <= r * r ? Color.White : Color.Transparent;
                }
            }
            return platform.CreateTexture(size, size, px);
        }
    }

}
