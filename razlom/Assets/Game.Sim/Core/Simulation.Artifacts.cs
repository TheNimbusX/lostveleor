namespace Game.Sim
{
    /// <summary>
    /// Действие артефакта забега (RunArtifact) — набор акта I по списку владельца, утверждён
    /// 24 сентября (DESIGN, «Выбор владельца, 24 сентября (ночь)»). Активные включаются
    /// клавишей F (InputFlags.UseArtifact) и уходят на перезарядку; Обет Хранителя срабатывает сам.
    /// Держит RiftRun: ставит при выборе и снимает в начале забега. Симуляция забега одна,
    /// тики идут сквозь арены — перезарядка переживает смену арены, а действие на ней кончается.
    /// </summary>
    public sealed partial class Simulation
    {
        public RunArtifact Artifact { get; private set; }

        private int _artifactReadyTick, _artifactActiveUntil;
        private bool _vowUsed;
        private int _vowImmuneUntil;

        /// <summary>Когда артефакт снова можно включить.</summary>
        public int ArtifactReadyTick => _artifactReadyTick;
        /// <summary>До какого тика действует включённый артефакт.</summary>
        public int ArtifactActiveUntil => _artifactActiveUntil;
        public bool ArtifactEffectActive => Tick < _artifactActiveUntil;
        /// <summary>Обет Хранителя уже спас в этом забеге.</summary>
        public bool VowUsed => _vowUsed;

        public void SetArtifact(RunArtifact artifact)
        {
            EnsureArtifactBuffers();
            EndArtifactEffects();
            Artifact = artifact;
            _artifactReadyTick = _artifactActiveUntil = 0;
            _vowUsed = false;
            _vowImmuneUntil = 0;
        }

        // ---- числа владельца (его список, доработано под нашу игру) ----

        public const int SunSealTicks = 4 * TicksPerSecond;
        public const int MirrorTicks = 6 * TicksPerSecond;
        public const int WinterTicks = 5 * TicksPerSecond;
        public const int HourglassTicks = 4 * TicksPerSecond;
        public const int VoidTicks = 6 * TicksPerSecond;
        public const int CrimsonTicks = 10 * TicksPerSecond;
        public const int VowImmuneTicks = 3 * TicksPerSecond;

        /// <summary>Сердце Вечной Зимы: радиус и «раскол» — удар способностью по замёрзшему.</summary>
        private static readonly Fix64 WinterRadius = Fix64.FromInt(8);
        public const int WinterShatterDamage = 60;
        /// <summary>Лик Пустоты: взрыв при досрочном выходе.</summary>
        public const int VoidBurstDamage = 80;
        private static readonly Fix64 VoidBurstRadius = Fix64.Ratio(5, 2);
        /// <summary>Багровое Сердце: радиус финального импульса.</summary>
        private static readonly Fix64 CrimsonBurstRadius = Fix64.FromInt(4);
        /// <summary>Обет Хранителя: волна отталкивания.</summary>
        private static readonly Fix64 VowWaveRadius = Fix64.FromInt(3);
        private static readonly Fix64 VowWavePush = Fix64.Ratio(5, 2);

        private const int WinterSlowId = 0x5749, CrimsonId = 0x4352;

        /// <summary>Перезарядка артефакта, тиков; 0 — пассивный.</summary>
        public static int ArtifactCooldownTicks(RunArtifact artifact)
        {
            switch (artifact)
            {
                case RunArtifact.SunSeal: return 60 * TicksPerSecond;
                case RunArtifact.ReturnDial: return 75 * TicksPerSecond;
                case RunArtifact.VengeanceMirror: return 50 * TicksPerSecond;
                case RunArtifact.WinterHeart: return 60 * TicksPerSecond;
                case RunArtifact.Hourglass: return 90 * TicksPerSecond;
                case RunArtifact.VoidVisage: return 45 * TicksPerSecond;
                case RunArtifact.CrimsonHeart: return 60 * TicksPerSecond;
                default: return 0;
            }
        }

        /// <summary>Сколько тиков действует; 0 — мгновенный или пассивный.</summary>
        public static int ArtifactDurationTicks(RunArtifact artifact)
        {
            switch (artifact)
            {
                case RunArtifact.SunSeal: return SunSealTicks;
                case RunArtifact.VengeanceMirror: return MirrorTicks;
                case RunArtifact.WinterHeart: return WinterTicks;
                case RunArtifact.Hourglass: return HourglassTicks;
                case RunArtifact.VoidVisage: return VoidTicks;
                case RunArtifact.CrimsonHeart: return CrimsonTicks;
                default: return 0;
            }
        }

        public static bool IsActiveArtifact(RunArtifact artifact) => ArtifactCooldownTicks(artifact) > 0;

        private bool Using(RunArtifact artifact) => Artifact == artifact && Tick < _artifactActiveUntil;

        /// <summary>Лик Пустоты: героя не бьют, он проходит сквозь врагов и сам не атакует.</summary>
        public bool VoidPhased => Using(RunArtifact.VoidVisage);

        /// <summary>Песочные Часы: враги и их снаряды стоят, урон по ним копится.</summary>
        public bool TimeStopped => Using(RunArtifact.Hourglass);

        /// <summary>Защита артефакта: Солнечная Печать, фаза Лика Пустоты, неуязвимость после Обета.</summary>
        private bool ArtifactShields => Using(RunArtifact.SunSeal) || VoidPhased || Tick < _vowImmuneUntil;

        // ---- включение ----

        private void ResolveArtifactUse(in InputFrame input)
        {
            if (!input.Has(InputFlags.UseArtifact) || !Entities.Alive[PlayerId]) return;
            // Лик Пустоты: повторное нажатие — выход из фазы раньше и взрыв в точке появления.
            if (VoidPhased)
            {
                _artifactActiveUntil = Tick;
                BurstAround(Entities.Position[PlayerId], VoidBurstRadius, VoidBurstDamage, DamageType.Physical);
                return;
            }
            if (!IsActiveArtifact(Artifact) || Tick < _artifactReadyTick) return;

            _artifactReadyTick = Tick + ArtifactCooldownTicks(Artifact);
            _artifactActiveUntil = Tick + ArtifactDurationTicks(Artifact);
            _events.Add(new SimEvent(SimEventType.ArtifactUsed, PlayerId, -1, ArtifactDurationTicks(Artifact), false,
                Entities.Position[PlayerId], actionVariant: (int)Artifact));

            switch (Artifact)
            {
                case RunArtifact.ReturnDial:
                    for (int slot = 0; slot < AbilitySlots; slot++) _abilityReadyTick[slot] = Tick;
                    _boardingSpareReadyTick = _flaskSpareReadyTick = Tick;
                    break;
                case RunArtifact.WinterHeart:
                    StartWinter();
                    break;
                case RunArtifact.Hourglass:
                    StartHourglass();
                    break;
                case RunArtifact.CrimsonHeart:
                    _crimsonLost = _crimsonStacks = 0;
                    break;
            }
        }

        /// <summary>Каждый тик: конец действия — отпустить часы, взорвать Багровое Сердце, снять замедление.</summary>
        private void UpdateArtifact()
        {
            if (_artifactActiveUntil <= 0 || Tick < _artifactActiveUntil) return;
            FinishArtifactEffect(natural: true);
            _artifactActiveUntil = 0;
        }

        private void FinishArtifactEffect(bool natural)
        {
            switch (Artifact)
            {
                case RunArtifact.Hourglass:
                    if (natural) ReleaseHeldDamage();
                    else if (_heldDamage != null) System.Array.Clear(_heldDamage, 0, _heldDamage.Length);
                    break;
                case RunArtifact.CrimsonHeart:
                    if (natural && _crimsonLost > 0 && Entities.Alive[PlayerId])
                        BurstAround(Entities.Position[PlayerId], CrimsonBurstRadius, _crimsonLost * 2, DamageType.Fire);
                    SetCrimsonStacks(0);
                    _crimsonLost = 0;
                    break;
                case RunArtifact.WinterHeart:
                    EndWinterSlow();
                    break;
            }
        }

        /// <summary>Смена арены или смерть: действие кончается без финальных ударов, перезарядка остаётся.</summary>
        private void EndArtifactEffects()
        {
            if (_artifactActiveUntil > 0) FinishArtifactEffect(natural: false);
            _artifactActiveUntil = 0;
            _vowImmuneUntil = 0;
            if (_frozenUntil != null) System.Array.Clear(_frozenUntil, 0, _frozenUntil.Length);
        }

        private void BurstAround(FixVec2 at, Fix64 radius, int damage, DamageType type)
        {
            if (damage <= 0) return;
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                Fix64 reach = radius + Entities.BodyRadius[i];
                if (FixVec2.DistanceSq(at, Entities.Position[i]) > reach * reach) continue;
                ApplyAbilityDamage(PlayerId, i, damage, -1, type);
            }
        }

        // ---- Сердце Вечной Зимы ----

        private int[] _frozenUntil;
        private bool[] _winterSlowed;

        private void StartWinter()
        {
            FixVec2 at = Entities.Position[PlayerId];
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                Fix64 reach = WinterRadius + Entities.BodyRadius[i];
                if (FixVec2.DistanceSq(at, Entities.Position[i]) > reach * reach) continue;
                if (IsElite(i))
                {
                    // Босса и элиту не остановить: сильное замедление на то же время.
                    var sheet = Entities.Stats[i];
                    sheet.RemoveSource(ModifierSource.Buff, WinterSlowId);
                    sheet.Add(StatModifier.Increased(StatType.MoveSpeed, Fix64.Ratio(-40, 100), ModifierSource.Buff, WinterSlowId));
                    sheet.Add(StatModifier.Increased(StatType.AttackSpeed, Fix64.Ratio(-40, 100), ModifierSource.Buff, WinterSlowId));
                    Entities.RefreshStats(i);
                    _winterSlowed[i] = true;
                    continue;
                }
                StunByTalent(i, WinterTicks);
                _frozenUntil[i] = Tick + WinterTicks;
            }
        }

        private void EndWinterSlow()
        {
            if (_winterSlowed == null) return;
            for (int i = 1; i < Entities.Count && i < _winterSlowed.Length; i++)
            {
                if (!_winterSlowed[i]) continue;
                _winterSlowed[i] = false;
                Entities.Stats[i].RemoveSource(ModifierSource.Buff, WinterSlowId);
                Entities.RefreshStats(i);
            }
        }

        /// <summary>«Раскол»: удар способностью по замёрзшему врагу бьёт сильнее и снимает лёд.</summary>
        private int WinterShatter(int target, int amount)
        {
            if (_frozenUntil == null || _frozenUntil[target] <= Tick) return amount;
            _frozenUntil[target] = 0;
            Statuses.StunUntilTick[target] = Tick;
            return amount + WinterShatterDamage;
        }

        // ---- Песочные Часы Безвременья ----

        private int[] _heldDamage;

        private void StartHourglass()
        {
            if (_heldDamage != null) System.Array.Clear(_heldDamage, 0, _heldDamage.Length);
            for (int i = 1; i < Entities.Count; i++)
                if (Entities.Alive[i] && Entities.Side[i] != Entities.Side[PlayerId]) StunByTalent(i, HourglassTicks);
            DelayForestFruit(HourglassTicks);
        }

        /// <summary>Урон по врагу в остановленном времени не проходит, а копится до конца остановки.</summary>
        private bool HoldDamage(int source, int target, int amount)
        {
            if (!TimeStopped || source != PlayerId || target == PlayerId || _heldDamage == null) return false;
            _heldDamage[target] += amount;
            return true;
        }

        private void ReleaseHeldDamage()
        {
            if (_heldDamage == null) return;
            for (int i = 1; i < Entities.Count; i++)
            {
                int held = _heldDamage[i];
                _heldDamage[i] = 0;
                if (held <= 0 || !Entities.Alive[i]) continue;
                Entities.Health[i] -= held;
                _events.Add(SimEvent.Damage(PlayerId, i, held, true, Entities.Position[i], DamageType.Physical, DamageOrigin.Ability, -1));
                if (Entities.Health[i] <= 0) Kill(i, PlayerId, -1);
            }
        }

        // ---- Зеркало Возмездия ----

        /// <summary>Урон по герою под Зеркалом: −25% ему, а полный — обратно атакующему.</summary>
        private int MirrorIncoming(int source, int target, int damage)
        {
            if (target != PlayerId || !Using(RunArtifact.VengeanceMirror) || damage <= 0) return damage;
            if ((uint)source < (uint)Entities.Count && source != PlayerId && Entities.Alive[source])
                ApplyAbilityDamage(PlayerId, source, damage, -1, DamageType.Physical);
            return damage * 75 / 100;
        }

        // ---- Багровое Сердце ----

        private int _crimsonLost, _crimsonStacks;

        /// <summary>Урон героя под Багровым Сердцем: +8% за каждые потерянные 10% здоровья.</summary>
        private int CrimsonOutgoing(int source, int amount)
            => source == PlayerId && Using(RunArtifact.CrimsonHeart) && _crimsonStacks > 0 ? amount * (100 + 8 * _crimsonStacks) / 100 : amount;

        private void CrimsonTookDamage(int target, int amount)
        {
            if (target != PlayerId || !Using(RunArtifact.CrimsonHeart) || amount <= 0) return;
            _crimsonLost += amount;
            int max = System.Math.Max(1, Entities.MaxHealth[PlayerId]);
            SetCrimsonStacks(_crimsonLost * 10 / max);
        }

        private void SetCrimsonStacks(int stacks)
        {
            if (stacks == _crimsonStacks) return;
            _crimsonStacks = stacks;
            var sheet = Entities.Stats[PlayerId];
            sheet.RemoveSource(ModifierSource.Buff, CrimsonId);
            if (stacks <= 0) return;
            Fix64 share = Fix64.Ratio(8 * stacks, 100);
            sheet.Add(StatModifier.Flat(StatType.AbilitySpeed, share, ModifierSource.Buff, CrimsonId));
            sheet.Add(StatModifier.Increased(StatType.AttackSpeed, share, ModifierSource.Buff, CrimsonId));
            sheet.Add(StatModifier.Increased(StatType.LavidiumRegen, share, ModifierSource.Buff, CrimsonId));
        }

        // ---- Обет Хранителя ----

        /// <summary>
        /// Смертельный удар по герою: Обет возвращает половину здоровья, даёт 3 с неуязвимости и
        /// отталкивает врагов вокруг. Раз за забег, пока в игре нет комнат отдыха.
        /// </summary>
        private bool VowSaves(int target)
        {
            if (target != PlayerId || Artifact != RunArtifact.GuardianVow || _vowUsed) return false;
            _vowUsed = true;
            Entities.Health[PlayerId] = System.Math.Max(1, Entities.MaxHealth[PlayerId] / 2);
            _vowImmuneUntil = Tick + VowImmuneTicks;
            _events.Add(new SimEvent(SimEventType.ArtifactUsed, PlayerId, -1, VowImmuneTicks, false,
                Entities.Position[PlayerId], actionVariant: (int)Artifact));
            FixVec2 at = Entities.Position[PlayerId];
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                FixVec2 delta = Entities.Position[i] - at;
                Fix64 distance = delta.Length;
                if (distance > VowWaveRadius + Entities.BodyRadius[i] || distance.Raw == 0) continue;
                ForcedMotion.Begin(Entities, i, Entities.Position[i] + delta / distance * VowWavePush, 8, ForcedMotionKind.Dragged);
            }
            return true;
        }

        // ---- общее ----

        private void EnsureArtifactBuffers()
        {
            if (_frozenUntil != null) return;
            _frozenUntil = new int[Entities.Capacity];
            _winterSlowed = new bool[Entities.Capacity];
            _heldDamage = new int[Entities.Capacity];
        }

        /// <summary>Отбор урона артефактом по пути способности: часы копят, зима раскалывает, сердце усиливает.</summary>
        private int ArtifactOutgoing(int source, int target, int amount, bool ability)
        {
            if (source != PlayerId || target == PlayerId) return amount;
            if (ability) amount = WinterShatter(target, amount);
            return CrimsonOutgoing(source, amount);
        }

        private void HashArtifact(ref ulong hash)
        {
            if (Artifact == RunArtifact.None) return;
            Hashing.Mix(ref hash, (int)Artifact);
            Hashing.Mix(ref hash, _artifactReadyTick);
            Hashing.Mix(ref hash, _artifactActiveUntil);
            Hashing.Mix(ref hash, _vowUsed ? 1 : 0);
            Hashing.Mix(ref hash, _vowImmuneUntil);
            Hashing.Mix(ref hash, _crimsonLost);
            Hashing.Mix(ref hash, _crimsonStacks);
            if (_frozenUntil == null) return;
            for (int i = 0; i < Entities.Count; i++)
            {
                Hashing.Mix(ref hash, _frozenUntil[i]);
                Hashing.Mix(ref hash, _heldDamage[i]);
                Hashing.Mix(ref hash, _winterSlowed[i] ? 1 : 0);
            }
        }
    }
}
