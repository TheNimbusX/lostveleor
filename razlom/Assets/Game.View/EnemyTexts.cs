using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Имена врагов для табличек элиты и полосы босса — из бестиария акта I (Чаща). Раньше
    /// табличка любой элиты говорила «Усиленный хранитель», а босс был зашит в полосу строкой
    /// (аудит UI, 25 сентября).
    /// </summary>
    public static class EnemyTexts
    {
        public static string Name(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.ForestGuardian: return "Лесной хранитель";
                case EnemyKind.ForestRootSwarm: return "Корнеполз";
                case EnemyKind.ForestBud: return "Плюй-плод";
                case EnemyKind.ForestWendigo: return "Лесной вендиго";
                case EnemyKind.ForestStonehoof: return "Камнекопыт";
                // Новые мобы леса (план 26.09); имена утверждены вместе с планом.
                case EnemyKind.ForestThorncaster: return "Шипомет";
                case EnemyKind.ForestRootSnarer: return "Корнехват";
                case EnemyKind.ForestSplitter: return "Расщепень";
                case EnemyKind.ForestSplitling: return "Детёныш Расщепеня";
                // Босс леса (план 01.10).
                case EnemyKind.ForestThicketMaster: return "Хозяин Чащи";
                default: return "Враг";
            }
        }

        /// <summary>
        /// Имя на полосе босса. Временный босс лугов — усиленный лесной хранитель
        /// («Хранитель лугов», пока переключатель Simulation.UseThicketMasterBoss выключен);
        /// настоящий босс леса — «Хозяин Чащи».
        /// </summary>
        public static string BossName(EnemyKind kind) => kind == EnemyKind.ForestGuardian ? "Хранитель лугов" : Name(kind);
    }
}
