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
    ///
    /// Ревью 02.10, вечер («буря — непонятно… дольше и более явно»): волны стали 90 и 75 тиков (3 и
    /// 2,5 с) — за долгую заливку конец теряется, поэтому последнюю секунду перед ударом поле бьётся
    /// пульсом (<see cref="PulseOf"/>): <see cref="PulseBeats"/> ударов всё чаще и ярче, последний — в
    /// тик удара, его подхватывает вспышка. Пульс — только у ждущей удара волны, от тика Sim.
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
        /// Пульс перед ударом: окно, тиков (разметка ударов — доля s = 1 − осталось/окно, удар k — при
        /// PulseBeats·s² = k). При 54 и 5 удары за ~30, 20, 12, 6 тиков до удара волны и в тик удара:
        /// всё чаще, первый — за секунду.
        /// </summary>
        public const float PulseLeadTicks = 54f;

        /// <summary>Ударов пульса за окно; последний — в тик удара волны.</summary>
        public const int PulseBeats = 5;

        /// <summary>
        /// Край пола: поле опасности целиком до поля поляны <see cref="FloorFadeInner"/>, нет его от
        /// <see cref="FloorFadeOuter"/> (GladeRegion.Field, ≤ 1 — пол). У скруглённого пола босса
        /// 20,98 × 15,74 м это ~0,4 м внутрь от кромки — ~0,55 м наружу, под камнями каймы.
        /// </summary>
        public const float FloorFadeInner = .85f, FloorFadeOuter = 1.25f;

        /// <summary>Волна бури, как её видит вид: тики Sim и что с ней стало.</summary>
        public struct Wave
        {
            public bool Present, Resolved, Cancelled;
            public int StartTick, ImpactTick, CancelTick;
        }

        /// <summary>Как рисовать волну в кадре: заливка, видимость, вспышка, золото укрытий, пульс перед ударом (всё 0…1).</summary>
        public readonly struct Look
        {
            public readonly float Progress, Opacity, Flash, Rim, Pulse;

            public Look(float progress, float opacity, float flash, float rim, float pulse = 0f)
            {
                Progress = progress; Opacity = opacity; Flash = flash; Rim = rim; Pulse = pulse;
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
            return new Look(Progress(tick, wave.StartTick, wave.ImpactTick), appear, 0f, appear, appear * PulseOf(tick, wave.ImpactTick));
        }

        /// <summary>
        /// Пульс ждущей волны в тик tick (0…1): 0 раньше окна <see cref="PulseLeadTicks"/> и после
        /// удара; в окне — удары при PulseBeats·s² = k (s — доля окна), каждый вспыхивает сразу и
        /// гаснет кубом до следующего, сила растёт к удару (0,55 → 1). В тик удара — 1.
        /// </summary>
        public static float PulseOf(float tick, int impact)
        {
            if (float.IsNaN(tick)) return 0f;
            float left = impact - tick;
            if (left < 0f || left > PulseLeadTicks) return 0f;
            float s = 1f - left / PulseLeadTicks;
            float phase = PulseBeats * s * s;
            if (phase < 1f) return 0f;
            float decay = 1f - (phase - (float)Math.Floor(phase));
            return (.55f + .45f * s) * decay * decay * decay;
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

        // ---------------------------------------------------------------- аура канала у ног босса

        /// <summary>
        /// Аура канала (розовое кольцо у ног и пульсы от тела) — рисует поле (ревью 02.10, вечер: «круг укрытия
        /// у босса розовый, а остальные золотые» — частицы ауры под полем заливали круг 0 розовым). Радиус, м
        /// (2,3 × рост 1,15, как была частица), раскрытие и угасание, с.
        /// </summary>
        public const float AuraRadiusMetres = 2.645f, AuraOpenSeconds = .35f, AuraCloseSeconds = .45f;

        /// <summary>
        /// Видимость ауры 0…1 в тик tick: раскрывается за AuraOpenSeconds от начала бури start, гаснет за
        /// AuraCloseSeconds к её концу end (EndTick; Часы его сдвигают — вид перечитывает); вне — 0.
        /// </summary>
        public static float AuraOf(float tick, int start, int end)
        {
            if (float.IsNaN(tick) || end <= start) return 0f;
            float age = (tick - start) / Simulation.TicksPerSecond, left = (end - tick) / Simulation.TicksPerSecond;
            if (age <= 0f || left <= 0f) return 0f;
            return SmoothStep01(Clamp01(age / AuraOpenSeconds)) * SmoothStep01(Clamp01(left / AuraCloseSeconds));
        }

        /// <summary>Секунды канала для пульсов ауры (от начала бури, по тикам Sim — пауза держит); до начала — 0.</summary>
        public static float AuraSeconds(float tick, int start)
            => float.IsNaN(tick) ? 0f : Math.Max(0f, (tick - start) / Simulation.TicksPerSecond);

        // ---------------------------------------------------------------- лепестки у героя

        /// <summary>
        /// Дольше этого, с, частица бури — не лепесток: лепестки, ветер, искры и листья бури живут до 3 с, луч
        /// канала («Beam», «Beam Core») — одна частица на всю бурю (5,9 с).
        /// </summary>
        public const float HeroClearLifetimeMax = 4f;

        /// <summary>
        /// Гасит ли вид у героя частицы системы бури (ThicketMasterCombatView.ClearAroundHero: альфа частицы только
        /// опускается и назад не встаёт — лепесток живёт 2–3 с, поток новых идёт). Одиночная или долгая частица
        /// (луч канала с крон) — нет: погасив её, когда крона на экране легла на героя, вид прятал бы луч до конца
        /// бури, даже когда герой ушёл (проверка находок 03.10). maxParticles и lifetimeMax — из модуля main.
        /// </summary>
        public static bool ClearsAroundHero(int maxParticles, float lifetimeMax)
            => maxParticles > 1 && lifetimeMax <= HeroClearLifetimeMax;

        private static float Appear(float age) => SmoothStep01(Clamp01(age / FadeInTicks));

        private static float Fade(float age, float ticks) => ticks <= 0f ? 0f : SmoothStep01(1f - Clamp01(age / ticks));

        private static float SmoothStep01(float x) => x * x * (3f - 2f * x);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : float.IsNaN(x) ? 0f : x;
    }
}
