namespace Game.Sim
{
    /// <summary>
    /// Чем бьют. Существует ровно затем, чтобы знать, какая защита срабатывает:
    /// броня гасит физический урон, сопротивление — огонь. Меньшего, чем это,
    /// разделения урона по типам не бывает, а большее пока не из чего собрать.
    ///
    /// Значение попадает в поведение и хеш — новые типы добавлять в конец.
    /// </summary>
    public enum DamageType : byte
    {
        Physical = 0,
        Fire = 1,
    }

    /// <summary>
    /// Перевод статов в числа, которыми оперирует тик боя.
    ///
    /// Живёт отдельным местом затем, что это ПРАВИЛО, а не арифметика: от него
    /// зависит, во что превращается «+20% скорости атаки» на предмете. Одна
    /// копия правила — одно место, где его читать и менять.
    ///
    /// Округление везде к БЛИЖАЙШЕМУ. Обычный сдвиг вправо у Fix64 — это пол,
    /// и на нём кулдаун в 36 тиков превратился бы в 35 от одной только
    /// неточности представления 5/6 двоичной дробью.
    /// </summary>
    public static class CombatStats
    {
        /// <summary>
        /// Потолок кулдауна — час игрового времени. Нужен как ответ на нулевую
        /// скорость атаки: NextAttackTick это обычный int, и «бесконечность»
        /// в нём молча переполнилась бы при сложении с текущим тиком.
        /// </summary>
        public const int MaxAttackCooldown = Simulation.TicksPerSecond * 3600;

        /// <summary>
        /// Округление к ближайшему. Статы неотрицательны, поэтому «пол от x+0.5»
        /// здесь и есть округление; для отрицательных оно даёт округление вверх,
        /// и это осознанно не покрыто — отрицательного здоровья не бывает.
        /// </summary>
        public static int RoundToInt(Fix64 value) => (value + Fix64.Half).ToInt();

        /// <summary>
        /// Кулдаун автоатаки из скорости атаки.
        ///
        /// Скорость атаки задана в АТАКАХ В СЕКУНДУ — в этих единицах её понимают
        /// аффиксы и узлы деревьев, и только в них «+20%» значит то, что игрок
        /// прочитает. Правило «всё в тиках» касается хранения длительностей,
        /// а перевод обязан случаться в одной точке, иначе одна и та же прибавка
        /// начнёт значить разное в разных системах.
        /// </summary>
        public static int AttackCooldownTicks(Fix64 attacksPerSecond)
        {
            if (attacksPerSecond.Raw <= 0) return MaxAttackCooldown;

            int ticks = RoundToInt(Fix64.FromInt(Simulation.TicksPerSecond) / attacksPerSecond);

            // Чаще одного удара за тик не бьёт никто: тик — неделимая единица
            // времени симуляции, и второй удар внутри него просто негде поставить.
            if (ticks < 1) return 1;
            return ticks > MaxAttackCooldown ? MaxAttackCooldown : ticks;
        }

        /// <summary>Шаг за тик из скорости в метрах в секунду.</summary>
        public static Fix64 MoveStepPerTick(Fix64 metersPerSecond)
            => metersPerSecond / Simulation.TicksPerSecond;

        // ---- защита ----

        /// <summary>
        /// Потолок сопротивления стихии. Полный иммунитет собрать нельзя:
        /// иначе один билд выключает целый класс контента, и балансировать
        /// огонь становится невозможно — он либо не страшен вообще, либо
        /// убивает всех, кто не собрал сотню.
        /// </summary>
        public static readonly Fix64 MaxResistance = Fix64.Ratio(3, 4);

        /// <summary>
        /// Броня по кривой Path of Exile:
        ///
        ///     снижение = броня / (броня + 5 × урон удара)
        ///
        /// Кривая, а не плоский процент: одна и та же броня почти целиком гасит
        /// рой мелких ударов и почти не мешает удару босса. Отсюда два следствия,
        /// ради которых модель и выбрана — броне не нужен потолок, он встроен
        /// в саму формулу, и она не обесценивается на поздних числах.
        ///
        /// Броня работает по ФИЗИЧЕСКОМУ урону. Стихии гасит сопротивление.
        /// </summary>
        public static int MitigateByArmor(int damage, Fix64 armor)
        {
            if (damage <= 0 || armor.Raw <= 0) return damage;

            // Пятикратный урон считается в int до перевода в Fix64: произведение
            // двух больших Fix64 переполнило бы 32.32, а это сложение — нет.
            Fix64 denominator = armor + Fix64.FromInt(5 * damage);
            Fix64 reduction = armor / denominator;

            return AtLeastOne(RoundToInt(Fix64.FromInt(damage) * (Fix64.One - reduction)));
        }

        /// <summary>
        /// Сопротивление стихии: урон × (1 − сопротивление), сопротивление
        /// обрезано сверху потолком MaxResistance.
        ///
        /// Отрицательное сопротивление урон НЕ усиливает: механики пробития
        /// в проекте нет, и заводить её походя нельзя.
        /// </summary>
        public static int MitigateByResistance(int damage, Fix64 resistance)
        {
            if (damage <= 0 || resistance.Raw <= 0) return damage;

            Fix64 capped = resistance > MaxResistance ? MaxResistance : resistance;
            return AtLeastOne(RoundToInt(Fix64.FromInt(damage) * (Fix64.One - capped)));
        }

        /// <summary>Защита, отвечающая за этот тип урона.</summary>
        public static int Mitigate(int damage, DamageType type, Fix64 armor, Fix64 fireResist)
            => type == DamageType.Fire
                ? MitigateByResistance(damage, fireResist)
                : MitigateByArmor(damage, armor);

        /// <summary>
        /// Прошедший защиту удар обязан отнять хотя бы единицу. Ни броня,
        /// ни сопротивление в этих формулах не гасят урон полностью, поэтому
        /// ноль на экране был бы не балансом, а следом округления.
        /// </summary>
        private static int AtLeastOne(int damage) => damage < 1 ? 1 : damage;
    }
}
