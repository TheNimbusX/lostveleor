namespace Game.Sim
{
    /// <summary>
    /// Две якорные способности: Крушение и «За борт!».
    ///
    /// Обе бьют по дуге перед героем и обе существуют ради РАСПОЛОЖЕНИЯ, а не
    /// ради урона. Крушение держит толпу на месте короткой серией; «За борт!»
    /// разбрасывает её и обращает саму толпу в оружие. Одна собирает, вторая
    /// разряжает — и обе спрашивают игрока об одном: как сейчас стоят враги.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Враги в секторе перед героем.
        ///
        /// Сектор задан ПОРОГОМ КОСИНУСА, а не углом: тригонометрии в
        /// симуляции нет, а скалярное произведение даёт ту же проверку без
        /// единого Atan2. Тело считается задетым, если его центр в радиусе;
        /// поправка на радиус тела делается по расстоянию, но не по углу —
        /// вплотную сбоку сектор иначе становится кругом.
        /// </summary>
        private int CollectArc(FixVec2 origin, FixVec2 direction, Fix64 radius,
            Fix64 arcCosine, int[] into)
        {
            int count = 0;
            for (int i = 1; i < Entities.Count && count < into.Length; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;

                FixVec2 delta = Entities.Position[i] - origin;
                Fix64 distance = delta.Length;
                if (distance > radius + Entities.BodyRadius[i]) continue;

                // Тело в самом центре: направления на него нет, но задето оно
                // безусловно — иначе враг, стоящий вплотную, был бы неуязвим.
                if (distance.Raw != 0
                    && FixVec2.Dot(delta / distance, direction) < arcCosine) continue;

                into[count++] = i;
            }
            return count;
        }

        // Абордаж — удар кулаком по прибытии с 02.10 в Simulation.Abordage.Strike
        // (прежний ResolveBoardingPunch переехал туда вместе с талантами линии).

        // Рывок героя (CastDash) — Simulation.Dash.

        // ---- Крушение ----
        // Серия с 03.10 в Simulation.Wreck (переделка по artifacts/wreck/plan/SPEC.md):
        // снимок WreckState, ход, удары, вал и формы. Здесь — общий для неё сектор.

        /// <summary>Четвёртый удар бьёт по земле вокруг героя, а не дугой.</summary>
        private static readonly Fix64 WreckGroundArc = Fix64.Ratio(-101, 100);

        /// <summary>Три удара: мах, обратный мах, удар якорем оземь.</summary>
        public const int WreckStages = 3;

        private readonly int[] _arcScratch = new int[64];
    }
}
