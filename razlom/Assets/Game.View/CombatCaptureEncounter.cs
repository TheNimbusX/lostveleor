using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Только capture fixture: реальные виды со штатными статами и контроллерами.
    // Обычный забег этот код не вызывает и своё начальное состояние не меняет.
    internal static class CombatCaptureEncounter
    {
        public static void Configure(TickDriver driver, string encounter, int count,
            CombatFeelCaptureTier tier, bool active)
        {
            // Лесные стенды поднимает сама CaptureRig: у каждого свой тестовый бой.
            if (encounter == "forest-bud" || encounter == "forest-wendigo"
                || encounter == "forest-guardian" || encounter == "forest-stonehoof"
                || encounter == "forest-thorncaster" || encounter == "forest-snarer" || encounter == "forest-splitter") return;
            var sim = driver.Sim;
            sim.SetupForestEncounter(driver.Run.Map, 20260829UL, 1000);
            var entities = sim.Entities;
            FixVec2 center = entities.Position[Simulation.PlayerId];
            int placed = 0;
            for (int i = 1; i < entities.Count; i++)
            {
                bool include = encounter == "mixed" || entities.Kind[i] == EnemyKind.ForestRootSwarm;
                include &= placed < Mathf.Max(1, count);
                if (!include) { entities.Alive[i] = false; continue; }
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio(placed, Mathf.Max(1, count));
                Fix64 radius = Fix64.Ratio(15 + (placed / 6) * 8, 10);
                entities.Position[i] = center + new FixVec2(Fix64.Cos(angle), Fix64.Sin(angle)) * radius;
                entities.Facing[i] = (center - entities.Position[i]).Normalized();
                entities.Health[i] = tier == CombatFeelCaptureTier.Kill ? 1 : 1000;
                if (!active)
                {
                    entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                    entities.Stats[i].SetBase(StatType.Damage, Fix64.Zero);
                    entities.RefreshStats(i);
                    entities.NextAttackTick[i] = int.MaxValue;
                }
                placed++;
            }
            entities.Stats[Simulation.PlayerId].SetBase(StatType.CritChance,
                tier == CombatFeelCaptureTier.Critical ? Fix64.One : Fix64.Zero);
            entities.RefreshStats(Simulation.PlayerId);
            sim.Grid.Rebuild(entities);
            Debug.Log($"[capture-encounter] {encounter} alive={placed} active={active}");
        }

        /// <summary>
        /// -Encounter forest-guardian: Хранители со штатными статами против героя на ровной
        /// арене без стен. Зовётся в том же кадре, что StartStonehoofTest: от стенда быка
        /// остаются поляна и точки героя и врага, сам бык до привязки видов не доживает.
        /// </summary>
        public static void SetupGuardians(TickDriver driver, int count)
        {
            var sim = driver.Sim; var entities = sim.Entities;
            FixVec2 hero = entities.Position[Simulation.PlayerId];
            FixVec2 front = entities.Count > 1 ? entities.Position[1] : hero + new FixVec2(Fix64.FromInt(6), Fix64.Zero);
            FixVec2 axis = (front - hero).Normalized(), side = new FixVec2(-axis.Y, axis.X);
            sim.SetupTestArena(count);
            entities.Position[Simulation.PlayerId] = hero; entities.Facing[Simulation.PlayerId] = axis;
            for (int i = 1; i < entities.Count; i++)
            {
                // Веер поперёк оси стенда, 2 м между телами: первый тик никого не расталкивает.
                FixVec2 spot = front + side * Fix64.FromInt(2 * (i - 1) - (count - 1));
                entities.Position[i] = spot; entities.Facing[i] = (hero - spot).Normalized();
                entities.Aggro[i] = true;
            }
            sim.Grid.Rebuild(entities);
            Debug.Log($"[capture-encounter] forest-guardian alive={count}");
        }

        /// <summary>
        /// -Encounter forest-thorncaster | forest-snarer | forest-splitter: новый моб леса (1–3)
        /// на стенде вида Simulation.SetupKindTestArena — та же дорога, что у волн встречи
        /// (здоровье и урон первой арены), шеренга поперёк оси в 6 м от героя. Карта — поляна
        /// стенда Камнекопыта: StartStonehoofTest зовётся в том же кадре, бык до привязки видов
        /// не доживает, стены и деревья режут линию шипов по-настоящему. Пока нет моделей,
        /// тела — заглушки ArenaView.
        ///
        /// Элитная табличка Шипомёта на стенде не видна: план встречи забега остаётся от
        /// быка (RiftRun.Encounters снаружи не заменить), элиту знает только Sim.
        /// </summary>
        public static void SetupForestMob(TickDriver driver, EnemyKind kind, int count)
        {
            var sim = driver.Sim;
            bool allForest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-all-forest") >= 0;
            sim.SetupKindTestArena(kind, allForest ? 1 : Mathf.Clamp(count, 1, 3), driver.Run != null ? driver.Run.Map : null,
                CaptureRig.SeedOverride);
            if (allForest) AddAllForestKinds(driver, kind);
            // -ExtraArgs '-capture-mend': к Корнехвату — два Хранителя на половине здоровья рядом
            // с ним, чтобы на записи была «Волна из корней» (сам себя и других Корнехватов он не лечит).
            if (!allForest && kind == EnemyKind.ForestRootSnarer && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-mend") >= 0)
            {
                var entities = sim.Entities;
                FixVec2 snarer = entities.Position[1], hero = entities.Position[Simulation.PlayerId];
                FixVec2 axis = (hero - snarer).Normalized(), side = new FixVec2(-axis.Y, axis.X);
                for (int n = 0; n < 2; n++)
                {
                    FixVec2 spot = snarer + axis * Fix64.FromInt(2) + side * Fix64.FromInt(n == 0 ? 2 : -2);
                    if (driver.Run != null && driver.Run.Map != null) spot = driver.Run.Map.ClampToWalkable(spot, Fix64.One);
                    sim.AddKindTestEnemy(EnemyKind.ForestGuardian, spot, 50);
                }
            }
            Debug.Log($"[capture-encounter] {kind} ({EnemyTexts.Name(kind)}) alive={sim.CountAliveEnemies()}");
        }

        // Только съёмка: ровно по одному представителю девяти видов, первый — выбранный стендом.
        private static void AddAllForestKinds(TickDriver driver, EnemyKind firstKind)
        {
            var sim = driver.Sim;
            var entities = sim.Entities;
            var map = driver.Run != null ? driver.Run.Map : null;
            FixVec2 hero = entities.Position[Simulation.PlayerId];
            FixVec2 axis = (entities.Position[1] - hero).Normalized();
            if (axis.LengthSq.Raw == 0) axis = new FixVec2(Fix64.One, Fix64.Zero);
            FixVec2 side = new FixVec2(-axis.Y, axis.X);
            int ordinal = 0;
            for (int value = (int)EnemyKind.ForestGuardian; value <= (int)EnemyKind.ForestSplitling; value++)
            {
                var kind = (EnemyKind)value;
                if (kind == firstKind) continue;
                ordinal++;
                Fix64 radius = sim.ArchetypeBodyRadius(kind);
                bool placed = false;
                // Фиксированный обход углов/радиусов: та же карта и seed дают те же точки.
                for (int attempt = 0; attempt < 360 && !placed; attempt++)
                {
                    Fix64 angle = Fix64.TwoPi * (Fix64.Ratio(ordinal, 9) + Fix64.Ratio(attempt / 5, 72));
                    Fix64 distance = Fix64.FromInt(6 + (ordinal + attempt % 5) % 5);
                    FixVec2 direction = axis * Fix64.Cos(angle) + side * Fix64.Sin(angle);
                    FixVec2 spot = hero + direction * distance;
                    if (map != null) spot = map.ClampToWalkable(spot, radius);
                    Fix64 fromHero = FixVec2.DistanceSq(hero, spot);
                    if (fromHero < Fix64.FromInt(36) || fromHero > Fix64.FromInt(100)
                        || (map != null && !map.IsWalkable(spot, radius))) continue;
                    bool clear = true;
                    for (int other = 1; other < entities.Count; other++)
                    {
                        if (!entities.Alive[other]) continue;
                        Fix64 spacing = radius + entities.BodyRadius[other] + Fix64.Ratio(1, 2);
                        if (FixVec2.DistanceSq(spot, entities.Position[other]) < spacing * spacing)
                        { clear = false; break; }
                    }
                    if (!clear) continue;
                    sim.AddKindTestEnemy(kind, spot, 100);
                    placed = true;
                }
                if (!placed) throw new System.InvalidOperationException("Нет свободного места в кольце 6–10 м для " + kind);
            }
            sim.Grid.Rebuild(entities);
            Debug.Log($"[capture-encounter] all-forest kinds=9 alive={sim.CountAliveEnemies()} seed={CaptureRig.SeedOverride}");
        }

        /// <summary>
        /// -EnemyCase: герою запас здоровья на всю запись и снятое бессмертие — удары обязаны
        /// доходить и попадать в журнал; врагам запас под удары якорем. Добивание (death)
        /// оставляет врагам штатное здоровье.
        /// </summary>
        public static void PrepareEnemyCase(TickDriver driver, string enemyCase)
        {
            if (driver.Session.Mode != GameMode.Rift || driver.Sim == null) return;
            var sim = driver.Sim; var entities = sim.Entities;
            driver.Session.SetDeveloperInvulnerable(false);
            entities.Stats[Simulation.PlayerId].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            entities.RefreshStats(Simulation.PlayerId);
            entities.Health[Simulation.PlayerId] = entities.MaxHealth[Simulation.PlayerId];
            if (enemyCase != "death")
                for (int i = 1; i < entities.Count; i++)
                {
                    if (!entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                    entities.Stats[i].SetBase(StatType.MaxHealth, Fix64.FromInt(5000));
                    entities.RefreshStats(i); entities.Health[i] = entities.MaxHealth[i];
                }
            // -capture-mend: союзники Корнехвата ранены уже после запаса здоровья — иначе лечить некого.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-mend") >= 0)
                for (int i = 1; i < entities.Count; i++)
                    if (entities.Alive[i] && entities.Kind[i] == EnemyKind.ForestGuardian)
                        entities.Health[i] = entities.MaxHealth[i] / 2;
            Debug.Log($"[enemy-qa] case={enemyCase} enemies={sim.CountAliveEnemies()}");
        }

        /// <summary>
        /// Строки [enemy-qa] в журнал плеера: замахи, удары, оглушения и смерти врагов.
        /// Зовётся в конце кадра — тики кадра прошли, их события ещё не стёрты.
        /// </summary>
        public static void LogEnemyEvents(TickDriver driver)
        {
            var sim = driver.Sim;
            if (sim == null) return;
            var entities = sim.Entities; var contexts = driver.FrameEventContexts;
            for (int i = 0; i < contexts.Count; i++)
            {
                var e = contexts[i].Event;
                if (e.Type == SimEventType.Spawn || e.Type == SimEventType.DamageOverTime) continue;
                bool enemySource = e.Source > Simulation.PlayerId && e.Source < entities.Count && entities.Side[e.Source] != Faction.Wole;
                bool enemyTarget = e.Target > Simulation.PlayerId && e.Target < entities.Count && entities.Side[e.Target] != Faction.Wole;
                if (!enemySource && !enemyTarget) continue;
                Debug.Log($"[enemy-qa] tick={contexts[i].SimulationTick} {e.Type} src={e.Source} dst={e.Target} " +
                          $"amount={e.Amount} hero_hp={entities.Health[Simulation.PlayerId]}");
            }
        }
    }
}
