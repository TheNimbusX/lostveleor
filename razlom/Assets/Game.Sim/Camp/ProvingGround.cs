namespace Game.Sim
{
    /// <summary>
    /// Полигон: манекен, счётчик урона и разбивка по типам.
    ///
    /// ЗАЧЕМ ОН ПЕРВЫЙ ИЗ ВСЕГО ЛАГЕРЯ. Без него найденная вещь — лотерея:
    /// игрок читает «+18% урона» и «+4 к урону» и не знает, что из этого
    /// лучше на его билде. С ним лут превращается в эксперимент, а лагерь —
    /// в место, где принимают решения. Стоимость близка к нулю: симуляция,
    /// статы и снаряжение уже написаны, здесь только расстановка и счёт.
    ///
    /// Своя симуляция, а не подмена сущностей в забеге: Полигон обязан
    /// начинаться с чистого листа по нажатию, а забег — единица жизни
    /// симуляции, и лезть в него сбоку нельзя.
    /// </summary>
    public sealed class ProvingGround
    {
        /// <summary>Манекен-мишень. Индекс фиксирован: он ставится первым после игрока.</summary>
        public const int DummyId = 1;

        /// <summary>Манекен, который бьёт в ответ.</summary>
        public const int SparringId = 2;

        private readonly Simulation _sim;

        private long _damageTotal;
        private readonly long[] _damageByType = new long[2];
        private long _hits;
        private long _crits;
        private int _ticks;

        public Simulation Sim => _sim;

        /// <summary>Здоровье манекена. Настраивается: проверять урон нужно и на толстом, и на тонком.</summary>
        public int DummyHealth { get; private set; }

        public Fix64 DummyArmor { get; private set; }
        public Fix64 DummyFireResist { get; private set; }

        public long DamageTotal => _damageTotal;
        public long PhysicalDamage => _damageByType[(int)DamageType.Physical];
        public long FireDamage => _damageByType[(int)DamageType.Fire];
        public long Hits => _hits;
        public long Crits => _crits;
        public int Ticks => _ticks;

        /// <summary>
        /// Урон в секунду с последнего сброса, целым числом. Целым, потому что
        /// читать его будет человек, а не формула.
        ///
        /// Счётчик накопительный, а не скользящее окно: окно даёт живое, но
        /// прыгающее число, а Полигон нужен для СРАВНЕНИЯ двух вещей — там
        /// важнее, чтобы число сходилось, чем чтобы оно шевелилось.
        /// </summary>
        public int DamagePerSecond
            => _ticks <= 0 ? 0 : (int)(_damageTotal * Simulation.TicksPerSecond / _ticks);

        public ProvingGround(int capacity = 16)
        {
            // Сид намеренно постоянный: Полигон меряет билд, а не везение.
            // Броски на крит всё равно свои у каждого прогона — поэтому судить
            // надо по накопленному числу, а не по одному удару.
            _sim = new Simulation(0x9E3779B97F4A7C15UL, capacity);
        }

        /// <summary>
        /// Ставит игрока и два манекена. Зовётся при каждом входе на Полигон:
        /// счётчики обязаны начинаться с нуля, иначе замер прошлого билда
        /// смешается с новым.
        /// </summary>
        public void Setup(int dummyHealth, Fix64 dummyArmor, Fix64 dummyFireResist)
        {
            DummyHealth = dummyHealth;
            DummyArmor = dummyArmor;
            DummyFireResist = dummyFireResist;

            _sim.SetupProvingGround(dummyHealth, dummyArmor, dummyFireResist);
            ResetCounters();
        }

        public void ResetCounters()
        {
            _damageTotal = 0;
            _damageByType[0] = 0;
            _damageByType[1] = 0;
            _hits = 0;
            _crits = 0;
            _ticks = 0;
        }

        /// <summary>
        /// Один тик Полигона: шаг симуляции плюс подсчёт того, что прилетело
        /// в мишень.
        ///
        /// Считается урон ТОЛЬКО по мишени и только от игрока: спарринг-манекен
        /// бьёт по игроку, и его удары в счётчик урона попадать не должны.
        /// </summary>
        public void Step(in InputFrame input)
        {
            _sim.Step(in input);
            _ticks++;

            System.Collections.Generic.IReadOnlyList<SimEvent> events = _sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type != SimEventType.Damage) continue;
                if (e.Target != DummyId || e.Source != Simulation.PlayerId) continue;

                _damageTotal += e.Amount;
                _damageByType[(int)e.DamageKind] += e.Amount;
                _hits++;
                if (e.Flag) _crits++;
            }

            // Мишень не умирает: она мишень. Здоровье возвращается тем же тиком,
            // в котором кончилось, — иначе замер обрывался бы ровно на сильном
            // билде, то есть на том, ради которого Полигон и нужен.
            if (_sim.Entities.Health[DummyId] <= 0 || !_sim.Entities.Alive[DummyId])
                _sim.ReviveDummy(DummyId);
        }
    }
}
