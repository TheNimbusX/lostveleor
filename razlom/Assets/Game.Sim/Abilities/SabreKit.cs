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
            _cleaveImpactTick = Tick + System.Math.Max(1, build.Get(AbilityStatType.WindupTicks).ToInt());
            _cleaveEndTick = Tick + System.Math.Max(build.Get(AbilityStatType.DurationTicks).ToInt(),
                build.Get(AbilityStatType.WindupTicks).ToInt() + build.Get(AbilityStatType.ContactWindowTicks).ToInt() + 1);
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
                || (Entities.Position[PlayerId] - _cleaveOrigin).LengthSq > Fix64.One)
            { StopCleave(); return; }
            if (!_cleaveHit && Tick >= _cleaveImpactTick && Tick < CleaveWindowEndTick)
            {
                FixVec2 from = Entities.Position[PlayerId];
                FixVec2 end = from + _cleaveDirection * build.Get(AbilityStatType.Radius);
                int best = -1;
                Fix64 bestDistance = Fix64.Zero;
                for (int target = 1; target < Entities.Count; target++)
                {
                    if (!Entities.Alive[target] || Entities.Side[target] == Entities.Side[PlayerId]) continue;
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
                if (best >= 0)
                {
                    _cleaveTarget = best;
                    _cleaveHit = true;
                    ApplyAbilityDamage(PlayerId, best,
                        build.Get(AbilityStatType.Damage).ToInt(), _cleaveSlot, DamageType.Physical);
                }
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
            _blazeUntilTick = Tick + build.Get(AbilityStatType.DurationTicks).ToInt();
            _events.Add(new SimEvent(SimEventType.BlazeBegin, PlayerId, -1,
                _blazeUntilTick - Tick, false, Entities.Position[PlayerId]));
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
            return true;
        }

        /// <summary>Половина. Число из диздока, а не подобранное.</summary>
        private static readonly Fix64 BlazeEvasion = Fix64.Ratio(1, 2);

        /// <summary>
        /// Добавка огнём ко ВСЕМУ, чем бьёт игрок, пока горит сабля.
        ///
        /// Диздок: «усиление относится ко всем атакам, а не только к
        /// автоатаке», и «дополнительный огненный урон сам по себе не означает
        /// наложение отдельного периодического горения». Поэтому здесь ровно
        /// один добавочный удар огнём и никакого поджига.
        ///
        /// Считается отдельным ударом, а не прибавкой к числу, потому что тип
        /// урона другой: физическую часть гасит броня, огненную —
        /// сопротивление огню. Сложить их в одно число значило бы пропустить
        /// одну из двух защит.
        /// </summary>
        private void ApplyBlazeBonus(int source, int target, int slot)
        {
            if (source != PlayerId || !BlazeActive || _blazeSlot < 0) return;
            if (!Entities.Alive[target]) return;

            AbilityBuild build = _abilityBuilds[_blazeSlot];
            if (build == null || build.DefinitionId != AbilityDefinition.BlazeId) return;

            int bonus = build.Get(AbilityStatType.Damage).ToInt();
            if (bonus <= 0) return;

            bonus = CombatStats.Mitigate(bonus, DamageType.Fire,
                Entities.Armor[target], Entities.FireResist[target]);
            if (bonus <= 0) return;

            Entities.Health[target] -= bonus;
            _events.Add(SimEvent.Damage(PlayerId, target, bonus, false,
                Entities.Position[target], DamageType.Fire, DamageOrigin.Ability, slot));

            if (Entities.Health[target] <= 0) Kill(target, PlayerId, slot);
        }

        private void HashBlaze(ref ulong hash)
        {
            Hashing.Mix(ref hash, _blazeUntilTick);
            Hashing.Mix(ref hash, _blazeSlot);
        }
    }
}
