using System;

namespace Game.Sim
{
    /// <summary>
    /// ТАКТ УДАРОВ ПО ГЕРОЮ (поток D, правки ИИ по замерам «ощущения», 29.09).
    ///
    /// Стенд (ArenaFeelProbe, 300 забегов бота) насчитал: 18–22% попаданий
    /// мобов ложатся на героя внахлёст — меньше чем через 0,2 с после чужого,
    /// и два удара читаются как один; следующий замах начинается в тот же
    /// тик, когда прошлый удар лёг, — ответить некогда; связанного корнями
    /// добивают Хранители (4,8% урона тяжёлых пачек за 1% времени боя).
    /// Владелец хочет бой уровня Hades 2: «не беготня, но и не избиение
    /// мешков». Отсюда три правила — для любого вида, который бьёт героя:
    ///
    /// 1. ПО ОДНОМУ. Атака начинается, только если её контакт ляжет не ближе
    ///    HeroContactSpacingTicks к контакту чужой атаки, которая уже идёт:
    ///    замаха, укуса, первого плода залпа, тарана, клыков, прыжка, воя и
    ///    круга Вендиго, линии шипов, удара корнями, переката. Не уложилась —
    ///    моб ждёт тик-другой и пробует снова.
    /// 2. ОКНО ОТВЕТА. После контакта ближнего удара по герою (попал или
    ///    увернулся) HeroBreatherTicks ни один ближник не начинает замах и
    ///    укус: ударивший стоит в позе отхода, соседи ждут, герой отвечает.
    ///    В лёгкой пачке окно длиннее (EasyHeroBreatherTicks) — это и есть
    ///    ритм по уровню пачки. Крупных атак издали окно не касается.
    /// 3. СВЯЗАННОГО НЕ БЬЮТ. Пока герой в корнях или оглушён — и пока под
    ///    ним лежит круг корней или он стоит на полосе тарана, которые его
    ///    свяжут или оглушат, — не начинается атака, чей контакт ляжет раньше
    ///    чем через HeroControlGraceTicks после конца контроля. Удар ложится
    ///    после освобождения — от него можно уйти. Залп Плюй-плода целит
    ///    каждый плод заново, поэтому плод, который упал бы на связанного,
    ///    не летит: залп прерывается (HeroControlledAt).
    ///
    /// Уже начатые атаки правила не трогают: метка на земле не врёт.
    ///
    /// Ближний замах спрашивает MeleeRhythmAllows (UpdateEnemySwing), крупные
    /// атаки — через BigMarkAllowed (жетон крупной атаки и бюджет меток),
    /// куда их уже приводят свои файлы. Клыки Камнекопыта спрашивают
    /// HeroContactAllowed (правила 1 и 3) в TryStartStonehoofTusk. Коготь
    /// Вендиго правил сам не спрашивает (Вендиго не ходит с корнями и тараном),
    /// но его контакт в расписании: остальные под него подстраиваются.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Контакты разных мобов по герою — не ближе этого, тиков (0,27 с).</summary>
        public const int HeroContactSpacingTicks = 8;

        /// <summary>
        /// После контакта ближнего удара по герою ближники молчат столько тиков (0,33 с).
        /// Короче в тяжёлой пачке и у элиты (5 тиков) пробовали: атак в секунду +5%,
        /// а смертей на 300 забегов +17 — давление растёт от свалки, а не от темпа.
        /// </summary>
        public const int HeroBreatherTicks = 10;

        /// <summary>
        /// В лёгкой пачке (шаблон встречи уровня Easy) окно ответа длиннее — 0,8 с.
        /// Стенд ощущения насчитал в лёгкой больше атак в секунду, чем в тяжёлой
        /// (1,41 против 0,97, почти все — укусы роя): разминка била чаще финала.
        /// С этим окном лёгкая — 0,97 атаки/с (полоса 0,5–1,0); 18 тиков давали
        /// 1,06. Жетон укуса 2 вместо 3 пробовали снова — атак меньше на 1%.
        /// </summary>
        public const int EasyHeroBreatherTicks = 24;

        /// <summary>Окно ответа этой арены: в лёгкой пачке — EasyHeroBreatherTicks.</summary>
        public int HeroBreatherTicksNow
            => _encounter != null && _encounter.Tier == EncounterTier.Easy ? EasyHeroBreatherTicks : HeroBreatherTicks;

        /// <summary>Контакт — не раньше чем через столько тиков после конца корней или оглушения.</summary>
        public const int HeroControlGraceTicks = 6;

        private int _heroBreatherUntil;

        /// <summary>
        /// Такт включён — так играется всегда. Выключают только тесты, которые
        /// проверяют жетоны сами по себе (несколько замахов в один тик): такт
        /// разнёс бы их по тикам. В хеше — меткой, как PlayerInvulnerable.
        /// </summary>
        internal bool AttackRhythmEnabled { get; set; } = true;

        // Расписание контактов по герою на один тик: чей, с какого по какой
        // тик. Строится из состояния атак при первом вопросе в тике и заново,
        // если с тех пор началась новая атака (сумма номеров атак и меток
        // другая). Не состояние: из него ничего не переходит в следующий тик,
        // поэтому в хеш не идёт. Один обход мобов на тик вместо обхода на
        // каждый вопрос — вопросов на 48 мобах до сотни за тик.
        private int[] _contactOwner = new int[16], _contactFrom = new int[16], _contactTo = new int[16];
        private bool[] _contactMelee = new bool[16];
        private int _contactCount;

        // Окна будущего контроля, [from, to): круг Корнехвата уже лежит под
        // героем или герой стоит на полосе тарана Камнекопыта.
        private int[] _controlFrom = new int[4], _controlTo = new int[4];
        private int _controlWindowCount;
        private int _contactTick = int.MinValue, _contactStamp;

        /// <summary>Сколько тиков ещё длится окно ответа после ближнего удара по герою; 0 — окна нет.</summary>
        public int HeroBreatherTicksLeft => _heroBreatherUntil > Tick ? _heroBreatherUntil - Tick : 0;

        /// <summary>
        /// Может ли ближник self начать замах (укус) с windup тиков до контакта:
        /// окно ответа кончилось и контакт укладывается в такт.
        /// </summary>
        private bool MeleeRhythmAllows(int self, int windup)
        {
            if (!AttackRhythmEnabled) return true;
            if (Tick < _heroBreatherUntil) return false;
            // Чужой ближний удар ложится в этот же тик — окно ответа уже открыто,
            // кто бы из двоих ни шёл по очереди первым.
            RefreshHeroContacts();
            for (int k = 0; k < _contactCount; k++)
                if (_contactMelee[k] && _contactFrom[k] <= Tick && _contactOwner[k] != self) return false;
            int contact = Tick + windup;
            return HeroContactAllowed(self, contact, contact);
        }

        /// <summary>
        /// Может ли моб self начать атаку на героя с контактами в [first, last]:
        /// ни один не ляжет под контроль (корни, оглушение, круг корней под
        /// героем, полоса тарана) и ближе HeroContactSpacingTicks к контакту
        /// чужой атаки.
        /// </summary>
        internal bool HeroContactAllowed(int self, int first, int last)
        {
            if (!AttackRhythmEnabled || !Entities.Alive[PlayerId]) return true;
            if (HeroControlledAt(first)) return false;
            RefreshHeroContacts();
            for (int k = 0; k < _controlWindowCount; k++)
                if (first < _controlTo[k] && last >= _controlFrom[k]) return false;
            for (int k = 0; k < _contactCount; k++)
            {
                if (_contactOwner[k] == self) continue;
                if (first < _contactTo[k] + HeroContactSpacingTicks && _contactFrom[k] < last + HeroContactSpacingTicks)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Ляжет ли контакт в тик contact на связанного или оглушённого героя —
        /// раньше чем через HeroControlGraceTicks после конца контроля, — или в
        /// контроль, который вот-вот будет: круг корней под героем, полоса
        /// тарана. Для атак из нескольких контактов, которые уже идут: каждый
        /// плод залпа Плюй-плода целит заново, и плод, который упал бы на
        /// связанного, не летит (UpdateForestBud прерывает залп).
        /// </summary>
        internal bool HeroControlledAt(int contact)
        {
            if (!AttackRhythmEnabled || !Entities.Alive[PlayerId]) return false;
            int controlled = Math.Max(HeroRootTicksLeft, HeroStunTicksLeft);
            if (controlled > 0 && contact < Tick + controlled + HeroControlGraceTicks) return true;
            RefreshHeroContacts();
            for (int k = 0; k < _controlWindowCount; k++)
                if (contact >= _controlFrom[k] && contact < _controlTo[k]) return true;
            return false;
        }

        /// <summary>Контакт ближнего удара по герою: открывается окно ответа.</summary>
        private void NoteMeleeContactOnHero() => _heroBreatherUntil = Tick + HeroBreatherTicksNow;

        private void RefreshHeroContacts()
        {
            // Номера всех атак, что попадают в расписание: удар корнями и перекат
            // начинаются без метки на земле, таран и линия шипов — с меткой, но
            // считаем и их номера, чтобы кэш не зависел от того, кто кладёт метку.
            int stamp = unchecked(_enemySwingSerial + _telegraphSerial + _wendigoSerial + _stonehoofTuskSerial + _forestSerial
                + _stonehoofSerial + _rootSnarerSerial + _splitterRollSerial + _thorncasterSerial);
            if (_contactTick == Tick && _contactStamp == stamp) return;
            _contactTick = Tick;
            _contactStamp = stamp;
            _contactCount = 0;
            _controlWindowCount = 0;
            FixVec2 hero = Entities.Position[PlayerId];
            Fix64 body = Entities.BodyRadius[PlayerId];
            for (int id = 1; id < Entities.Count; id++)
            {
                EnemyKind kind = Entities.Kind[id];
                if (!Entities.Alive[id])
                {
                    // Плоды мёртвого стрелка ещё падают — их контакты тоже в такте.
                    if (kind != EnemyKind.ForestBud) continue;
                }
                else
                {
                    var swing = _enemySwings[id];
                    if (swing.Serial != 0 && !swing.HitResolved && swing.Target == PlayerId && swing.ImpactTick >= Tick)
                        AddHeroContact(id, swing.ImpactTick, swing.ImpactTick, melee: true);
                }
                switch (kind)
                {
                    case EnemyKind.ForestBud:
                    {
                        // Залп — по первому плоду: он летит туда, где герой стоит, и
                        // бьёт стоящего. Следующие целят заново и ложатся за спину
                        // идущему; держать под весь залп (0,8 с) ближний такт — стенд
                        // показал: пауза без атак на А3 выходит из полосы 2,5 с.
                        if (BigMarkWeightOf(id, out _, out int first) > 0 && first >= Tick) AddHeroContact(id, first, first);
                        break;
                    }
                    case EnemyKind.ForestWendigo:
                    {
                        // Коготь, прыжок, вой и круг: у каждого один контакт.
                        var a = _wendigoActions[id];
                        if (a.Serial != 0 && !a.HitResolved && a.ImpactTick >= Tick) AddHeroContact(id, a.ImpactTick, a.ImpactTick);
                        break;
                    }
                    case EnemyKind.ForestStonehoof:
                    {
                        // Таран после разгона бьёт «сейчас»: такт под него уже не подстроить.
                        if (BigMarkWeightOf(id, out _, out int launch) > 0)
                        {
                            AddHeroContact(id, Math.Max(launch, Tick), Math.Max(launch, Tick));
                            ChargeStunWindow(id, hero, body);
                        }
                        if (TryGetStonehoofTusk(id, out StonehoofTuskState tusk) && !tusk.HitResolved && tusk.ImpactTick >= Tick)
                            AddHeroContact(id, tusk.ImpactTick, tusk.ImpactTick);
                        break;
                    }
                    case EnemyKind.ForestRootSnarer:
                    {
                        if (!RootSnarerHoldsBigToken(id)) break;
                        var a = _rootSnarers[id];
                        AddHeroContact(id, a.ImpactTick, a.ImpactTick);
                        // Круг уже на земле и герой в нём: не выйдет — будет связан
                        // (если иммунитет к контролю не отобьёт корни).
                        if (a.SlamTick > Tick || _heroControlImmuneUntil > a.ImpactTick) break;
                        int slot = FindTelegraph(a.TelegraphSerial);
                        if (slot >= 0 && TryGetTelegraph(slot, out EnemyTelegraph circle) && circle.IsActive
                            && TelegraphContains(in circle, hero, body))
                            AddControlWindow(a.ImpactTick, a.ImpactTick + RootSnarerRootTicks + HeroControlGraceTicks);
                        break;
                    }
                    case EnemyKind.ForestThorncaster:
                    case EnemyKind.ForestSplitter:
                    {
                        // Линия и всплеск шипов, перекат: контакт — первый.
                        if (BigMarkWeightOf(id, out _, out int impact) > 0 && impact >= Tick) AddHeroContact(id, impact, impact);
                        break;
                    }
                    case EnemyKind.ForestThicketMaster:
                        AddThicketMasterContacts(id);
                        break;
                }
            }
        }

        private void AddHeroContact(int owner, int from, int to, bool melee = false)
        {
            if (_contactCount == _contactOwner.Length)
            {
                Array.Resize(ref _contactOwner, _contactCount * 2);
                Array.Resize(ref _contactFrom, _contactCount * 2);
                Array.Resize(ref _contactTo, _contactCount * 2);
                Array.Resize(ref _contactMelee, _contactCount * 2);
            }
            _contactOwner[_contactCount] = owner;
            _contactFrom[_contactCount] = from;
            _contactTo[_contactCount] = to;
            _contactMelee[_contactCount] = melee;
            _contactCount++;
        }

        /// <summary>
        /// Таран кабана id ещё не попал, а герой стоит на его полосе: удар
        /// оглушит на StonehoofChargeStunTicks — чужие контакты в это окно не
        /// ложатся (стенд: худшие секунды А6–А8 — таран, оглушение и следом
        /// уже взведённый Хранитель с плодами по стоящему). Тик удара — когда
        /// разгон по полосе (StonehoofTravel) дойдёт до тела героя. Ушёл с
        /// полосы — окна нет, соседи бьют как обычно.
        /// </summary>
        private void ChargeStunWindow(int id, FixVec2 hero, Fix64 body)
        {
            var a = _stonehoofActions[id];
            if (a.HitResolved || a.Phase == StonehoofPhase.WallImpact) return;
            var lane = EnemyTelegraph.Lane(a.Origin, a.Direction, a.Distance, StonehoofRadius * 2);
            if (!TelegraphContains(in lane, hero, body)) return;
            Fix64 along = FixVec2.Dot(hero - a.Origin, a.Direction) - (StonehoofRadius + body);
            int hit = Math.Max(Tick, a.LaunchTick);
            while (hit < a.StopTick && StonehoofTravel(hit - a.LaunchTick) < along) hit++;
            // Иммунитет к контролю отобьёт оглушение — держать соседей незачем.
            if (_heroControlImmuneUntil > hit) return;
            AddControlWindow(hit, hit + StonehoofChargeStunTicks + HeroControlGraceTicks);
        }

        private void AddControlWindow(int from, int to)
        {
            if (_controlWindowCount == _controlFrom.Length)
            {
                Array.Resize(ref _controlFrom, _controlWindowCount * 2);
                Array.Resize(ref _controlTo, _controlWindowCount * 2);
            }
            _controlFrom[_controlWindowCount] = from;
            _controlTo[_controlWindowCount] = to;
            _controlWindowCount++;
        }

        private void ResetAttackRhythm()
        {
            _heroBreatherUntil = 0;
            _contactTick = int.MinValue;
            _contactCount = _controlWindowCount = 0;
        }

        private void HashAttackRhythm(ref ulong hash)
        {
            if (!AttackRhythmEnabled) Hashing.Mix(ref hash, 0x4E4F5441);   // "NOTA"
            if (_heroBreatherUntil == 0) return;
            Hashing.Mix(ref hash, 0x5441544B);   // "TATK"
            Hashing.Mix(ref hash, _heroBreatherUntil);
        }
    }
}
