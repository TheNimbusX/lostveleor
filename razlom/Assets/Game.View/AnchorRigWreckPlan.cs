using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// Снимок Крушения глазами рига (зеркало полей WreckState, без типов Sim): заполняет PelagAnchorRig.WreckSnapshot.cs.
    /// Фазы — числа WreckPhase (только дописываются).
    /// </summary>
    public struct AnchorRigWreckInput
    {
        public const byte PhaseNone = 0, PhaseWindup = 1, PhaseFollow = 2, PhaseWindow = 3, PhaseCharge = 4, PhaseHold = 5, PhaseExit = 6;

        public int Serial;
        public byte Phase;
        public int Stage, Side;
        public int StageStartTick, ContactTick, OverheadTick;
        public int ChargeStartTick, Charge;
        public int WindowEndTick, HoldEndTick, ExitEndTick;
        /// <summary>Направление этапа (x, z мира), единичное.</summary>
        public Vector2 Direction;
        /// <summary>Точка удара оземь (x, z мира); HasImpact — этап с точкой (выпад, «Четвёртый удар»).</summary>
        public Vector2 ImpactPoint;
        public bool HasImpact;
    }

    /// <summary>
    /// Крушение v4 (06.10) на риге якоря — чистые правила, проверяются тестами (AnchorRigWreckPlanTests).
    /// Голова идёт по запечке ТОГО ЖЕ клипа и кадра, что играет тело (CharacterAnimatorView.Wreck2 → TryGetWreck4Pose):
    /// Draw, Swing1, Swing2, Lunge, Stow — Resources/Weapons/Pelag/AnchorBakes/Pelag_AN_Wreck4_*.anchorbake.json
    /// (artifacts/wreck/v4/integrate/bake: превью wreck4anchor, слой «цепь-хлыст», 120 Гц). Мах без следующего нажатия
    /// с кадра 7 и пауза — живой маятник на короткой цепи (гашение относительно хвата, веретено вдоль цепи, крен не
    /// ведётся). Поверх управляемого поворота — вторичная пружина (AnchorSecondary), цепь — всегда симуляция
    /// (AnchorWhipChain, 16 звеньев). Абордаж и Бросок якоря этих правил не видят (другие режимы рига).
    /// </summary>
    public static class AnchorRigWreckPlan
    {
        public const string Draw = "Pelag_AN_Wreck4_Draw";
        public const string Swing1 = "Pelag_AN_Wreck4_Swing1";
        public const string Swing2 = "Pelag_AN_Wreck4_Swing2";
        public const string Lunge = "Pelag_AN_Wreck4_Lunge";
        public const string Stow = "Pelag_AN_Wreck4_Stow";

        /// <summary>Без запечек этих клипов риг Крушение не берёт (тогда — прежний путь PelagAnchorSlamView).</summary>
        public static readonly string[] RequiredClips = { Draw, Swing1, Swing2, Lunge, Stow };

        /// <summary>Мах без следующего нажатия: с этого кадра голова — живой маятник (кадры 7–9 запечки — уже замах следующего этапа).</summary>
        public const float SwingLiveFrame = 7f;

        /// <summary>Длина троса живого маятника (хват → кольцо), м при росте 1,82: хорда маха 0,45–0,50 (timing.json chain).</summary>
        public const float LiveCable = .48f;

        /// <summary>Цепь-хлыст: длина в махе и в паузе (хорда + слабина), скорость выдачи/выбора, м/с.</summary>
        public const float WhipSwing = .53f, HangSlack = .015f, WhipRate = 40f;

        /// <summary>Подвод выпада к точке Sim: предел, м (полёт свободный — сдвиг виден меньше, чем у маха).</summary>
        public const float ImpactCorrectionLimit = 2.5f;

        /// <summary>Состояние аниматора клипа: «Base Layer.Wreck4_Swing1» — имя клипа без «Pelag_AN_».</summary>
        public static string StateName(string clip)
            => "Base Layer." + (clip != null && clip.StartsWith("Pelag_AN_") ? clip.Substring("Pelag_AN_".Length) : clip);

        /// <summary>Имя файла запечки: клип + вариант замаха (_w7) или фазы выхода (_p3); у v4 — без вариантов.</summary>
        public static string BakeName(string clip, int windupTicks, int releaseVariant = -1)
            => releaseVariant >= 0 ? clip + "_p" + releaseVariant
                : windupTicks > 0 ? clip + "_w" + windupTicks : clip;

        public static bool IsSwing(string clip) => clip == Swing1 || clip == Swing2;

        /// <summary>Голова этого кадра — живой маятник, а не запечка (мах доигран без нажатия; конец запечки).</summary>
        public static bool GoesLive(string clip, float frame, float liveFrom, bool nextStarted)
            => IsSwing(clip) && !nextStarted && frame >= SwingLiveFrame || frame >= liveFrom - 1e-3f && clip != Stow;

        /// <summary>Доля вторичного поворота: снятие — нарастает за 60 % после хвата, уборка — спадает к посадке, иначе 1.</summary>
        public static float SecondaryWeight(string clip, float frame, AnchorBakePhases p)
        {
            if (clip == Draw)
            {
                float grab = p != null && p.grab > 0 ? p.grab : 3.25f;
                return Smooth((frame - grab) / Math.Max(1e-3f, (8f - grab) * .6f));
            }
            if (clip == Stow)
            {
                float land = p != null && p.land > 0 ? p.land : 5.5f;
                return 1f - Smooth(frame / Math.Max(1e-3f, land - 1.5f));
            }
            return 1f;
        }

        /// <summary>Настройка вторичной пружины: полёт выпада (к удару жёстче), укус в воронке, иначе булава.</summary>
        public static AnchorSecondary.Tune SecondaryTune(string clip, float frame, float contactFrame, AnchorBakePhases p)
        {
            if (clip != Lunge || p == null || p.release <= 0) return AnchorSecondary.Mace;
            if (frame >= p.release && frame < contactFrame)
                return AnchorSecondary.Blend(AnchorSecondary.Flight, AnchorSecondary.Bite, Smooth((frame - (contactFrame - 1.8f)) / 1.8f));
            if (frame >= contactFrame && frame < p.hold) return AnchorSecondary.Bite;
            return AnchorSecondary.Mace;
        }

        /// <summary>Рукоять в руках (1) или на спине (0): снятие — за кадр после хвата, уборка — с lay0 до lay1.</summary>
        public static float HandleWeight(string clip, float frame, AnchorBakePhases p)
        {
            if (clip == Draw)
            {
                float grab = p != null && p.grab > 0 ? p.grab : 3.25f;
                return Smooth(frame - grab);
            }
            if (clip == Stow)
            {
                float a = p != null && p.lay0 > 0 ? p.lay0 : 3f, b = p != null && p.lay1 > a ? p.lay1 : a + 1f;
                return 1f - Smooth((frame - a) / (b - a));
            }
            return 1f;
        }

        /// <summary>
        /// Вес подвода выпада к точке Sim: пятистепенно с выпуска до удара (к удару — целиком), держится в воронке,
        /// гаснет за выбор цепи (удержание → натяг), к руке голова идёт без сдвига.
        /// </summary>
        public static float ImpactWeight(float frame, float contactFrame, AnchorBakePhases p)
        {
            if (p == null || p.release <= 0 || contactFrame <= p.release) return 0f;
            float w = Quintic((frame - p.release) / (contactFrame - p.release));
            if (p.hold > 0 && p.@short > p.hold) w *= 1f - Smooth((frame - p.hold) / (p.@short - p.hold));
            return w;
        }

        /// <summary>Пауза: гашение скорости головы относительно хвата, 1/с — 0,8 + 7·e^(−t/0,25) (timing.json hang.drag).</summary>
        public static float HangDrag(float seconds) => .8f + 7f * (float)Math.Exp(-Math.Max(0f, seconds) / .25f);

        /// <summary>Длина цепи-хлыста: к цели не быстрее WhipRate, не короче хорды + 2 мм.</summary>
        public static float WhipLength(float current, float target, float chord, float dt, float scale = 1f)
        {
            float step = WhipRate * scale * Math.Max(0f, dt);
            float next = current <= 0f ? target : current + Math.Clamp(target - current, -step, step);
            return Math.Max(next, chord + .002f * scale);
        }

        public static float Smooth(float x)
        {
            x = Math.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        public static float Quintic(float x)
        {
            x = Math.Clamp(x, 0f, 1f);
            return x * x * x * (10f - 15f * x + 6f * x * x);
        }
    }
}
