namespace Game.Sim
{
    /// <summary>
    /// Клятвы и грани сердца в бою (решение 06.10, план «Лагерь 06–10.10», T2).
    ///
    /// Снимок RunBoons приходит один раз на забег (SetBoons из RiftRun.StartRun) или, для
    /// героя лагеря, при покупке клятвы (GameSession.RefreshCampOaths). Хуки в общих файлах
    /// стоят с 06.10 и без клятв нейтральны: ×1, без щита, без спасения, хеш не трогается —
    /// поэтому без клятв StateHash и прибитые хеши прежние бит в бит.
    ///
    /// ДОЛГОЕ СОСТОЯНИЕ — на весь забег: «Последний вдох» потрачен, неуязвимость после него,
    /// бесплатный переброс «Второго взгляда», таймер бутона «Цветения». Оно здесь, потому что
    /// симуляция одна на весь забег и тики идут сквозь арены (тот же приём, что у _vowUsed).
    ///
    /// КОРОТКОЕ — на арену: корни, облака пыльцы и замедления. Расстановка рождает тела
    /// заново, и их листы статов, и номера сущностей — уже другие враги. Отдельного хука на
    /// смену арены нет (общие файлы не трогаются), поэтому смену видно по герою: рождение
    /// стирает его лист целиком, а с ним и метку клятв (OathModifierId). Пропала метка —
    /// была расстановка: состояние арены сбрасывается, прибавки героя вешаются заново.
    /// </summary>
    public sealed partial class Simulation
    {
        // "OATH" — прибавки героя и метка расстановки; "HRRT", "HPOL" — корни и пыльца на врагах.
        private const int OathModifierId = 0x4F415448, HeartRootModifierId = 0x48525254, HeartPollenModifierId = 0x48504F4C;

        public const int ToughHideHealthPerRank = 30, LightStepPercentPerRank = 5, KeenEyePercentPerRank = 4,
            DeepReserveLavidiumPerRank = 20, HeavyHandPercentPerRank = 8, QuickRollPercentPerRank = 10;
        public const int LastBreathHealthPercent = 30, LastBreathImmuneTicks = TicksPerSecond;
        public const int SteadfastPercent = 90, EnemyBloodHealPercent = 5;

        /// <summary>«Корни»: 0,5 с ходьбы 0 у врагов в 3 м от начала рывка (пробел №28).</summary>
        public const int HeartRootTicks = TicksPerSecond / 2;
        public static readonly Fix64 HeartRootRadius = Fix64.FromInt(3);

        /// <summary>«Пыльца»: облако 2 м на 3 с, −30% скорости, ещё 0,5 с после выхода, не больше 8 разом.</summary>
        public const int PollenTicks = 3 * TicksPerSecond, PollenSlowPercent = 30, PollenLingerTicks = TicksPerSecond / 2,
            MaxPollenClouds = 8;
        public static readonly Fix64 PollenRadius = Fix64.FromInt(2);

        /// <summary>«Цветение»: каждые 20 с боя — 5% здоровья, если герой ранен.</summary>
        public const int BloomPeriodTicks = 20 * TicksPerSecond, BloomHealPercent = 5;

        private RunBoons _boons;
        private bool _lastBreathUsed, _secondLookUsed;
        private int _oathImmuneUntil, _bloomReadyTick = -1;
        // Множитель «Тяжёлой руки» считается раз в SetBoons, а не на каждом ударе.
        private Fix64 _heavyHandScale = Fix64.One;

        // По номеру сущности; лениво, только когда есть грань.
        private int[] _heartRootUntil, _pollenSlowUntil;
        private int _rootedCount, _pollenSlowedCount;
        private FixVec2[] _pollenAt;
        private int[] _pollenUntil;
        private int _pollenNext;

        /// <summary>Снимок клятв и граней этой симуляции.</summary>
        public RunBoons Boons => _boons;

        /// <summary>«Последний вдох» уже поднял героя в этом забеге.</summary>
        public bool LastBreathUsed => _lastBreathUsed;

        /// <summary>Бесплатный переброс «Второго взгляда» уже потрачен в этом забеге.</summary>
        public bool SecondLookUsed => _secondLookUsed;

        /// <summary>Для вида: сколько тиков враг ещё в корнях (0 — свободен).</summary>
        public int HeartRootTicksLeft(int id)
            => _heartRootUntil != null && (uint)id < (uint)_heartRootUntil.Length && _heartRootUntil[id] > Tick
                ? _heartRootUntil[id] - Tick : 0;

        /// <summary>Для вида: замедлен ли враг пыльцой.</summary>
        public bool PollenSlowed(int id)
            => _pollenSlowUntil != null && (uint)id < (uint)_pollenSlowUntil.Length && _pollenSlowUntil[id] != 0;

        /// <summary>Для вида: облако пыльцы i (0…MaxPollenClouds−1), если оно живо.</summary>
        public bool TryGetPollenCloud(int i, out FixVec2 at, out int ticksLeft)
        {
            at = default;
            ticksLeft = 0;
            if (_pollenUntil == null || (uint)i >= MaxPollenClouds || _pollenUntil[i] <= Tick) return false;
            at = _pollenAt[i];
            ticksLeft = _pollenUntil[i] - Tick;
            return true;
        }

        /// <summary>Для вида: тиков до готовности бутона; 0 — готов и ждёт раны; −1 — «Цветения» нет.</summary>
        public int BloomTicksLeft
            => !_boons.Has(HeartFacet.ThicketBloom) ? -1 : _bloomReadyTick < 0 ? BloomPeriodTicks
                : _bloomReadyTick > Tick ? _bloomReadyTick - Tick : 0;

        /// <summary>
        /// Закрепляет снимок на забег. Новый снимок — новый забег: «Последний вдох», переброс
        /// и бутон снова готовы, корни и облака прошлой арены снимаются. Прибавки героя
        /// встают сразу, если он уже есть; иначе — после его рождения (см. UpdateOaths).
        /// </summary>
        public void SetBoons(in RunBoons b)
        {
            // Без клятв — ни бита разницы с прежним, даже ни одного выделения памяти.
            if (b.IsEmpty && _boons.IsEmpty) return;
            ClearOathArenaState();
            _boons = b;
            _lastBreathUsed = _secondLookUsed = false;
            _oathImmuneUntil = 0;
            _bloomReadyTick = -1;
            int heavy = b.Rank(OathId.HeavyHand);
            _heavyHandScale = heavy > 0 ? Fix64.Ratio(100 + HeavyHandPercentPerRank * heavy, 100) : Fix64.One;
            if (Entities.Count > PlayerId) ApplyOathStats();
        }

        /// <summary>«Второй взгляд» потрачен (RiftRun.TryFreeReroll).</summary>
        internal void SpendSecondLook() => _secondLookUsed = true;

        /// <summary>
        /// Конец тика: смена арены, события тика (рывок, смерти), корни, пыльца, бутон.
        /// Без клятв — сразу выход: ни обхода сущностей, ни чтения событий.
        /// </summary>
        void UpdateOaths()
        {
            if (_boons.IsEmpty || Entities.Count <= PlayerId) return;
            if (!HeroCarriesOathMark()) OnHeroReborn();
            ReadOathEvents();
            UpdateHeartRoots();
            UpdatePollen();
            UpdateBloom();
        }

        /// <summary>
        /// Состояние клятв в StateHash. Пусто без клятв — хеш прежний. Порядок — по спеке
        /// забега §4.4; «Второй взгляд» дописан последним (его там не было).
        /// </summary>
        void HashOaths(ref ulong h)
        {
            if (_boons.IsEmpty && !_lastBreathUsed && !_secondLookUsed && _oathImmuneUntil == 0) return;
            Hashing.Mix(ref h, 0x4F415448);
            _boons.HashInto(ref h);
            Hashing.Mix(ref h, _lastBreathUsed ? 1 : 0);
            Hashing.Mix(ref h, _oathImmuneUntil);
            Hashing.Mix(ref h, _bloomReadyTick);
            if (_pollenUntil != null)
                for (int s = 0; s < MaxPollenClouds; s++)
                {
                    if (_pollenUntil[s] <= Tick) continue;
                    Hashing.Mix(ref h, s);
                    Hashing.Mix(ref h, _pollenAt[s].X);
                    Hashing.Mix(ref h, _pollenAt[s].Y);
                    Hashing.Mix(ref h, _pollenUntil[s]);
                }
            Hashing.Mix(ref h, _pollenNext);
            if (_heartRootUntil != null)
                for (int i = 0; i < _heartRootUntil.Length; i++)
                    if (_heartRootUntil[i] != 0) { Hashing.Mix(ref h, i); Hashing.Mix(ref h, _heartRootUntil[i]); }
            if (_pollenSlowUntil != null)
                for (int i = 0; i < _pollenSlowUntil.Length; i++)
                    if (_pollenSlowUntil[i] != 0) { Hashing.Mix(ref h, i); Hashing.Mix(ref h, _pollenSlowUntil[i]); }
            Hashing.Mix(ref h, _secondLookUsed ? 1 : 0);
        }

        // ---- хуки общих файлов ----

        /// <summary>Секунда неуязвимости после «Последнего вдоха» (PlayerImmune).</summary>
        bool OathShields => Tick < _oathImmuneUntil;

        /// <summary>
        /// «Последний вдох»: смертельный удар по герою оставляет его с 30% здоровья, раз за
        /// забег. Зовётся после Обета Хранителя — тот спасает первым. Следующая секунда —
        /// неуязвимость: иначе второй удар того же тика добил бы поднявшегося.
        /// </summary>
        bool OathSaves(int target)
        {
            if (target != PlayerId || _lastBreathUsed || _boons.Rank(OathId.LastBreath) <= 0) return false;
            _lastBreathUsed = true;
            int health = System.Math.Max(1, Entities.MaxHealth[PlayerId] * LastBreathHealthPercent / 100);
            Entities.Health[PlayerId] = health;
            _oathImmuneUntil = Tick + LastBreathImmuneTicks;
            // Новых SimEventType нет (SimEvent.cs не трогается): подъём виден виду штатным лечением.
            _events.Add(SimEvent.Heal(PlayerId, PlayerId, health, Entities.Position[PlayerId]));
            return true;
        }

        /// <summary>
        /// «Стойкость»: −10% урона по герою, если источник — элита или босс (босс в маске
        /// элит). Опасности без источника (−1) и обычные враги не режутся (пробел №25).
        /// </summary>
        Fix64 OathIncomingDamageScale(int source)
            => source > PlayerId && _boons.Rank(OathId.Steadfast) > 0 && IsElite(source)
                ? Fix64.Ratio(SteadfastPercent, 100) : Fix64.One;

        /// <summary>«Тяжёлая рука»: ×1,08 за ступень на все удары ЛКМ героя (сабля и базовая серия).</summary>
        Fix64 OathSabreDamageScale => _heavyHandScale;

        /// <summary>
        /// «Быстрый кувырок»: −10% перезарядки рывка за ступень, вниз — 45 → 40 / 36 / 31 тик.
        /// Хук в Simulation.Tempo зовёт это только для рывка.
        /// </summary>
        int OathCooldownTicks(int ticks)
        {
            int rank = _boons.Rank(OathId.QuickRoll);
            return rank <= 0 ? ticks : System.Math.Max(1, ticks * (100 - QuickRollPercentPerRank * rank) / 100);
        }

        // ---- прибавки героя ----

        /// <summary>
        /// Шкура, шаг, глаз и запас — модификаторы листа героя. Метка (нулевая прибавка)
        /// стоит и без статовых клятв: по ней UpdateOaths узнаёт, что расстановка родила
        /// героя заново.
        ///
        /// Прирост максимума доходит и до текущего здоровья и лавидия. Это не лечение вещью
        /// посреди боя (EntityStore.RefreshStats нарочно так не делает): клятва — часть героя,
        /// а вешается заново только после рождения, которое срезало её вместе с запасом.
        /// Недостача, перенесённая между аренами, так и остаётся недостачей.
        /// </summary>
        private void ApplyOathStats()
        {
            StatSheet sheet = Entities.Stats[PlayerId];
            int maxHealth = Entities.MaxHealth[PlayerId], maxLavidium = Entities.MaxLavidium[PlayerId];
            sheet.RemoveSource(ModifierSource.Oath, OathModifierId);
            // Снимок сбросили в пустой: лист героя — как без клятв вовсе, без метки.
            if (_boons.IsEmpty) { Entities.RefreshStats(PlayerId); return; }
            int rank = _boons.Rank(OathId.ToughHide);
            sheet.Add(StatModifier.Flat(StatType.MaxHealth, Fix64.FromInt(ToughHideHealthPerRank * rank),
                ModifierSource.Oath, OathModifierId));
            if ((rank = _boons.Rank(OathId.LightStep)) > 0)
                sheet.Add(StatModifier.Increased(StatType.MoveSpeed, Fix64.Ratio(LightStepPercentPerRank * rank, 100),
                    ModifierSource.Oath, OathModifierId));
            if ((rank = _boons.Rank(OathId.KeenEye)) > 0)
                sheet.Add(StatModifier.Flat(StatType.CritChance, Fix64.Ratio(KeenEyePercentPerRank * rank, 100),
                    ModifierSource.Oath, OathModifierId));
            if ((rank = _boons.Rank(OathId.DeepReserve)) > 0)
                sheet.Add(StatModifier.Flat(StatType.MaxLavidium, Fix64.FromInt(DeepReserveLavidiumPerRank * rank),
                    ModifierSource.Oath, OathModifierId));
            Entities.RefreshStats(PlayerId);
            if (!Entities.Alive[PlayerId]) return;
            int grownHealth = Entities.MaxHealth[PlayerId] - maxHealth;
            if (grownHealth > 0)
                Entities.Health[PlayerId] = System.Math.Min(Entities.MaxHealth[PlayerId], Entities.Health[PlayerId] + grownHealth);
            int grownLavidium = Entities.MaxLavidium[PlayerId] - maxLavidium;
            if (grownLavidium > 0)
            {
                Fix64 cap = Fix64.FromInt(Entities.MaxLavidium[PlayerId]);
                Fix64 lavidium = Entities.Lavidium[PlayerId] + Fix64.FromInt(grownLavidium);
                Entities.Lavidium[PlayerId] = lavidium > cap ? cap : lavidium;
            }
        }

        /// <summary>Есть ли на листе героя метка клятв. Обход — десятки модификаторов, только при клятвах.</summary>
        private bool HeroCarriesOathMark()
        {
            StatSheet sheet = Entities.Stats[PlayerId];
            for (int i = 0; i < sheet.ModifierCount; i++)
            {
                StatModifier m = sheet.GetModifier(i);
                if (m.Source == ModifierSource.Oath && m.SourceId == OathModifierId) return true;
            }
            return false;
        }

        /// <summary>Расстановка родила героя и врагов заново: арена сбрасывается, прибавки — заново.</summary>
        private void OnHeroReborn()
        {
            ClearOathArenaState();
            ApplyOathStats();
        }

        /// <summary>
        /// Снимает корни и пыльцу с тел и гасит облака. После расстановки тела уже новые и
        /// модификаторов не несут — снятие с них ничего не делает и листы не пачкает.
        /// </summary>
        private void ClearOathArenaState()
        {
            if (_heartRootUntil != null)
            {
                for (int i = 0; i < _heartRootUntil.Length && i < Entities.Count; i++)
                    if (_heartRootUntil[i] != 0) Entities.Stats[i].RemoveSource(ModifierSource.Oath, HeartRootModifierId);
                System.Array.Clear(_heartRootUntil, 0, _heartRootUntil.Length);
            }
            if (_pollenSlowUntil != null)
            {
                for (int i = 0; i < _pollenSlowUntil.Length && i < Entities.Count; i++)
                    if (_pollenSlowUntil[i] != 0) Entities.Stats[i].RemoveSource(ModifierSource.Oath, HeartPollenModifierId);
                System.Array.Clear(_pollenSlowUntil, 0, _pollenSlowUntil.Length);
            }
            if (_pollenUntil != null) System.Array.Clear(_pollenUntil, 0, MaxPollenClouds);
            _rootedCount = _pollenSlowedCount = _pollenNext = 0;
        }

        // ---- события тика ----

        /// <summary>
        /// Хука на убийство и начало рывка в общих файлах нет: события этого тика уже в
        /// _events (они чистятся в начале шага), а UpdateOaths — последняя стадия шага.
        /// Начало рывка — DashStarted (Position — откуда), смерть — Death (Source — убийца).
        /// </summary>
        private void ReadOathEvents()
        {
            bool roots = _boons.Has(HeartFacet.ThicketRoots), pollen = _boons.Has(HeartFacet.ThicketPollen);
            bool blood = _boons.Rank(OathId.EnemyBlood) > 0;
            if (!roots && !pollen && !blood) return;
            for (int i = 0; i < _events.Count; i++)
            {
                SimEvent e = _events[i];
                if (e.Type == SimEventType.DashStarted)
                {
                    if (roots && e.Source == PlayerId) RootAround(e.Position);
                    continue;
                }
                if (e.Type != SimEventType.Death) continue;
                int target = e.Target;
                if (target <= PlayerId || target >= Entities.Count || Entities.Side[target] == Entities.Side[PlayerId]) continue;
                if (pollen) LayPollen(e.Position);
                // «Кровь врага»: элиту (и босса — он в маске элит) добил сам герой.
                if (blood && e.Source == PlayerId && IsElite(target)) HealHero(EnemyBloodHealPercent);
            }
        }

        /// <summary>Лечит раненого живого героя на percent% максимума (не меньше 1). false — лечить нечего.</summary>
        private bool HealHero(int percent)
        {
            if (!Entities.Alive[PlayerId]) return false;
            int max = Entities.MaxHealth[PlayerId], missing = max - Entities.Health[PlayerId];
            if (missing <= 0) return false;
            int amount = System.Math.Max(1, max * percent / 100);
            if (amount > missing) amount = missing;
            Entities.Health[PlayerId] += amount;
            _events.Add(SimEvent.Heal(PlayerId, PlayerId, amount, Entities.Position[PlayerId]));
            return true;
        }

        // ---- «Корни» ----

        /// <summary>
        /// Начало рывка связывает врагов в 3 м (плюс тело) от точки старта: ходьба 0 на 15
        /// тиков, удары и особые броски не прерываются (это лист, а не оглушение). Босс не
        /// связывается. Повторный рывок продлевает срок, второй модификатор не вешает.
        /// </summary>
        private void RootAround(FixVec2 from)
        {
            EnsureOathBuffers();
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                if (i == _encounterBoss || Entities.Kind[i] == EnemyKind.ForestThicketMaster) continue;
                Fix64 reach = HeartRootRadius + Entities.BodyRadius[i];
                if (FixVec2.DistanceSq(Entities.Position[i], from) > reach * reach) continue;
                if (_heartRootUntil[i] == 0)
                {
                    Entities.Stats[i].Add(StatModifier.More(StatType.MoveSpeed, -Fix64.One,
                        ModifierSource.Oath, HeartRootModifierId));
                    _rootedCount++;
                }
                _heartRootUntil[i] = Tick + HeartRootTicks;
            }
        }

        /// <summary>
        /// Снимает истёкшие корни. Лист пересчитается в начале следующего тика
        /// (RefreshDirtyStats): повешенные в тике T корни держат тики T+1…T+15.
        /// </summary>
        private void UpdateHeartRoots()
        {
            if (_rootedCount == 0) return;
            for (int i = PlayerId + 1; i < _heartRootUntil.Length && i < Entities.Count; i++)
            {
                if (_heartRootUntil[i] == 0 || (_heartRootUntil[i] > Tick && Entities.Alive[i])) continue;
                Entities.Stats[i].RemoveSource(ModifierSource.Oath, HeartRootModifierId);
                _heartRootUntil[i] = 0;
                _rootedCount--;
            }
        }

        // ---- «Пыльца» ----

        /// <summary>Облако на месте смерти. Кольцо из восьми: девятое вытесняет самое старое.</summary>
        private void LayPollen(FixVec2 at)
        {
            EnsureOathBuffers();
            int s = _pollenNext;
            _pollenNext = (s + 1) % MaxPollenClouds;
            _pollenAt[s] = at;
            _pollenUntil[s] = Tick + PollenTicks;
        }

        /// <summary>
        /// Враг в облаке (2 м плюс тело) замедлен на 30%, и ещё 15 тиков после выхода — как
        /// пена Шквала. Пыльца держит и элит, и босса (пробел №28). Без живых облаков и
        /// замедленных — ни одного обхода.
        /// </summary>
        private void UpdatePollen()
        {
            if (_pollenUntil == null) return;
            bool live = false;
            for (int s = 0; s < MaxPollenClouds; s++) live |= _pollenUntil[s] > Tick;
            if (!live && _pollenSlowedCount == 0) return;
            for (int i = PlayerId + 1; i < _pollenSlowUntil.Length && i < Entities.Count; i++)
            {
                bool inside = false;
                if (live && Entities.Alive[i] && Entities.Side[i] != Entities.Side[PlayerId])
                {
                    Fix64 reach = PollenRadius + Entities.BodyRadius[i];
                    Fix64 reachSq = reach * reach;
                    for (int s = 0; s < MaxPollenClouds && !inside; s++)
                        inside = _pollenUntil[s] > Tick && FixVec2.DistanceSq(Entities.Position[i], _pollenAt[s]) <= reachSq;
                }
                if (inside)
                {
                    if (_pollenSlowUntil[i] == 0)
                    {
                        Entities.Stats[i].Add(StatModifier.Increased(StatType.MoveSpeed, Fix64.Ratio(-PollenSlowPercent, 100),
                            ModifierSource.Oath, HeartPollenModifierId));
                        _pollenSlowedCount++;
                    }
                    _pollenSlowUntil[i] = Tick + PollenLingerTicks;
                }
                else if (_pollenSlowUntil[i] != 0 && (_pollenSlowUntil[i] <= Tick || !Entities.Alive[i]))
                {
                    Entities.Stats[i].RemoveSource(ModifierSource.Oath, HeartPollenModifierId);
                    _pollenSlowUntil[i] = 0;
                    _pollenSlowedCount--;
                }
            }
        }

        // ---- «Цветение» ----

        /// <summary>
        /// Бутон созревает за 20 с боя (симуляция забега шагает только в бою и по пути к
        /// выходу) и лечит 5%, если герой ранен. При полном здоровье зрелый бутон ждёт раны.
        /// </summary>
        private void UpdateBloom()
        {
            if (!_boons.Has(HeartFacet.ThicketBloom)) return;
            if (_bloomReadyTick < 0) { _bloomReadyTick = Tick + BloomPeriodTicks; return; }
            if (Tick < _bloomReadyTick) return;
            if (HealHero(BloomHealPercent)) _bloomReadyTick = Tick + BloomPeriodTicks;
        }

        private void EnsureOathBuffers()
        {
            if (_heartRootUntil != null) return;
            _heartRootUntil = new int[Entities.Capacity];
            _pollenSlowUntil = new int[Entities.Capacity];
            _pollenAt = new FixVec2[MaxPollenClouds];
            _pollenUntil = new int[MaxPollenClouds];
        }
    }
}
