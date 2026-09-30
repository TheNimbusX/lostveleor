using System.IO;
using Game.Sim;

namespace Game.Tests
{
    // Фикстура старого формата не обрезает текущий: v9 меняет рецепт каждого предмета.
    internal static class LegacyCampSaveFixture
    {
        internal static byte[] Encode(Camp camp, int version)
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(0x43575254); w.Write(version); w.Write(camp.Act); w.Write(camp.Bag.Capacity);
                for (int i = 0; i < (int)CurrencyType.Count; i++) w.Write(camp.Money((CurrencyType)i));
                for (int i = 0; i < camp.Bag.Capacity; i++) { Item(w, camp.Bag.At(i)); w.Write(camp.Bag.IsKept(i)); }
                for (int i = 0; i < (int)EquipSlot.Count; i++) Item(w, camp.Worn.Worn((EquipSlot)i));
                w.Write(camp.Level); w.Write(camp.Experience);
                w.Write(camp.TraderGeneration); w.Write(camp.TraderBossStock); w.Write(camp.TraderStockCount);
                for (int i = 0; i < camp.TraderStockCount; i++) Item(w, camp.TraderStock(i));
                if (version >= 8)
                {
                    for (int i = 0; i < 6; i++) w.Write(camp.PotionCount((PotionKind)i));
                    w.Write((byte)camp.SelectedPotion(0)); w.Write((byte)camp.SelectedPotion(1));
                }
                else if (version >= 6)
                { for (int i = 0; i < 4; i++) w.Write(camp.PotionCount((PotionKind)i)); w.Write(camp.PotionSelection); }
                if (version >= 7)
                { w.Write(camp.DiscoveredCount); for (int i = 0; i < camp.DiscoveredCount; i++) w.Write(camp.DiscoveredAt(i)); }
                if (version >= 8)
                { w.Write(camp.HasMetAlchemist); w.Write((byte)camp.AlchemyStatus(AlchemistOrder.Resin)); w.Write((byte)camp.AlchemyStatus(AlchemistOrder.Surge)); }
                w.Flush(); var payload = stream.ToArray(); uint checksum = 2166136261;
                foreach (byte value in payload) { checksum ^= value; checksum = unchecked(checksum * 16777619); }
                w.Write(checksum); w.Flush(); return stream.ToArray();
            }
        }
        static void Item(BinaryWriter w, ItemInstance item)
        { w.Write(item.BaseId); w.Write(item.ItemLevel); w.Write((byte)item.Rarity); w.Write(item.Seed); w.Write(item.ForgeRecipe); }
    }
}
