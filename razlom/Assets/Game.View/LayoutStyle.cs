using System;
using UnityEngine;

namespace Game.View
{
    [Serializable]
    public sealed class LayoutStyle
    {
        // Пол занимает большую часть кадра, поэтому он задаёт дневную яркость
        // всей локации. Тёплый светлый камень не превращает толпу в чёрное пятно;
        // холодный цвет остаётся только в небольших акцентах и дальнем фоне.
        public Color RoomColor = new Color(0.68f, 0.65f, 0.55f);
        public Color EntranceColor = new Color(0.61f, 0.69f, 0.57f);
        public float Thickness = 0.2f;
        public Color ExitColor = new Color(0.72f, 0.58f, 0.30f);
        [Header("Окружение лугов")]
        public bool NaturalGround = true;
        public GameObject PortalPrefab;
        public GameObject CachePrefab;
        [Range(0, 30)] public float ForestBandWidth = 16;
        [Range(4, 12)] public float ForestSpacing = 6;
        public Color SunColor = new Color(1f, 0.92f, 0.8f);
        [Range(0.1f, 3)] public float SunIntensity = 1.25f;
        public Vector3 SunAngles = new Vector3(52, -35, 0);
        public Color SkyColor = new Color(0.62f, 0.7f, 0.63f);
        public Color AmbientColor = new Color(0.4f, 0.46f, 0.36f);
        public Color FogColor = new Color(0.55f, 0.63f, 0.54f);
        [Min(20)] public float FogStart = 45;
        [Min(30)] public float FogEnd = 100;

        // Зазор был отладочным: он показывал, что карта собралась из модулей.
        // На картинке это читалось как сетка на полу — прямой запрет из брифа.
        // Плиты теперь стыкуются, а границы комнат показывает сам пол.
        [Tooltip("Зазор между плитами. 0 — плиты стыкуются без шва.")]
        public float Gap = 0f;

        [Header("Текстуры пола")]
        [Tooltip("Трава — под пол комнат. Пусто — берётся из Resources (UNS_Terrain_Grass).")]
        public Texture2D RoomFloorTexture;

        [Tooltip("Грунт/грязь — под вход и выход, читается как утоптанная тропа. " +
                 "Пусто — берётся из Resources (UNS_Terrain_Dirt).")]
        public Texture2D PathFloorTexture;

        public float FloorTextureTiling = 0.3f;
        public float FloorTextureStrength = 2f;

        [Header("Декор")]
        [Tooltip("Варианты декора с весами. Пустой Prefab — процедурная заглушка по Kind.")]
        public DecorVariant[] DecorVariants =
        {
            new DecorVariant { Kind = DecorKind.Bush, ResourcePath = "Decor/UNS_Bush", Weight = 3f,
                ScaleRange = new Vector2(0.85f, 1.25f), UseAsBoundary = true },

            // Пять разных камней пака вместо одного — иначе граница читалась
            // бы одним и тем же валуном, отштампованным по контуру.
            new DecorVariant { Kind = DecorKind.Rock, ResourcePath = "Decor/UNS_Standard_Rock_01", Weight = 1.2f,
                ScaleRange = new Vector2(0.7f, 1.3f), UseAsBoundary = true },
            new DecorVariant { Kind = DecorKind.Rock, ResourcePath = "Decor/UNS_Standard_Rock_02", Weight = 1.2f,
                ScaleRange = new Vector2(0.7f, 1.3f), UseAsBoundary = true },
            new DecorVariant { Kind = DecorKind.Rock, ResourcePath = "Decor/UNS_Standard_Rock_03", Weight = 1.2f,
                ScaleRange = new Vector2(0.7f, 1.3f), UseAsBoundary = true },
            new DecorVariant { Kind = DecorKind.Rock, ResourcePath = "Decor/UNS_Standard_Rock_04", Weight = 1.2f,
                ScaleRange = new Vector2(0.7f, 1.3f), UseAsBoundary = true },
            new DecorVariant { Kind = DecorKind.Rock, ResourcePath = "Decor/UNS_Standard_Rock_05", Weight = 1.2f,
                ScaleRange = new Vector2(0.7f, 1.3f), UseAsBoundary = true },

            new DecorVariant { Kind = DecorKind.GrassTuft, ResourcePath = "Decor/UNS_Grass", Weight = 4f,
                ScaleRange = new Vector2(0.8f, 1.3f), UseAsBoundary = false },

            // UNS_Spruce — настоящие ели метров на двенадцать. Масштаб подобран
            // так, чтобы за границей локации они читались как высокий дальний
            // лес, а не соразмерная комнатам декорация.
            new DecorVariant { Kind = DecorKind.Tree, ResourcePath = "Decor/UNS_Spruce_01", Weight = 1.5f,
                ScaleRange = new Vector2(0.35f, 0.55f), UseAsBoundary = true },
            new DecorVariant { Kind = DecorKind.Tree, ResourcePath = "Decor/UNS_Spruce_02", Weight = 1.5f,
                ScaleRange = new Vector2(0.35f, 0.55f), UseAsBoundary = true },
        };

        [Tooltip("Среднее число объектов декора на клетку пола модуля.")]
        [Range(0f, 1f)]
        public float DecorPerCell = 0.12f;

        [Tooltip("Отступ декора от края модуля, метры.")]
        public float DecorEdgeMargin = 0.7f;

        [Tooltip("Радиус вокруг точки стыковки модуля, свободный от декора, метры. " +
                 "Без зазора куст мог бы встать прямо в проходе между комнатами.")]
        public float DecorConnectorMargin = 1.6f;

        [Header("Граница локации")]
        // Стен-заглушек больше нет: IsWalkable в Game.Sim и так не пускает
        // игрока дальше занятых клеток — рисовать это ещё и кубом было
        // избыточно. Вместо этого по каждому открытому краю с вероятностью
        // BoundaryDecorChance садится кустарник/камень/дерево — граница
        // читается как заросли, а не как прямоугольная выгородка.
        [Tooltip("Вероятность декора на каждом открытом крае клетки.")]
        [Range(0f, 1f)]
        public float BoundaryDecorChance = 0.55f;

        [Tooltip("Насколько декор выступает наружу от края клетки, метры.")]
        public float BoundaryDecorOutset = 0.6f;

        [Tooltip("Случайный разброс позиции декора у границы, метры.")]
        public float BoundaryDecorJitter = 0.9f;

        [Header("Заливка вне карты")]
        [Tooltip("Сторона огромной плоскости под всей зоной — чтобы в разрывах " +
                 "декора на границе не проглядывала пустота.")]
        public float GroundFillSize = 240f;
        public float GroundFillDepthOffset = 0.03f;

        [Header("Маршрут по локации")]
        [Range(0.8f, 2f)] public float RouteWidth = 1.6f;
        [Tooltip("Дополнительный зазор между краем тропы и габаритами декора.")]
        public float RouteClearance = 0.35f;
        [Tooltip("Свободное от декора место вокруг точки появления, метры.")]
        public float EntryClearance = 3f;
        // Retained for serialized profiles from before walkable route trails.
        [HideInInspector] public int PathTrailSteps = 4;

        [HideInInspector] public float PathTrailJitter = 0.4f;

        [Header("Цвета заглушек декора")]
        public Color BushPlaceholderColor = new Color(0.30f, 0.45f, 0.24f);
        public Color RockPlaceholderColor = new Color(0.55f, 0.53f, 0.50f);
        public Color GrassTuftPlaceholderColor = new Color(0.42f, 0.58f, 0.30f);
        public Color TreePlaceholderColor = new Color(0.22f, 0.32f, 0.16f);


        public LayoutStyle Copy()
        {
            var copy = (LayoutStyle)MemberwiseClone();
            copy.DecorVariants = DecorVariants == null ? Array.Empty<DecorVariant>() : (DecorVariant[])DecorVariants.Clone();
            return copy;
        }

        public void Validate()
        {
            if (ForestBandWidth < 0 || ForestBandWidth > 30 || ForestSpacing < 4 || ForestSpacing > 12
                || FogStart < 20 || FogEnd <= FogStart || SunIntensity < 0.1f || SunIntensity > 3)
                throw new ArgumentException("Проверьте ширину леса, шаг деревьев, свет и дальность тумана.");
            if (RouteWidth < 0.8f || RouteWidth > 2f || RouteClearance < 0 || EntryClearance < 0
                || Thickness <= 0 || Gap < 0 || Gap >= Game.Sim.LayoutMap.CellSize.ToFloat()
                || FloorTextureTiling <= 0 || FloorTextureStrength < 0
                || DecorPerCell < 0 || DecorPerCell > 1 || DecorEdgeMargin < 0 || DecorConnectorMargin < 0
                || BoundaryDecorChance < 0 || BoundaryDecorChance > 1 || BoundaryDecorOutset < 0
                || BoundaryDecorJitter < 0 || GroundFillSize < 0 || GroundFillDepthOffset < 0
                || PathTrailSteps < 0 || PathTrailSteps > 32 || PathTrailJitter < 0 || PathTrailJitter > 1)
                throw new ArgumentException("Проверьте размеры, плотность декора и отступы в профиле оформления.");
            if (DecorVariants == null) return;
            for (int i = 0; i < DecorVariants.Length; i++)
                if (DecorVariants[i].Weight < 0 || DecorVariants[i].ScaleRange.x <= 0
                    || DecorVariants[i].ScaleRange.y < DecorVariants[i].ScaleRange.x)
                    throw new ArgumentException($"Декор {i + 1}: вес >= 0, масштаб 0 < min <= max.");
        }
    }
}
