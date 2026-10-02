using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// БУРЯ ЦВЕТЕНИЯ: ГДЕ УРОН, ГДЕ УКРЫТИЕ — чистые правила вида (без UnityEngine, тесты —
    /// tools/Combat.Presentation.Tests/ThicketStormDangerRulesTests.cs). Владелец 02.10, п. 12:
    /// «буря цветения не особо читается где урон а где сейв зона».
    ///
    /// Пока волна бури впереди, ВЕСЬ пол поляны говорит языком наших меток (GroundTelegraphStyle:
    /// красная заливка, светящийся пунктир по кромке, шевроны), а круги света вырезаны из него
    /// чисто и обведены золотом — это и есть укрытия. Рисует ThicketStormDangerView шейдером
    /// Razlom/Thicket Storm Danger; здесь — время и пол.
    ///
    /// Время — по тикам Sim (тик − 1 + доля кадра драйвера): пауза, хит-стоп и съёмка держат кадр
    /// сами. Волна k — круги TryGetThicketShape k·3 … k·3 + 2 (места Simulation.ThicketStormSafeCircles):
    /// • ждёт удара — заливка 0 → 1 от начала волны (буря — от её начала, вторая волна — от удара
    ///   первой) до удара круга; появляется за <see cref="FadeInTicks"/>; Часы сдвигают удар — заливка
    ///   тянется, как у общих меток;
    /// • ударила — полная, вспыхивает на <see cref="FlashTicks"/> и гаснет за <see cref="ResolvedFadeTicks"/>;
    ///   золото укрытий гаснет быстрее (<see cref="RimFadeTicks"/>): после удара круг уже не укрытие;
    /// • снята без удара (смерть босса или героя) — заливка замирает и гаснет за
    ///   Simulation.TelegraphLingerTicks, без вспышки: удара не было.
    /// </summary>
    public static class ThicketStormDangerRules
    {
        /// <summary>Мест под укрытия у шейдера: две волны по три круга.</summary>
        public const int Slots = Simulation.ThicketStormWaves * Simulation.ThicketStormSafeCircles;

        /// <summary>Поле опасности проявляется за столько тиков: весь пол разом не щёлкает.</summary>
        public const float FadeInTicks = 4f;

        /// <summary>Вспышка удара волны, тиков (общие метки — 3, у поля бури чуть дольше: оно на весь экран).</summary>
        public const float FlashTicks = 5f;

        /// <summary>Ударившая волна гаснет за столько тиков.</summary>
        public const float ResolvedFadeTicks = 9f;

        /// <summary>Золото укрытий ударившей волны гаснет за столько тиков: дальше круг уже не спасает.</summary>
        public const float RimFadeTicks = 3f;

        /// <summary>Снятая волна гаснет, как снятая общая метка.</summary>
        public const float CancelFadeTicks = Simulation.TelegraphLingerTicks;

        /// <summary>
        /// Край пола: поле опасности целиком до поля поляны <see cref="FloorFadeInner"/>, нет его от
        /// <see cref="FloorFadeOuter"/> (GladeRegion.Field, ≤ 1 — пол). У скруглённого пола босса
        /// 20 × 15 м это ~0,4 м внутрь от кромки — ~0,55 м наружу, под камнями каймы.
        /// </summary>
        public const float FloorFadeInner = .85f, FloorFadeOuter = 1.25f;

        /// <summary>Волна бури, как её видит вид: тики Sim и что с ней стало.</summary>
        public struct Wave
        {
            public bool Present, Resolved, Cancelled;
            public int StartTick, ImpactTick, CancelTick;
        }

        /// <summary>Как рисовать волну в кадре: заливка, видимость, вспышка, золото укрытий (всё 0…1).</summary>
        public readonly struct Look
        {
            public readonly float Progress, Opacity, Flash, Rim;

            public Look(float progress, float opacity, float flash, float rim)
            {
                Progress = progress; Opacity = opacity; Flash = flash; Rim = rim;
            }

            public bool Visible => Opacity > 0f || Rim > 0f;
        }

        /// <summary>Место круга k волны wave в TryGetThicketShape и в шейдере (0–5).</summary>
        public static int SlotOf(int wave, int k) => wave * Simulation.ThicketStormSafeCircles + k;

        /// <summary>Обратный отсчёт до удара: 0 в начале волны, 1 в тик удара.</summary>
        public static float Progress(float tick, int start, int impact)
        {
            if (float.IsNaN(tick)) return 0f;
            return Clamp01((tick - start) / Math.Max(1, impact - start));
        }

        /// <summary>
        /// Волна в Sim (её круги на месте): начало, удар, сработала ли. Новая волна или та же —
        /// поля просто переписываются: Часы двигают удар, снятие отменяется возвратом круга.
        /// </summary>
        public static void Observe(ref Wave wave, int start, int impact, bool resolved)
        {
            wave.Present = true;
            wave.Cancelled = false;
            wave.CancelTick = 0;
            wave.StartTick = start;
            wave.ImpactTick = impact;
            wave.Resolved = resolved;
        }

        /// <summary>
        /// Кругов волны в Sim больше нет (буря снята или кончилась): ждавшая удара — снята в тик
        /// now, ударившая доживает свою вспышку.
        /// </summary>
        public static void Lose(ref Wave wave, int now)
        {
            if (!wave.Present || wave.Resolved || wave.Cancelled) return;
            wave.Cancelled = true;
            wave.CancelTick = now;
        }

        /// <summary>Вид волны в тик tick (тик Sim с долей кадра).</summary>
        public static Look LookOf(in Wave wave, float tick)
        {
            if (!wave.Present || float.IsNaN(tick)) return default;
            if (wave.Resolved)
            {
                float age = Math.Max(0f, tick - wave.ImpactTick);
                return new Look(1f, Fade(age, ResolvedFadeTicks), Fade(age, FlashTicks), Fade(age, RimFadeTicks));
            }
            if (wave.Cancelled)
            {
                float age = Math.Max(0f, tick - wave.CancelTick);
                float frozen = Progress(wave.CancelTick, wave.StartTick, wave.ImpactTick);
                float shown = Fade(age, CancelFadeTicks) * Appear(wave.CancelTick - wave.StartTick);
                return new Look(frozen, shown, 0f, shown);
            }
            float appear = Appear(tick - wave.StartTick);
            return new Look(Progress(tick, wave.StartTick, wave.ImpactTick), appear, 0f, appear);
        }

        /// <summary>
        /// Доля «поле есть» в точке (x, z) пола glade: 1 на полу, плавно к 0 за его кромкой. Шейдер
        /// берёт её из вершины сетки (uv.x).
        /// </summary>
        public static float FloorMask(in GladeRegion glade, double x, double z)
            => ArenaMoodRules.GladeInside(ArenaMoodRules.GladeField(glade, x, z), FloorFadeInner, FloorFadeOuter);

        /// <summary>
        /// Длина заливки: от источника (sx, sz) до самой дальней точки пола (доля ≥ .5) — к удару
        /// фронт доходит до края поляны. Точки — вершины сетки поля; пола нет — fallback.
        /// </summary>
        public static float FillReach(float sx, float sz, float[] xs, float[] zs, float[] mask, int count, float fallback)
        {
            float best = 0f;
            if (xs != null && zs != null && mask != null)
            {
                int n = Math.Min(count, Math.Min(xs.Length, Math.Min(zs.Length, mask.Length)));
                for (int i = 0; i < n; i++)
                {
                    if (!(mask[i] >= .5f)) continue;
                    float dx = xs[i] - sx, dz = zs[i] - sz;
                    float d = dx * dx + dz * dz;
                    if (d > best) best = d;
                }
            }
            return best > 0f ? (float)Math.Sqrt(best) : Math.Max(1f, fallback);
        }

        private static float Appear(float age) => SmoothStep01(Clamp01(age / FadeInTicks));

        private static float Fade(float age, float ticks) => ticks <= 0f ? 0f : SmoothStep01(1f - Clamp01(age / ticks));

        private static float SmoothStep01(float x) => x * x * (3f - 2f * x);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : float.IsNaN(x) ? 0f : x;
    }
}
