using System;

namespace Game.Sim
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ, этап 3 «Буря» (спецификация 01.10): буря цветения и
    /// связки фазы 3. Ядро (Simulation.ForestBoss.cs / .Brain.cs) и этап 2
    /// (.Attacks.cs) зовут методы этого файла; нового состояния нет — круги
    /// бури лежат в местах кругов каста (ThicketShape*, хеш этапа 2), фаза
    /// бури — в ThicketMasterState, расписание и связка — в ThicketMasterMemory.
    ///
    /// БУРЯ ЦВЕТЕНИЯ. Фаза 3: сразу после рёва на 33% и потом раз в
    /// ThicketStormEveryTicks от начала прошлой. Босс стоит. Две волны: первая
    /// через ThicketStormFirstWaveTicks после начала, вторая — через
    /// ThicketStormSecondWaveTicks после первой. Волна бьёт героя везде, КРОМЕ
    /// трёх кругов света r2 (центр героя в круге — укрыт): круг 0 — вплотную к
    /// боссу (из него можно бить), круг 1 — в 3–5 м от героя, круг 2 — где
    /// угодно на полу в 5–9 м от героя. Все — на полу, круги 1 и 2 — по прямой
    /// от героя без стен. Круги второй волны — новые, встают в тик удара
    /// первой (круг 0 — с другой стороны босса). Урон волны — доля 70/41 удара.
    /// Круги — метки Circle с SharedView | SafeZone (бот и вид их читают),
    /// места кругов 0–2 — первая волна, 3–5 — вторая (TryGetThicketShape).
    /// Пока буря идёт, она занимает весь бюджет крупных меток (не меньше 4):
    /// другой крупной метки на земле нет. Начинается, только когда на земле
    /// нет чужих крупных меток; пока ждёт — держит крупный жетон, подмога
    /// новых крупных атак не начинает.
    /// Stage: 0 — ждёт первую волну, 1 — вторую, 2 — стойка после второй.
    /// EnemyActionStarted ThicketStorm: Amount 0 — начало, 1 — круги второй
    /// волны. EnemyActionImpact: Amount — номер волны, Position — босс,
    /// Flag — героя задело.
    ///
    /// СВЯЗКИ (фазы 2–3, темп 02.10). Нырок→лапа или Нырок→топот: после
    /// стойки 24 после выхода — сразу серия лапы (герой в её досягаемости)
    /// или топот (герой не дальше ThicketChainReach), мимо отдыха, правила
    /// «60 из 90» и перезарядки топота. Серия лапы фазы 3 (3 удара) — сама
    /// связка; прежней «Лапа→Лапа→Топот» нет. Окно после связки — окно её
    /// действия (30). Не встала за ThicketChainWaitTicks (бюджет, такт, Часы,
    /// герой ушёл от лапы) — рвётся. Рёв связку рвёт.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- буря цветения ----

        public const int ThicketStormFirstWaveTicks = 60, ThicketStormSecondWaveTicks = 45;

        /// <summary>Стойка после второй волны, до отдыха.</summary>
        public const int ThicketStormRecoveryTicks = 12;

        /// <summary>Раз в ~20 с от начала прошлой бури: ×1,25 при подмоге, ×0,85 с половины здоровья (в фазе 3 — всегда), Часы сдвигают.</summary>
        public const int ThicketStormEveryTicks = 600;

        public const int ThicketStormWaves = 2, ThicketStormSafeCircles = 3;

        /// <summary>Вес бури в бюджете крупных меток: весь бюджет, не меньше 4.</summary>
        public const int ThicketStormMarkWeight = 4;

        public static readonly Fix64 ThicketStormSafeRadius = Fix64.FromInt(2);

        /// <summary>Круг 0: центр в 2 м от центра босса — край круга заходит под тело, из круга герой достаёт босса саблей.</summary>
        public static readonly Fix64 ThicketStormBossOffset = Fix64.FromInt(2);

        /// <summary>Круг 1 — центр в 3–5 м от героя: дойти за 2 с, но стоя на месте не укрыться.</summary>
        public static readonly Fix64 ThicketStormNearMin = Fix64.FromInt(3), ThicketStormNearMax = Fix64.FromInt(5);

        /// <summary>Круг 2 — центр в 5–9 м от героя.</summary>
        public static readonly Fix64 ThicketStormFarMin = Fix64.FromInt(5), ThicketStormFarMax = Fix64.FromInt(9);

        /// <summary>Круг 0 второй волны — на 100–140° в сторону от круга 0 первой.</summary>
        private static readonly Fix64 ThicketStormTurnMin = Fix64.Pi * 5 / 9, ThicketStormTurnMax = Fix64.Pi * 7 / 9;

        private static readonly Fix64 ThicketStormProbeStep = Fix64.Pi / 6;

        // ---- связки фаз 2–3 ----

        /// <summary>Связка ждёт старта не дольше этого (бюджет, такт, Часы) — потом рвётся.</summary>
        public const int ThicketChainWaitTicks = 30;

        /// <summary>Нырок→Топот — если герой не дальше внешнего края второго кольца топота (7,5 м между центрами).</summary>
        public static readonly Fix64 ThicketChainReach = Fix64.Ratio(15, 2);

        private FixVec2[] _thicketStormScratch;

        // ---- чтение для вида, тестов и стенда ----

        public int ThicketStormDamageOf(int id) => ThicketShareOf(id, ThicketStormDamageA9);

        public static EnemyTelegraph ThicketStormSafeCircle(FixVec2 center) => EnemyTelegraph.Circle(center, ThicketStormSafeRadius);

        /// <summary>Вес бури в бюджете: весь бюджет арены, не меньше ThicketStormMarkWeight.</summary>
        private int ThicketStormWeight => Math.Max(ThicketStormMarkWeight, _bigMarkBudget);

        /// <summary>Центр героя (point) в одном из кругов света волны wave босса id.</summary>
        public bool ThicketStormSafeAt(int id, int wave, FixVec2 point)
        {
            Fix64 r = ThicketStormSafeRadius;
            for (int k = 0; k < ThicketStormSafeCircles; k++)
                if (TryGetThicketShape(id, wave * ThicketStormSafeCircles + k, out FixVec2 center, out _, out _)
                    && FixVec2.DistanceSq(point, center) <= r * r) return true;
            return false;
        }

        /// <summary>Пора ли буре: фаза 3, срок пришёл (Часы его сдвигают), стенд не закрыл её готовностью Storm, буря не идёт.</summary>
        public bool ThicketStormDue(int id)
        {
            if (_thicketMemory == null || (uint)id >= (uint)Entities.Count || Entities.Kind[id] != EnemyKind.ForestThicketMaster
                || !Entities.Alive[id]) return false;
            var m = _thicketMemory[id];
            return m.Awake && m.Phase >= 3 && m.StormNextTick != 0 && Tick >= m.StormNextTick
                && Tick >= ThicketReadyAt(id, ThicketMasterAction.Storm)
                && ThicketMasters[id].Action != ThicketMasterAction.Storm;
        }

        // ---- выбор: связка, потом буря ----

        partial void ThicketChooseForced(int id, ref ThicketMasterAction choice)
        {
            ref var m = ref ThicketMemory[id];
            if (m.ChainNext != ThicketMasterAction.None)
            {
                // Связка-лапа — только пока герой в досягаемости лапы: иначе рвётся.
                bool reach = m.ChainNext != ThicketMasterAction.Paw || ThicketPawInReach(id);
                if (reach && Tick - m.ChainStep <= ThicketChainWaitTicks) { choice = m.ChainNext; return; }
                m.ChainNext = ThicketMasterAction.None;
                m.ChainStep = 0;
            }
            if (ThicketStormDue(id)) choice = ThicketMasterAction.Storm;
        }

        partial void ThicketHoldsTokenExtra(int id, ref bool holds)
        {
            if (ThicketStormDue(id)) holds = true;
        }

        /// <summary>Рёв рвёт связку; рёв на 33% ставит бурю сразу.</summary>
        partial void ThicketRoarDone(int id, int thresholds)
        {
            ref var m = ref ThicketMemory[id];
            m.ChainNext = ThicketMasterAction.None;
            m.ChainStep = 0;
            if ((thresholds & ThicketRoar33Bit) != 0) m.StormNextTick = Tick;
        }

        /// <summary>
        /// Связка после нырка (фазы 2–3, темп 02.10): серия лапы, если герой в
        /// её досягаемости, иначе топот, если герой не дальше ThicketChainReach;
        /// иначе связки нет. Связка ещё не идёт.
        /// </summary>
        private ThicketMasterAction ThicketDiveChainOf(int id)
        {
            ref var m = ref ThicketMemory[id];
            if (m.Phase < 2 || m.ChainNext != ThicketMasterAction.None || !Entities.Alive[PlayerId] || !Entities.Alive[id])
                return ThicketMasterAction.None;
            if (ThicketPawInReach(id)) return ThicketMasterAction.Paw;
            Fix64 reach = ThicketChainReach;
            return FixVec2.DistanceSq(Entities.Position[PlayerId], Entities.Position[id]) <= reach * reach
                ? ThicketMasterAction.Stomp : ThicketMasterAction.None;
        }

        /// <summary>
        /// Конец действия. Нырок в фазах 2–3 (после стойки 24) — сразу связка:
        /// серия лапы или топот, мимо отдыха, правила «60 из 90» и перезарядки
        /// топота. Окно после связки — окно её действия (30 тиков).
        /// </summary>
        partial void ThicketFinishedExtra(int id, ThicketMasterAction finished)
        {
            ref var m = ref ThicketMemory[id];
            if (m.ChainNext != ThicketMasterAction.None && finished == m.ChainNext)
            {
                m.ChainNext = ThicketMasterAction.None;
                m.ChainStep = 0;
                return;
            }
            if (finished != ThicketMasterAction.Dive) return;
            var next = ThicketDiveChainOf(id);
            if (next == ThicketMasterAction.None) return;
            m.ChainNext = next;
            m.ChainStep = Tick;
            m.NextActionTick = Tick;
        }

        // ---------- буря: начало ----------

        /// <summary>
        /// Буря встаёт, только когда на земле нет чужих крупных меток (кислые
        /// лужи не в счёт — это не удар), прошлая чужая встала не ближе
        /// BigMarkStaggerTicks, и обе волны укладываются в такт ударов.
        /// </summary>
        private bool StartThicketStorm(int id)
        {
            int first = Tick + ThicketStormFirstWaveTicks;
            int last = first + ThicketStormSecondWaveTicks;
            if (!ThicketStormGroundClear(id) || !HeroContactAllowed(id, first, first)
                || !HeroContactAllowed(id, last, last)) return false;
            ClearThicketShapes(id);
            ref var a = ref BeginThicketAction(id, ThicketMasterAction.Storm, first, last, last + ThicketStormRecoveryTicks,
                ThicketStormWaves, Entities.Facing[id], Entities.Position[id]);
            PlaceThicketStormWave(id, ref a, 0, first);
            ThicketMemory[id].StormNextTick = Tick + ThicketScaled(id, ThicketStormEveryTicks);
            return true;
        }

        /// <summary>Нет ни одной чужой крупной метки (плоды мёртвого стрелка ещё падают — в счёт).</summary>
        private bool ThicketStormGroundClear(int id)
        {
            for (int other = 1; other < Entities.Count; other++)
            {
                if (other == id) continue;
                if (!Entities.Alive[other] && Entities.Kind[other] != EnemyKind.ForestBud) continue;
                if (BigMarkWeightOf(other, out _, out _) != 0) return false;
            }
            return true;
        }

        // ---------- буря: круги света ----------

        /// <summary>Три круга волны wave с ударом в impact: места wave*3 .. wave*3+2, метки SharedView | SafeZone.</summary>
        private void PlaceThicketStormWave(int id, ref ThicketMasterState a, int wave, int impact)
        {
            _thicketStormScratch ??= new FixVec2[ThicketStormSafeCircles];
            FixVec2 boss = Entities.Position[id];
            FixVec2 previous = FixVec2.Zero;
            if (wave > 0 && TryGetThicketShape(id, (wave - 1) * ThicketStormSafeCircles, out FixVec2 old, out _, out _))
                previous = old - boss;
            ThicketStormCircles(id, wave, previous, _thicketStormScratch);
            for (int k = 0; k < ThicketStormSafeCircles; k++)
            {
                int index = id * ThicketShapeSlots + wave * ThicketStormSafeCircles + k;
                int slot = OpenTelegraph(id, ThicketStormSafeCircle(_thicketStormScratch[k]), impact, impact + TelegraphLingerTicks,
                    TelegraphFlags.SharedView | TelegraphFlags.SafeZone);
                int serial = TryGetTelegraph(slot, out var t) ? t.Serial : 0;
                ThicketShapeSerial[index] = serial;
                ThicketShapeImpact[index] = impact;
                ThicketShapeCenter[index] = _thicketStormScratch[k];
                ThicketShapeState[index] = ThicketShapePending;
                if (k == 0) a.TelegraphSerial = serial;
            }
            a.Target = Entities.Position[PlayerId];
        }

        /// <summary>
        /// Где лягут три круга волны (свой поток босса). Круг 0 — в
        /// ThicketStormBossOffset от босса: в первой волне — к герою, во второй —
        /// на 100–140° в сторону от прошлого (previous — от босса к прошлому
        /// кругу 0); первая свободная от стен точка через 30°. Круг 1 — в 3–5 м
        /// от героя, круг 2 — в 5–9 м, оба на полу и по прямой без стен, по
        /// возможности не внахлёст с другими (центры не ближе 4 м, потом 2 м).
        /// Ничего не нашлось (тупик у стены) — круг 1 на самом герое.
        /// </summary>
        private void ThicketStormCircles(int id, int wave, FixVec2 previous, FixVec2[] centers)
        {
            ref var rng = ref ThicketMemory[id].Rng;
            FixVec2 boss = Entities.Position[id];
            FixVec2 hero = Entities.Position[PlayerId];
            Fix64 r = ThicketStormSafeRadius;

            // Круг 0 — у босса.
            FixVec2 look = wave == 0 ? hero - boss : previous;
            if (look.LengthSq.Raw == 0) look = Entities.Facing[id];
            if (look.LengthSq.Raw == 0) look = new FixVec2(Fix64.One, Fix64.Zero);
            Fix64 angle = look.Angle;
            if (wave > 0)
            {
                Fix64 turn = rng.NextFix(ThicketStormTurnMin, ThicketStormTurnMax);
                angle += (rng.NextUInt() & 1) == 0 ? turn : -turn;
            }
            centers[0] = ThicketStormClamp(boss + FixVec2.FromAngle(angle) * ThicketStormBossOffset);
            for (int k = 0; k <= 12; k++)
            {
                int side = (k + 1) / 2;
                Fix64 delta = ThicketStormProbeStep * side * ((k & 1) == 1 ? 1 : -1);
                FixVec2 c = boss + FixVec2.FromAngle(angle + delta) * ThicketStormBossOffset;
                if (ThicketStormFloor(c)) { centers[0] = c; break; }
            }

            // Круг 1 — рядом с героем.
            Fix64 nearAngle = rng.NextFix() * Fix64.TwoPi;
            Fix64 near = rng.NextFix(ThicketStormNearMin, ThicketStormNearMax);
            if (!ThicketStormPick(hero, nearAngle, near, ThicketStormNearMin, centers, 1, r * 2, out centers[1])
                && !ThicketStormPick(hero, nearAngle, near, ThicketStormNearMin, centers, 1, Fix64.Zero, out centers[1]))
                centers[1] = _layout != null ? _layout.ClampToWalkable(hero, Entities.BodyRadius[PlayerId]) : hero;

            // Круг 2 — где угодно на полу дальше.
            Fix64 farAngle = rng.NextFix() * Fix64.TwoPi;
            Fix64 far = rng.NextFix(ThicketStormFarMin, ThicketStormFarMax);
            if (!ThicketStormPick(hero, farAngle, far, ThicketStormFarMin, centers, 2, r * 2, out centers[2])
                && !ThicketStormPick(hero, farAngle, far, ThicketStormFarMin, centers, 2, r, out centers[2])
                && !ThicketStormPick(hero, farAngle, far, ThicketStormNearMin, centers, 2, Fix64.Zero, out centers[2]))
                centers[2] = centers[1];
        }

        /// <summary>
        /// Первая годная точка вокруг hero: 12 направлений через 30° от angle,
        /// на расстоянии distance, потом fallback; на полу, по прямой от героя,
        /// центр не ближе separation к уже выбранным кругам 0..count-1.
        /// </summary>
        private bool ThicketStormPick(FixVec2 hero, Fix64 angle, Fix64 distance, Fix64 fallback, FixVec2[] centers,
            int count, Fix64 separation, out FixVec2 found)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Fix64 d = pass == 0 ? distance : fallback;
                for (int k = 0; k < 12; k++)
                {
                    FixVec2 c = hero + FixVec2.FromAngle(angle + ThicketStormProbeStep * k) * d;
                    if (!ThicketStormFloor(c) || !ThicketStormReach(hero, c)) continue;
                    bool apart = true;
                    for (int j = 0; j < count && apart; j++)
                        if (FixVec2.DistanceSq(c, centers[j]) < separation * separation) apart = false;
                    if (!apart) continue;
                    found = c;
                    return true;
                }
            }
            found = hero;
            return false;
        }

        /// <summary>Центр круга на полу: тело героя там помещается.</summary>
        private bool ThicketStormFloor(FixVec2 point)
            => _layout == null || _layout.IsWalkable(point, Entities.BodyRadius[PlayerId]);

        /// <summary>От героя до центра круга — по прямой без стен и воды.</summary>
        private bool ThicketStormReach(FixVec2 from, FixVec2 to)
            => (_layout == null && _campWalkMap == null) || CanTravel(from, to, Entities.BodyRadius[PlayerId]);

        private FixVec2 ThicketStormClamp(FixVec2 point)
            => _layout != null ? _layout.ClampToWalkable(point, Entities.BodyRadius[PlayerId]) : point;

        // ---------- буря: волны ----------

        private void AdvanceThicketStorm(int id)
        {
            var a = ThicketMasters[id];
            if (!a.HitResolved && Tick >= a.ImpactTick) ResolveThicketStormWave(id);
            a = ThicketMasters[id];
            if (a.Serial != 0 && a.Action == ThicketMasterAction.Storm && a.HitResolved && Tick >= a.EndTick)
                FinishThicketAction(id, ThicketRestTicks(id));
        }

        /// <summary>
        /// Удар волны: герой вне всех трёх кругов своей волны — урон. После
        /// первой волны в этот же тик встают три новых круга второй. Состояние —
        /// до урона: отражение может убить босса внутри ApplyAbilityDamage.
        /// </summary>
        private void ResolveThicketStormWave(int id)
        {
            ref var a = ref ThicketMasters[id];
            int wave = a.Stage;
            FixVec2 hero = Entities.Position[PlayerId];
            bool hit = Entities.Alive[PlayerId] && !ThicketStormSafeAt(id, wave, hero);
            int from = id * ThicketShapeSlots + wave * ThicketStormSafeCircles;
            for (int k = from; k < from + ThicketStormSafeCircles; k++)
            {
                if (ThicketShapeState[k] != ThicketShapePending) continue;
                ThicketShapeState[k] = ThicketShapeDone;
                ResolveTelegraphSerial(ThicketShapeSerial[k]);
            }
            a.Stage = wave + 1;
            a.StageStartTick = Tick;
            if (a.Stage < a.Stages)
            {
                a.ImpactTick = a.LastImpactTick;
                PlaceThicketStormWave(id, ref a, a.Stage, a.LastImpactTick);
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                    EnemyActionKind.ThicketStorm, Entities.Position[id], a.Stage));
            }
            else a.HitResolved = true;
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, id, PlayerId,
                EnemyActionKind.ThicketStorm, Entities.Position[id], wave, hit));
            if (hit) ApplyAbilityDamage(id, PlayerId, ThicketStormDamageOf(id), -1, DamageType.Physical);
        }

        /// <summary>Контакты бури в такт ударов: волны, что ещё впереди.</summary>
        private void AddThicketStormContacts(int id, in ThicketMasterState a)
        {
            if (a.HitResolved) return;
            if (a.ImpactTick >= Tick) AddHeroContact(id, a.ImpactTick, a.ImpactTick);
            if (a.Stage == 0 && a.LastImpactTick >= Tick) AddHeroContact(id, a.LastImpactTick, a.LastImpactTick);
        }
    }
}
