using System;

namespace Game.View
{
    /// <summary>Клипы Броска якоря (03.10). Значения — только дописывать: идут в имена состояний и параметров.</summary>
    public enum PelagAnchorThrowClip : byte
    {
        None = 0,
        Throw = 1,
        Fly = 2,
        Yank = 3,
        Haul = 4,
        Catch = 5,
    }

    /// <summary>
    /// Чистые правила показа Броска якоря — без Unity, проверяются тестами представления
    /// (tools/Combat.Presentation.Tests/AnchorThrowClipRulesTests.cs). Источник чисел —
    /// ART/characters/pelag/anchor-throw-2026-10-03/animation/timing.json: кадр = тик при самой
    /// длинной раскладке (W = 2, F = 10, R = 8), 30 к/с, корень клипа стоит (таз по XY постоянен),
    /// Sim героя не двигает, поворот к Dir — вид (вокруг левой лодыжки, как Абордаж v2).
    /// Руки (лист B): сабля за кушаком, рукоять цепи в ЛЕВОМ кулаке, якорь бросает и ловит ПРАВАЯ.
    ///
    /// * Throw [0..5] — замах 0→2 (W = 2, курсор за спиной — 3; выпуск — кадр 2 в тик выпуска), проводка 2→5.
    /// * Fly [0..3] — остаток полёта растянут на F − 2 тиков (flight table), кадр 3 = Yank 0.
    /// * Yank [0..2] — натяг T, рывок T+1, тянет T+2 (1:1); кадр 2 = Haul 0 = Haul 3.
    /// * Haul [0..6] — возврат k = 2..R (haul table; R ≤ 5 входит с кадра 3), кадр 6 = Catch 0 в тик ловли.
    /// * Catch [0..9] — удержание 3 + выход 6, 1:1; кадр 3 — якорь на спину, 5 — можно идти, 9 — стойка серии сабли.
    ///
    /// Между тиками кадр идёт по прямой по ЦЕПОЧКЕ клипов (стыки — одна поза, 0°): тик, который
    /// проходит шов, проходит и все кадры до него — как проверял at_check.py (путь кости за тик ≤ 35°).
    /// Цепочка — одно число c: Throw [0,5], Fly [5,8], Yank [8,10], Haul [10, 16 − e], Catch [16 − e, 25 − e],
    /// где e — кадр входа в Haul (0 или 3 при коротком возврате).
    /// </summary>
    public static class PelagAnchorThrowClipRules
    {
        public const string BindReference = "Pelag_AN_AnchorThrowBind";

        /// <summary>Предел таза сборки: замер Blender 0,387 длины корпуса (timing.json view_requirements), запас как у Абордажа.</summary>
        public const float HipLimit = .45f;

        public const float ReleaseFrame = 2f;
        public const int ThrowLast = 5, FlyLast = 3, YankLast = 2, HaulLast = 6, CatchLast = 9;

        /// <summary>Кадр Catch, где якорь уходит на спину (timing.json keys.stow_handoff): риг кончает маятник и убирает.</summary>
        public const float StowHandoffFrame = 3f;

        /// <summary>Кадр Catch, с которого Sim пускает ходьбу (keys.walk_from = ловля + 5).</summary>
        public const float WalkFromFrame = 5f;

        /// <summary>Удержание (Simulation.AnchorThrowHoldTicks) и выход (AnchorThrowExitTicks) — кадры Catch 1:1.</summary>
        public const int HoldTicks = 3, ExitTicks = 6;

        public static readonly PelagAnchorThrowClip[] Clips =
        {
            PelagAnchorThrowClip.Throw, PelagAnchorThrowClip.Fly, PelagAnchorThrowClip.Yank,
            PelagAnchorThrowClip.Haul, PelagAnchorThrowClip.Catch,
        };

        public static string Suffix(PelagAnchorThrowClip clip) => clip switch
        {
            PelagAnchorThrowClip.Throw => "Throw",
            PelagAnchorThrowClip.Fly => "Fly",
            PelagAnchorThrowClip.Yank => "Yank",
            PelagAnchorThrowClip.Haul => "Haul",
            PelagAnchorThrowClip.Catch => "Catch",
            _ => "",
        };

        /// <summary>Имя FBX и клипа (.anim) — Assets/Resources/Characters/Pelag_v5/Mixamo/{имя}.fbx.</summary>
        public static string ClipName(PelagAnchorThrowClip clip) => "Pelag_AN_AnchorThrow_" + Suffix(clip);

        public static string StateName(PelagAnchorThrowClip clip) => "AnchorThrow_" + Suffix(clip) + "_v5";

        public static string StatePath(PelagAnchorThrowClip clip) => "Base Layer." + StateName(clip);

        /// <summary>Своё время у каждого клипа: при смешивании уходящий не двигается чужим параметром.</summary>
        public static string PhaseParameter(PelagAnchorThrowClip clip) => "AnchorThrowPhase" + Suffix(clip);

        /// <summary>Последний кадр клипа (кадров на один больше).</summary>
        public static int LastFrame(PelagAnchorThrowClip clip) => clip switch
        {
            PelagAnchorThrowClip.Throw => ThrowLast,
            PelagAnchorThrowClip.Fly => FlyLast,
            PelagAnchorThrowClip.Yank => YankLast,
            PelagAnchorThrowClip.Haul => HaulLast,
            PelagAnchorThrowClip.Catch => CatchLast,
            _ => 1,
        };

        /// <summary>Стык без смешивания: конец одного — та же поза, что начало (вход) другого (timing.json seams, 0°).</summary>
        public static bool IsSeam(PelagAnchorThrowClip from, PelagAnchorThrowClip to)
            => (from == PelagAnchorThrowClip.Throw && to == PelagAnchorThrowClip.Fly)
               || (from == PelagAnchorThrowClip.Throw && to == PelagAnchorThrowClip.Yank)   // F ≤ 2: проводка и упор за тик
               || (from == PelagAnchorThrowClip.Fly && to == PelagAnchorThrowClip.Yank)
               || (from == PelagAnchorThrowClip.Yank && to == PelagAnchorThrowClip.Haul)
               || (from == PelagAnchorThrowClip.Haul && to == PelagAnchorThrowClip.Catch);

        // ---- цепочка клипов ----

        public const float FlyStart = 5f, YankStart = 8f, HaulStart = 10f;

        /// <summary>Кадр входа в Haul: R ≥ 6 — 0 (два перехвата), R ≤ 5 — 3 (та же поза, один перехват).</summary>
        public static int HaulEntry(int returnTicks) => returnTicks <= 5 ? 3 : 0;

        public static float CatchStart(int returnTicks) => HaulStart + HaulLast - HaulEntry(returnTicks);

        public static float ChainEnd(int returnTicks) => CatchStart(returnTicks) + CatchLast;

        /// <summary>Клип и кадр точки цепочки c. На шве — следующий клип (кадр 0 = последний кадр прежнего).</summary>
        public static PelagAnchorThrowClip ClipAt(float c, int returnTicks, out float frame)
        {
            if (c < FlyStart) { frame = Math.Max(0f, c); return PelagAnchorThrowClip.Throw; }
            if (c < YankStart) { frame = c - FlyStart; return PelagAnchorThrowClip.Fly; }
            if (c < HaulStart) { frame = c - YankStart; return PelagAnchorThrowClip.Yank; }
            float catchStart = CatchStart(returnTicks);
            if (c < catchStart) { frame = HaulEntry(returnTicks) + (c - HaulStart); return PelagAnchorThrowClip.Haul; }
            frame = Math.Min(CatchLast, c - catchStart);
            return PelagAnchorThrowClip.Catch;
        }

        // ---- ключи по тикам Sim ----

        /// <summary>Замах: тик каста + j (0…W) — Throw 2j/W (timing.json retime.windup: W = 2 — 0, 1, 2; W = 3 — 0, ⅔, 1⅓, 2).</summary>
        public static float WindupChain(int windupTicks, int j)
        {
            int w = Math.Max(1, windupTicks);
            if (j <= 0) return 0f;
            return j >= w ? ReleaseFrame : ReleaseFrame * j / w;
        }

        /// <summary>
        /// Полёт: тик выпуска + u (0…F+1) — точка цепочки (timing.json retime.flight.table).
        /// F ≥ 3: проводка Throw 2..5 1:1 (u ≤ 3), затем Fly растянут на F − 2 тиков до Yank 0 в натяг.
        /// F ≤ 2: Throw 2, 3, затем по прямой до Yank 0 за F тиков (F = 2: Fly 0,5; F = 1: сразу Yank 0).
        /// </summary>
        public static float FlightChain(int flightTicks, int u)
        {
            int f = Math.Max(1, flightTicks);
            if (u <= 0) return ReleaseFrame;
            if (u >= f + 1) return YankStart;
            if (f >= 3)
                return u <= 3 ? ReleaseFrame + u : FlyStart + (u - 3) * (float)FlyLast / (f - 2);
            return u <= 1 ? ReleaseFrame + 1f : ReleaseFrame + 1f + (u - 1) * (YankStart - ReleaseFrame - 1f) / f;
        }

        /// <summary>Возврат: тики натяга + k (2…R) — кадр Haul (timing.json retime.haul.table).</summary>
        public static float HaulKey(int returnTicks, int k)
        {
            int r = Math.Max(4, Math.Min(8, returnTicks));
            float[] table = r switch
            {
                4 => HaulR4,
                5 => HaulR5,
                6 => HaulR6,
                7 => HaulR7,
                _ => HaulR8,
            };
            int i = Math.Max(0, Math.Min(table.Length - 1, k - 2));
            return table[i];
        }

        private static readonly float[] HaulR4 = { 3f, 4.5f, 6f };
        private static readonly float[] HaulR5 = { 3f, 4f, 5f, 6f };
        private static readonly float[] HaulR6 = { 0f, 1.5f, 3f, 4.5f, 6f };
        private static readonly float[] HaulR7 = { 0f, 1f, 2f, 3f, 4.5f, 6f };
        private static readonly float[] HaulR8 = { 0f, 1f, 2f, 3f, 4f, 5f, 6f };

        /// <summary>
        /// Точка цепочки на целом тике n по расписанию Sim (каст, замах, выпуск, F, натяг, R, ловля).
        /// До каста — стойка (0); после ловли + 9 — конец Catch.
        /// </summary>
        public static float KeyChain(int castTick, int windupTicks, int releaseTick, int flightTicks, int tautTick,
            int returnTicks, int catchTick, int n)
        {
            if (n <= castTick) return 0f;
            if (n <= releaseTick) return WindupChain(Math.Max(1, releaseTick - castTick), n - castTick);
            if (n <= tautTick) return FlightChain(flightTicks, n - releaseTick);
            int y = n - tautTick;
            if (n < catchTick && y <= YankLast) return YankStart + y;
            if (n < catchTick) return HaulStart + HaulKey(returnTicks, y) - HaulEntry(returnTicks);
            return CatchStart(returnTicks) + Math.Min(CatchLast, n - catchTick);
        }

        // ---- выход ----

        /// <summary>Скорость, с которой корень возвращается к точке Sim после броска (стопы переступают шагом выхода).</summary>
        public const float ExitGlideSpeed = 3f;
        public const float ExitGlideMin = .10f, ExitGlideMax = .30f;

        /// <summary>
        /// Сдвиг опоры (поворот вокруг левой лодыжки) держится весь бросок — герой стоит. В конце
        /// корень возвращается к точке Sim за столько секунд, а стопы переступают шагом выхода
        /// (BeginSquallExitStep) за то же время: 90° — 0,66 м за 0,22 с, 180° — 0,93 м за 0,30 с.
        /// </summary>
        public static float ExitGlideSeconds(float shiftMeters)
            => Clamp(Math.Abs(shiftMeters) / ExitGlideSpeed, ExitGlideMin, ExitGlideMax);

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
