using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// Фаза броска по линии для рига — число в число <c>AnchorThrowPhase</c> спеки Броска якоря (§2.6: None 0, Windup 1,
    /// Flight 2, Taut 3, Return 4, Catch 5, Exit 6), чтобы кормушка не переводила одно в другое. Только дописывать.
    /// </summary>
    public enum AnchorLinePhase : byte { None = 0, Windup = 1, Flight = 2, Taut = 3, Return = 4, Catch = 5, Exit = 6 }

    /// <summary>
    /// Снимок Броска якоря глазами рига (поля <c>AnchorThrowState</c> спеки Броска §2.6 и синонимы контракта DESIGN §1.4:
    /// ReachTick = TautTick, YankEndTick = CatchTick, AnchorAt = AnchorThrowHead(s, 0, tick)). Заполняет
    /// PelagAnchorRig.ThrowSnapshot.cs, когда Sim Броска влит; Head* — голова полосы 0 в тиках floor(shown) и +1.
    /// </summary>
    public struct AnchorRigThrowInput
    {
        public int Serial;
        public byte Phase;
        public int CastTick, ReleaseTick, TautTick, CatchTick, PhaseEndTick;
        /// <summary>Рука в выпуск (x, z мира) и направление полосы 0, единичное.</summary>
        public Vector2 Origin, Direction;
        /// <summary>Голова полосы 0 по формуле Sim в тик floor(shown) и в следующий.</summary>
        public Vector2 HeadNow, HeadNext;
        /// <summary>Гарпун вонзился (StopKind0 == 3): голова стоит в теле цели до рывка.</summary>
        public bool Stuck;
    }

    /// <summary>Кадр броска в показанный тик: фаза, расстояние головы вдоль линии и его скорость (м, м/с).</summary>
    public struct AnchorLineState
    {
        public AnchorLinePhase Phase;
        public float Along, AlongSpeed;
        /// <summary>Тик натяга пройден: дёрг цепи (отскок 0,1–0,2 м), не замирание.</summary>
        public bool Reached;
        /// <summary>Кадр замаха (кадр = тик от каста) для запечки Throw_Windup, если она есть.</summary>
        public float WindupFrame;
    }

    /// <summary>Решение рига по снимку Броска (DESIGN §1.4, §7.3) — чистая функция, проверяется тестами.</summary>
    public static class AnchorRigThrowPlan
    {
        public const float TicksPerSecond = 30f;

        /// <summary>
        /// Фаза в показанный тик (вид отстаёт от Sim на 2 тика, поэтому фаза — по тикам снимка, а не по его Phase).
        /// Sim уже без броска (Phase None) до ловли — срыв: голова сразу в ловлю живой физикой (цепь её ловит).
        /// </summary>
        public static AnchorLinePhase PhaseAt(in AnchorRigThrowInput s, float shown)
        {
            if (s.Serial <= 0) return AnchorLinePhase.None;
            if (shown < s.ReleaseTick) return s.Phase == 0 ? AnchorLinePhase.Catch : AnchorLinePhase.Windup;
            if (shown < s.TautTick) return s.Phase == 0 ? AnchorLinePhase.Catch : AnchorLinePhase.Flight;
            if (shown < s.TautTick + 1) return AnchorLinePhase.Taut;
            if (shown < s.CatchTick) return s.Phase == 0 ? AnchorLinePhase.Catch : AnchorLinePhase.Return;
            if (s.PhaseEndTick > s.CatchTick && shown < s.PhaseEndTick) return AnchorLinePhase.Catch;
            return AnchorLinePhase.Exit;
        }

        /// <summary>false — показывать нечего (броска нет). Along — проекция головы на линию от Origin, не своя кривая.</summary>
        public static bool Frame(in AnchorRigThrowInput s, float shown, out AnchorLineState state)
        {
            state = default;
            state.Phase = PhaseAt(s, shown);
            if (state.Phase == AnchorLinePhase.None) return false;
            Vector2 dir = s.Direction.LengthSquared() > 1e-8f ? Vector2.Normalize(s.Direction) : Vector2.UnitY;
            float u = shown - (float)Math.Floor(shown);
            Vector2 head = Vector2.Lerp(s.HeadNow, s.HeadNext, u);
            state.Along = Math.Max(0f, Vector2.Dot(head - s.Origin, dir));
            state.AlongSpeed = Vector2.Dot(s.HeadNext - s.HeadNow, dir) * TicksPerSecond;
            state.Reached = shown >= s.TautTick && s.TautTick > s.ReleaseTick;
            state.WindupFrame = Math.Max(0f, shown - s.CastTick);
            return true;
        }
    }
}
