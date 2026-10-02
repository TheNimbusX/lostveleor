namespace Game.Sim
{
    /// <summary>
    /// Как кончилась Буря — ActionVariant события WhirlwindStormEnded. Значения
    /// навсегда, только дописывать.
    /// </summary>
    public enum WhirlwindStormEnd : byte
    {
        /// <summary>Клавишу отпустили.</summary>
        Released = 0,

        /// <summary>Кончилась Концентрация (лавидий) на поддержание.</summary>
        OutOfConcentration = 1,

        /// <summary>Докрутила до предела StormHoldTicks.</summary>
        TimeUp = 2,

        /// <summary>Сняли: уход, другая способность, оглушение, смерть, форма ушла из слота.</summary>
        Interrupted = 3,
    }

    /// <summary>
    /// ТРИ ФОРМЫ ВИХРЯ (владелец 02.10): Буря, Водоворот, Пенные волны.
    ///
    /// Работает только когда в слоте форма — код каждый тик спрашивает FormIs и не
    /// держит форму в своём состоянии (Simulation.Forms). Без формы Вихрь прежний
    /// бит в бит: ни нового состояния в хеше, ни новых событий, ни новых бросков.
    ///
    /// * БУРЯ — удержание Вихря до 3 с от нажатия. Это то же удержание, что у
    ///   таланта «удержание» (Simulation.Talents: _whirlChannel*), только со
    ///   своими числами; Буря работает без таланта и главнее него. Пока держат,
    ///   герой идёт медленнее — доля шага стат сборки StartMoveMultiplier (узел
    ///   формы кладёт 45%, талант Бури поднимет). Концентрация: обычная цена каста
    ///   плюс расход в секунду; кончилась — Буря встала.
    /// * ВОДОВОРОТ — в каст тянет врагов в 4 м к герою (волок ForcedMotion, к
    ///   контакту доезжают), в контакт — обычный удар Вихря и оглушение 0,5 с всем
    ///   в 4 м. Тяжёлых (правило веса ForcedMotion), элиты и босса не тянет — им
    ///   только оглушение; Хозяин Чащи не оглушается (StunByTalent).
    /// * ПЕННЫЕ ВОЛНЫ — обычный оборот и два кольца от места контакта: первое
    ///   1,3 → 3,5 м, второе чуть позже 1,3 → 5 м. Кольцо бьёт врага один раз
    ///   (доля урона Вихря) и отталкивает лёгких — по правилу толчка серии сабли.
    ///
    /// ЧИСЛА — ЗАГЛУШКИ под приёмку владельцем, все именованные.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- Буря ----

        /// <summary>Буря держится до 3 с от нажатия.</summary>
        public const int StormHoldTicks = 3 * TicksPerSecond;

        /// <summary>Пока Бурю держат, оборот бьёт каждую треть секунды.</summary>
        public const int StormPulseTicks = TicksPerSecond / 3;

        /// <summary>
        /// Оборот удержания — треть удара Вихря. Полная Буря: контакт (100%) и семь
        /// оборотов по 33% — около 3,3 удара Вихря за 3 с.
        /// </summary>
        public const int StormPulseDamagePercent = 33;

        /// <summary>Концентрация на поддержание, ед/с (сверх цены каста).</summary>
        public const int StormDrainPerSecond = 15;

        private static readonly Fix64 StormDrainPerTick = Fix64.Ratio(StormDrainPerSecond, TicksPerSecond);

        /// <summary>Сейчас крутится Буря (удержание Вихря в форме Буря).</summary>
        public bool WhirlwindStorming => _whirlChannelSlot >= 0 && FormIs(_whirlChannelSlot, PelagForm.WhirlwindStorm);

        /// <summary>Период оборотов текущего удержания — виду: у Бури свой, у таланта прежний.</summary>
        public int WhirlwindChannelPulseTicks => WhirlwindStorming ? StormPulseTicks : WhirlwindPulseTicks;

        /// <summary>Доля шага в Буре: стат сборки StartMoveMultiplier, 0–1. Только пока WhirlwindStorming.</summary>
        private Fix64 StormMoveScale
            => Fix64.Clamp(_abilityBuilds[_whirlChannelSlot].Get(AbilityStatType.StartMoveMultiplier), Fix64.Zero, Fix64.One);

        /// <summary>Контакт с зажатой клавишей: Буря пошла. Слот уже в _whirlChannelSlot.</summary>
        private void BeginStorm()
        {
            // Предел — от нажатия: контакт пришёл через AbilityExecutionTicks(задержка) после каста.
            _whirlChannelEndTick = Tick - AbilityExecutionTicks(WhirlwindContactDelayTicks) + StormHoldTicks;
            _whirlChannelNextPulse = Tick + StormPulseTicks;
            _events.Add(new SimEvent(SimEventType.WhirlwindStormStarted, PlayerId, -1, _whirlChannelEndTick - Tick,
                false, Entities.Position[PlayerId]));
        }

        private void StormPulse(int slot)
        {
            _whirlChannelNextPulse = Tick + StormPulseTicks;
            // Событие оборота — до урона: Damage по целям идут сразу за ним.
            _events.Add(new SimEvent(SimEventType.WhirlwindStormPulse, PlayerId, -1, EnemiesInWhirlwind(slot), false,
                Entities.Position[PlayerId], actionVariant: _whirlChannelEndTick - Tick));
            WhirlwindPulse(slot, firstContact: false, StormPulseDamagePercent);
        }

        private int EnemiesInWhirlwind(int slot)
        {
            int found = QueryRadiusIntoScratch(
                Entities.Position[PlayerId], _abilityBuilds[slot].Get(AbilityStatType.Radius), PlayerId);
            int victims = 0;
            for (int i = 0; i < found; i++)
            {
                int target = HitScratch[i];
                if (Entities.Alive[target] && Entities.Side[target] != Entities.Side[PlayerId]) victims++;
            }
            return victims;
        }

        /// <summary>Почему удержание снимается — тем же порядком, что проверки UpdateWhirlwindChannel.</summary>
        private WhirlwindStormEnd StormStopReason(in InputFrame input, Fix64 drain)
        {
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick)) return WhirlwindStormEnd.Interrupted;
            if (Tick >= _whirlChannelEndTick) return WhirlwindStormEnd.TimeUp;
            if ((input.AbilityHoldMask & (1 << _whirlChannelSlot)) == 0) return WhirlwindStormEnd.Released;
            if (Entities.Lavidium[PlayerId] < drain) return WhirlwindStormEnd.OutOfConcentration;
            return WhirlwindStormEnd.Interrupted;
        }

        /// <summary>Конец Бури виду. Удержание таланта (без формы) событий не даёт — как раньше.</summary>
        private void EmitStormEnded(WhirlwindStormEnd reason)
        {
            if (_whirlChannelSlot < 0 || !FormIs(_whirlChannelSlot, PelagForm.WhirlwindStorm)) return;
            int left = _whirlChannelEndTick - Tick;
            _events.Add(new SimEvent(SimEventType.WhirlwindStormEnded, PlayerId, -1, left > 0 ? left : 0,
                reason == WhirlwindStormEnd.OutOfConcentration, Entities.Position[PlayerId], actionVariant: (int)reason));
        }

        // ---- Водоворот ----

        /// <summary>Водоворот тянет и оглушает врагов в 4 м от героя (до края тела).</summary>
        public static readonly Fix64 MaelstromRadius = Fix64.FromInt(4);

        /// <summary>Оглушение Водоворота в контакт: 0,5 с.</summary>
        public const int MaelstromStaggerTicks = TicksPerSecond / 2;

        /// <summary>Зазор между телами, где тяга останавливает врага (как у «Затягивает»).</summary>
        private static readonly Fix64 MaelstromStopGap = Fix64.Ratio(1, 2);

        /// <summary>Каст Вихря (зовётся из ResolveAbilityCasts после WhirlwindUpgradesAtCast).</summary>
        private void WhirlwindFormAtCast(int slot)
        {
            if (FormIs(slot, PelagForm.WhirlwindMaelstrom)) MaelstromPull();
        }

        /// <summary>Контакт Вихря, сразу за обычным оборотом (ResolveWhirlwindImpact).</summary>
        private void WhirlwindFormAtContact(int slot)
        {
            if (FormIs(slot, PelagForm.WhirlwindMaelstrom)) MaelstromStagger();
            else if (FormIs(slot, PelagForm.WhirlwindFoamWaves)) StartFoamWaves(slot);
        }

        /// <summary>
        /// Тяга к герою; доезжают к контакту. Тяжёлых не тянет правило веса
        /// ForcedMotion.Begin (Dragged), элиты и босс стоят сами.
        /// </summary>
        private void MaelstromPull()
        {
            FixVec2 center = Entities.Position[PlayerId];
            Fix64 near = Entities.BodyRadius[PlayerId] + MaelstromStopGap;
            int contact = AbilityExecutionTicks(WhirlwindContactDelayTicks);
            int ticks = contact > ForcedMotion.MinTicks ? contact - 1 : ForcedMotion.MinTicks;
            int pulled = 0;
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId] || ThicketShielded(i)) continue;
                FixVec2 delta = Entities.Position[i] - center;
                Fix64 distance = delta.Length;
                if (distance > MaelstromRadius + Entities.BodyRadius[i]) continue;
                Fix64 stop = near + Entities.BodyRadius[i];
                if (distance <= stop || distance.Raw == 0) continue;
                if (IsElite(i) || Entities.Kind[i] == EnemyKind.ForestThicketMaster) continue;
                if (ForcedMotion.Begin(Entities, i, center + delta / distance * stop, ticks, ForcedMotionKind.Dragged))
                    pulled++;
            }
            _events.Add(new SimEvent(SimEventType.WhirlwindMaelstromPull, PlayerId, -1, pulled, false, center,
                actionVariant: ticks));
        }

        /// <summary>Оглушение всем врагам в радиусе Водоворота — и тем, кого не тянуло.</summary>
        private void MaelstromStagger()
        {
            FixVec2 center = Entities.Position[PlayerId];
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId] || ThicketShielded(i)) continue;
                Fix64 reach = MaelstromRadius + Entities.BodyRadius[i];
                if (FixVec2.DistanceSq(Entities.Position[i], center) > reach * reach) continue;
                StunByTalent(i, MaelstromStaggerTicks);
            }
        }

        // ---- Пенные волны ----

        public const int FoamRingCount = 2;

        /// <summary>Оба кольца рождаются в 1,3 м от центра.</summary>
        public static readonly Fix64 FoamRingInnerRadius = Fix64.Ratio(13, 10);

        private static readonly Fix64 FoamRingFirstOuter = Fix64.Ratio(35, 10);
        private static readonly Fix64 FoamRingSecondOuter = Fix64.FromInt(5);

        /// <summary>Первое кольцо: 1,3 → 3,5 м за 8 тиков с контакта.</summary>
        public const int FoamRingFirstTravelTicks = 8;

        /// <summary>Второе кольцо выходит через 6 тиков после первого: 1,3 → 5 м за 12 тиков.</summary>
        public const int FoamRingSecondDelayTicks = 6;
        public const int FoamRingSecondTravelTicks = 12;

        /// <summary>Удар кольца — 30% удара Вихря; каждое кольцо бьёт врага не больше раза.</summary>
        public const int FoamRingDamagePercent = 30;

        /// <summary>Толчок кольца: полметра от центра за 4 тика.</summary>
        private static readonly Fix64 FoamRingPushDistance = Fix64.Ratio(1, 2);
        private const int FoamRingPushTicks = 4;

        private int _foamSlot = -1;      // −1 — колец нет
        private int _foamStartTick;      // контакт: первое кольцо
        private int _foamDamage;
        private FixVec2 _foamCentre;
        private byte[] _foamHits;        // бит k — кольцо k уже задело это тело

        public static Fix64 FoamRingOuterRadius(int ring) => ring == 0 ? FoamRingFirstOuter : FoamRingSecondOuter;
        public static int FoamRingDelayTicks(int ring) => ring == 0 ? 0 : FoamRingSecondDelayTicks;
        public static int FoamRingTravelTicks(int ring) => ring == 0 ? FoamRingFirstTravelTicks : FoamRingSecondTravelTicks;

        /// <summary>Радиус фронта кольца после step тиков хода (0 — внутренний, travel — внешний).</summary>
        public static Fix64 FoamRingRadiusAt(int ring, int step)
        {
            int travel = FoamRingTravelTicks(ring);
            if (step <= 0) return FoamRingInnerRadius;
            if (step >= travel) return FoamRingOuterRadius(ring);
            return FoamRingInnerRadius + (FoamRingOuterRadius(ring) - FoamRingInnerRadius) * Fix64.Ratio(step, travel);
        }

        /// <summary>
        /// Кольца виду: центр и тик выхода кольца (фронт на тике t — FoamRingRadiusAt(ring,
        /// t − startTick + 1)). False — колец нет.
        /// </summary>
        public bool TryGetFoamRing(int ring, out FixVec2 centre, out int startTick)
        {
            centre = _foamCentre;
            startTick = _foamStartTick + FoamRingDelayTicks(ring);
            return _foamSlot >= 0 && (uint)ring < FoamRingCount;
        }

        private void EnsureWhirlwindFormBuffers()
        {
            if (_foamHits == null) _foamHits = new byte[Entities.Capacity];
        }

        private void StartFoamWaves(int slot)
        {
            if (_foamHits == null) return;
            _foamSlot = slot;
            _foamStartTick = Tick;
            _foamCentre = Entities.Position[PlayerId];
            _foamDamage = _abilityBuilds[slot].Get(AbilityStatType.Damage).ToInt() * FoamRingDamagePercent / 100;
            System.Array.Clear(_foamHits, 0, _foamHits.Length);
        }

        /// <summary>Каждый тик после удержания Вихря: фронты колец идут и бьют.</summary>
        private void UpdateFoamWaves()
        {
            if (_foamSlot < 0) return;
            bool running = false;
            for (int ring = 0; ring < FoamRingCount; ring++)
            {
                int travel = FoamRingTravelTicks(ring);
                int step = Tick - (_foamStartTick + FoamRingDelayTicks(ring)) + 1;
                if (step < travel) running = true;
                if (step < 1 || step > travel) continue;
                if (step == 1)
                    _events.Add(new SimEvent(SimEventType.WhirlwindFoamRing, PlayerId, -1, ring, false, _foamCentre,
                        actionVariant: travel));
                SweepFoamRing(ring, FoamRingRadiusAt(ring, step - 1), FoamRingRadiusAt(ring, step));
            }
            if (!running) _foamSlot = -1;
        }

        /// <summary>
        /// Фронт кольца за тик прошёл полосу [inner, outer]: бьёт тело, которое её
        /// касается. Кто оказался внутри позади фронта — уже не задет этим кольцом.
        /// </summary>
        private void SweepFoamRing(int ring, Fix64 inner, Fix64 outer)
        {
            int bit = 1 << ring;
            for (int i = 1; i < Entities.Count; i++)
            {
                if ((_foamHits[i] & bit) != 0) continue;
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId] || ThicketShielded(i)) continue;
                FixVec2 delta = Entities.Position[i] - _foamCentre;
                Fix64 distance = delta.Length;
                Fix64 body = Entities.BodyRadius[i];
                if (distance - body > outer || distance + body < inner) continue;
                _foamHits[i] = (byte)(_foamHits[i] | bit);
                _events.Add(new SimEvent(SimEventType.WhirlwindFoamRingHit, PlayerId, i, ring, false, Entities.Position[i]));
                ApplyAbilityDamage(PlayerId, i, _foamDamage, _foamSlot, DamageType.Physical);
                if (Entities.Alive[i]) ShoveByFoam(i, delta, distance);
            }
        }

        /// <summary>
        /// Толчок кольца — по правилу толчка добивающего серии сабли
        /// (Simulation.SabreCombo.ShoveBySabre): только лёгкие рядовые, которых
        /// ничто другое не держит; элиты, тяжёлые, Вендиго, Шипомёт и босс стоят.
        /// Shoved не сбивает замах.
        /// </summary>
        private void ShoveByFoam(int target, FixVec2 delta, Fix64 distance)
        {
            if (Entities.PushWeight[target].Raw <= 0 || IsElite(target)) return;
            if (ForcedMotion.IsActive(Entities, target)) return;
            EnemyKind kind = Entities.Kind[target];
            if (kind != EnemyKind.None && kind != EnemyKind.ForestGuardian && kind != EnemyKind.ForestRootSwarm
                && kind != EnemyKind.ForestSplitling && kind != EnemyKind.ForestBud) return;
            FixVec2 away = distance.Raw == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : delta / distance;
            ForcedMotion.Begin(Entities, target, Entities.Position[target] + away * FoamRingPushDistance,
                FoamRingPushTicks, ForcedMotionKind.Shoved);
        }

        private void ResetWhirlwindForms() => _foamSlot = -1;

        /// <summary>Только живые кольца: без них хеш прежний бит в бит.</summary>
        private void HashWhirlwindForms(ref ulong hash)
        {
            if (_foamSlot < 0) return;
            Hashing.Mix(ref hash, 0x464F414D);   // "FOAM"
            Hashing.Mix(ref hash, _foamSlot);
            Hashing.Mix(ref hash, _foamStartTick);
            Hashing.Mix(ref hash, _foamDamage);
            Hashing.Mix(ref hash, _foamCentre.X);
            Hashing.Mix(ref hash, _foamCentre.Y);
            for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, (int)_foamHits[i]);
        }
    }
}
