namespace Game.Sim
{
    /// <summary>
    /// ЗАМЕДЛЕНИЕ ГЕРОЯ — ОДНО НА ВСЕХ МОБОВ.
    ///
    /// Раньше героя замедлял только вой Вендиго, и модификатор жил у него.
    /// Теперь держат ещё корни Корнехвата, а два своих модификатора
    /// перемножались бы в замедление, которого нет ни у одного моба. Здесь
    /// модификатор один: сильнейшее замедление побеждает, срок продлевается
    /// до самого позднего конца. Слабое поверх сильного не ослабляет его.
    ///
    /// Своего статуса «замедлен» в StatusStore нет — это модификатор More на
    /// скорость бега, как у зелий и Сердца Зимы. Лист героя пересчитывается
    /// первой стадией тика, поэтому замедление, повешенное в тик T, действует
    /// ровно на шаги T+1 … T+ticks. 100% — корни: шаг героя ноль, а кувырок и
    /// прочее принудительное движение идут мимо скорости бега (ForcedMotion)
    /// и работают и в корнях.
    ///
    /// Состояние входит в хеш (HashHeroSlow) и сбрасывается расстановкой.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Замедление 100% — корни: герой стоит, но кувыркается и бьёт.</summary>
        public const int HeroRootPercent = 100;

        private const int HeroSlowId = 0x534C4F57; // "SLOW"

        // Первый тик, в который герой снова свободен. Ноль — не замедлен.
        private int _heroSlowUntil;
        private int _heroSlowPercent;

        /// <summary>Сколько ещё шагов герой пройдёт замедленным. Для HUD, вида и тестов.</summary>
        public int HeroSlowTicksLeft => _heroSlowUntil > Tick ? _heroSlowUntil - Tick : 0;

        /// <summary>Действующее замедление, %: 0 — нет, 100 — корни.</summary>
        public int HeroSlowPercent => HeroSlowTicksLeft > 0 ? _heroSlowPercent : 0;

        /// <summary>Герой в корнях: бежать не может.</summary>
        public bool HeroRooted => HeroSlowPercent >= HeroRootPercent;

        /// <summary>
        /// Замедляет героя на percent процентов (1–100, 100 — корни) на ticks
        /// его шагов, начиная со следующего тика. Уже идущее замедление не
        /// складывается с новым: остаётся сильнейший процент, а срок — самый
        /// поздний из двух. Мёртвого героя не замедляет.
        ///
        /// Вешать — только если удар действительно достал: уклонение,
        /// неуязвимость и отложенный урон замедления не дают (правило воя).
        /// </summary>
        public void ApplyHeroSlow(int percent, int ticks)
        {
            if (percent <= 0 || ticks <= 0 || Entities.Count <= PlayerId || !Entities.Alive[PlayerId]) return;
            if (percent > HeroRootPercent) percent = HeroRootPercent;
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
        /// Первой стадией тика, до пересчёта листов: снятый здесь модификатор
        /// этот же тик уже не замедляет. Замедление переживает и смерть того,
        /// кто его повесил.
        /// </summary>
        private void ExpireHeroSlow()
        {
            if (_heroSlowUntil == 0 || Tick < _heroSlowUntil) return;
            _heroSlowUntil = 0;
            _heroSlowPercent = 0;
            if (Entities.Count > PlayerId) Entities.Stats[PlayerId].RemoveSource(ModifierSource.Buff, HeroSlowId);
        }

        // Модификатор снимать не нужно: сброс идёт вместе с Entities.Clear,
        // и Spawn героя очищает его лист целиком.
        private void ResetHeroSlow()
        {
            _heroSlowUntil = 0;
            _heroSlowPercent = 0;
        }

        private void HashHeroSlow(ref ulong hash)
        {
            if (_heroSlowUntil == 0) return;
            Hashing.Mix(ref hash, 0x534C4F57);
            Hashing.Mix(ref hash, _heroSlowUntil);
            Hashing.Mix(ref hash, _heroSlowPercent);
        }
    }
}
