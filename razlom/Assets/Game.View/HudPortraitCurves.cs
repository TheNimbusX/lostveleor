using System;
using System.Globalization;

namespace Game.View
{
    /// <summary>
    /// Кривые живого портрета боевого HUD и подписи полос героя без UnityEngine (владелец 29.09:
    /// «рисованный + реакции»; цифры HP и лавидия всегда видны, опыт — подписью рядом). Отдельно от
    /// HudPortraitMotion и CombatHudView, чтобы их проверяли тесты вне Unity
    /// (tools/Combat.Presentation.Tests/HudPortraitCurvesTests.cs). Время — в секундах интерфейса.
    /// </summary>
    public static class HudPortraitCurves
    {
        const float Tau = (float)(Math.PI * 2.0);

        /// <summary>Вдох 0..1 в фазе <paramref name="phase"/> (обороты): 0 — выдох, 0,5 — вдох, мягкий косинус.</summary>
        public static float Breath(float phase) => .5f - .5f * (float)Math.Cos(phase * Tau);

        /// <summary>
        /// Вдох «внимания» в такт HudPulse: та же формула, что у пульса красной дымки
        /// (0,5 + 0,5·sin(t·2π/период)), — портрет и дымка дышат вместе.
        /// </summary>
        public static float PulseBreath(float now, float period) =>
            .5f + .5f * (float)Math.Sin(now * Tau / Math.Max(.1f, period));

        /// <summary>Масштаб рисунка при вдохе <paramref name="breath"/> и глубине <paramref name="depth"/> (0,01 — 1 %).</summary>
        public static float BreathScale(float breath, float depth) => 1f + Math.Max(0f, depth) * Clamp01(breath);

        /// <summary>
        /// Окно выборки портрета при масштабе <paramref name="scale"/> ≥ 1 (uvRect: x, y, ширина, высота):
        /// низ и середина по x стоят, рисунок растёт вверх и в стороны — грудь поднимается. Окно не
        /// выходит за 0..1: у краёв выреза не тянутся крайние пиксели.
        /// </summary>
        public static void BreathWindow(float scale, out float x, out float y, out float width, out float height)
        {
            float size = 1f / Math.Max(1f, scale);
            x = .5f - .5f * size;
            y = 0f;
            width = height = size;
        }

        /// <summary>
        /// Толчок от удара: сдвиг в долях амплитуды через <paramref name="age"/> секунд — сразу
        /// полный, дальше затухающая волна; к концу <paramref name="duration"/> ровно ноль.
        /// </summary>
        public static float Jolt(float age, float duration)
        {
            if (!(age >= 0f) || !(duration > 0f) || age >= duration) return 0f;
            float k = age / duration;
            return (1f - k) * (1f - k) * (float)Math.Cos(k * Tau * 1.5f);
        }

        /// <summary>
        /// Красный тон удара 0..1: полный <paramref name="hold"/> секунд (2–3 кадра), дальше гаснет за
        /// <paramref name="fade"/>. После — ноль: портрет не остаётся ни красным, ни светлее.
        /// </summary>
        public static float HitTint(float age, float hold, float fade)
        {
            if (!(age >= 0f)) return 0f;
            if (age <= hold) return 1f;
            if (!(fade > 0f)) return 0f;
            return Clamp01(1f - (age - hold) / fade);
        }

        /// <summary>
        /// Тёплый свет лечения и уровня 0..1: быстро разгорается за <paramref name="rise"/> и мягко
        /// гаснет к концу <paramref name="duration"/>.
        /// </summary>
        public static float Warmth(float age, float rise, float duration)
        {
            if (!(age >= 0f) || !(duration > 0f) || age >= duration) return 0f;
            if (rise > 0f && age < rise) return age / rise;
            float k = (age - Math.Max(0f, rise)) / Math.Max(1e-4f, duration - Math.Max(0f, rise));
            float left = 1f - Clamp01(k);
            return left * left;
        }

        /// <summary>Свет лечения по вылеченной доле здоровья: даже малое лечение заметно, крупное — ярче.</summary>
        public static float HealStrength(float healedShare) => Clamp01(.45f + Clamp01(healedShare) * 1.1f);

        /// <summary>Подпись рядом с полосой опыта: «Ур. 4 · 120 / 300».</summary>
        public static string ExperienceLabel(int level, int xp, int next)
        {
            if (level < 1) level = 1;
            if (next < 1) next = 1;
            if (xp < 0) xp = 0;
            else if (xp > next) xp = next;
            return "Ур. " + level.ToString(CultureInfo.InvariantCulture) + " · " + xp.ToString(CultureInfo.InvariantCulture)
                   + " / " + next.ToString(CultureInfo.InvariantCulture);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
