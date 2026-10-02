using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Сценарии для пинов «формы выключены — забег прежний» (план форм Пелага, 02.10).
    ///
    /// ЗДЕСЬ ТОЛЬКО API, КОТОРЫЙ БЫЛ ДО ФОРМ: эти же функции собраны против кода
    /// до правки, и их значения прибиты в FormPinTests. Сдвинулся пин — сдвинулись
    /// награды обычного забега; причину искать в правке, а не переснимать число.
    ///
    /// Без NUnit намеренно: сборка «до правки» компилирует этот файл отдельно.
    /// Награды здесь не зависят от боя: враги снимаются, герой ставится у выхода.
    /// </summary>
    internal static class FormBaselineScenarios
    {
        public const int ArenaCount = 8;

        /// <summary>Восемь простых арен (без шаблонов встреч) и, по желанию, босс девятым уровнем.</summary>
        public static LocationDefinition Location(bool boss)
        {
            int count = boss ? ArenaCount + 1 : ArenaCount;
            var levels = new RiftLevelSettings[count];
            for (int i = 0; i < count; i++)
                levels[i] = boss && i == ArenaCount
                    ? new RiftLevelSettings(20, 1, 1, 2, 1, 3, 100, BossEncounters(), true, playerHealth: 150,
                        entryClearance: 14, solidEnvironment: true, naturalGlade: true).WithArenaSize(4)
                    : new RiftLevelSettings(12, 1, 0, 0, 1, 2, 100, arenaSize: 3);
            return new LocationDefinition(StableId.Of("location.test-forms"), PrototypeContent.Modules(), levels,
                64, completeAtEnd: true);
        }

        private static EncounterSettings BossEncounters()
        {
            var guardian = new EncounterGroup(EnemyKind.ForestGuardian, 1, 1);
            return new EncounterSettings(new[] { new EncounterPack(1, 100, new[] { guardian }) },
                new[] { new EncounterPack(2, 100, new[] { guardian }) }, new[] { new EncounterPack(3, 100, new[] { guardian }) },
                new[] { new EncounterPack(4, 100, new[] { guardian }) },
                1, 0, EnemyArchetypes.DepthDamagePercent(ArenaCount + 1), Fix64.FromInt(5));
        }

        /// <summary>Забег по Location(boss); StartRun — за вызывающим (сначала включатели).</summary>
        public static RiftRun NewArenaRun(ulong seed, bool boss = true)
        {
            LocationDefinition location = Location(boss);
            return new RiftRun(new Simulation(seed, 1024), location.Modules, PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds(), location: location);
        }

        /// <summary>Прототипный забег без локации: Разломы без конца, без маршрутов.</summary>
        public static RiftRun NewPrototypeRun(ulong seed)
            => new RiftRun(new Simulation(seed, 1024), PrototypeContent.Modules(), PrototypeContent.Items(),
                PrototypeContent.ItemBaseIds());

        public static InputFrame Command(RunCommand command) => new InputFrame { Command = (byte)command };

        public static InputFrame Choice(int card) => Command((RunCommand)((int)RunCommand.ChooseReward1 + card));

        /// <summary>Снимает врагов (волны тоже), ставит героя у выхода. True — открыт экран награды.</summary>
        public static bool ClearToReward(RiftRun run)
        {
            for (int guard = 0; guard < 4000 && run.Phase == RunPhase.Clearing; guard++)
            {
                EntityStore e = run.Sim.Entities;
                for (int i = 0; i < e.Count; i++)
                    if (e.Side[i] != Faction.Wole) e.Alive[i] = false;
                run.Step(InputFrame.Empty);
            }
            if (run.Phase != RunPhase.SeekingExit) return false;
            run.Sim.Entities.Position[Simulation.PlayerId] = run.Map.ExitPoint(0);
            run.Step(InputFrame.Empty);
            return run.Phase == RunPhase.ChoosingReward;
        }

        /// <summary>Герой ниже 75%: на экране появляется родник.</summary>
        public static void Wound(RiftRun run)
        {
            EntityStore e = run.Sim.Entities;
            e.Health[Simulation.PlayerId] = e.MaxHealth[Simulation.PlayerId] * 6 / 10;
        }

        /// <summary>Экран награды: карточки, потоки Loot/Affix, набор.</summary>
        public static void MixScreen(RiftRun run, ref ulong hash)
        {
            Hashing.Mix(ref hash, run.Depth);
            Hashing.Mix(ref hash, (int)run.Phase);
            for (int i = 0; i < RiftRun.RewardChoices; i++) run.GetOffer(i).HashInto(ref hash);
            Hashing.Mix(ref hash, run.Sim.Rng.Loot.State);
            Hashing.Mix(ref hash, run.Sim.Rng.Affix.State);
            run.Loadout.HashInto(ref hash);
        }

        /// <summary>Карточка card; способность при полной панели — замена слота или разбор, по глубине.</summary>
        public static void Choose(RiftRun run, int card, int depth)
        {
            run.Step(Choice(card));
            if (run.Phase == RunPhase.ReplacingAbility)
                run.Step(Command(depth % 2 == 0 ? RunCommand.SalvageAbility
                    : (RunCommand)((int)RunCommand.ReplaceSlot1 + depth % RunLoadout.Slots)));
        }

        /// <summary>
        /// Восемь арен одного сида: каждая третья — раненым (родник), карточка и
        /// ветка маршрута — от сида и глубины. Хеш всех экранов и наборов после выбора.
        /// Экран босса не входит: его ведёт своя сессия.
        /// </summary>
        public static ulong ArenaTrail(ulong seed)
        {
            RiftRun run = NewArenaRun(seed);
            run.StartRun();
            ulong hash = Hashing.Offset;
            for (int depth = 1; depth <= ArenaCount; depth++)
            {
                if (run.Depth != depth || run.Phase != RunPhase.Clearing) return 0;
                if (depth % 3 == 1) Wound(run);
                if (!ClearToReward(run)) return 1;
                MixScreen(run, ref hash);
                Choose(run, (int)((seed + (ulong)depth) % RiftRun.RewardChoices), depth);
                Hashing.Mix(ref hash, (int)run.Phase);
                Hashing.Mix(ref hash, run.Gold);
                run.Loadout.HashInto(ref hash);
                if (depth == ArenaCount) break;
                run.Step(Command((RunCommand)((int)RunCommand.ChooseRoute1 + depth % 3)));
            }
            return hash;
        }

        /// <summary>То же на прототипном забеге: восемь Разломов подряд, без маршрутов.</summary>
        public static ulong PrototypeTrail(ulong seed)
        {
            RiftRun run = NewPrototypeRun(seed);
            run.StartRun();
            ulong hash = Hashing.Offset;
            for (int depth = 1; depth <= ArenaCount; depth++)
            {
                if (run.Depth != depth || run.Phase != RunPhase.Clearing) return 0;
                if (depth % 3 == 2) Wound(run);
                if (!ClearToReward(run)) return 1;
                MixScreen(run, ref hash);
                Choose(run, (int)((seed * 7 + (ulong)depth) % RiftRun.RewardChoices), depth);
                Hashing.Mix(ref hash, (int)run.Phase);
                run.Loadout.HashInto(ref hash);
            }
            return hash;
        }

        /// <summary>Полная панель с частью усилений: таланты — 55% карточек.</summary>
        public static void TalentLayoutA(RunLoadout loadout)
        {
            for (int slot = 1; slot < RunLoadout.Slots; slot++) loadout.Put(slot, slot);
            loadout.TakeTalent(0, 0); loadout.TakeTalent(0, 2); loadout.TakeTalent(0, 5);
            for (int i = 0; i < SabreTalents.TalentsPerLine; i++) loadout.TakeTalent(1, i);
            for (int i = 0; i < SabreTalents.TalentsPerLine - 1; i++) loadout.TakeTalent(3, i);
        }

        /// <summary>Пул 8 и 9 (линий талантов нет) и Удар якорем с двумя усилениями.</summary>
        public static void TalentLayoutB(RunLoadout loadout)
        {
            loadout.Put(1, 8); loadout.Put(2, 9); loadout.Put(3, 4);
            loadout.TakeTalent(4, 1); loadout.TakeTalent(4, 6);
        }

        /// <summary>
        /// Карточки талантов на линиях без форм: 50 сидов, два набора, по два экрана
        /// (между ними берётся первое предложенное усиление).
        /// </summary>
        public static ulong TalentCardTrail()
        {
            ulong hash = Hashing.Offset;
            for (int layout = 0; layout < 2; layout++)
                for (ulong seed = 1; seed <= 50; seed++)
                {
                    RiftRun run = NewPrototypeRun(seed);
                    run.StartRun();
                    if (layout == 0) TalentLayoutA(run.Loadout); else TalentLayoutB(run.Loadout);
                    for (int screen = 0; screen < 2; screen++)
                    {
                        if (!ClearToReward(run)) return 1;
                        MixScreen(run, ref hash);
                        int card = 0;
                        for (int i = RiftRun.RewardChoices - 1; i >= 0; i--)
                            if (run.GetOffer(i).Kind == RewardKind.Talent) card = i;
                        Choose(run, card, 2);
                        run.Loadout.HashInto(ref hash);
                    }
                }
            return hash;
        }

        /// <summary>Хеши наборов без форм: стартовый, A, B, A после замены слота с усилениями.</summary>
        public static ulong[] LoadoutHashes()
        {
            var result = new ulong[4];
            var loadout = new RunLoadout();
            result[0] = LoadoutHash(loadout);
            TalentLayoutA(loadout);
            result[1] = LoadoutHash(loadout);
            loadout.Put(1, 5);
            result[3] = LoadoutHash(loadout);
            var b = new RunLoadout();
            TalentLayoutB(b);
            result[2] = LoadoutHash(b);
            return result;
        }

        public static ulong LoadoutHash(RunLoadout loadout)
        {
            ulong hash = Hashing.Offset;
            loadout.HashInto(ref hash);
            return hash;
        }

        /// <summary>
        /// Хеши сборок без формы. Определение своё — баланс игры пин не сдвинет:
        /// без узлов и с узлами разных видов, включая старшую половину флагов.
        /// </summary>
        public static ulong[] BuildHashes()
        {
            var definition = new AbilityDefinition("test.forms.build")
                .Set(AbilityStatType.Damage, 100)
                .Set(AbilityStatType.Radius, Fix64.Ratio(23, 10))
                .Set(AbilityStatType.CooldownTicks, 72);
            var nodes = new AbilityNode[8];
            var result = new ulong[2];

            var build = new AbilityBuild();
            build.Rebuild(definition, nodes, 0);
            result[0] = BuildHash(build);

            nodes[0] = AbilityNode.Flag("test.forms.flag.high", AbilityFlag.CleaveDouble);
            nodes[1] = AbilityNode.StatMod("test.forms.radius", AbilityStatType.Radius, ModifierOp.Increased, Fix64.Ratio(1, 4));
            nodes[2] = AbilityNode.Flag("test.forms.flag.low", AbilityFlag.WhirlwindChannel);
            build.Rebuild(definition, nodes, 3);
            result[1] = BuildHash(build);
            return result;
        }

        public static ulong BuildHash(AbilityBuild build)
        {
            ulong hash = Hashing.Offset;
            build.HashInto(ref hash);
            return hash;
        }
    }
}
