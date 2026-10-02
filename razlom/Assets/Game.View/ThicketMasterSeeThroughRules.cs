using System;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — СКВОЗЬ БОССА ВИДНО ГЕРОЯ: чистое правило (без UnityEngine, тесты —
    /// tools/Combat.Presentation.Tests/ThicketMasterSeeThroughRulesTests.cs). Владелец 02.10:
    /// «прозрачность должна быть, чтоб было видно».
    ///
    /// • Прозрачность сетчатая (Resources/Shaders/RazlomSeeThrough.hlsl): пиксели тела, кроны,
    ///   куста и накладок фаз в круге вокруг героя, которые ближе к камере, чем он, выбиваются
    ///   по Байеру 4×4. Не смешивание и не высветление; тень остаётся.
    /// • Включается, только когда тело и правда стоит между героем и камерой: луч от точек героя
    ///   к камере пересекает рамку тела (<see cref="RayHitsBox"/>). Герой сбоку или перед боссом —
    ///   тело целое.
    /// • Появляется и уходит за <see cref="FadeSeconds"/> (по тикам Sim: пауза держит кадр).
    /// </summary>
    public static class ThicketMasterSeeThroughRules
    {
        /// <summary>Шейдеры с прозрачностью: тело и ягоды (URP Lit), цветы накладок (URP Simple Lit).</summary>
        public const string LitShader = "Razlom/Boss See-Through Lit";
        public const string SimpleLitShader = "Razlom/Boss See-Through Simple Lit";

        /// <summary>Появление и уход прозрачности, с.</summary>
        public const float FadeSeconds = .15f;

        /// <summary>Доля выбитых пикселей в середине круга при полной прозрачности (60–75 %).</summary>
        public const float Density = .7f;

        /// <summary>Радиус круга в плоскости экрана, м (камера боя: орто 6,2, наклон 48°).</summary>
        public const float RadiusMetres = 1.8f;

        /// <summary>
        /// Пиксель по земле (вдоль горизонтального взгляда камеры) на столько метров ближе
        /// вертикальной оси героя — выбит в полную силу; за осью — целый. Мера одна для
        /// ступней и головы: тело, закрывшее ноги, выбивается так же, как закрывшее грудь.
        /// Корпус держит ось героя в 0,45 м от тела босса — закрывающее уже в полную силу.
        /// </summary>
        public const float DepthRampMetres = .35f;

        /// <summary>Середина героя над землёй, м: центр круга на экране (точка в мире для шейдера).</summary>
        public const float HeroCentreHeight = .95f;

        /// <summary>Рамка тела раздута на столько метров: край кроны и лап не должен щёлкать.</summary>
        public const float BoxMarginMetres = .25f;

        /// <summary>Точки героя, из которых пускаются лучи к камере: высота и сдвиг вдоль «вправо» камеры, м.</summary>
        public static readonly float[] SampleHeights = { .3f, .95f, 1.6f, .95f, .95f };
        public static readonly float[] SampleSides = { 0f, 0f, 0f, -.4f, .4f };

        /// <summary>Шаг появления: к цели (0 или 1) не быстрее 1 / FadeSeconds в секунду.</summary>
        public static float Step(float fade, float target, float dt)
        {
            if (float.IsNaN(fade)) fade = 0f;
            target = Clamp01(target);
            if (dt <= 0f) return Clamp01(fade);
            float step = dt / FadeSeconds;
            if (fade < target) return Math.Min(target, fade + step);
            return Math.Max(target, fade - step);
        }

        /// <summary>Доля выбитых пикселей для шейдера: плавное появление × плотность сетки.</summary>
        public static float Shown(float fade)
        {
            float x = Clamp01(fade);
            return Density * x * x * (3f - 2f * x);
        }

        /// <summary>
        /// Луч o + t·d, t ≥ 0 (t ≤ maxT), пересекает рамку [min, max] (оси рамки, метод плит).
        /// Начало внутри рамки — пересекает: герой под кроной или между лап закрыт телом.
        /// </summary>
        public static bool RayHitsBox(float ox, float oy, float oz, float dx, float dy, float dz,
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ, float maxT)
        {
            float near = 0f, far = maxT;
            return Slab(ox, dx, minX, maxX, ref near, ref far)
                   && Slab(oy, dy, minY, maxY, ref near, ref far)
                   && Slab(oz, dz, minZ, maxZ, ref near, ref far);
        }

        private static bool Slab(float o, float d, float min, float max, ref float near, ref float far)
        {
            if (Math.Abs(d) < 1e-7f) return o >= min && o <= max;
            float t0 = (min - o) / d, t1 = (max - o) / d;
            if (t0 > t1) { float swap = t0; t0 = t1; t1 = swap; }
            if (t0 > near) near = t0;
            if (t1 < far) far = t1;
            return near <= far;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
