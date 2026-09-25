namespace Game.Sim
{
    /// <summary>
    /// Две сабельные способности: Рассекающий удар и огненное усиление.
    ///
    /// Вместе они и есть ответ сабельной ветки на вопрос «когда бить группу, а
    /// когда одну цель»: Вихрь и Шквал разбираются с толпой, Рассекающий удар
    /// существует ради одного тела, а усиление выбирает МОМЕНТ, в который всё
    /// это стоит делать. Отсюда и соседство в одном файле.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- Рассекающий удар ----

        private int _cleaveSlot = -1;
        private int _cleaveImpactTick = -1;
        private int _cleaveTarget = -1;
        private int _cleaveStartTick = -1;
        private int _cleaveEndTick = -1;
        private bool _cleaveHit;
        private FixVec2 _cleaveDirection;
        private readonly FixVec2[] _cleavePreviousPositions;
        private int _cleavePreviousCount;
        private FixVec2 _cleaveOrigin;

        public bool CleaveActive => _cleaveSlot >= 0;
        public FixVec2 CleaveDirection => _cleaveDirection;
        public int CleaveContactTick => _cleaveImpactTick;
        public int CleaveStartTick => _cleaveStartTick;
        public int CleaveEndTick => _cleaveEndTick;
        public int CleavePresentationTarget => CleaveActive ? _cleaveTarget : -1;
        public int CleaveSwingStartTick => CleaveActive ? _cleaveImpactTick
            - _abilityBuilds[_cleaveSlot].Get(AbilityStatType.SwingLeadTicks).ToInt() : -1;
        public int CleaveWindowEndTick => CleaveActive ? _cleaveImpactTick
            + _abilityBuilds[_cleaveSlot].Get(AbilityStatType.ContactWindowTicks).ToInt() : -1;

        private void StopCleave()
        {
            _cleaveSlot = _cleaveImpactTick = _cleaveTarget = _cleaveStartTick = _cleaveEndTick = -1;
            _cleaveHit = false;
            _cleaveDirection = _cleaveOrigin = FixVec2.Zero;
            _cleavePreviousCount = 0;
        }

        private void BeginCleave(int slot)
        {
            AbilityBuild build = _abilityBuilds[slot];
            _cleaveSlot = slot;
            _cleaveTarget = -1;
            _cleaveStartTick = Tick;
            _cleaveImpactTick = Tick + AbilityExecutionTicks(build.Get(AbilityStatType.WindupTicks).ToInt());
            _cleaveEndTick = System.Math.Max(Tick + AbilityExecutionTicks(build.Get(AbilityStatType.DurationTicks).ToInt()),
                _cleaveImpactTick + build.Get(AbilityStatType.ContactWindowTicks).ToInt() + 1);
            _cleaveDirection = Entities.Facing[PlayerId];
            _cleaveOrigin = Entities.Position[PlayerId];
            _cleavePreviousCount = Entities.Count;
            System.Array.Copy(Entities.Position, _cleavePreviousPositions, Entities.Count);
            _cleaveHit = false;
        }

        private void UpdateCleave()
        {
            if (!CleaveActive) return;
            AbilityBuild build = _abilityBuilds[_cleaveSlot];
            if (build == null || build.DefinitionId != AbilityDefinition.CleaveId || !Entities.Alive[PlayerId]
                || Statuses.IsStunned(PlayerId, Tick) || Tick >= _cleaveEndTick
                || (!build.Has(AbilityFlag.CleaveOnTheMove)
                    && (Entities.Position[PlayerId] - _cleaveOrigin).LengthSq > Fix64.One))
            { StopCleave(); return; }
            if (!_cleaveHit && Tick >= _cleaveImpactTick && Tick < CleaveWindowEndTick)
            {
                // Талант «Тройной веер» бьёт тремя направлениями: центр и ±35°.
                // Каждое направление находит своё тело, одно тело дважды не бьётся.
                int directions = build.Has(AbilityFlag.CleaveFan) ? 3 : 1;
                if (Tick == _cleaveImpactTick)
                {
                    _cleaveFanMask = 0;
                    for (int i = 0; i < _cleaveFanTargets.Length; i++) _cleaveFanTargets[i] = -1;
                    CleaveWave(build);
                }
                FixVec2 from = Entities.Position[PlayerId];
                for (int d = 0; d < directions; d++)
                {
                    if ((_cleaveFanMask & (1 << d)) != 0) continue;
                    FixVec2 end = from + CleaveFanDirection(d) * build.Get(AbilityStatType.Radius);
                    int best = -1;
                    Fix64 bestDistance = Fix64.Zero;
                    for (int target = 1; target < Entities.Count; target++)
                    {
                        if (!Entities.Alive[target] || Entities.Side[target] == Entities.Side[PlayerId]) continue;
                        if (target == _cleaveFanTargets[0] || target == _cleaveFanTargets[1] || target == _cleaveFanTargets[2]) continue;
                        FixVec2 now = Entities.Position[target];
                        FixVec2 previous = target < _cleavePreviousCount ? _cleavePreviousPositions[target] : now;
                        Fix64 radius = Entities.BodyRadius[target] + build.Get(AbilityStatType.Width);
                        // Выбираем тело в объёме клинка на контакте, а не цель при нажатии.
                        if (!CleaveSegmentsNear(previous, now, from, end, radius) || !CleaveLineClear(now)) continue;
                        Fix64 distance = (now - from).LengthSq;
                        if (best >= 0 && distance >= bestDistance) continue;
                        best = target;
                        bestDistance = distance;
                    }
                    if (best < 0) continue;

                    _cleaveFanMask |= 1 << d;
                    _cleaveFanTargets[d] = best;
                    if (d == 0 || _cleaveTarget < 0) _cleaveTarget = best;
                    int damage = build.Get(AbilityStatType.Damage).ToInt();
                    if (build.Has(AbilityFlag.CleaveBigGame) && IsElite(best)) damage = damage * 140 / 100;
                    ApplyAbilityDamage(PlayerId, best, damage, _cleaveSlot, DamageType.Physical);
                    CleaveUpgradesOnHit(build, best, d, damage);
                }
                _cleaveHit = _cleaveFanMask == (1 << directions) - 1;
            }
            _cleavePreviousCount = Entities.Count;
            System.Array.Copy(Entities.Position, _cleavePreviousPositions, Entities.Count);
        }

        private bool CleaveLineClear(FixVec2 to)
        {
            FixVec2 from = Entities.Position[PlayerId];
            if (_campWalkMap != null) return _campWalkMap.CanTravel(from, to);
            if (_layout == null) return true;
            FixVec2 delta = to - from;
            int steps = System.Math.Max(1, (delta.Length / (LayoutMap.CellSize / Fix64.FromInt(8))).ToInt() + 1);
            for (int i = 1; i <= steps; i++)
                if (!_layout.IsWalkable(from + delta * Fix64.Ratio(i, steps), Fix64.Zero)) return false;
            return true;
        }

        private static Fix64 CleavePointSegmentSq(FixVec2 p, FixVec2 a, FixVec2 b)
        {
            FixVec2 edge = b - a;
            Fix64 t = edge.LengthSq.Raw == 0 ? Fix64.Zero
                : Fix64.Max(Fix64.Zero, Fix64.Min(Fix64.One, FixVec2.Dot(p - a, edge) / edge.LengthSq));
            return (p - a - edge * t).LengthSq;
        }

        private static Fix64 CleaveCross(FixVec2 a, FixVec2 b) => a.X * b.Y - a.Y * b.X;
        private static bool CleaveSegmentsNear(FixVec2 a, FixVec2 b, FixVec2 c, FixVec2 d, Fix64 radius)
        {
            FixVec2 ab = b - a, cd = d - c;
            Fix64 denominator = CleaveCross(ab, cd);
            if (denominator.Raw != 0)
            {
                Fix64 t = CleaveCross(c - a, cd) / denominator;
                Fix64 u = CleaveCross(c - a, ab) / denominator;
                if (t >= Fix64.Zero && t <= Fix64.One && u >= Fix64.Zero && u <= Fix64.One) return true;
            }
            Fix64 sq = radius * radius;
            return CleavePointSegmentSq(a, c, d) <= sq || CleavePointSegmentSq(b, c, d) <= sq
                || CleavePointSegmentSq(c, a, b) <= sq || CleavePointSegmentSq(d, a, b) <= sq;
        }

        private void HashCleave(ref ulong hash)
        {
            Hashing.Mix(ref hash, _cleaveSlot);
            Hashing.Mix(ref hash, _cleaveImpactTick);
            Hashing.Mix(ref hash, _cleaveTarget);
            Hashing.Mix(ref hash, _cleaveStartTick);
            Hashing.Mix(ref hash, _cleaveEndTick);
            Hashing.Mix(ref hash, _cleaveHit ? 1 : 0);
            Hashing.Mix(ref hash, _cleaveDirection.X.Raw);
            Hashing.Mix(ref hash, _cleaveDirection.Y.Raw);
            Hashing.Mix(ref hash, _cleavePreviousCount);
            for (int i = 0; i < _cleavePreviousCount; i++)
            {
                Hashing.Mix(ref hash, _cleavePreviousPositions[i].X.Raw);
                Hashing.Mix(ref hash, _cleavePreviousPositions[i].Y.Raw);
            }
            Hashing.Mix(ref hash, _cleaveOrigin.X.Raw);
            Hashing.Mix(ref hash, _cleaveOrigin.Y.Raw);
        }
        // ---- Огненное усиление ----

        private int _blazeUntilTick;
        private int _blazeSlot = -1;
        private int _blazeStartTick = -1;
        private int _blazeIgniteTick = -1;
        private int _blazeEndTick = -1;
        public const int BlazeIgnitionDelayTicks = 36;
        public const int BlazeGestureTicks = 60;
        public int BlazeStartTick => _blazeStartTick;
        public int BlazeIgniteTick => _blazeIgniteTick;
        public int BlazeEndTick => _blazeEndTick;
        public bool BlazeCasting => Entities.Alive[PlayerId] && _blazeStartTick >= 0 && Tick < _blazeEndTick;

        /// <summary>Горит ли сабля прямо сейчас. Показу — рисовать пламя.</summary>
        public bool BlazeActive => Entities.Alive[PlayerId] && Tick < _blazeUntilTick;

        /// <summary>
        /// ОГНЕННОЕ УСИЛЕНИЕ. Три секунды, в которые Пелаг бьёт сильнее и
        /// уворачивается чаще.
        ///
        /// Усиление не создаёт нового действия — оно меняет цену уже принятых
        /// решений. Поэтому окно короткое и сама способность ничего не бьёт:
        /// её содержание в том, ЧТО игрок успеет сделать внутри этих секунд.
        /// </summary>
        private void CastBlaze(int slot)
        {
            AbilityBuild build = _abilityBuilds[slot];
            _blazeSlot = slot;
            _blazeStartTick = Tick;
            _blazeIgniteTick = Tick + AbilityExecutionTicks(BlazeIgnitionDelayTicks);
            _blazeEndTick = Tick + AbilityExecutionTicks(BlazeGestureTicks);
            // Руки заняты бутылкой: прежний незавершённый взмах не попадает сквозь жест.
            Entities.PendingAttackTarget[PlayerId] = -1;
            Entities.AttackImpactTick[PlayerId] = 0;
            Entities.PendingAttackVariant[PlayerId] = 0;
        }

        private void CancelBlazeGesture()
        {
            _blazeStartTick = _blazeIgniteTick = _blazeEndTick = -1;
        }

        private void UpdateBlaze()
        {
            if (_blazeStartTick < 0 || _blazeIgniteTick < 0) return;
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick))
            { CancelBlazeGesture(); return; }
            if (Tick != _blazeIgniteTick) return;
            var build = _blazeSlot >= 0 ? _abilityBuilds[_blazeSlot] : null;
            if (build == null || build.DefinitionId != AbilityDefinition.BlazeId)
            { CancelBlazeGesture(); return; }
            // Три секунды начинаются у огня, а не у нажатия кнопки.
            int duration = build.Get(AbilityStatType.DurationTicks).ToInt();
            _blazeUntilTick = Tick + duration;
            BlazeUpgradesAtIgnition(build);
            _events.Add(new SimEvent(SimEventType.BlazeBegin, PlayerId, -1,
                duration, false, Entities.Position[PlayerId]));
        }

        /// <summary>
        /// Уклонение от входящего удара, пока горит сабля.
        ///
        /// БРОСОК ДЕЛАЕТСЯ ТОЛЬКО ПОД УСИЛЕНИЕМ, и это осознанно: базового
        /// уклонения в игре нет, и тратить боевой поток случайности на каждый
        /// удар «на будущее» значило бы привязать расход потока к тому, чего
        /// ещё не существует. Появится базовое уклонение — бросок переедет
        /// выше и станет безусловным, как бросок на крит.
        ///
        /// ЧТО ИМЕННО МОЖНО ИЗБЕЖАТЬ, диздок оставляет открытым. Здесь избегается
        /// любой прямой урон по игроку и не избегается урон по времени: гасить
        /// тик горения броском монеты тридцать раз в секунду — это не уклонение,
        /// а случайное сопротивление.
        /// </summary>
        private bool BlazeEvades(int target, bool overTime)
        {
            if (target != PlayerId || overTime || !BlazeActive) return false;
            if (!Rng.Combat.Chance(BlazeEvasion)) return false;

            _events.Add(new SimEvent(SimEventType.Evaded, PlayerId, -1, 0, false,
                Entities.Position[PlayerId]));
            BlazeEvadeRefund();
            return true;
        }

        /// <summary>Пятая часть. Число владельца от 12 сентября, раньше стояла половина.</summary>
        private static readonly Fix64 BlazeEvasion = Fix64.Ratio(1, 5);

        /// <summary>
        /// Добавка огнём к ОБЫЧНЫМ атакам, пока горит сабля.
        ///
        /// Решение владельца от 12 сентября: усиление трогает только автоатаку,
        /// способности оно не усиливает. Поэтому вызов остался ровно один — в
        /// ApplyAttack, а из пути урона способностей убран. Иначе усиление
        /// складывалось бы с уже усиленными числами способностей дважды.
        ///
        /// Считается ДОЛЕЙ ОТ СИЛЫ УДАРА, а не плоским числом: плоская прибавка
        /// решала бы всё в первом акте и не значила бы ничего к третьему.
        ///
        /// Приходит сила удара ДО брони и ПОСЛЕ крита: огонь едет на самом
        /// взмахе. Гасит его сопротивление огню, а не броня — поэтому это
        /// отдельный удар, а не прибавка к числу; иначе одна из двух защит
        /// оказалась бы пропущена.
        /// </summary>
        private void ApplyBlazeBonus(int source, int target, int attackPower)
        {
            if (source != PlayerId || !BlazeActive || _blazeSlot < 0) return;
            if (!Entities.Alive[target] || attackPower <= 0) return;

            AbilityBuild build = _abilityBuilds[_blazeSlot];
            if (build == null || build.DefinitionId != AbilityDefinition.BlazeId) return;

            int bonus = CombatStats.RoundToInt(Fix64.FromInt(attackPower)
                * build.Get(AbilityStatType.BonusDamagePercent));
            if (bonus <= 0) return;

            bonus = CombatStats.Mitigate(bonus, DamageType.Fire,
                Entities.Armor[target], Entities.FireResist[target]);
            if (bonus <= 0) return;

            Entities.Health[target] -= bonus;
            _events.Add(SimEvent.Damage(PlayerId, target, bonus, false,
                Entities.Position[target], DamageType.Fire, DamageOrigin.Ability, _blazeSlot));

            if (Entities.Health[target] <= 0) Kill(target, PlayerId, _blazeSlot);
        }

        private void HashBlaze(ref ulong hash)
        {
            Hashing.Mix(ref hash, _blazeUntilTick);
            Hashing.Mix(ref hash, _blazeSlot);
            Hashing.Mix(ref hash, _blazeStartTick);
            Hashing.Mix(ref hash, _blazeIgniteTick);
            Hashing.Mix(ref hash, _blazeEndTick);
        }
    }
}
