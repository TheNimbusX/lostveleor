using System;

namespace Game.View
{
    public enum CampObstacleRole { Passable, Trunk, Solid, Boundary, FirePit }

    /// <summary>Чистые правила старых моделей; явные авторские препятствия задаются отдельно.</summary>
    public static class CampNavigationRules
    {
        static bool Has(string name, params string[] words)
        {
            foreach (string word in words) if (name.Contains(word)) return true;
            return false;
        }

        public static bool IsVisualOnly(string name) => Has((name ?? "").ToLowerInvariant(), "shadow", "тень", "тени");

        public static CampObstacleRole LegacyRole(string name, float height, float groundClearance)
        {
            name = (name ?? "").ToLowerInvariant();
            // Тень изгороди или дерева остаётся рисунком на земле, независимо от имени источника.
            if (IsVisualOnly(name)) return CampObstacleRole.Passable;
            if (Has(name, "изгород", "hedge", "fence", "ограда")) return CampObstacleRole.Boundary;
            if (Has(name, "tree", "spruce", "pine", "ель", "дерево") && height >= .6f) return CampObstacleRole.Trunk;
            if (Has(name, "fire+pit", "firepit", "кострище")) return CampObstacleRole.FirePit;
            if (Has(name, "grass", "flower", "bush", "holly", "plant", "leaf", "leaves", "трава", "цвет", "куст",
                "banner", "flag", "флаг", "знамя", "rug", "rope", "дым")) return CampObstacleRole.Passable;
            if (groundClearance > 1.5f || height < .35f) return CampObstacleRole.Passable;
            if (Has(name, "lantern", "crosspost", "signboard", "fonar", "фонар", "столб")) return CampObstacleRole.Trunk;
            if (Has(name, "tent", "market", "forge", "workbench", "anvil", "cauldron", "apparatus", "crate", "barrel",
                "bench", "cart", "bucket", "rock", "stone", "log", "stump", "палат", "лавк", "кузн", "камень"))
                return CampObstacleRole.Solid;
            return CampObstacleRole.Passable;
        }

        public static bool IsModelRootName(string name, bool hasOwnMesh)
        {
            name = (name ?? "").ToLowerInvariant();
            // «Живая изгородь» объединяет кольцо кустов: её общий box запер бы весь лагерь.
            return name.Contains("+3d+model") || name == "market" || name == "workbench" || name == "fonar"
                || name.Contains("uns_") || hasOwnMesh && name.Contains("изгород");
        }

        /// <summary>Возвращает локальный радиус, ограниченный 12..40 см после масштаба модели.</summary>
        public static float LegacyTrunkRadius(float localExtent, float worldScale)
        {
            worldScale = Math.Max(.0001f, Math.Abs(worldScale));
            float worldRadius = Math.Max(.12f, Math.Min(.4f, Math.Max(0, localExtent) * worldScale * .2f));
            return worldRadius / worldScale;
        }
    }
}
