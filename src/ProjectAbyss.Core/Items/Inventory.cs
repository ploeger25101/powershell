using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectAbyss.Core.Items
{
    /// <summary>Fixed-size slot container (24 inventory slots, 16 run-chest slots). Never silently drops items.</summary>
    public sealed class Inventory
    {
        public List<ItemInstance?> Slots { get; set; } = new();

        public Inventory()
        {
        }

        public Inventory(int size)
        {
            this.Slots = Enumerable.Repeat<ItemInstance?>(null, size).ToList();
        }

        public int Size => this.Slots.Count;
        public int FreeSlots => this.Slots.Count(s => s == null);
        public IEnumerable<ItemInstance> Items => this.Slots.Where(s => s != null)!;

        public bool Contains(string instanceId) => this.IndexOf(instanceId) >= 0;

        public int IndexOf(string instanceId) => this.Slots.FindIndex(s => s?.InstanceId == instanceId);

        /// <summary>Adds the item (merging stacks of the same definition when possible). Returns false if there is no room.</summary>
        public bool TryAdd(ItemInstance item, int maxStack)
        {
            if (maxStack > 1)
            {
                foreach (var s in this.Slots)
                {
                    if (s != null && s.DefId == item.DefId && s.StackCount + item.StackCount <= maxStack && s.CurseId == null)
                    {
                        s.StackCount += item.StackCount;
                        return true;
                    }
                }
            }
            int free = this.Slots.FindIndex(s => s == null);
            if (free < 0)
                return false;
            this.Slots[free] = item;
            return true;
        }

        public ItemInstance? Remove(string instanceId)
        {
            int idx = this.IndexOf(instanceId);
            if (idx < 0)
                return null;
            var item = this.Slots[idx];
            this.Slots[idx] = null;
            return item;
        }

        public void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= this.Size || b >= this.Size)
                throw new ArgumentOutOfRangeException(nameof(a));
            (this.Slots[a], this.Slots[b]) = (this.Slots[b], this.Slots[a]);
        }

        /// <summary>Sort: favourites first, then by rarity (desc), then by definition id; empty slots last.</summary>
        public void Sort()
        {
            var sorted = this.Items.OrderByDescending(i => i.Favorite).ThenByDescending(i => i.Rarity).ThenBy(i => i.DefId, StringComparer.Ordinal).ThenBy(i => i.InstanceId, StringComparer.Ordinal).ToList();
            for (int i = 0; i < this.Size; i++)
                this.Slots[i] = i < sorted.Count ? sorted[i] : null;
        }
    }
}
