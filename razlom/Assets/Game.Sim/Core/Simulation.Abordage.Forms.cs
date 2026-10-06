namespace Game.Sim
{
    /// <summary>
    /// ТРИ ФОРМЫ АБОРДАЖА (владелец 02.10 ~22:35, AGENTS/DESIGN.md «Формы навыка»):
    /// каждая — своя фигура воды в миг удара; тайминг каста у всех один.
    ///
    /// * ОБВАЛ (кольцо) — кулак в землю: от точки посадки по земле бежит волна
    ///   до 3 м (+ тело), каждый задет один раз, когда до него дошёл фронт: доля
    ///   кулака и сбивание (оглушение), лёгких толкает наружу. Цель кулака —
    ///   кулак и сбивание, без урона волны. Босс не сбивается.
    /// * ГЕЙЗЕР (столб) — апперкот: под целью столб воды подбрасывает лёгкую цель
    ///   (в воздухе оглушена, не ходит, не толкается и не толкает, урон проходит).
    ///   Тяжёлые, элиты и босс не взлетают — им столб и оглушение. Через 0,8 с
    ///   вода падает: всплеск в 2 м (+ тело) бьёт всех, включая цель.
    /// * ПРОБОИНА (струя) — за спиной цели по направлению тяги бьёт конус воды
    ///   4 м, ±25°; каждый задет один раз приходом фронта, лёгких отбрасывает от
    ///   вершины. Сама цель получает только кулак и стоит.
    ///
    /// Все числа — именованные ЗАГЛУШКИ под приёмку (кадры D, F-geyser-seagreen, G).
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Доля кулака (без «Разгона») у волны Обвала, падения Гейзера и струи Пробоины: 50 % (75 → 38).</summary>
        public const int AbordageFormDamagePercent = 50;

        // ---- Обвал ----

        /// <summary>Радиус волны Обвала (+ тело): 3 м — радиус, видимый край = край урона.</summary>
        public static readonly Fix64 AbordageQuakeRadius = Fix64.FromInt(3);

        /// <summary>Фронт волны: 0,6 м/тик — до края за AbordageQuakeTravelTicks.</summary>
        public static readonly Fix64 AbordageQuakeFrontStep = Fix64.Ratio(6, 10);
        public const int AbordageQuakeTravelTicks = 5;

        /// <summary>Сбивание Обвала (и кулака формы): оглушение 0,8 с.</summary>
        public const int AbordageQuakeKnockdownTicks = 24;

        // ---- Гейзер ----

        /// <summary>Подъём: цель в воздухе 0,8 с, потом падает вода.</summary>
        public const int AbordageGeyserLiftTicks = 24;

        /// <summary>Падение воды: всплеск в 2 м (+ тело) от места столба.</summary>
        public static readonly Fix64 AbordageGeyserFallRadius = Fix64.FromInt(2);

        // ---- Пробоина ----

        /// <summary>Струя: 4 м за целью, полуугол 25°, фронт 1 м/тик.</summary>
        public static readonly Fix64 AbordageBreachLength = Fix64.FromInt(4);
        public static readonly Fix64 AbordageBreachConeCos = Fix64.Ratio(9063, 10000);
        public static readonly Fix64 AbordageBreachFrontStep = Fix64.One;
        public const int AbordageBreachTravelTicks = 4;

        /// <summary>Вершина у босса: точка удара по корпусу + 0,3 м вглубь.</summary>
        public static readonly Fix64 AbordageBreachHullDepth = Fix64.Ratio(3, 10);

        /// <summary>Отброс струёй: 1,5 м за 4 тика, только лёгкие.</summary>
        public static readonly Fix64 AbordageBreachKnockback = Fix64.Ratio(3, 2);
        public const int AbordageBreachKnockbackTicks = 4;

        /// <summary>Толчок волны Обвала и падения Гейзера: 0,5 м наружу за 4 тика, только лёгкие.</summary>
        public static readonly Fix64 AbordageShoveDistance = Fix64.Ratio(1, 2);
        public const int AbordageShoveTicks = 4;

        private bool[] _abordageWaveHits;      // «задет один раз» фронтом Обвала или Пробоины
        private int[] _abordageFall;           // тик падения Гейзера (0 — столба нет)
        private FixVec2[] _abordageFallAt;
        private bool[] _abordageAirborne;      // подброшена (лёгкая) — в воздухе до падения
        private int[] _abordageFallDamage;
        private int _abordageFallCount;

        /// <summary>Цель Гейзера в воздухе: оглушена, расталкивание её не трогает. Вид поднимает модель.</summary>
        public bool AbordageLifted(int id)
            => _abordageAirborne != null && (uint)id < (uint)_abordageAirborne.Length && _abordageAirborne[id];

        /// <summary>
        /// Фронт воды формы виду: центр (у Пробоины — вершина конуса), направление
        /// струи (у Обвала — ноль) и докуда фронт дошёл к этому тику. False — фронта нет.
        /// </summary>
        public bool TryGetAbordageWave(out FixVec2 center, out FixVec2 direction, out Fix64 reachNow)
        {
            center = _abordage.WaveCenter;
            direction = _abordage.WaveDir;
            reachNow = Fix64.Zero;
            if (_abordage.WaveTick < 0) return false;
            bool quake = _abordage.WaveForm == PelagForm.AbordageQuake;
            int travel = quake ? AbordageQuakeTravelTicks : AbordageBreachTravelTicks;
            int steps = System.Math.Max(0, System.Math.Min(travel, Tick - _abordage.WaveTick));
            reachNow = (quake ? AbordageQuakeFrontStep : AbordageBreachFrontStep) * steps;
            return true;
        }

        private static int AbordageFormDamage(AbilityBuild build)
            => (build.Get(AbilityStatType.Damage).ToInt() * AbordageFormDamagePercent + 50) / 100;

        /// <summary>
        /// Лёгкий — по правилу толчка серии сабли и колец Вихря (ShoveByFoam): рядовые
        /// без веса-исключения; элиты, тяжёлые, Вендиго, Шипомёт и босс стоят.
        /// </summary>
        private bool AbordageLight(int id)
        {
            if (Entities.PushWeight[id].Raw <= 0 || IsElite(id) || AbordageLifted(id)) return false;
            EnemyKind kind = Entities.Kind[id];
            return kind == EnemyKind.None || kind == EnemyKind.ForestGuardian || kind == EnemyKind.ForestRootSwarm
                || kind == EnemyKind.ForestSplitling || kind == EnemyKind.ForestBud;
        }

        /// <summary>Толкнуть лёгкого прочь от точки. Толчок (Shoved) не перебивает уже идущее движение; отброс — перебивает.</summary>
        private void AbordagePush(int id, FixVec2 from, Fix64 distance, int ticks, ForcedMotionKind kind)
        {
            if (!Entities.Alive[id] || !AbordageLight(id)) return;
            if (kind == ForcedMotionKind.Shoved && ForcedMotion.IsActive(Entities, id)) return;
            FixVec2 delta = Entities.Position[id] - from;
            Fix64 length = delta.Length;
            FixVec2 away = length.Raw == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : delta / length;
            ForcedMotion.Begin(Entities, id, Entities.Position[id] + away * distance, ticks, kind);
        }

        private bool AbordageEnemy(int id)
            => Entities.Alive[id] && Entities.Side[id] != Entities.Side[PlayerId] && !ThicketShielded(id);

        // ---------- Обвал и Пробоина: фронт ----------

        private bool StartAbordageWave(AbilityBuild build, PelagForm form, FixVec2 center, FixVec2 direction, int skip)
        {
            if (_abordageWaveHits == null) return false;
            System.Array.Clear(_abordageWaveHits, 0, _abordageWaveHits.Length);
            if ((uint)skip < (uint)_abordageWaveHits.Length) _abordageWaveHits[skip] = true;
            _abordage.WaveTick = Tick;
            _abordage.WaveCenter = center;
            _abordage.WaveDir = direction;
            _abordage.WaveForm = form;
            _abordage.WaveDamage = AbordageFormDamage(build);
            return true;
        }

        private void StartAbordageQuake(AbilityBuild build, int fistTarget)
        {
            FixVec2 center = Entities.Position[PlayerId];
            if (!StartAbordageWave(build, PelagForm.AbordageQuake, center, FixVec2.Zero, fistTarget)) return;
            _events.Add(new SimEvent(SimEventType.AbordageQuake, PlayerId, fistTarget, AbordageQuakeTravelTicks, false, center,
                DamageType.Physical, DamageOrigin.Ability, (AbordageQuakeRadius * 100).ToInt()));
            SweepAbordageWave(1);
        }

        private void StartAbordageBreach(AbilityBuild build, int victim, FixVec2 direction)
        {
            FixVec2 hero = Entities.Position[PlayerId];
            FixVec2 apex = ThicketHullActive(victim)
                ? AbordageBitePoint(victim, hero) + direction * AbordageBreachHullDepth
                : Entities.Position[victim] + direction * Entities.BodyRadius[victim];
            if (!StartAbordageWave(build, PelagForm.AbordageBreach, apex, direction, victim)) return;
            _events.Add(new SimEvent(SimEventType.AbordageBreach, PlayerId, victim, AbordageBreachTravelTicks, false, apex,
                DamageType.Physical, DamageOrigin.Ability, (AbordageBreachLength * 100).ToInt()));
            SweepAbordageWave(1);
        }

        /// <summary>Каждый тик: фронт идёт дальше; после последнего шага фронта нет.</summary>
        private void UpdateAbordageWave()
        {
            if (_abordage.WaveTick < 0) return;
            int step = Tick - _abordage.WaveTick + 1;
            int travel = _abordage.WaveForm == PelagForm.AbordageQuake ? AbordageQuakeTravelTicks : AbordageBreachTravelTicks;
            if (step > travel) { _abordage.WaveTick = -1; return; }
            SweepAbordageWave(step);
        }

        /// <summary>
        /// Фронт за тик step прошёл полосу [(step − 1)·шаг, step·шаг]: бьёт тело,
        /// которое её касается (босс — по корпусу). Кто уже позади фронта — не задет.
        /// </summary>
        private void SweepAbordageWave(int step)
        {
            bool quake = _abordage.WaveForm == PelagForm.AbordageQuake;
            Fix64 front = quake ? AbordageQuakeFrontStep : AbordageBreachFrontStep;
            Fix64 inner = front * (step - 1), outer = front * step;
            FixVec2 center = _abordage.WaveCenter;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (_abordageWaveHits[i] || !AbordageEnemy(i)) continue;
                FixVec2 delta = Entities.Position[i] - center;
                Fix64 distance = delta.Length;
                Fix64 body = ThicketBodyFrom(i, center);
                if (distance - body > outer || distance + body < inner) continue;
                if (!quake && distance.Raw != 0 && FixVec2.Dot(delta, _abordage.WaveDir) < AbordageBreachConeCos * distance) continue;
                _abordageWaveHits[i] = true;
                ApplyAbilityDamage(PlayerId, i, _abordage.WaveDamage, _abordage.Slot, DamageType.Physical);
                if (_abordage.WaveTick < 0) return;   // герой умер от отражения — всё сброшено
                if (!Entities.Alive[i]) continue;
                if (quake)
                {
                    StunByTalent(i, AbordageQuakeKnockdownTicks);
                    AbordagePush(i, center, AbordageShoveDistance, AbordageShoveTicks, ForcedMotionKind.Shoved);
                }
                else AbordagePush(i, center, AbordageBreachKnockback, AbordageBreachKnockbackTicks, ForcedMotionKind.Knockback);
            }
        }

        // ---------- Гейзер ----------

        /// <summary>Столб под целью кулака. На одну цель — один подъём за раз: пока её вода не упала, нового нет.</summary>
        private void StartAbordageGeyser(AbilityBuild build, int victim)
        {
            if (_abordageFall == null || _abordageFall[victim] != 0) return;
            bool lifted = Entities.Alive[victim] && AbordageLight(victim);
            FixVec2 at = Entities.Position[victim];
            _abordageFall[victim] = Tick + AbordageGeyserLiftTicks;
            _abordageFallAt[victim] = at;
            _abordageAirborne[victim] = lifted;
            _abordageFallDamage[victim] = AbordageFormDamage(build);
            _abordageFallCount++;
            if (lifted)
            {
                // В воздухе тело не едет: ни шаг, ни начатый толчок.
                Entities.Velocity[victim] = FixVec2.Zero;
                ForcedMotion.Clear(Entities, victim);
            }
            _abordage.GeyserTarget = victim;
            _abordage.GeyserFallTick = Tick + AbordageGeyserLiftTicks;
            _events.Add(new SimEvent(SimEventType.AbordageGeyserLift, PlayerId, victim, AbordageGeyserLiftTicks, lifted, at,
                DamageType.Physical, DamageOrigin.Ability, (AbordageGeyserFallRadius * 100).ToInt()));
        }

        /// <summary>Каждый тик: вода, чей срок пришёл, падает (и после конца каста).</summary>
        private void UpdateAbordageGeysers()
        {
            if (_abordageFallCount <= 0) return;
            for (int i = PlayerId + 1; i < Entities.Count && _abordageFallCount > 0; i++)
                if (_abordageFall[i] != 0 && Tick >= _abordageFall[i]) GeyserFall(i);
        }

        /// <summary>Падение: всплеск в 2 м (+ тело) от места столба бьёт всех, включая подброшенную; лёгких толкает наружу.</summary>
        private void GeyserFall(int lifted)
        {
            FixVec2 center = _abordageFallAt[lifted];
            int damage = _abordageFallDamage[lifted];
            _abordageFall[lifted] = 0;
            _abordageAirborne[lifted] = false;
            _abordageFallDamage[lifted] = 0;
            _abordageFallCount--;
            if (_abordage.GeyserTarget == lifted) _abordage.GeyserFallTick = -1;
            _events.Add(new SimEvent(SimEventType.AbordageGeyserFall, PlayerId, lifted, 0, false, center,
                DamageType.Physical, DamageOrigin.Ability, (AbordageGeyserFallRadius * 100).ToInt()));
            int serial = _abordage.Serial;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (!AbordageEnemy(i)) continue;
                Fix64 reach = AbordageGeyserFallRadius + ThicketBodyFrom(i, center);
                if (FixVec2.DistanceSq(Entities.Position[i], center) > reach * reach) continue;
                ApplyAbilityDamage(PlayerId, i, damage, _abordage.Slot, DamageType.Physical);
                if (_abordage.Serial != serial) return;   // герой умер от отражения — всё сброшено
                if (i != lifted) AbordagePush(i, center, AbordageShoveDistance, AbordageShoveTicks, ForcedMotionKind.Shoved);
            }
        }
    }
}
