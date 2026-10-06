using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Крушение v4 на РИСОВАННЫХ текстурах (06.10, лист ART/characters/pelag/wreck-look-2026-10-06/vfx-textures/
    /// sheet-base-v1.png; нарезка artifacts/wreck/v4/vfx-painted/tools/cut_painted.py). Чистые числа вида без Unity:
    /// цвет формы (множитель только синих мест рисунка), раскладка полумесяца маха и полосы выпада. Префабы —
    /// Editor/PelagWreckPaintedVfxSetup, рождение — PelagVfxController.WreckPainted*.
    /// </summary>
    public static class PelagWreckPaintedLook
    {
        // ---------------------------------------------------------------- цвет формы

        /// <summary>Синий рисунка = база (#4FA8FF): у базы множитель 1 — свои цвета текстуры.</summary>
        public const int BaseHex = 0x4FA8FF;

        /// <summary>Тон формы для синих мест: Волнорез #1FB37E, Девятый вал #4B3FD0, Призрачный якорь — бледно-фиолетовый белый.</summary>
        public static int TintHex(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WreckBreakwater: return 0x1FB37E;
                case PelagForm.WreckNinthWave: return 0x4B3FD0;
                case PelagForm.WreckGhostAnchor: return 0xD4CCFF;
                default: return BaseHex;
            }
        }

        /// <summary>Осветление синих мест к жемчугу (Призрачный якорь — призрак, не тёмный металл).</summary>
        public static float TintWhite(PelagForm form) => form == PelagForm.WreckGhostAnchor ? .45f : 0f;

        /// <summary>Множитель синих мест рисунка: тон формы / синий базы по каналам (база — ровно 1, 1, 1).</summary>
        public static void TintMul(PelagForm form, out float r, out float g, out float b)
        {
            int tint = TintHex(form);
            PelagWreckSwingLook.Rgb(tint, out r, out g, out b);
            PelagWreckSwingLook.Rgb(BaseHex, out float br, out float bg, out float bb);
            r /= br; g /= bg; b /= bb;
        }

        // ---------------------------------------------------------------- мах

        /// <summary>
        /// Полумесяц маха (индекс 0 — мах 1, 1 — мах 2 и «Четвёртый»): наружный радиус, м (досягаемость цепи — 1,2–1,5 м
        /// и чуть больше у маха 2), центр впереди героя, м, высота центра — пояс, м.
        /// </summary>
        public static readonly float[] SwingRadius = { 1.55f, 1.70f };
        public static readonly float[] SwingAhead = { .45f, .50f };
        public static readonly float[] SwingHeight = { .88f, .92f };

        /// <summary>
        /// Дуга от хвоста (откуда шёл якорь, за спиной) до головы (чуть за якорем): хвост — на <see cref="SwingTailBack"/>
        /// от направления удара назад, голова — на <see cref="SwingHeadPast"/> вперёд по ходу, град.
        /// </summary>
        public const float SwingTailBack = 150f, SwingHeadPast = 55f;

        /// <summary>
        /// Внутренний радиус полосы (доля наружного): рисунок полосы 2048 × 187 px на дугу ~1780 px — при размахе
        /// <paramref name="spanDegrees"/> толщина без растяжения ≈ 0,105 × размах в радианах; берётся 0,15 (×1,4 поперёк —
        /// полоса на кадрах шире: ~0,5 R у головы).
        /// </summary>
        public static float SwingInner(float spanDegrees)
        {
            float thickness = .15f * spanDegrees * (float)System.Math.PI / 180f;
            return thickness > .6f ? .4f : 1f - thickness;
        }

        // ---------------------------------------------------------------- выпад

        /// <summary>Центр рисованной вспышки на полосе выпада (u текстуры WreckPainted_Lunge).</summary>
        public const float LungeBurstU = .107f;

        /// <summary>Длина / ширина рисунка полосы (1211 × 318 px листа) — квад держит это соотношение.</summary>
        public const float LungeAspect = 3.81f;

        /// <summary>
        /// Квад полосы вдоль Sim: от точки удара (там вспышка рисунка) до конца вала. start/end — вдоль оси от героя, м.
        /// Возвращает начало квада, длину и полуширину (по рисунку, но не уже полосы Sim × 0,7 и не шире её × 1,2).
        /// </summary>
        public static void LungeQuad(float start, float end, float laneHalfWidth, out float from, out float length, out float half)
        {
            float run = end > start + .1f ? end - start : .1f;
            length = run / (1f - LungeBurstU);
            from = start - LungeBurstU * length;
            half = .5f * length / LungeAspect;
            float lo = .7f * laneHalfWidth, hi = 1.2f * laneHalfWidth;
            if (laneHalfWidth > 0f) half = half < lo ? lo : half > hi ? hi : half;
        }

        /// <summary>Фронт Sim (м вдоль оси) → u квада; голова не уходит назад вспышки.</summary>
        public static float LungeFrontU(float front, float from, float length)
        {
            float u = length > 1e-4f ? (front - from) / length : 1f;
            return u < LungeBurstU ? LungeBurstU : u > 1f ? 1f : u;
        }

        /// <summary>Вспышка удара на земле: диаметр рисунка звезды, м, по кругу удара Sim (1,2 м → ~2 м).</summary>
        public static float LungeStarDiameter(float impactRadius) => 1.7f * (impactRadius > .3f ? impactRadius : .3f);

        /// <summary>Доля квада звезды, которую занимает рисунок (1024 холст, звезда ~0,52).</summary>
        public const float StarFill = .52f;

        /// <summary>Раскрытие звезды: ~3 кадра (60 к/с) с перелётом, потом держится.</summary>
        public static float StarPop(float age)
        {
            const float pop = .05f;
            if (age <= 0f) return .45f;
            if (age < pop) { float t = age / pop; return .45f + .65f * t * (2f - t); }
            if (age < pop * 2f) return 1.1f - .1f * (age - pop) / pop;
            return 1f;
        }
    }
}
