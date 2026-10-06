using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// Снимок Крушения глазами рига (зеркало полей WreckState из SPEC 2.6, без типов Sim):
    /// заполняет PelagAnchorRig.WreckSnapshot.cs. Фазы — числа WreckPhase (только дописываются).
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
        /// <summary>Точка удара оземь (x, z мира); HasImpact — этап с точкой (2, 3).</summary>
        public Vector2 ImpactPoint;
        public bool HasImpact;
    }

    public enum AnchorWreckBeatKind : byte
    {
        /// <summary>Серии нет — риг не владеет Крушением.</summary>
        None = 0,
        /// <summary>Запечённый удар этапа (Swing1, Swing2, Slam, ChargeRelease).</summary>
        Bake = 1,
        /// <summary>Цикл Девятого вала над головой (ChargeLoop).</summary>
        Loop = 2,
        /// <summary>Живой маятник (этап без запечки: «Четвёртый удар», сторона не сходится).</summary>
        Live = 3,
        /// <summary>Серия кончилась: живая физика и уборка на спину.</summary>
        Stow = 4,
    }

    /// <summary>Что риг делает в этом кадре Крушения.</summary>
    public struct AnchorWreckBeat
    {
        public AnchorWreckBeatKind Kind;
        /// <summary>Имя клипа без варианта: Pelag_AN_Wreck2_Swing1 и т. д.</summary>
        public string Clip;
        /// <summary>Тиков замаха этого этапа (вариант запечки _w&lt;N&gt;); 0 — не важно.</summary>
        public int WindupTicks;
        public int StartTick, ContactTick, OverheadTick;
        /// <summary>Сшивка начинается заново при смене сегмента (серия × этап × вид).</summary>
        public int Segment;
        /// <summary>Подвести точку контакта к точке Sim (только удары оземь, DESIGN §2.3).</summary>
        public bool CorrectContact;
        /// <summary>Сторона маха не сошлась с запечкой: в лог, голова живая (запечку не зеркалим).</summary>
        public bool SideMismatch;
    }

    /// <summary>Решение рига по снимку Крушения (DESIGN §1.4) — чистая функция, проверяется тестами.</summary>
    public static class AnchorRigWreckPlan
    {
        public const string Swing1 = "Pelag_AN_Wreck2_Swing1";
        public const string Swing2 = "Pelag_AN_Wreck2_Swing2";
        public const string Slam = "Pelag_AN_Wreck2_Slam";
        public const string ChargeLoop = "Pelag_AN_Wreck2_ChargeLoop";
        public const string ChargeRelease = "Pelag_AN_Wreck2_ChargeRelease";

        /// <summary>Девятый вал: оборот в начале и в конце заряда, с (вопрос 3 владельцу: без ответа — 0,30).</summary>
        public const float ChargePeriodStart = .40f, ChargePeriodEnd = .30f;
        public const int ChargeMaxTicks = 30;
        /// <summary>Вариантов выхода из цикла по фазе (через 45°).</summary>
        public const int ReleaseVariants = 8;

        /// <summary>Клипы базовой серии, без запечек которых риг Крушение не берёт (иначе — прежний путь).</summary>
        public static readonly string[] RequiredClips = { Swing1, Swing2, Slam };

        public static AnchorWreckBeat Decide(in AnchorRigWreckInput s)
        {
            var beat = new AnchorWreckBeat { Kind = AnchorWreckBeatKind.None, StartTick = s.StageStartTick, ContactTick = s.ContactTick, OverheadTick = s.OverheadTick };
            if (s.Serial <= 0) return beat;
            if (s.Phase == AnchorRigWreckInput.PhaseNone) { beat.Kind = AnchorWreckBeatKind.Stow; return beat; }
            beat.Segment = s.Serial * 16 + s.Stage * 2;
            if (s.Phase == AnchorRigWreckInput.PhaseCharge)
            {
                beat.Kind = AnchorWreckBeatKind.Loop;
                beat.Clip = ChargeLoop;
                beat.Segment = s.Serial * 16 + 9;
                return beat;
            }
            switch (s.Stage)
            {
                case 0:
                case 1:
                    beat.Clip = s.Stage == 0 ? Swing1 : Swing2;
                    int expected = s.Stage == 0 ? 1 : -1;
                    if (s.Side != 0 && s.Side != expected) { beat.Kind = AnchorWreckBeatKind.Live; beat.SideMismatch = true; return beat; }
                    beat.Kind = AnchorWreckBeatKind.Bake;
                    beat.WindupTicks = Math.Max(0, s.ContactTick - s.StageStartTick);
                    return beat;
                case 2:
                    beat.Kind = AnchorWreckBeatKind.Bake;
                    beat.CorrectContact = s.HasImpact;
                    if (s.ChargeStartTick >= 0)
                    {
                        // Выход из цикла: вариант по фазе выбирает слой Unity (ReleaseVariant), сегмент свой.
                        beat.Clip = ChargeRelease;
                        beat.Segment = s.Serial * 16 + 11;
                        beat.OverheadTick = -1;
                        return beat;
                    }
                    beat.Clip = Slam;
                    beat.WindupTicks = Math.Max(0, s.ContactTick - s.StageStartTick);
                    return beat;
                default:
                    // «Четвёртый удар» таланта — своя запечка позже; пока живой маятник от руки.
                    beat.Kind = AnchorWreckBeatKind.Live;
                    return beat;
            }
        }

        /// <summary>Кадр цикла Девятого вала в показанный тик и его темп (кадров на тик).</summary>
        public static float ChargeLoopFrame(in AnchorRigWreckInput s, float shown, float loopFrames, out float rate)
            => AnchorBakeClock.LoopFrame(shown, s.ChargeStartTick, loopFrames, ChargePeriodStart, ChargePeriodEnd, ChargeMaxTicks, out rate);

        /// <summary>
        /// Выход из цикла: какой из 8 вариантов ChargeRelease и с какого тика он начинается.
        /// Тик отпуска = тик удара − кадр контакта запечки выхода (кадр = тик); фаза — по циклу в этот тик.
        /// </summary>
        public static int ReleaseVariant(in AnchorRigWreckInput s, float releaseContactFrame, float loopFrames, out float startTick)
        {
            float release = s.ContactTick - releaseContactFrame;
            float frame = AnchorBakeClock.LoopFrame(release, s.ChargeStartTick, loopFrames, ChargePeriodStart, ChargePeriodEnd, ChargeMaxTicks, out float rate);
            int variant = AnchorBakeClock.ReleaseVariant(frame, loopFrames, ReleaseVariants, out float offset);
            startTick = release - offset / Math.Max(1e-3f, rate);
            return variant;
        }

        /// <summary>
        /// Состояние аниматора (слой 0) клипа серии: «Base Layer.Wreck2_Swing1» и т. д. — имя клипа без «Pelag_AN_».
        /// Риг берёт Крушение, только если контроллер их содержит и Tempo входит именно в них (иначе голова шла бы по
        /// запечке одного клипа, а рука — по другому). Сборщику клипов Wreck2_* — называть состояния так.
        /// </summary>
        public static string StateName(string clip)
            => "Base Layer." + (clip != null && clip.StartsWith("Pelag_AN_") ? clip.Substring("Pelag_AN_".Length) : clip);

        /// <summary>Имя файла запечки: клип + вариант замаха (_w7) или фазы выхода (_p3).</summary>
        public static string BakeName(string clip, int windupTicks, int releaseVariant = -1)
            => releaseVariant >= 0 ? clip + "_p" + releaseVariant
                : windupTicks > 0 ? clip + "_w" + windupTicks : clip;
    }
}
