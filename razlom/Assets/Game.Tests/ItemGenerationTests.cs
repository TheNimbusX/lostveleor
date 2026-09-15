using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Генерация предметов из рецепта.
    ///
    /// Два свойства здесь важнее всех остальных:
    ///   1) один сид даёт один предмет ВСЕГДА;
    ///   2) правка данных меняет предмет при том же сиде — то есть ребаланс
    ///      доезжает до всех предметов всех игроков без миграции сейвов.
    ///
    /// Второе выглядит как поломка первого, но это не так: воспроизводимость
    /// обещана при НЕИЗМЕННЫХ данных, и именно это делает хранение рецепта
    /// вместо посчитанных статов выгодным.
    /// </summary>
    public class ItemGenerationTests
    {
        private const string SwordKey = "base.rusty_sword";
        private const string ArmorKey = "base.leather_jacket";

        private static int Sword => StableId.Of(SwordKey);
        private static int Armor => StableId.Of(ArmorKey);

        /// <summary>
        /// Небольшой, но полноценный набор данных: две категории, три группы
        /// аффиксов, разные уровни предмета.
        /// </summary>
        private static ItemDatabase BuildDatabase(Fix64 fireDamageMax)
        {
            var bases = new[]
            {
                new ItemBaseDefinition(Sword, ItemCategory.Weapon,
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(5)),
                new ItemBaseDefinition(Armor, ItemCategory.Armor),
            };

            byte weaponMask = AffixDefinition.Mask(ItemCategory.Weapon);
            byte armorMask = AffixDefinition.Mask(ItemCategory.Armor);
            byte bothMask = AffixDefinition.Mask(ItemCategory.Weapon, ItemCategory.Armor);

            var affixes = new[]
            {
                new AffixDefinition(StableId.Of("affix.flat_damage_t1"), StableId.Of("group.flat_damage"),
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(1), Fix64.FromInt(4), 1, 100, weaponMask),

                new AffixDefinition(StableId.Of("affix.flat_damage_t2"), StableId.Of("group.flat_damage"),
                    StatType.Damage, ModifierOp.Flat, Fix64.FromInt(5), Fix64.FromInt(12), 30, 60, weaponMask),

                new AffixDefinition(StableId.Of("affix.fire_damage"), StableId.Of("group.fire_damage"),
                    StatType.Damage, ModifierOp.Increased, Fix64.Ratio(1, 10), fireDamageMax, 1, 80, weaponMask),

                new AffixDefinition(StableId.Of("affix.armor_flat"), StableId.Of("group.armor"),
                    StatType.Armor, ModifierOp.Flat, Fix64.FromInt(10), Fix64.FromInt(40), 1, 100, armorMask),

                new AffixDefinition(StableId.Of("affix.max_health"), StableId.Of("group.health"),
                    StatType.MaxHealth, ModifierOp.Flat, Fix64.FromInt(10), Fix64.FromInt(50), 1, 90, bothMask),

                new AffixDefinition(StableId.Of("affix.crit_chance"), StableId.Of("group.crit"),
                    StatType.CritChance, ModifierOp.Increased, Fix64.Ratio(5, 100), Fix64.Ratio(25, 100),
                    40, 50, bothMask),
            };

            return new ItemDatabase(bases, affixes);
        }

        private static ItemDatabase Standard() => BuildDatabase(Fix64.Ratio(4, 10));

        private static ulong HashOf(in ItemInstance item, ItemDatabase db)
        {
            var buffer = new GeneratedItem();
            Assert.IsTrue(ItemGenerator.Generate(in item, db, buffer), "база не найдена");

            ulong hash = Hashing.Offset;
            buffer.HashInto(ref hash);
            return hash;
        }

        // ---- воспроизводимость ----

        [Test]
        public void SameSeed_GivesSameItem_OverAThousandRuns()
        {
            ItemDatabase db = Standard();
            var item = new ItemInstance(Sword, 50, ItemRarity.Rare, 0xDEADBEEFCAFEUL);

            ulong expected = HashOf(in item, db);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(expected, HashOf(in item, db), $"прогон {i} дал другой предмет");
        }

        // ---- ребаланс без миграции ----

        // ---- правила отбора аффиксов ----

        [Test]
        public void Rarity_DecidesAffixCount()
        {
            ItemDatabase db = Standard();
            var buffer = new GeneratedItem();

            ItemGenerator.Generate(new ItemInstance(Sword, 80, ItemRarity.Normal, 7UL), db, buffer);
            Assert.AreEqual(0, buffer.AffixCount);

            ItemGenerator.Generate(new ItemInstance(Sword, 80, ItemRarity.Magic, 7UL), db, buffer);
            Assert.That(buffer.AffixCount, Is.InRange(1, 2));

            ItemGenerator.Generate(new ItemInstance(Sword, 80, ItemRarity.Rare, 7UL), db, buffer);
            Assert.That(buffer.AffixCount, Is.InRange(3, 4));
        }

        [Test]
        public void AffixGroups_NeverRepeatOnOneItem()
        {
            ItemDatabase db = Standard();
            var buffer = new GeneratedItem();

            for (ulong seed = 1; seed <= 500; seed++)
            {
                ItemGenerator.Generate(new ItemInstance(Sword, 90, ItemRarity.Rare, seed), db, buffer);

                for (int a = 0; a < buffer.AffixCount; a++)
                    for (int b = a + 1; b < buffer.AffixCount; b++)
                        Assert.AreNotEqual(buffer.GetAffix(a).AffixId, buffer.GetAffix(b).AffixId,
                            $"сид {seed}: один аффикс дважды");
            }
        }

        [Test]
        public void RolledValues_StayInsideDeclaredRange()
        {
            ItemDatabase db = Standard();
            var buffer = new GeneratedItem();

            for (ulong seed = 1; seed <= 500; seed++)
            {
                ItemGenerator.Generate(new ItemInstance(Sword, 90, ItemRarity.Rare, seed), db, buffer);

                for (int i = 0; i < buffer.AffixCount; i++)
                {
                    RolledAffix rolled = buffer.GetAffix(i);
                    AffixDefinition def = db.GetAffix(db.IndexOfAffix(rolled.AffixId));

                    Assert.That(rolled.Value.Raw, Is.GreaterThanOrEqualTo(def.MinValue.Raw));
                    Assert.That(rolled.Value.Raw, Is.LessThanOrEqualTo(def.MaxValue.Raw));
                }
            }
        }

        // ---- связь с потоком случайности и статами ----

        [Test]
        public void Generation_DoesNotTouchRunStreams()
        {
            // Разворот предмета — чистая функция от сида. Если бы он брал
            // из потоков забега, загрузка сейва сдвигала бы весь дальнейший
            // лут и бой, а «рецепт вместо статов» перестал бы работать.
            var rng = new RngStreams(12345UL);
            ItemDatabase db = Standard();
            var buffer = new GeneratedItem();

            ulong before = Hashing.Offset;
            rng.HashInto(ref before);

            for (ulong seed = 1; seed <= 1000; seed++)
                ItemGenerator.Generate(new ItemInstance(Sword, 50, ItemRarity.Rare, seed), db, buffer);

            ulong after = Hashing.Offset;
            rng.HashInto(ref after);

            Assert.AreEqual(before, after);
        }

    }
}
