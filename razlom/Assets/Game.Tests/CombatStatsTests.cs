using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Статы решают бой.
    ///
    /// Приёмка задачи: надетый предмет меняет числа в бою, снятый возвращает их
    /// ровно назад, а порядок надевания на результат не влияет.
    ///
    /// Первый тест здесь — сторожевой. Перенос боевых констант в базы статов
    /// не имел права поменять ни одной цифры, и если он что-то поменял, узнать
    /// об этом надо здесь, а не по ощущению «враги стали дохлее».
    /// </summary>
    public class CombatStatsTests
    {
        private static ItemInstance Sword(ulong seed, short itemLevel = 20)
            => new ItemInstance(StableId.Of("base.rusty_sword"), itemLevel, ItemRarity.Rare, seed);

        private static ItemInstance Jacket(ulong seed, short itemLevel = 20)
            => new ItemInstance(StableId.Of("base.leather_jacket"), itemLevel, ItemRarity.Rare, seed);

        private static Equipment Bound(ItemDatabase items, StatSheet sheet)
        {
            var equipment = new Equipment(items);
            equipment.Bind(sheet);
            return equipment;
        }

        private static Simulation Arena(ulong seed = 1UL, int enemies = 4)
        {
            var sim = new Simulation(seed, 64);
            sim.SetupTestArena(enemies);
            return sim;
        }

        // ---- сторож ----

        [Test]
        public void MovingCombatNumbersIntoStats_ChangedNothing()
        {
            Simulation sim = Arena();
            EntityStore e = sim.Entities;

            Assert.AreEqual(34, e.Damage[Simulation.PlayerId], "урон игрока");
            Assert.AreEqual(7, e.Damage[1], "урон врага");
            Assert.AreEqual(24, e.AttackCooldown[Simulation.PlayerId], "кулдаун игрока в тиках");
            Assert.AreEqual(36, e.AttackCooldown[1], "кулдаун врага в тиках");
            Assert.AreEqual(1000, e.MaxHealth[Simulation.PlayerId], "здоровье игрока");
            Assert.AreEqual(100, e.MaxHealth[1], "здоровье врага");

            // Побитово, а не «примерно»: шаг за тик участвует в позициях, а те —
            // в хеше состояния. Разойдись он в младшем разряде, реплеи бы поехали.
            Assert.AreEqual(Fix64.Ratio(6, 30).Raw, e.MoveStep[Simulation.PlayerId].Raw,
                "шаг игрока за тик");
            Assert.AreEqual(Fix64.Ratio(35, 300).Raw, e.MoveStep[1].Raw, "шаг врага за тик");

            Assert.AreEqual(Fix64.Ratio(15, 100).Raw, e.CritChance[Simulation.PlayerId].Raw, "шанс крита");
            Assert.AreEqual(Fix64.FromInt(2).Raw, e.CritMultiplier[Simulation.PlayerId].Raw,
                "множитель крита");
        }

        // ---- перевод статов в числа тика ----

        [Test]
        public void AttackSpeed_TurnsIntoCooldownTicks()
        {
            Assert.AreEqual(24, CombatStats.AttackCooldownTicks(Fix64.Ratio(30, 24)),
                "1.25 удара в секунду при 30 Гц — это 24 тика");
            Assert.AreEqual(36, CombatStats.AttackCooldownTicks(Fix64.Ratio(30, 36)),
                "0.833 удара в секунду — это 36 тиков");
        }

        [Test]
        public void AttackSpeed_IsClampedAtBothEnds()
        {
            Assert.AreEqual(CombatStats.MaxAttackCooldown, CombatStats.AttackCooldownTicks(Fix64.Zero),
                "нулевая скорость атаки не имеет права разделить на ноль");
            Assert.AreEqual(1, CombatStats.AttackCooldownTicks(Fix64.FromInt(1000)),
                "чаще одного удара за тик не бьёт никто: тик неделим");
        }

        [Test]
        public void IncreasedAttackSpeed_ShortensTheCooldown()
        {
            Simulation sim = Arena();
            StatSheet sheet = sim.Entities.Stats[Simulation.PlayerId];

            sheet.Add(StatModifier.Increased(StatType.AttackSpeed, Fix64.Ratio(25, 100),
                ModifierSource.Equipment, 0));
            sim.RefreshPlayerStats(false);

            // 1.25 * 1.25 = 1.5625 удара в секунду, 30 / 1.5625 = 19.2 тика.
            Assert.AreEqual(19, sim.Entities.AttackCooldown[Simulation.PlayerId]);
        }

        // ---- снаряжение ----

        [Test]
        public void EquippedItem_ChangesCombatNumbers()
        {
            Simulation sim = Arena();
            var equipment = Bound(PrototypeContent.Items(),
                sim.Entities.Stats[Simulation.PlayerId]);

            int bare = sim.Entities.Damage[Simulation.PlayerId];

            ItemInstance replaced;
            Assert.IsTrue(equipment.Equip(Sword(0xABCDEF01UL), out replaced), "меч должен надеться");
            Assert.IsTrue(replaced.IsEmpty, "в пустом слоте нечего было менять");

            sim.RefreshPlayerStats(false);

            Assert.Greater(sim.Entities.Damage[Simulation.PlayerId], bare,
                "надетое оружие обязано менять урон, иначе весь лут — украшение");
        }

        [Test]
        public void Unequip_RestoresTheSheetExactly()
        {
            Simulation sim = Arena();
            StatSheet sheet = sim.Entities.Stats[Simulation.PlayerId];
            var equipment = Bound(PrototypeContent.Items(), sheet);

            int bareDamage = sim.Entities.Damage[Simulation.PlayerId];
            ulong bareHash = Hashing.Offset;
            sheet.HashInto(ref bareHash);

            ItemInstance replaced;
            equipment.Equip(Sword(7UL), out replaced);
            sim.RefreshPlayerStats(false);

            equipment.Unequip(EquipSlot.Weapon);
            sim.RefreshPlayerStats(false);

            ulong afterHash = Hashing.Offset;
            sheet.HashInto(ref afterHash);

            Assert.AreEqual(bareDamage, sim.Entities.Damage[Simulation.PlayerId], "урон вернулся");
            Assert.AreEqual(bareHash, afterHash,
                "снятие обязано убирать ровно свои прибавки и ничего кроме них");
        }

        [Test]
        public void EquipOrder_DoesNotAffectResult()
        {
            ItemDatabase items = PrototypeContent.Items();
            ItemInstance sword = Sword(77UL);
            ItemInstance jacket = Jacket(88UL);
            ItemInstance replaced;

            Simulation first = Arena(9UL, 2);
            var a = Bound(items, first.Entities.Stats[Simulation.PlayerId]);
            a.Equip(sword, out replaced);
            a.Equip(jacket, out replaced);
            first.RefreshPlayerStats(false);

            Simulation second = Arena(9UL, 2);
            var b = Bound(items, second.Entities.Stats[Simulation.PlayerId]);
            b.Equip(jacket, out replaced);
            b.Equip(sword, out replaced);
            second.RefreshPlayerStats(false);

            ulong hashA = Hashing.Offset;
            first.Entities.Stats[Simulation.PlayerId].HashInto(ref hashA);

            ulong hashB = Hashing.Offset;
            second.Entities.Stats[Simulation.PlayerId].HashInto(ref hashB);

            // Умножение в Fix64 округляет и не ассоциативно. Если бы список
            // модификаторов шёл в порядке действий игрока, два одинаковых билда
            // считали бы разный урон — и разошлись бы в реплее.
            Assert.AreEqual(hashA, hashB, "лист статов обязан быть побитово одинаковым");
            Assert.AreEqual(first.Entities.Damage[Simulation.PlayerId],
                second.Entities.Damage[Simulation.PlayerId], "урон одинаков");
        }

        [Test]
        public void ReplacingAnItem_ReturnsThePreviousOne()
        {
            Simulation sim = Arena();
            var equipment = Bound(PrototypeContent.Items(),
                sim.Entities.Stats[Simulation.PlayerId]);

            ItemInstance replaced;
            equipment.Equip(Sword(1UL), out replaced);
            equipment.Equip(Sword(2UL), out replaced);

            Assert.AreEqual(1UL, replaced.Seed, "замена обязана отдать прежний предмет");
            Assert.AreEqual(2UL, equipment.Worn(EquipSlot.Weapon).Seed, "в слоте лежит новый");
        }

        [Test]
        public void UnknownBase_IsNotEquipped()
        {
            Simulation sim = Arena();
            var equipment = Bound(PrototypeContent.Items(),
                sim.Entities.Stats[Simulation.PlayerId]);

            ItemInstance replaced;
            var alien = new ItemInstance(StableId.Of("base.who_is_this"), 1, ItemRarity.Rare, 5UL);

            Assert.IsFalse(equipment.Equip(alien, out replaced),
                "чужой предмет лучше не надеть, чем надеть пустым");
            Assert.IsFalse(equipment.IsWorn(EquipSlot.Weapon));
        }

        // ---- здоровье ----

        [Test]
        public void MaxHealthBonus_DoesNotHeal()
        {
            Simulation sim = Arena();
            sim.Entities.Health[Simulation.PlayerId] = 500;

            sim.Entities.Stats[Simulation.PlayerId].Add(StatModifier.Flat(StatType.MaxHealth,
                Fix64.FromInt(200), ModifierSource.Equipment, 1));
            sim.RefreshPlayerStats(false);

            Assert.AreEqual(1200, sim.Entities.MaxHealth[Simulation.PlayerId], "максимум вырос");
            Assert.AreEqual(500, sim.Entities.Health[Simulation.PlayerId],
                "надетая посреди боя вещь не должна работать зельем");
        }

        [Test]
        public void LosingMaxHealth_ClampsCurrentHealth()
        {
            Simulation sim = Arena();
            StatSheet sheet = sim.Entities.Stats[Simulation.PlayerId];

            sheet.Add(StatModifier.Flat(StatType.MaxHealth, Fix64.FromInt(200),
                ModifierSource.Equipment, 1));
            sim.RefreshPlayerStats(true);
            Assert.AreEqual(1200, sim.Entities.Health[Simulation.PlayerId]);

            sheet.RemoveSource(ModifierSource.Equipment, 1);
            sim.RefreshPlayerStats(false);

            Assert.AreEqual(1000, sim.Entities.MaxHealth[Simulation.PlayerId]);
            Assert.AreEqual(1000, sim.Entities.Health[Simulation.PlayerId],
                "текущее здоровье обязано подрезаться под новый максимум");
        }

        // ---- атака по приказу ----

        [Test]
        public void Player_DoesNotAttackWithoutTheOrder()
        {
            Simulation sim = Arena(31UL, 6);
            int enemyHealth = sim.Entities.Health[1];

            for (int t = 0; t < 300; t++)
            {
                InputFrame frame = InputFrame.Empty;
                sim.Step(in frame);
            }

            int totalDamage = 0;
            for (int i = 1; i < sim.Entities.Count; i++)
                totalDamage += sim.Entities.MaxHealth[i] - sim.Entities.Health[i];

            Assert.AreEqual(0, totalDamage,
                "персонаж, который бьёт сам по себе, отнимает у игрока единственное решение боя");
            Assert.AreEqual(enemyHealth, sim.Entities.Health[1]);
        }

        [Test]
        public void Player_AttacksWhileTheOrderIsHeld()
        {
            Simulation sim = Arena(31UL, 6);

            // Ставим врага вплотную перед игроком: взгляд по умолчанию по оси X,
            // дальность автоатаки два метра.
            int victim = sim.Entities.Spawn(new FixVec2(Fix64.One, Fix64.Zero), 1000, Faction.Orvill);

            var order = new InputFrame { Flags = (byte)InputFlags.Attack };
            for (int t = 0; t < 120; t++) sim.Step(in order);

            Assert.Less(sim.Entities.Health[victim], 1000, "по приказу персонаж бьёт");
        }

        // ---- приказ атаковать ----

        [Test]
        public void AttackOrder_KeepsHittingAfterTheClick()
        {
            Simulation sim = Arena(51UL, 0);

            // Враг прямо перед игроком: взгляд по умолчанию по оси X.
            int victim = sim.Entities.Spawn(new FixVec2(Fix64.One, Fix64.Zero), 5000, Faction.Orvill);

            // ОДИН кадр с приказом — дальше игрок кнопку не трогает.
            var order = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = victim,
            };
            sim.Step(in order);

            for (int t = 0; t < 200; t++)
            {
                InputFrame idle = InputFrame.Empty;
                sim.Step(in idle);
            }

            Assert.Less(sim.Entities.Health[victim], 5000,
                "щёлкнул по врагу — персонаж бьёт сам, без удержания кнопки");
            Assert.AreEqual(victim, sim.AttackTarget, "и цель всё ещё назначена");
        }

        [Test]
        public void AttackOrder_EndsWithTheTarget()
        {
            Simulation sim = Arena(52UL, 0);
            int victim = sim.Entities.Spawn(new FixVec2(Fix64.One, Fix64.Zero), 40, Faction.Orvill);

            var order = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = victim,
            };
            sim.Step(in order);

            for (int t = 0; t < 300; t++)
            {
                InputFrame idle = InputFrame.Empty;
                sim.Step(in idle);
            }

            Assert.IsFalse(sim.Entities.Alive[victim], "цель добита");
            Assert.AreEqual(-1, sim.AttackTarget, "приказ снят вместе с целью");
        }

        [Test]
        public void MoveOrder_CancelsTheAttackOrder()
        {
            Simulation sim = Arena(53UL, 0);
            int victim = sim.Entities.Spawn(new FixVec2(Fix64.One, Fix64.Zero), 5000, Faction.Orvill);

            var attack = new InputFrame
            {
                Flags = (byte)InputFlags.Attack,
                AttackTarget = victim,
            };
            sim.Step(in attack);
            Assert.AreEqual(victim, sim.AttackTarget);

            // Щелчок по земле — игрок передумал.
            var walk = new InputFrame
            {
                Aim = new FixVec2(Fix64.FromInt(-8), Fix64.Zero),
                Flags = (byte)InputFlags.MoveOrder,
            };
            sim.Step(in walk);

            Assert.AreEqual(-1, sim.AttackTarget, "приказ идти отменяет приказ бить");
        }

        // ---- вес движения ----

        [Test]
        public void Movement_HasWeight_InsteadOfInstantSpeed()
        {
            Simulation sim = Arena(43UL, 0);

            var order = new InputFrame
            {
                Aim = new FixVec2(Fix64.FromInt(30), Fix64.Zero),
                Flags = (byte)InputFlags.MoveOrder,
            };

            sim.Step(in order);
            float firstTick = sim.Entities.Velocity[Simulation.PlayerId].Length.ToFloat();

            for (int t = 0; t < 12; t++) sim.Step(in order);
            float cruise = sim.Entities.Velocity[Simulation.PlayerId].Length.ToFloat();

            Assert.Less(firstTick, cruise * 0.5f,
                "с места тело не выпрыгивает на полную скорость: это и есть вес");
            Assert.Greater(cruise, 0.15f,
                "но за доли секунды доходит до полной — задержка не должна читаться");
        }

        [Test]
        public void Movement_SlowsDownInsteadOfStoppingDead()
        {
            Simulation sim = Arena(44UL, 0);

            var order = new InputFrame
            {
                Aim = new FixVec2(Fix64.FromInt(30), Fix64.Zero),
                Flags = (byte)InputFlags.MoveOrder,
            };
            for (int t = 0; t < 20; t++) sim.Step(in order);

            float cruise = sim.Entities.Velocity[Simulation.PlayerId].Length.ToFloat();

            // Приказ отменяем, поставив цель под ноги: тело обязано гасить ход,
            // а не выключаться.
            var stop = new InputFrame
            {
                Aim = sim.Entities.Position[Simulation.PlayerId],
                Flags = (byte)InputFlags.MoveOrder,
            };
            sim.Step(in stop);

            float justAfter = sim.Entities.Velocity[Simulation.PlayerId].Length.ToFloat();

            Assert.Greater(justAfter, 0f, "мгновенная остановка — это отсутствие тела");
            Assert.Less(justAfter, cruise, "но ход гасится");
        }

        // ---- расталкивание тел ----

        [Test]
        public void Bodies_DoNotStackInOnePoint()
        {
            var sim = new Simulation(7UL, 32);
            sim.SetupTestArena(0);

            // Игрок «мёртв», чтобы враги никуда не шли: проверяется именно
            // расталкивание, а не движение к цели.
            sim.Entities.Alive[Simulation.PlayerId] = false;

            var spot = new FixVec2(Fix64.FromInt(5), Fix64.Zero);
            int a = sim.Entities.Spawn(spot, 100, Faction.Orvill);
            int b = sim.Entities.Spawn(spot, 100, Faction.Orvill);

            for (int t = 0; t < 120; t++)
            {
                InputFrame frame = InputFrame.Empty;
                sim.Step(in frame);
            }

            float distance = FixVec2.Distance(sim.Entities.Position[a],
                sim.Entities.Position[b]).ToFloat();

            Assert.Greater(distance, 0.7f,
                "два тела в одной точке обязаны разъехаться, иначе толпа не читается");
        }

        [Test]
        public void ThePlayerIsHeavierThanTheCrowd()
        {
            var sim = new Simulation(8UL, 32);
            sim.SetupTestArena(0);

            FixVec2 start = sim.Entities.Position[Simulation.PlayerId];
            int enemy = sim.Entities.Spawn(start, 100, Faction.Orvill);

            for (int t = 0; t < 60; t++)
            {
                InputFrame frame = InputFrame.Empty;
                sim.Step(in frame);
            }

            float playerMoved = FixVec2.Distance(start, sim.Entities.Position[Simulation.PlayerId]).ToFloat();
            float enemyMoved = FixVec2.Distance(start, sim.Entities.Position[enemy]).ToFloat();

            Assert.Greater(enemyMoved, playerMoved,
                "сорок тел не должны возить по арене того, кто ими управляет");
        }

        // ---- приказ на движение ----

        [Test]
        public void MoveOrder_SurvivesTheReleasedButton()
        {
            Simulation sim = Arena(41UL, 0);

            var order = new InputFrame
            {
                Aim = new FixVec2(Fix64.FromInt(10), Fix64.Zero),
                Flags = (byte)InputFlags.MoveOrder,
            };
            sim.Step(in order);

            // Кнопку отпустили сразу. Персонаж обязан идти дальше сам:
            // это управление жанра, щелчок задаёт цель, а не толкает на шаг.
            for (int t = 0; t < 60; t++)
            {
                InputFrame idle = InputFrame.Empty;
                sim.Step(in idle);
            }

            Assert.Greater(sim.Entities.Position[Simulation.PlayerId].X.ToFloat(), 5f,
                "щёлкнул один раз — идёт дальше без кнопки");
        }

        [Test]
        public void MoveOrder_EndsOnArrival()
        {
            Simulation sim = Arena(41UL, 0);

            var order = new InputFrame
            {
                Aim = new FixVec2(Fix64.FromInt(6), Fix64.Zero),
                Flags = (byte)InputFlags.MoveOrder,
            };
            sim.Step(in order);

            for (int t = 0; t < 200; t++)
            {
                InputFrame idle = InputFrame.Empty;
                sim.Step(in idle);
            }

            FixVec2 dummy;
            Assert.IsFalse(sim.TryGetMoveOrder(out dummy), "дошёл — приказа больше нет");
            Assert.AreEqual(0f, sim.Entities.Velocity[Simulation.PlayerId].LengthSq.ToFloat(),
                "и стоит, а не топчется вокруг точки");
        }

        // ---- защита ----

        [Test]
        public void Armor_MitigatesSmallHitsMuchMoreThanBigOnes()
        {
            // Ровно та причина, по которой выбрана кривая, а не плоский процент:
            // одна и та же броня спасает от роя и почти не мешает удару босса.
            Assert.AreEqual(1, CombatStats.MitigateByArmor(10, Fix64.FromInt(500)),
                "мелкий удар при броне 500 гасится почти целиком");
            Assert.AreEqual(909, CombatStats.MitigateByArmor(1000, Fix64.FromInt(500)),
                "крупный удар при той же броне гасится на девять процентов");
        }

        [Test]
        public void Armor_NeverFullyNegatesAHit()
        {
            Assert.GreaterOrEqual(CombatStats.MitigateByArmor(1, Fix64.FromInt(1000000)), 1,
                "ноль на экране был бы следом округления, а не балансом");
        }

        [Test]
        public void ZeroArmor_ChangesNothing()
        {
            Assert.AreEqual(34, CombatStats.MitigateByArmor(34, Fix64.Zero));
        }

        [Test]
        public void Resistance_IsCappedAtThreeQuarters()
        {
            Assert.AreEqual(70, CombatStats.MitigateByResistance(100, Fix64.Ratio(30, 100)),
                "30% сопротивления снимают тридцать процентов");
            Assert.AreEqual(25, CombatStats.MitigateByResistance(100, Fix64.One),
                "сто процентов сопротивления обрезаются до потолка в 75%");
            Assert.AreEqual(25, CombatStats.MitigateByResistance(100, Fix64.FromInt(4)),
                "выше потолка не пускает никакая сумма аффиксов");
        }

        [Test]
        public void NegativeResistance_DoesNotAmplifyDamage()
        {
            Assert.AreEqual(100, CombatStats.MitigateByResistance(100, -Fix64.Ratio(50, 100)),
                "механики пробития в проекте нет, и заводить её походя нельзя");
        }

        [Test]
        public void FireResistance_ProtectsFromBurningButArmorDoesNot()
        {
            Assert.AreEqual(50,
                CombatStats.Mitigate(100, DamageType.Fire, Fix64.FromInt(9999), Fix64.Ratio(50, 100)),
                "огонь гасит сопротивление, а броня в нём не участвует");
            Assert.AreEqual(100,
                CombatStats.Mitigate(100, DamageType.Physical, Fix64.Zero, Fix64.Ratio(90, 100)),
                "сопротивление огню не мешает мечу");
        }

        [Test]
        public void WornArmor_ReducesIncomingDamage()
        {
            int Lost(bool armored)
            {
                Simulation sim = Arena(21UL, 8);
                if (armored)
                {
                    sim.Entities.Stats[Simulation.PlayerId].Add(StatModifier.Flat(StatType.Armor,
                        Fix64.FromInt(60), ModifierSource.Equipment, (int)EquipSlot.Armor));
                    sim.RefreshPlayerStats(false);
                }

                for (int t = 0; t < 600; t++)
                {
                    InputFrame frame = InputFrame.Empty;
                    sim.Step(in frame);
                }
                return 1000 - sim.Entities.Health[Simulation.PlayerId];
            }

            int bare = Lost(false);
            int armored = Lost(true);

            Assert.Greater(bare, 0, "без брони игрока обязаны бить, иначе тест ничего не проверяет");
            Assert.Less(armored, bare, "надетая броня обязана уменьшать входящий урон: " +
                bare + " -> " + armored);
        }

        // ---- цена пересчёта ----

        [Test]
        public void CleanSheets_AreNotRecalculatedEveryTick()
        {
            Simulation sim = Arena(5UL, 8);
            StatSheet sheet = sim.Entities.Stats[Simulation.PlayerId];

            int before = sheet.RecalculateCount;
            for (int t = 0; t < 60; t++)
            {
                InputFrame frame = InputFrame.Empty;
                sim.Step(in frame);
            }

            Assert.AreEqual(before, sheet.RecalculateCount,
                "чистый лист не пересчитывается: именно на этом ARPG и начинают тормозить");
        }

        [Test]
        public void DirtySheet_IsRecalculatedOnceAndThenLeftAlone()
        {
            Simulation sim = Arena(5UL, 8);
            StatSheet sheet = sim.Entities.Stats[Simulation.PlayerId];

            sheet.Add(StatModifier.Flat(StatType.Damage, Fix64.FromInt(10),
                ModifierSource.Buff, 0));
            int before = sheet.RecalculateCount;

            for (int t = 0; t < 30; t++)
            {
                InputFrame frame = InputFrame.Empty;
                sim.Step(in frame);
            }

            Assert.AreEqual(before + 1, sheet.RecalculateCount, "ровно один пересчёт на одну правку");
            Assert.AreEqual(44, sim.Entities.Damage[Simulation.PlayerId],
                "правка листа доехала до чисел боя сама, без ручного обновления");
        }

        // ---- забег ----

        [Test]
        public void Equipment_SurvivesEnteringTheNextRift()
        {
            ItemDatabase items = PrototypeContent.Items();

            var sim = new Simulation(4242UL, 1024);
            var run = new RiftRun(sim, PrototypeContent.Modules(), items,
                PrototypeContent.ItemBaseIds());

            var equipment = Bound(items, sim.Entities.Stats[Simulation.PlayerId]);
            ItemInstance replaced;
            equipment.Equip(Sword(0x5EEDUL), out replaced);
            run.PlayerEquipment = equipment;

            run.StartRun();

            int armedDamage = sim.Entities.Damage[Simulation.PlayerId];
            Assert.Greater(armedDamage, 34, "в первом Разломе оружие уже работает");

            // Зачищаем напрямую: проверяется петля со снаряжением, а не бой.
            for (int i = 0; i < sim.Entities.Count; i++)
                if (sim.Entities.Side[i] != Faction.Wole) sim.Entities.Alive[i] = false;

            InputFrame idle = InputFrame.Empty;
            run.Step(in idle);
            Assert.AreEqual(RunPhase.ChoosingReward, run.Phase);

            var pick = new InputFrame { Command = (byte)RunCommand.ChooseReward1 };
            run.Step(in pick);

            Assert.AreEqual(2, run.Depth, "начался следующий Разлом");
            Assert.GreaterOrEqual(sim.Entities.Damage[Simulation.PlayerId], armedDamage,
                "вход в Разлом рождает игрока заново — снаряжение обязано вернуться на лист");
            Assert.AreEqual(sim.Entities.MaxHealth[Simulation.PlayerId],
                sim.Entities.Health[Simulation.PlayerId],
                "в новый Разлом игрок входит с полным здоровьем");
        }

        [Test]
        public void SameSeed_GivesTheSameRun_WithEquipment()
        {
            ItemDatabase items = PrototypeContent.Items();

            RiftRun Armed(ulong seed)
            {
                var sim = new Simulation(seed, 1024);
                var run = new RiftRun(sim, PrototypeContent.Modules(), items,
                    PrototypeContent.ItemBaseIds());

                var equipment = Bound(items, sim.Entities.Stats[Simulation.PlayerId]);
                ItemInstance replaced;
                equipment.Equip(Sword(0x5EEDUL), out replaced);
                run.PlayerEquipment = equipment;

                run.StartRun();
                return run;
            }

            RiftRun a = Armed(31337UL);
            RiftRun b = Armed(31337UL);

            for (int t = 0; t < 600; t++)
            {
                InputFrame frame = InputFrame.Empty;
                a.Step(in frame);
                b.Step(in frame);
                Assert.AreEqual(a.Hash(), b.Hash(), $"забеги разошлись на тике {t}");
            }
        }
    }
}
