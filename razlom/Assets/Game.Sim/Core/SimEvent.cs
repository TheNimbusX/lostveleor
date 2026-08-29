namespace Game.Sim
{
    public enum SimEventType : byte
    {
        None = 0,
        Damage = 1,
        Death = 2,
        Heal = 3,
        AbilityCast = 4,
        Spawn = 5,
        Attack = 6,

        /// <summary>
        /// Урон по времени: горение и всё, что будет после него.
        ///
        /// Отдельный тип, а не флаг у Damage, по одной практической причине.
        /// Горение тикает ТРИДЦАТЬ РАЗ В СЕКУНДУ на каждой горящей цели, и если
        /// представление считает это попаданиями, оно ставит hit-stop каждый
        /// кадр — игра встаёт в слоу-мо, пока хоть что-то горит. Тик урона
        /// и удар — разные события, и путать их нельзя.
        /// </summary>
        DamageOverTime = 7,
    }

    /// <summary>
    /// Единица связи «симуляция → представление». Симуляция описывает ЧТО произошло,
    /// представление решает, как это показать.
    ///
    /// Структура, а не класс: за забег их будут миллионы, аллокации недопустимы.
    /// Событие не влияет на симуляцию и не читается ею обратно.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly SimEventType Type;
        public readonly int Source;     // индекс сущности-источника, -1 если нет
        public readonly int Target;     // индекс сущности-цели, -1 если нет
        public readonly int Amount;     // урон, лечение, номер способности
        public readonly bool Flag;      // крит, добивание — по смыслу типа
        public readonly FixVec2 Position;

        /// <summary>
        /// Чем ударили. Осмысленно только у Damage.
        ///
        /// Нужен не отрисовке, а Полигону: «разбивка урона по источникам» —
        /// это его смысл, а без типа в событии разбить урон не по чему.
        /// </summary>
        public readonly DamageType DamageKind;

        public SimEvent(SimEventType type, int source, int target, int amount, bool flag,
            FixVec2 position, DamageType damageKind = DamageType.Physical)
        {
            Type = type;
            Source = source;
            Target = target;
            Amount = amount;
            Flag = flag;
            Position = position;
            DamageKind = damageKind;
        }

        public static SimEvent Damage(int source, int target, int amount, bool crit, FixVec2 at,
            DamageType kind)
            => new SimEvent(SimEventType.Damage, source, target, amount, crit, at, kind);

        /// <summary>Тик урона по времени. Не удар: ни стопа, ни тряски, ни звука попадания.</summary>
        public static SimEvent DamageOverTime(int source, int target, int amount, FixVec2 at,
            DamageType kind)
            => new SimEvent(SimEventType.DamageOverTime, source, target, amount, false, at, kind);

        public static SimEvent Death(int target, FixVec2 at)
            => new SimEvent(SimEventType.Death, -1, target, 0, false, at);

        public static SimEvent Cast(int source, int abilityIndex, FixVec2 at)
            => new SimEvent(SimEventType.AbilityCast, source, -1, abilityIndex, false, at);

        public static SimEvent Spawn(int target, FixVec2 at)
            => new SimEvent(SimEventType.Spawn, -1, target, 0, false, at);

        public static SimEvent Attack(int source, int target, FixVec2 at)
            => new SimEvent(SimEventType.Attack, source, target, 0, false, at);
    }
}
