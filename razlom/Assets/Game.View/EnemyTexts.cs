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
                default: return "Враг";
            }
        }

        /// <summary>
        /// Босс лугов — пока усиленный лесной хранитель, место настоящего Хозяина Чащи
        /// (DESIGN, «Бой рогалика»); появится он — имя придёт отсюда же.
        /// </summary>
        public static string BossName(EnemyKind kind) => kind == EnemyKind.ForestGuardian ? "Хранитель лугов" : Name(kind);
    }
}
