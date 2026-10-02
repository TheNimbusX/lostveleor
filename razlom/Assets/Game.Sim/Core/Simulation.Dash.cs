namespace Game.Sim
{
    /// <summary>
    /// Снимок рывка героя (Simulation.Dash). Пишет только симуляция; вид
    /// читает отсюда, куда и как долго летит герой, тесты — сроки. Входит в
    /// хеш, пока за расстановку был хоть один рывок.
    /// </summary>
    public struct PelagDashState
    {
        /// <summary>Номер рывка с начала расстановки; 0 — рывков ещё не было.</summary>
        public int Serial;

        /// <summary>Слот, которым нажат рывок.</summary>
        public int Slot;

        /// <summary>Тик нажатия: с него герой неуязвим.</summary>
        public int StartTick;

        /// <summary>
        /// Последний тик неуязвимости и прохода сквозь тела, ВКЛЮЧИТЕЛЬНО:
        /// тик нажатия + длительность рывка, то есть и тик, в который тело
        /// доехало. Стена его не сокращает; снятие рывка другим уходом — да.
        /// </summary>
        public int InvulnerableUntilTick;

        /// <summary>Тик, когда тело встало; −1 — ещё едет.</summary>
        public int StopTick;

        /// <summary>Откуда, куда на полную дальность (без учёта стен) и куда смотрит рывок.</summary>
        public FixVec2 From, To, Direction;

        /// <summary>Где тело встало. Осмысленно при StopTick ≥ 0.</summary>
        public FixVec2 StoppedAt;

        /// <summary>Встало раньше полной дальности: стена или снятие другим уходом.</summary>
        public bool CutShort;

        /// <summary>Тело ещё едет рывком.</summary>
        public bool Moving => Serial != 0 && StopTick < 0;

        /// <summary>Неуязвим ли герой рывком в тик tick.</summary>
        public bool InvulnerableAt(int tick)
            => Serial != 0 && tick >= StartTick && tick <= InvulnerableUntilTick;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Slot);
            Hashing.Mix(ref hash, StartTick); Hashing.Mix(ref hash, InvulnerableUntilTick);
            Hashing.Mix(ref hash, StopTick);
            Hashing.Mix(ref hash, From.X); Hashing.Mix(ref hash, From.Y);
            Hashing.Mix(ref hash, To.X); Hashing.Mix(ref hash, To.Y);
            Hashing.Mix(ref hash, Direction.X); Hashing.Mix(ref hash, Direction.Y);
            Hashing.Mix(ref hash, StoppedAt.X); Hashing.Mix(ref hash, StoppedAt.Y);
            Hashing.Mix(ref hash, CutShort ? 1 : 0);
        }
    }

    /// <summary>
    /// РЫВОК ГЕРОЯ (Пробел) — решение владельца 02.10, вместо кувырка.
    ///
    /// Само перемещение — одно назначение ForcedMotion (вид Roll): прямо на
    /// полную дальность за DurationTicks тиков, у стены тело встаёт и рывок
    /// кончается (Simulation.ResolveForcedMotion), вдоль стены не скользит.
    ///
    /// НЕУЯЗВИМОСТЬ — окно тиков, а не флаг полёта: с тика нажатия по тик
    /// нажатия + длительность включительно (PelagDashState.InvulnerableAt).
    /// Пока оно открыто:
    /// * урон по герою не проходит (PlayerImmune: удары, способности мобов,
    ///   метки босса, тики луж и горения) — нет ни урона, ни события Damage,
    ///   значит и реакции на удар;
    /// * контроль не ложится: корни, оглушение и замедление отбиваются в
    ///   ApplyHeroSlow/ApplyHeroControl (без события HeroControl и без
    ///   иммунитета после), отброс мобы вешают только при прошедшем уроне или
    ///   проверкой PlayerImmune;
    /// * тела врагов героя не держат и не толкают: в расталкивании он
    ///   исключение, как в фазе (Simulation.SeparateBodies).
    /// Окно — по времени, чтобы рывок в стену защищал так же, как в поле.
    /// Снимает его раньше только другой уход, сорвавший рывок.
    ///
    /// В корнях рывка нет (Simulation.AbilityHeldByRoots), в лагере он есть
    /// везде (CampTraining.AbilitiesOutsideZone).
    /// </summary>
    public sealed partial class Simulation
    {
        private PelagDashState _dash;

        /// <summary>Последний рывок героя: куда, когда, где встал.</summary>
        public PelagDashState PelagDash => _dash;

        /// <summary>Последний тик неуязвимости рывка (включительно); −1 — рывков не было.</summary>
        public int DashInvulnerableUntilTick => _dash.Serial != 0 ? _dash.InvulnerableUntilTick : -1;

        /// <summary>Герой неуязвим рывком в текущий тик: урон и контроль не проходят, тела не держат.</summary>
        public bool DashInvulnerable => _dash.InvulnerableAt(Tick);

        /// <summary>
        /// Рывок в сторону курсора.
        ///
        /// ИДЁТ НА ПОЛНУЮ ДАЛЬНОСТЬ, а не до точки клика. Рывок отвечает на
        /// «уйти отсюда», а не «попасть туда»: короткий рывок из-за того, что
        /// курсор оказался близко к телу, — это не решение игрока, а его
        /// случайность.
        /// </summary>
        private void CastDash(int slot, FixVec2 aim)
        {
            AbilityBuild build = _abilityBuilds[slot];
            FixVec2 from = Entities.Position[PlayerId];

            FixVec2 direction = aim - from;
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
            direction = direction.Normalized();

            Entities.Facing[PlayerId] = direction;

            int ticks = build.Get(AbilityStatType.DurationTicks).ToInt();
            if (ticks < ForcedMotion.MinTicks) ticks = ForcedMotion.MinTicks;
            FixVec2 to = from + direction * build.Get(AbilityStatType.Radius);
            ForcedMotion.Begin(Entities, PlayerId, to, ticks, ForcedMotionKind.Roll);

            _dash = new PelagDashState
            {
                Serial = _dash.Serial + 1, Slot = slot, StartTick = Tick,
                InvulnerableUntilTick = Tick + ticks, StopTick = -1,
                From = from, To = to, Direction = direction,
            };
            _events.Add(new SimEvent(SimEventType.DashStarted, PlayerId, -1, ticks, false, from,
                actionVariant: _dash.Serial));
        }

        /// <summary>
        /// Тело встало: доехало (cutShort = false), упёрлось в стену или рывок
        /// сняли. Неуязвимость не трогает. Событие DashEnded — один раз на рывок.
        /// </summary>
        private void StopDash(bool cutShort)
        {
            if (!_dash.Moving) return;
            FixVec2 at = Entities.Position[PlayerId];
            _dash.StopTick = Tick;
            _dash.StoppedAt = at;
            _dash.CutShort = cutShort;
            int travelCm = CombatStats.RoundToInt(FixVec2.Distance(_dash.From, at) * Fix64.FromInt(100));
            _events.Add(new SimEvent(SimEventType.DashEnded, PlayerId, -1, travelCm, cutShort, at,
                actionVariant: _dash.Serial));
        }

        /// <summary>
        /// Рывок сорван собственным новым действием героя (другим уходом):
        /// тело встаёт там, где есть, и неуязвимость кончается — рывка больше
        /// нет. Зовётся из CancelPlayerAction до снятия ForcedMotion.
        /// </summary>
        private void CancelDash()
        {
            if (!_dash.Moving) return;
            StopDash(cutShort: true);
            if (_dash.InvulnerableUntilTick >= Tick) _dash.InvulnerableUntilTick = Tick - 1;
        }

        private void ResetDash() => _dash = default;

        private void HashDash(ref ulong hash)
        {
            if (_dash.Serial == 0) return;
            Hashing.Mix(ref hash, 0x44415348); // "DASH"
            _dash.HashInto(ref hash);
        }
    }
}
