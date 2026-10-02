using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ВНЕШНОСТЬ ПО ФАЗАМ: чистое правило (без UnityEngine, тесты —
    /// tools/Combat.Presentation.Tests/ThicketMasterPhaseRulesTests.cs). Владелец 02.10:
    /// «босс по фазам не меняется вообще» — теперь меняется на каждом рёве порога.
    ///
    /// • Уровень одежды: 1 — до рёва 66 (руны тёмные, куст зелёный с редкими ягодами),
    ///   2 — с удара рёва 66 (руны и глаза янтарём, ягоды набухли), 3 — с удара рёва 33
    ///   (руны ярче, смоляные прожилки, цветы куста и кроны, падают лепестки). Рёв 50 —
    ///   ярость: руны ярче внутри уровня 2.
    /// • Смена — в тик УДАРА рёва, а не по HP: Sim ставит биты в RoarsDone в начале рёва
    ///   (Brain.StartThicketRoar), поэтому до удара биты текущего рёва (a.Tag) вычитаются.
    /// • Яркость рун k (множитель _EmissionColor, цвет — в карте эмиссии): вспышка ×1,45
    ///   на 4 тика с тика удара, к цели за 12 тиков, дыхание ±8 % с периодом 2 с от тика
    ///   Sim (пауза и съёмка держат кадр), после смерти — к нулю за 1,5 с.
    /// Тело не высветляется: светятся только руны и глаза по маске, k — только их яркость.
    /// </summary>
    public static class ThicketMasterPhaseRules
    {
        /// <summary>Биты порогов, которые меняют внешность (вступительный рёв — нет).</summary>
        public const int PhaseBits = Simulation.ThicketRoar66Bit | Simulation.ThicketRoar50Bit | Simulation.ThicketRoar33Bit;

        /// <summary>Яркость рун по уровням (сон и Ф1 — 0: руны тёмные).</summary>
        public const float Phase2Glow = 1.6f, EnragedGlow = 2.0f, Phase3Glow = 2.4f;

        /// <summary>Вспышка на рёве: k × FlashGain с тика удара FlashTicks тиков, потом к цели за SettleTicks.</summary>
        public const float FlashGain = 1.45f;
        public const int FlashTicks = 4, SettleTicks = 12;

        /// <summary>Дыхание рун в Ф2–Ф3: ±BreathDepth с периодом BreathPeriodTicks тиков Sim.</summary>
        public const float BreathDepth = .08f;
        public const int BreathPeriodTicks = 60;

        /// <summary>После смерти руны гаснут за столько тиков (1,5 с), пока тело валится.</summary>
        public const int DeathFadeTicks = 45;

        /// <summary>Глаза проявляются за столько тиков с удара рёва 66.</summary>
        public const int EyeAppearTicks = 6;

        /// <summary>Нет тика: смена была до привязки вида (съёмка с середины боя) — без вспышки и роста.</summary>
        public const int None = int.MinValue;

        /// <summary>
        /// Биты порогов, чья смена уже видна в тик tick (тик Sim с долей кадра): RoarsDone
        /// без битов рёва, который ещё не ударил.
        /// </summary>
        public static int ShownBits(in ThicketMasterMemory m, bool acting, in ThicketMasterState a, float tick)
        {
            int bits = m.RoarsDone & PhaseBits;
            if (acting && a.Action == ThicketMasterAction.Roar && tick < a.ImpactTick) bits &= ~a.Tag;
            return bits;
        }

        /// <summary>Уровень одежды: 3 — прозвучал рёв 33, 2 — рёв 66, иначе 1.</summary>
        public static int Level(int bits)
        {
            if ((bits & Simulation.ThicketRoar33Bit) != 0) return 3;
            return (bits & Simulation.ThicketRoar66Bit) != 0 ? 2 : 1;
        }

        /// <summary>Ярость (рёв 50): руны ярче внутри уровня 2.</summary>
        public static bool Enraged(int bits) => (bits & Simulation.ThicketRoar50Bit) != 0;

        /// <summary>
        /// Тик смены, если она идёт сейчас: рёв порога уже ударил (tick ≥ ImpactTick) —
        /// его ImpactTick; иначе <see cref="None"/> (смена давно прошла или её нет).
        /// </summary>
        public static int ChangeTick(bool acting, in ThicketMasterState a, float tick)
            => acting && a.Action == ThicketMasterAction.Roar && (a.Tag & PhaseBits) != 0 && tick >= a.ImpactTick
                ? a.ImpactTick : None;

        /// <summary>Яркость рун без вспышки и дыхания.</summary>
        public static float TargetGlow(int level, bool enraged)
        {
            if (level >= 3) return Phase3Glow;
            if (level == 2) return enraged ? EnragedGlow : Phase2Glow;
            return 0f;
        }

        /// <summary>
        /// Множитель _EmissionColor: цель уровня, вспышка на смене changeTick (None — без
        /// вспышки), дыхание от тика Sim, после смерти deathTick (None — жив) — к нулю.
        /// </summary>
        public static float Glow(int level, bool enraged, float tick, int changeTick, int deathTick)
        {
            float target = TargetGlow(level, enraged);
            if (target <= 0f) return 0f;
            float k = target;
            if (changeTick != None)
            {
                float t = tick - changeTick;
                if (t >= 0f && t <= FlashTicks) k = target * FlashGain;
                else if (t > FlashTicks && t < FlashTicks + SettleTicks)
                {
                    float rest = 1f - (t - FlashTicks) / SettleTicks;
                    k = target * (1f + (FlashGain - 1f) * rest * rest);
                }
            }
            k *= 1f + BreathDepth * (float)Math.Sin(2.0 * Math.PI * tick / BreathPeriodTicks);
            if (deathTick != None) k *= 1f - Clamp01((tick - deathTick) / DeathFadeTicks);
            return k;
        }

        /// <summary>F3-карта (руны + прожилки + глаза) — с уровня 3; до него F2.</summary>
        public static bool UsesPhase3Map(int level) => level >= 3;

        /// <summary>
        /// Доля роста накладки 0…1: с тика смены changeTick, задержка delay и длина
        /// seconds — в секундах. None — смена была до привязки: накладка сразу целиком.
        /// </summary>
        public static float Bloom(float tick, int changeTick, float delay, float seconds)
        {
            if (changeTick == None) return 1f;
            float age = (tick - changeTick) / Simulation.TicksPerSecond - delay;
            return seconds <= 0f ? (age >= 0f ? 1f : 0f) : Clamp01(age / seconds);
        }

        /// <summary>Набухание ягоды по доле x 0…1: быстрый рост до 1,15 к 0,7, потом оседает к 1.</summary>
        public static float Swell(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            const float peak = 1.15f, at = .7f;
            if (x < at)
            {
                float u = 1f - x / at;
                return peak * (1f - u * u * u);
            }
            float s = (x - at) / (1f - at);
            s = s * s * (3f - 2f * s);
            return peak + (1f - peak) * s;
        }

        /// <summary>Глаза: 0…1 за EyeAppearTicks с удара рёва 66 (None — уже светятся).</summary>
        public static float EyeAppear(float tick, int changeTick)
            => changeTick == None ? 1f : Clamp01((tick - changeTick) / EyeAppearTicks);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
