namespace Game.Sim
{
    /// <summary>
    /// Две якорные способности: Крушение и «За борт!».
    ///
    /// Обе бьют по дуге перед героем и обе существуют ради РАСПОЛОЖЕНИЯ, а не
    /// ради урона. Крушение держит толпу на месте короткой серией; «За борт!»
    /// разбрасывает её и обращает саму толпу в оружие. Одна собирает, вторая
    /// разряжает — и обе спрашивают игрока об одном: как сейчас стоят враги.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Враги в секторе перед героем.
        ///
        /// Сектор задан ПОРОГОМ КОСИНУСА, а не углом: тригонометрии в
        /// симуляции нет, а скалярное произведение даёт ту же проверку без
        /// единого Atan2. Тело считается задетым, если его центр в радиусе;
        /// поправка на радиус тела делается по расстоянию, но не по углу —
        /// вплотную сбоку сектор иначе становится кругом.
        /// </summary>
        private int CollectArc(FixVec2 origin, FixVec2 direction, Fix64 radius,
            Fix64 arcCosine, int[] into)
        {
            int count = 0;
            for (int i = 1; i < Entities.Count && count < into.Length; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;

                FixVec2 delta = Entities.Position[i] - origin;
                Fix64 distance = delta.Length;
                if (distance > radius + Entities.BodyRadius[i]) continue;

                // Тело в самом центре: направления на него нет, но задето оно
                // безусловно — иначе враг, стоящий вплотную, был бы неуязвим.
                if (distance.Raw != 0
                    && FixVec2.Dot(delta / distance, direction) < arcCosine) continue;

                into[count++] = i;
            }
            return count;
        }

        /// <summary>
        /// АБОРДАЖ. Удар свободным кулаком в момент прибытия.
        ///
        /// Урон нанесён именно ПО ПРИБЫТИИ, а не в момент нажатия: способность
        /// стоит замаха и полёта, и удар, случившийся до полёта, превратил бы
        /// перемещение в бесплатное приложение к атаке. Всё остальное в этой
        /// связке — тяга себя цепью — работает ровно как раньше, включая
        /// анимацию: сменилась только цель, точка в мире стала врагом.
        /// </summary>
        private void ResolveBoardingPunch()
        {
            if (_leapPunchTick < 0 || Tick < _leapPunchTick) return;
            _leapPunchTick = -1;

            if (!Entities.Alive[PlayerId]) return;
            AbilityBuild build = _leapSlot >= 0 && _leapSlot < AbilitySlots
                ? _abilityBuilds[_leapSlot] : null;
            if (build == null || build.DefinitionId != AbilityDefinition.AnchorLeapId) return;

            int damage = build.Get(AbilityStatType.Damage).ToInt();
            if (damage <= 0) return;

            // Бьём того, за кого цеплялись. Если он умер по дороге — ближайшего
            // из тех, к кому мы в итоге приехали: кулак уже занесён.
            int victim = -1;
            if ((uint)_leapTarget < (uint)Entities.Count && Entities.Alive[_leapTarget]
                && Entities.Side[_leapTarget] != Entities.Side[PlayerId])
                victim = _leapTarget;

            FixVec2 at = Entities.Position[PlayerId];
            if (victim < 0)
            {
                Fix64 closest = Fix64.Zero;
                for (int i = 1; i < Entities.Count; i++)
                {
                    if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                    Fix64 gap = (Entities.Position[i] - at).LengthSq;
                    Fix64 limit = AnchorKit.ChainStandoff + Entities.BodyRadius[i];
                    if (gap > limit * limit) continue;
                    if (victim >= 0 && gap >= closest) continue;
                    victim = i;
                    closest = gap;
                }
            }

            if (victim >= 0)
                ApplyAbilityDamage(PlayerId, victim, damage, _leapSlot, DamageType.Physical);
        }

        /// <summary>
        /// БАЗОВЫЙ РЫВОК. Короткий уход в сторону курсора.
        ///
        /// Состояния не держит вовсе: рывок — это одно назначение
        /// принудительного перемещения, всё остальное доделывает ForcedMotion.
        /// Ни попадания, ни этапов, ни окна — поэтому нет ни Update, ни хеша.
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
            ForcedMotion.Begin(Entities, PlayerId,
                from + direction * build.Get(AbilityStatType.Radius),
                ticks, ForcedMotionKind.Roll);
        }

        // ---- Крушение ----

        private int _wreckSlot = -1;
        private int _wreckStage;
        private int _wreckImpactTick = -1;
        private int _wreckWindowEndTick = -1;
        private FixVec2 _wreckDirection;

        /// <summary>Сколько ударов комбо уже нанесено: 0..3.</summary>
        public int WreckStage => _wreckStage;

        /// <summary>Открыто ли окно следующего нажатия. Показу — подсветить кнопку.</summary>
        public bool WreckComboOpen
            => _wreckSlot >= 0 && _wreckStage > 0 && _wreckStage < WreckStages
               && Tick <= _wreckWindowEndTick;

        /// <summary>Три удара: справа, обратный слева, тяжёлый перед собой.</summary>
        public const int WreckStages = 3;

        private void StopWreck()
        {
            _wreckSlot = _wreckImpactTick = _wreckWindowEndTick = -1;
            _wreckStage = 0;
            _wreckDirection = FixVec2.Zero;
        }

        private void AimWreck(int slot, FixVec2 aim)
        {
            FixVec2 direction = aim - Entities.Position[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);

            _wreckDirection = direction.Normalized();
            Entities.Facing[PlayerId] = _wreckDirection;

            AbilityBuild build = _abilityBuilds[slot];
            int windup = build.Get(AbilityStatType.WindupTicks).ToInt();
            _wreckImpactTick = Tick + (windup < 1 ? 1 : windup);
            _wreckWindowEndTick = -1;
        }

        /// <summary>Первое нажатие: комбо начинается с первого удара.</summary>
        private void BeginWreck(int slot, FixVec2 aim)
        {
            _wreckSlot = slot;
            _wreckStage = 0;
            AimWreck(slot, aim);
        }

        /// <summary>
        /// Следующее нажатие внутри окна: продолжение той же серии.
        ///
        /// Направление переспрашивается на каждом ударе. Комбо, намертво
        /// прибитое к направлению первого нажатия, разворачивало бы героя
        /// спиной к тем, кто подошёл за эти полсекунды.
        /// </summary>
        private void AdvanceWreck(FixVec2 aim)
        {
            AimWreck(_wreckSlot, aim);
        }

        private void UpdateWreck()
        {
            if (_wreckSlot < 0) return;

            AbilityBuild build = _abilityBuilds[_wreckSlot];
            if (build == null || build.DefinitionId != AbilityDefinition.WreckId
                || !Entities.Alive[PlayerId])
            { StopWreck(); return; }

            // Окно закрылось, а нажатия не было — серия оборвана по решению
            // игрока или по опозданию. И то и другое законно.
            if (_wreckImpactTick < 0 && _wreckWindowEndTick >= 0 && Tick > _wreckWindowEndTick)
            {
                EndWreckCombo(build);
                return;
            }

            if (_wreckImpactTick < 0 || Tick < _wreckImpactTick) return;
            _wreckImpactTick = -1;

            bool finisher = _wreckStage == WreckStages - 1;
            int damage = build.Get(AbilityStatType.Damage).ToInt();

            // ЗАВЕРШАЮЩИЙ УДАР ТЯЖЕЛЕЕ ВДВОЕ. Ровный урон по этапам означал бы,
            // что прерывать серию никогда не жалко, и третьего нажатия просто
            // не существовало бы как решения.
            if (finisher) damage *= 2;

            Fix64 radius = build.Get(AbilityStatType.Radius);
            Fix64 arc = build.Get(AbilityStatType.ArcCosine);
            int stunTicks = finisher ? build.Get(AbilityStatType.StunTicks).ToInt() : 0;

            _events.Add(new SimEvent(SimEventType.WreckStage, PlayerId, -1, _wreckStage,
                finisher, Entities.Position[PlayerId]));

            int count = CollectArc(Entities.Position[PlayerId], _wreckDirection, radius, arc, _arcScratch);
            for (int c = 0; c < count; c++)
            {
                int id = _arcScratch[c];
                ApplyAbilityDamage(PlayerId, id, damage, _wreckSlot, DamageType.Physical);
                if (!Entities.Alive[id] || stunTicks <= 0) continue;

                Statuses.ApplyStun(id, Tick + stunTicks);
                Entities.Velocity[id] = FixVec2.Zero;
                Entities.PendingAttackTarget[id] = -1;
                Entities.AttackImpactTick[id] = 0;
                Entities.PendingAttackVariant[id] = 0;
                _events.Add(new SimEvent(SimEventType.Stun, PlayerId, id, stunTicks, false,
                    Entities.Position[id]));
            }

            _wreckStage++;
            if (_wreckStage >= WreckStages) { EndWreckCombo(build); return; }

            _wreckWindowEndTick = Tick + build.Get(AbilityStatType.ComboWindowTicks).ToInt();
        }

        /// <summary>
        /// КУЛДАУН ОТСЧИТЫВАЕТСЯ ОТ КОНЦА СЕРИИ, а не от первого нажатия.
        ///
        /// Иначе игрок, отыгравший все три удара, получал бы способность назад
        /// почти сразу, а оборвавший её после первого — ждал бы столько же.
        /// Плата обязана зависеть от того, сколько ты взял.
        /// </summary>
        private void EndWreckCombo(AbilityBuild build)
        {
            int slot = _wreckSlot;
            StopWreck();
            if (slot >= 0) _abilityReadyTick[slot] = Tick + build.CooldownTicks;
        }

        private void HashWreck(ref ulong hash)
        {
            Hashing.Mix(ref hash, _wreckSlot);
            Hashing.Mix(ref hash, _wreckStage);
            Hashing.Mix(ref hash, _wreckImpactTick);
            Hashing.Mix(ref hash, _wreckWindowEndTick);
            Hashing.Mix(ref hash, _wreckDirection.X);
            Hashing.Mix(ref hash, _wreckDirection.Y);
        }

        private readonly int[] _arcScratch = new int[64];
    }
}
