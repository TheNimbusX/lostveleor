namespace Game.Sim
{
    /// <summary>
    /// ЗАМЕДЛЕНИЕ И КОНТРОЛЬ ГЕРОЯ — ОДНИ НА ВСЕХ МОБОВ.
    ///
    /// Замедление. Раньше героя замедлял только вой Вендиго, и модификатор жил
    /// у него. Теперь замедлять могут разные мобы, а два своих модификатора
    /// перемножались бы в замедление, которого нет ни у одного моба. Здесь
    /// модификатор один: сильнейшее замедление побеждает, срок продлевается
    /// до самого позднего конца. Слабое поверх сильного не ослабляет его.
    ///
    /// Своего статуса «замедлен» в StatusStore нет — это модификатор More на
    /// скорость бега, как у зелий и Сердца Зимы. Лист героя пересчитывается
    /// первой стадией тика, поэтому замедление, повешенное в тик T, действует
    /// ровно на шаги T+1 … T+ticks.
    ///
    /// КОНТРОЛЬ (решение владельца 29.09) — два настоящих состояния, не
    /// замедление:
    /// * корни (Корнехват) — не ходит и не кувыркается: шаг героя ноль
    ///   (свой модификатор More −100%), а уходы (кувырок, Выпад, Отскок —
    ///   IsEvade) не разрешает CastAvailable. Бьёт, кастует и
    ///   разворачивается как обычно; чужой отброс его двигает, а уход,
    ///   начатый до корней, доезжает;
    /// * оглушение (разбег Камнекопыта) — ни шага, ни удара, ни каста, ни
    ///   кувырка: общее оглушение StatusStore, которое уже умеют
    ///   PrepareCombatInput и MovePlayer. Отброс, пришедший вместе с ним,
    ///   доезжает до конца.
    /// Тайминг как у замедления: повешенный в тик T держит шаги T+1 … T+ticks
    /// (вызов между шагами — ещё и ближайший шаг).
    ///
    /// ИММУНИТЕТ, ЧТОБЫ НЕ БЫЛО ЦЕПОЧКИ: пока герой под контролем и ещё
    /// HeroControlImmunityTicks после его конца новый контроль не ложится
    /// вовсе — ни корни, ни оглушение, ни продление. Отбитый контроль события
    /// HeroControl не даёт; на замедление иммунитет не действует.
    ///
    /// Вешать и то, и другое — только если удар действительно достал:
    /// уклонение, неуязвимость и отложенный урон не дают ни замедления, ни
    /// контроля (правило воя). Это проверяет тот, кто вешает.
    ///
    /// Состояние входит в хеш (HashHeroSlow) и сбрасывается расстановкой.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Замедление 100% — корни: ApplyHeroSlow с этим процентом вешает
        /// корни (ApplyHeroRoot), а не замедление.
        /// </summary>
        public const int HeroRootPercent = 100;

        /// <summary>
        /// Иммунитет к контролю после конца корней или оглушения: 1,5 с.
        /// Отброс, корни, оглушение и замедление иначе складываются в цепочку.
        /// </summary>
        public const int HeroControlImmunityTicks = 45;

        private const int HeroSlowId = 0x534C4F57; // "SLOW"
        private const int HeroRootId = 0x524F4F54; // "ROOT"

        // Первый тик, в который герой снова свободен. Ноль — не замедлен.
        private int _heroSlowUntil;
        private int _heroSlowPercent;

        // Первый тик без корней; ноль — корней нет.
        private int _heroRootUntil;

        // Первый тик, с которого на героя снова ложится контроль: конец
        // последнего контроля + HeroControlImmunityTicks. Ноль — ложится.
        private int _heroControlImmuneUntil;

        /// <summary>Сколько ещё шагов герой пройдёт замедленным. Для HUD, вида и тестов.</summary>
        public int HeroSlowTicksLeft => _heroSlowUntil > Tick ? _heroSlowUntil - Tick : 0;

        /// <summary>Действующее замедление, %: 0 — нет. Корни сюда не входят (HeroRooted).</summary>
        public int HeroSlowPercent => HeroSlowTicksLeft > 0 ? _heroSlowPercent : 0;

        /// <summary>Сколько ещё шагов герой в корнях.</summary>
        public int HeroRootTicksLeft => _heroRootUntil > Tick ? _heroRootUntil - Tick : 0;

        /// <summary>Герой в корнях: не ходит и не кувыркается, но бьёт и кастует.</summary>
        public bool HeroRooted => HeroRootTicksLeft > 0;

        /// <summary>Сколько ещё шагов герой оглушён.</summary>
        public int HeroStunTicksLeft
        {
            get
            {
                int until = Statuses.StunUntilTick[PlayerId];
                return until > Tick ? until - Tick : 0;
            }
        }

        /// <summary>Герой оглушён: ни шага, ни удара, ни каста, ни кувырка.</summary>
        public bool HeroStunned => HeroStunTicksLeft > 0;

        /// <summary>
        /// Через сколько шагов на героя снова ляжет контроль: остаток
        /// действующего контроля плюс иммунитет. Ноль — ляжет сейчас.
        /// </summary>
        public int HeroControlImmuneTicksLeft => _heroControlImmuneUntil > Tick ? _heroControlImmuneUntil - Tick : 0;

        /// <summary>
        /// Замедляет героя на percent процентов (1–99) на ticks его шагов,
        /// начиная со следующего тика. Уже идущее замедление не складывается
        /// с новым: остаётся сильнейший процент, а срок — самый поздний из
        /// двух. Мёртвого героя не замедляет.
        ///
        /// 100% и больше — корни на ticks шагов (ApplyHeroRoot, source — кто
        /// наложил): отдельное состояние со своим иммунитетом, с замедлением
        /// не сливается.
        ///
        /// Вешать — только если удар действительно достал: уклонение,
        /// неуязвимость и отложенный урон замедления не дают (правило воя).
        /// </summary>
        public void ApplyHeroSlow(int percent, int ticks, int source = -1)
        {
            if (percent >= HeroRootPercent) { ApplyHeroRoot(ticks, source); return; }
            if (percent <= 0 || ticks <= 0 || Entities.Count <= PlayerId || !Entities.Alive[PlayerId]) return;
            int until = Tick + 1 + ticks;
            if (_heroSlowUntil > Tick)
            {
                if (percent < _heroSlowPercent) percent = _heroSlowPercent;
                if (until < _heroSlowUntil) until = _heroSlowUntil;
            }
            var sheet = Entities.Stats[PlayerId];
            sheet.RemoveSource(ModifierSource.Buff, HeroSlowId);
            sheet.Add(StatModifier.More(StatType.MoveSpeed, Fix64.Ratio(-percent, 100),
                ModifierSource.Buff, HeroSlowId));
            _heroSlowPercent = percent;
            _heroSlowUntil = until;
        }

        /// <summary>
        /// Корни на ticks шагов героя, начиная со следующего тика: не ходит и
        /// не кувыркается, но бьёт и кастует. source — кто наложил (−1 — никто).
        /// false — не легли: герой мёртв, уже под контролем или в иммунитете.
        /// Легли — событие HeroControl (Flag = true).
        /// </summary>
        public bool ApplyHeroRoot(int ticks, int source = -1) => ApplyHeroControl(ticks, true, source);

        /// <summary>
        /// Оглушение на ticks шагов героя, начиная со следующего тика: ни шага,
        /// ни удара, ни каста, ни кувырка; начатое действие снимается, отброс
        /// доезжает. source — кто наложил (−1 — никто). false — не легло: герой
        /// мёртв, уже под контролем или в иммунитете. Легло — событие
        /// HeroControl (Flag = false).
        /// </summary>
        public bool ApplyHeroStun(int ticks, int source = -1) => ApplyHeroControl(ticks, false, source);

        /// <summary>
        /// Общий вход корней и оглушения. Новый контроль не продлевает и не
        /// усиливает действующий: пока тот идёт и ещё
        /// HeroControlImmunityTicks после — отбивается целиком.
        /// </summary>
        private bool ApplyHeroControl(int ticks, bool rooted, int source)
        {
            if (ticks <= 0 || Entities.Count <= PlayerId || !Entities.Alive[PlayerId]) return false;
            if (Tick < _heroControlImmuneUntil) return false;
            int until = Tick + 1 + ticks;
            if (rooted)
            {
                // Модификатор свой, не замедления: корни не трогают ни процент,
                // ни срок идущего замедления, и оно доживает своё после них.
                var sheet = Entities.Stats[PlayerId];
                sheet.RemoveSource(ModifierSource.Buff, HeroRootId);
                sheet.Add(StatModifier.More(StatType.MoveSpeed, -Fix64.One, ModifierSource.Buff, HeroRootId));
                _heroRootUntil = until;
            }
            else Statuses.ApplyStun(PlayerId, until);
            _heroControlImmuneUntil = until + HeroControlImmunityTicks;
            _events.Add(SimEvent.HeroControl(source, PlayerId, ticks, rooted, Entities.Position[PlayerId]));
            return true;
        }

        /// <summary>
        /// Первой стадией тика, до пересчёта листов: снятый здесь модификатор
        /// этот же тик уже не замедляет. Замедление и корни переживают и
        /// смерть того, кто их повесил.
        /// </summary>
        private void ExpireHeroSlow()
        {
            ExpireHeroControl();
            if (_heroSlowUntil == 0 || Tick < _heroSlowUntil) return;
            _heroSlowUntil = 0;
            _heroSlowPercent = 0;
            if (Entities.Count > PlayerId) Entities.Stats[PlayerId].RemoveSource(ModifierSource.Buff, HeroSlowId);
        }

        private void ExpireHeroControl()
        {
            if (_heroRootUntil != 0 && Tick >= _heroRootUntil)
            {
                _heroRootUntil = 0;
                if (Entities.Count > PlayerId) Entities.Stats[PlayerId].RemoveSource(ModifierSource.Buff, HeroRootId);
            }
            if (_heroControlImmuneUntil != 0 && Tick >= _heroControlImmuneUntil) _heroControlImmuneUntil = 0;
        }

        /// <summary>
        /// При расстановке модификаторы снимать не нужно: сброс идёт после
        /// Entities.Clear, и Spawn героя очищает его лист целиком. Но стенд
        /// мобов («Сброс») зовёт сброс посреди боя с живым героем — тогда
        /// замедление, корни и оглушение героя снимаются руками, иначе
        /// модификатор без срока держал бы героя до конца боя.
        /// </summary>
        private void ResetHeroSlow()
        {
            if (Entities.Count > PlayerId)
            {
                var sheet = Entities.Stats[PlayerId];
                sheet.RemoveSource(ModifierSource.Buff, HeroSlowId);
                sheet.RemoveSource(ModifierSource.Buff, HeroRootId);
                // Героя оглушает только контроль отсюда.
                Statuses.StunUntilTick[PlayerId] = 0;
            }
            _heroSlowUntil = 0;
            _heroSlowPercent = 0;
            _heroRootUntil = 0;
            _heroControlImmuneUntil = 0;
        }

        private void HashHeroSlow(ref ulong hash)
        {
            if (_heroSlowUntil != 0)
            {
                Hashing.Mix(ref hash, 0x534C4F57);
                Hashing.Mix(ref hash, _heroSlowUntil);
                Hashing.Mix(ref hash, _heroSlowPercent);
            }
            // Оглушение героя хешируется в StatusStore.
            if (_heroRootUntil == 0 && _heroControlImmuneUntil == 0) return;
            Hashing.Mix(ref hash, 0x4354524C); // "CTRL"
            Hashing.Mix(ref hash, _heroRootUntil);
            Hashing.Mix(ref hash, _heroControlImmuneUntil);
        }
    }
}
