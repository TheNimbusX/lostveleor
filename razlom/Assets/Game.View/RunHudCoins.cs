using System;

namespace Game.View
{
    /// <summary>
    /// Монеты в строку добычи (выбор владельца 30.09, кадр 1a): золото прибавилось — из точки на экране,
    /// где оно взялось (убитый враг, разобранная способность), вылетают 3–6 монет и мягкой дугой летят в
    /// знак золота строки; число досчитывает, когда монеты садятся. Здесь чистая логика без Unity
    /// (проверяется в Combat.Presentation.Tests): сколько монет, какая доля золота у каждой, когда вылет и
    /// посадка, где монета на дуге. Вид — RunHud.Coins, пул монет — миграция v3 RunHudWcBuilder.
    ///
    /// Координаты — единицы холста, ось y вверх (как у anchoredPosition): дуга поднимается над хордой.
    /// Всё детерминировано по номеру монеты — без случайных чисел: разброс одинаков от раза к разу.
    /// </summary>
    public static class RunHudCoins
    {
        /// <summary>Монет в пуле префаба: больше одновременно не летит.</summary>
        public const int Pool = 18;
        public const int MinPerDrop = 3, MaxPerDrop = 6;
        /// <summary>Золота на каждую монету сверх трёх.</summary>
        public const int GoldPerExtraCoin = 20;

        /// <summary>Разнос вылета монет одной горсти, с.</summary>
        public const float Stagger = .07f;
        /// <summary>Полёт одной монеты, с: основа и прибавка у части монет (не летят строем).</summary>
        public const float Flight = .62f, FlightSpread = .14f;
        /// <summary>Подъём дуги над хордой — доля длины хорды.</summary>
        public const float Lift = .32f;
        /// <summary>Разлёт горсти в стороны у источника, единицы холста.</summary>
        public const float Burst = 26f;
        /// <summary>Дольше этого после события источник уже не тот: монеты не летят, число досчитывает само.</summary>
        public const float SourceFreshness = 1.5f;

        /// <summary>Сколько монет у горсти в <paramref name="amount"/> золота: 3–6.</summary>
        public static int CountFor(int amount)
        {
            if (amount <= 0) return 0;
            return Math.Min(MaxPerDrop, MinPerDrop + amount / GoldPerExtraCoin);
        }

        /// <summary>
        /// Сколько монет выпустить, если в пуле свободно <paramref name="free"/>: не больше свободных;
        /// меньше одной — не летят, число досчитывает само.
        /// </summary>
        public static int CountFor(int amount, int free) => Math.Max(0, Math.Min(CountFor(amount), free));

        /// <summary>Доля золота монеты <paramref name="index"/> из <paramref name="count"/>: сумма долей — ровно amount.</summary>
        public static int Share(int amount, int count, int index)
        {
            if (count <= 0 || amount <= 0) return 0;
            return amount / count + (index < amount % count ? 1 : 0);
        }

        /// <summary>Когда монета вылетает от начала горсти, с.</summary>
        public static float Launch(int index) => index * Stagger;

        /// <summary>Сколько летит монета, с: у каждой третьей — чуть дольше.</summary>
        public static float Duration(int index) => Flight + FlightSpread * (index % 3) * .5f;

        /// <summary>Когда монета садится в знак золота от начала горсти, с.</summary>
        public static float Landing(int index) => Launch(index) + Duration(index);

        /// <summary>Доля пути монеты через <paramref name="since"/> с от начала горсти: меньше нуля — ещё не вылетела.</summary>
        public static float Progress(int index, float since)
        {
            float t = since - Launch(index);
            if (t < 0f) return -1f;
            return Math.Min(1f, t / Duration(index));
        }

        /// <summary>
        /// Точка монеты на дуге при доле пути <paramref name="t"/> (0…1). Квадратичная кривая: из источника
        /// с разлётом горсти вверх-в-сторону, вершина над серединой хорды, конец — ровно в цели. Ход —
        /// разгон и мягкая посадка (smoothstep).
        /// </summary>
        public static void Arc(float fromX, float fromY, float toX, float toY, int index, float t, out float x, out float y)
        {
            if (t <= 0f) t = 0f;
            if (t >= 1f) { x = toX; y = toY; return; }
            float k = t * t * (3f - 2f * t);
            // Разлёт у источника: золотой угол по номеру, чуть вверх.
            double angle = index * 2.39996323 + .6;
            float sx = fromX + (float)Math.Cos(angle) * Burst;
            float sy = fromY + Math.Abs((float)Math.Sin(angle)) * Burst * .8f;
            float dx = toX - sx, dy = toY - sy;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            // Вершина: середина хорды, поднятая вверх, и немного в сторону по номеру — горсть не летит одной ниткой.
            float side = ((index % 3) - 1) * .12f * length;
            float nx = length > 1e-3f ? -dy / length : 0f, ny = length > 1e-3f ? dx / length : 1f;
            float cx = (sx + toX) * .5f + nx * side;
            float cy = (sy + toY) * .5f + ny * side + Lift * length;
            float u = 1f - k;
            x = u * u * sx + 2f * u * k * cx + k * k * toX;
            y = u * u * sy + 2f * u * k * cy + k * k * toY;
        }

        /// <summary>Размер монеты на пути: вспыхивает из точки, летит в полный рост, у цели чуть сжимается.</summary>
        public static float Scale(float t)
        {
            if (t < 0f) return 0f;
            if (t < .12f) return t / .12f;
            if (t > .85f) return 1f - (t - .85f) / .15f * .35f;
            return 1f;
        }

        /// <summary>Прозрачность монеты: проступает сразу, у самой цели тает — её «съедает» знак золота.</summary>
        public static float Alpha(float t)
        {
            if (t < 0f) return 0f;
            if (t > .9f) return Math.Max(0f, 1f - (t - .9f) / .1f);
            return 1f;
        }
    }
}
