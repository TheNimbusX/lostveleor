using System;

namespace Game.View
{
    /// <summary>Клипы Крушения (03.10; v4 06.10 — Draw, Lunge). Значения — только дописывать: идут в журнал и тесты.</summary>
    public enum PelagWreckClip : byte
    {
        None = 0,
        Swing1 = 1,
        Swing2 = 2,
        Slam = 3,
        Wait1 = 4,
        Wait2 = 5,
        Stow = 6,
        ChargeLoop = 7,
        ChargeRelease = 8,
        Swing1Braced = 9,
        Swing2Braced = 10,
        SlamDrag = 11,
        Draw = 12,
        Lunge = 13,
    }

    /// <summary>Слой аниматора клипа: у v4 всё тело на базовом слое.</summary>
    public enum PelagWreckLayer : byte { Base = 0, Upper = 1, Lower = 2 }

    /// <summary>
    /// Чистые правила клипов Крушения v4 — без Unity, проверяются тестами представления
    /// (tools/Combat.Presentation.Tests/WreckClipRulesTests.cs). Источник чисел — ART/characters/pelag/wreck-2026-10-03/
    /// animation-v4/timing.json (06.10, принято владельцем): серия как у сабли — мах влево, мах вправо, выпад; тело —
    /// принятые Pelag_AN_Sabre1–3, обе кисти на рукояти якоря. Кадр клипа = тик Sim при самом быстром темпе (30 к/с).
    /// Корня в клипах нет: таз по XZ стоит, корень ведёт Sim (у выпада — шаг 0,6 м, Simulation.Wreck.Lunge).
    ///
    /// * Draw [0..8] — рукоять со спины (хват 3,25), кадр 8 = Swing1@0. Sim времени на снятие не даёт (первый мах — 5
    ///   тиков, как у сабли): вид сжимает Draw с DrawEntryFrame и замах маха 1 в замах этапа 0 (DrawShare).
    /// * Swing1 / Swing2 [0..15] — контакт 5, стык 9 (= следующий@0), хвост 9–15 — стойка с якорем.
    /// * Lunge [0..24] — выпуск 4,5, удар 8, стык 16 (= Stow@0), хвост 16–24 = тело Stow.
    /// * Stow [0..8] — рукоять на спину (3–4), голова на креплении 5,5.
    /// Следующий этап раньше стыка (нажатие из буфера — удар + 1): хвост прошлого и замах нового играются одним
    /// равномерным ходом за замах (без смешивания поз — «догон»); позже стыка — смешивание SeamBlendTicks.
    /// </summary>
    public static class PelagWreckClipRules
    {
        public const string BindReference = "Pelag_AN_Wreck4Bind";
        public const string ClipPrefix = "Pelag_AN_Wreck4_";
        public const string StatePrefix = "Wreck4_";

        /// <summary>Предел таза сборки (timing.json clips: Build("Pelag_AN_Wreck4_*", false, "Pelag_AN_Wreck4Bind", .5f)).</summary>
        public const float HipLimit = .5f;

        // ---- кадры (timing.json proposal.*) ----
        public const int DrawLast = 8;
        public const float DrawGrab = 3.25f;
        public const int SwingContact = 5, SwingSeam = 9, SwingLast = 15;
        public const int LungeContact = 8, LungeSeam = 16, LungeLast = 24;
        public const float LungeRelease = 4.5f;
        public const int StowLast = 8;
        /// <summary>Stow: рукоять на креплении (timing.json Stow.on_back_tick 3,5).</summary>
        public const float StowOnBackFrame = 3.5f;
        /// <summary>Девятый вал: в заряде тело держит кадр перед выпуском (Lunge@4).</summary>
        public const float ChargeHoldFrame = 4f;

        /// <summary>Снятие в замахе маха 1 начинается с этого кадра Draw (кадры до него — кисть идёт к рукояти, смешивание).</summary>
        public const float DrawEntryFrame = 1f;

        /// <summary>Доля замаха этапа 0, отданная снятию: кадры Draw (с DrawEntryFrame) и маха 1 идут одним ходом.</summary>
        public static float DrawShare => (DrawLast - DrawEntryFrame) / (DrawLast - DrawEntryFrame + SwingContact);

        /// <summary>Смешивание в клип из несовпадающей позы (нажатие после стыка, уборка из маха), тиков.</summary>
        public const float SeamBlendTicks = 2f;
        /// <summary>Вход в серию из стойки или бега (в Draw), тиков.</summary>
        public const float CastBlendTicks = 1f;
        /// <summary>Поворот корня к направлению этапа, тиков (превью v4: разворот за 3 кадра в начале замаха).</summary>
        public const float TurnTicks = 3f;

        // ---- стопы (низ стопы ≤ 8 мм по rows.json клипов v4: [0] — левая, [1] — правая) ----
        private static readonly int[][] DrawPlanted = { Range(0, 8), Range(0, 8) };
        private static readonly int[][] Swing1Planted = { Join(Range(0, 1), Range(4, 15)), Join(Range(0, 4), Range(7, 15)) };
        private static readonly int[][] Swing2Planted = { Join(Range(0, 6), Range(9, 15)), Join(Range(0, 1), Range(4, 15)) };
        private static readonly int[][] LungePlanted = { Join(Range(0, 1), Range(3, 7), new[] { 17 }, Range(22, 24)), Join(Range(0, 3), Range(8, 15), Range(19, 24)) };
        private static readonly int[][] StowPlanted = { Join(new[] { 1 }, Range(6, 8)), Range(3, 8) };

        public static readonly PelagWreckClip[] Required =
        {
            PelagWreckClip.Draw, PelagWreckClip.Swing1, PelagWreckClip.Swing2, PelagWreckClip.Lunge, PelagWreckClip.Stow,
        };

        public static string Suffix(PelagWreckClip clip) => clip switch
        {
            PelagWreckClip.Draw => "Draw",
            PelagWreckClip.Swing1 => "Swing1",
            PelagWreckClip.Swing2 => "Swing2",
            PelagWreckClip.Lunge => "Lunge",
            PelagWreckClip.Stow => "Stow",
            _ => "",
        };

        /// <summary>Имя FBX и клипа (.anim): Assets/Resources/Characters/Pelag_v5/Mixamo/{имя}.fbx. Совпадает с AnchorRigWreckPlan.</summary>
        public static string ClipName(PelagWreckClip clip) => ClipPrefix + Suffix(clip);

        public static PelagWreckLayer LayerOf(PelagWreckClip clip) => PelagWreckLayer.Base;

        /// <summary>Имя состояния базового слоя: «Wreck4_Swing1» — ровно AnchorRigWreckPlan.StateName без слоя.</summary>
        public static string StateName(PelagWreckClip clip) => StatePrefix + Suffix(clip);

        public static string StatePath(PelagWreckClip clip) => "Base Layer." + StateName(clip);

        /// <summary>Своё время у каждого клипа: при смешивании уходящий не двигается чужим параметром.</summary>
        public static string PhaseParameter(PelagWreckClip clip) => "Wreck4Phase" + Suffix(clip);

        public static bool Loops(PelagWreckClip clip) => false;

        /// <summary>Последний кадр клипа (кадров на один больше).</summary>
        public static int LastFrame(PelagWreckClip clip) => clip switch
        {
            PelagWreckClip.Draw => DrawLast,
            PelagWreckClip.Swing1 or PelagWreckClip.Swing2 => SwingLast,
            PelagWreckClip.Lunge => LungeLast,
            PelagWreckClip.Stow => StowLast,
            _ => 1,
        };

        /// <summary>Кадр стыка: здесь клип переходит в следующий без смешивания (Draw 8, махи 9, выпад 16 — в Stow).</summary>
        public static int SeamFrame(PelagWreckClip clip) => clip switch
        {
            PelagWreckClip.Draw => DrawLast,
            PelagWreckClip.Swing1 or PelagWreckClip.Swing2 => SwingSeam,
            PelagWreckClip.Lunge => LungeSeam,
            _ => LastFrame(clip),
        };

        /// <summary>Нормированное время состояния (параметр Wreck4Phase*).</summary>
        public static float Phase(PelagWreckClip clip, float frame) => Clamp(frame / LastFrame(clip), 0f, 1f);

        public static int ContactFrame(PelagWreckClip clip) => clip switch
        {
            PelagWreckClip.Swing1 or PelagWreckClip.Swing2 => SwingContact,
            PelagWreckClip.Lunge => LungeContact,
            _ => 0,
        };

        /// <summary>Клип этапа: 0 — мах влево, 1 — мах вправо, 2 — выпад, 3 («Четвёртый удар», своего клипа нет) — выпад.</summary>
        public static PelagWreckClip StageClip(int stage) => stage == 0 ? PelagWreckClip.Swing1 : stage == 1 ? PelagWreckClip.Swing2 : PelagWreckClip.Lunge;

        /// <summary>После контакта кадр идёт 1:1 (проводка, удержание, выход — тики Sim без темпа).</summary>
        public static float AfterContactFrame(PelagWreckClip clip, float sinceContact)
            => Clamp(ContactFrame(clip) + Math.Max(0f, sinceContact), 0f, LastFrame(clip));

        /// <summary>Кадр замаха: [0 … контакт] за windupTicks тиков (иной темп — равномерная растяжка).</summary>
        public static float WindupFrame(PelagWreckClip clip, int windupTicks, float k)
            => ContactFrame(clip) * Clamp(k / Math.Max(1, windupTicks), 0f, 1f);

        public static float StowFrame(float sinceStow) => Clamp(sinceStow, 0f, StowLast);

        /// <summary>Кадры, где стопа стоит (side 0 — левая, 1 — правая): по ним сборка сажает клипы на землю.</summary>
        public static int[] PlantedFrames(PelagWreckClip clip, int side)
        {
            int[][] p = clip switch
            {
                PelagWreckClip.Draw => DrawPlanted,
                PelagWreckClip.Swing1 => Swing1Planted,
                PelagWreckClip.Swing2 => Swing2Planted,
                PelagWreckClip.Lunge => LungePlanted,
                PelagWreckClip.Stow => StowPlanted,
                _ => null,
            };
            return p != null ? p[side & 1] : new int[0];
        }

        private static int[] Range(int from, int to)
        {
            var r = new int[to - from + 1];
            for (int i = 0; i < r.Length; i++) r[i] = from + i;
            return r;
        }

        private static int[] Join(params int[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var r = new int[n];
            int k = 0;
            foreach (var p in parts) { Array.Copy(p, 0, r, k, p.Length); k += p.Length; }
            return r;
        }

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
