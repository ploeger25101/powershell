using System.Collections.Generic;

namespace ProjectAbyss.Core.Data
{
    public enum Rarity
    {
        Common = 1,
        Uncommon = 2,
        Rare = 3,
        Epic = 4,
        Legendary = 5,
        Mythic = 6,
    }

    public enum EquipSlot
    {
        None,
        MainHand,
        OffHand,
        Head,
        Chest,
        Hands,
        Feet,
        Ring,
        Amulet,
        Relic,
    }

    public enum ItemCategory
    {
        Weapon,
        Armor,
        Jewelry,
        Relic,
        Rune,
        Consumable,
        Material,
    }

    public sealed class ItemDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ItemCategory Category { get; set; }
        public EquipSlot Slot { get; set; }
        /// <summary>Weapon type id for weapons (links to <see cref="WeaponTypeDef"/>).</summary>
        public string? WeaponType { get; set; }
        /// <summary>Empty = universal.</summary>
        public string? ClassId { get; set; }
        public Rarity MinRarity { get; set; } = Rarity.Common;
        public int MaxStack { get; set; } = 1;
        public int BaseValue { get; set; } = 10;
        public int MaxSockets { get; set; }
        public Dictionary<string, float> BaseStats { get; set; } = new();
        /// <summary>Must be found as a blueprint before it can drop (false = in the default loot pool).</summary>
        public bool RequiresBlueprint { get; set; }
        public string Icon { get; set; } = "";
    }

    public sealed class AffixDef
    {
        public string Id { get; set; } = "";
        public string Stat { get; set; } = "";
        public float Min { get; set; }
        public float Max { get; set; }
        public List<ItemCategory> AllowedCategories { get; set; } = new();
        public Rarity MinRarity { get; set; } = Rarity.Common;
    }
}
