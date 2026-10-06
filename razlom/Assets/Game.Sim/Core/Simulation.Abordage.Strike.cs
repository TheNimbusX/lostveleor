namespace Game.Sim
{
    /// <summary>Абордаж: удар кулаком, таланты линии и метка, сброс и хеш (Simulation.Abordage).</summary>
    public sealed partial class Simulation
    {
        /// <summary>Метка (заготовка таланта линии Абордажа, черта AbordageMark): цель 3 с получает от Пелага ×1,30.</summary>
        public const int AbordageMarkTicks = 3 * TicksPerSecond;
        public const int AbordageMarkPercent = 130;

        private int[] _abordageMarkUntil;
        private bool _abordageMarkUsed;

        /// <summary>
        /// Прибытие — удар. Цель жива и в досягаемости — кулак в неё; умерла или
        /// ушла — в ближайшего в той же досягаемости, иначе в воздух (Flag false).
        /// Событие удара — до урона: Damage, Death и Stun идут сразу за ним; потом форма.
        /// </summary>
        private void StrikeAbordage(AbilityBuild build)
        {
            if (Entities.ForcedKind[PlayerId] == (byte)ForcedMotionKind.Lunge) ForcedMotion.Clear(Entities, PlayerId);
            int slot = _abordage.Slot;
            int target = _abordage.Target;
            int victim = SquallTargetValid(target) && AbordageContactReachable(target) ? target : AbordageNearestInReach();
            bool landed = victim >= 0;
            FixVec2 direction = AbordagePullDirection();

            _abordage.Phase = AbordagePhase.Strike;
            _abordage.ArriveTick = Tick;
            _abordage.PhaseEndTick = Tick + AbordageHoldTicks;
            _abordage.Landed = landed;
            if (landed) _abordage.Target = victim;
            // Взгляд в конце — по тяге, без доворота: конец каста его не трогает.
            AbordageFace(direction);
            PelagForm form = FormAt(slot);
            _events.Add(new SimEvent(SimEventType.AbordagePunch, PlayerId, landed ? victim : target, 0, landed,
                Entities.Position[PlayerId], DamageType.Physical, DamageOrigin.Ability, (int)form));

            int damage = BoardingMomentum(build, build.Get(AbilityStatType.Damage).ToInt());
            bool any = false;
            if (landed && damage > 0)
            {
                ApplyAbilityDamage(PlayerId, victim, damage, slot, DamageType.Physical);
                if (_abordage.Phase != AbordagePhase.Strike) return;   // герой умер от отражения — всё сброшено
                int stun = AbordageFistStun(build, slot);
                if (stun > 0 && Entities.Alive[victim]) StunByTalent(victim, stun);
                AbordageMarkOnStrike(build, victim);
                any = true;
            }

            // «На абордаж!»: кулак достаёт всех врагов в 2 м от точки прибытия (100 %).
            if (build.Has(AbilityFlag.BoardingSweep) && damage > 0)
            {
                FixVec2 at = Entities.Position[PlayerId];
                for (int i = PlayerId + 1; i < Entities.Count; i++)
                {
                    if (i == victim || !Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                    Fix64 reach = BoardingSweepRadius + Entities.BodyRadius[i];
                    if ((Entities.Position[i] - at).LengthSq > reach * reach) continue;
                    ApplyAbilityDamage(PlayerId, i, damage, slot, DamageType.Physical);
                    if (_abordage.Phase != AbordagePhase.Strike) return;
                    BoardingAfterHit(build, i);
                    any = true;
                }
            }
            if (any && build.Has(AbilityFlag.BoardingHilt)) ShortenOtherCooldowns(slot, BoardingHiltTicks);
            if (any) BoardingSureCrit(build);

            if (form == PelagForm.AbordageQuake) StartAbordageQuake(build, landed ? victim : -1);
            else if (form == PelagForm.AbordageGeyser && landed) StartAbordageGeyser(build, victim);
            else if (form == PelagForm.AbordageBreach && landed) StartAbordageBreach(build, victim, direction);
        }

        /// <summary>
        /// Оглушение кулаком: «Тяжёлый кулак» (0,5 с); у Обвала (сбивание) и Гейзера
        /// (подъём) — 0,8 с; вместе берётся большее, не складывается. Босс не оглушается (StunByTalent).
        /// </summary>
        private int AbordageFistStun(AbilityBuild build, int slot)
        {
            int stun = build.Has(AbilityFlag.BoardingStun) ? BoardingStunTicks : 0;
            if (FormIs(slot, PelagForm.AbordageQuake) && stun < AbordageQuakeKnockdownTicks) stun = AbordageQuakeKnockdownTicks;
            if (FormIs(slot, PelagForm.AbordageGeyser) && stun < AbordageGeyserLiftTicks) stun = AbordageGeyserLiftTicks;
            return stun;
        }

        /// <summary>
        /// Метка — заготовка таланта линии Абордажа (критик 02.10: числа, цвет и
        /// событие — в проходе талантов). Включается чертой AbilityTrait.AbordageMark;
        /// узла, который её даёт, пока нет — в игре не срабатывает.
        /// </summary>
        private void AbordageMarkOnStrike(AbilityBuild build, int victim)
        {
            if (!build.Has(AbilityTrait.AbordageMark) || _abordageMarkUntil == null || !Entities.Alive[victim]) return;
            _abordageMarkUntil[victim] = Tick + AbordageMarkTicks;
            _abordageMarkUsed = true;
        }

        /// <summary>Цель под Меткой (до тика конца, не включая его).</summary>
        public bool AbordageMarked(int id)
            => _abordageMarkUsed && (uint)id < (uint)_abordageMarkUntil.Length && _abordageMarkUntil[id] > Tick;

        /// <summary>Урон Пелага по цели под Меткой ×1,30 — в обоих путях урона (способности и сабля).</summary>
        private int AbordageMarkAmplify(int source, int target, int amount)
            => source == PlayerId && AbordageMarked(target) ? amount * AbordageMarkPercent / 100 : amount;

        private void EnsureAbordageBuffers()
        {
            if (_abordageWaveHits != null) return;
            int capacity = Entities.Capacity;
            _abordageWaveHits = new bool[capacity];
            _abordageFall = new int[capacity];
            _abordageFallAt = new FixVec2[capacity];
            _abordageAirborne = new bool[capacity];
            _abordageFallDamage = new int[capacity];
            _abordageMarkUntil = new int[capacity];
        }

        private void ResetAbordage()
        {
            _abordage = new AbordageState { Target = -1, WaveTick = -1, GeyserTarget = -1, GeyserFallTick = -1 };
            _abordageFallCount = 0;
            _abordageMarkUsed = false;
            if (_abordageWaveHits == null) return;
            System.Array.Clear(_abordageWaveHits, 0, _abordageWaveHits.Length);
            System.Array.Clear(_abordageFall, 0, _abordageFall.Length);
            System.Array.Clear(_abordageFallAt, 0, _abordageFallAt.Length);
            System.Array.Clear(_abordageAirborne, 0, _abordageAirborne.Length);
            System.Array.Clear(_abordageFallDamage, 0, _abordageFallDamage.Length);
            System.Array.Clear(_abordageMarkUntil, 0, _abordageMarkUntil.Length);
        }

        /// <summary>
        /// Только после первого Абордажа расстановки, пока живы фронт формы, падения
        /// Гейзера и Метки: без них хеш прежний бит в бит.
        /// </summary>
        private void HashAbordage(ref ulong hash)
        {
            if (_abordage.Serial != 0)
            {
                Hashing.Mix(ref hash, 0x41425244);   // "ABRD"
                _abordage.HashInto(ref hash);
            }
            if (_abordage.WaveTick >= 0 && _abordageWaveHits != null)
            {
                Hashing.Mix(ref hash, 0x41425756);   // "ABWV"
                for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, _abordageWaveHits[i] ? 1 : 0);
            }
            if (_abordageFallCount > 0)
            {
                Hashing.Mix(ref hash, 0x41424759);   // "ABGY"
                Hashing.Mix(ref hash, _abordageFallCount);
                for (int i = 0; i < Entities.Count; i++)
                {
                    if (_abordageFall[i] == 0) continue;
                    Hashing.Mix(ref hash, i); Hashing.Mix(ref hash, _abordageFall[i]);
                    Hashing.Mix(ref hash, _abordageFallAt[i].X); Hashing.Mix(ref hash, _abordageFallAt[i].Y);
                    Hashing.Mix(ref hash, _abordageAirborne[i] ? 1 : 0); Hashing.Mix(ref hash, _abordageFallDamage[i]);
                }
            }
            if (_abordageMarkUsed)
            {
                Hashing.Mix(ref hash, 0x41424D4B);   // "ABMK"
                for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, _abordageMarkUntil[i]);
            }
        }
    }
}
