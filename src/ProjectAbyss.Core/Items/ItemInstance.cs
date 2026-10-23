using System.Collections.Generic;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Core.Items
{
    public sealed class AffixRoll
    {
        public string AffixId { get; set; } = "";
        public string Stat { get; set; } = "";
        public float Value { get; set; }
    }

    /// <summary>
    /// A concrete item. <see cref="InstanceId"/> is unique within the run and assigned only by the host, so every
    /// transfer can be checked for duplication.
    /// </summary>
    public sealed class ItemInstance
    {
        public string InstanceId { get; set; } = "";
        public string DefId { get; set; } = "";
        public Rarity Rarity { get; set; } = Rarity.Common;
        public int ItemLevel { get; set; } = 1;
        public int StackCount { get; set; } = 1;
        public int Upgrade { get; set; }
        public int Sockets { get; set; }
        /// <summary>Instance ids of inserted runes (a rune exists in at most one item).</summary>
        public List<string> Runes { get; set; } = new();
        public List<AffixRoll> Affixes { get; set; } = new();
        public string? CurseId { get; set; }
        public bool CursePurified { get; set; }
        public int ForgePotential { get; set; }
        public bool Favorite { get; set; }

        public ItemInstance Clone()
        {
            var c = (ItemInstance)this.MemberwiseClone();
            c.Runes = new List<string>(this.Runes);
            c.Affixes = this.Affixes.ConvertAll(a => new AffixRoll { AffixId = a.AffixId, Stat = a.Stat, Value = a.Value });
            return c;
        }
    }
}
