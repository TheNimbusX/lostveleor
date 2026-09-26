namespace Game.Sim
{
    // Фракция задаёт враждебность, а вид — тело и правила конкретного моба.
    public enum EnemyKind : byte
    {
        None = 0,
        ForestGuardian = 1,
        ForestRootSwarm = 2,
        ForestBud = 3,
        ForestWendigo = 4,
        ForestStonehoof = 5,

        /// <summary>Шипомёт — элита: линия шипов из-под земли и всплеск, если его обняли.</summary>
        ForestThorncaster = 6,

        /// <summary>Корнехват: удар корнями по месту героя, замедление.</summary>
        ForestRootSnarer = 7,

        /// <summary>Расщепень: при смерти распадается на двух детёнышей.</summary>
        ForestSplitter = 8,

        /// <summary>Детёныш Расщепеня. Только из распада — в пачки и волны не ставится.</summary>
        ForestSplitling = 9,
    }
}
