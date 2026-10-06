using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Клипы Абордажа v2 (02.10) и v3 (03.10). Значения — только дописывать: идут в имена состояний и параметров.</summary>
    public enum PelagAbordageClip : byte
    {
        None = 0,
        Throw = 1,
        Pull = 2,
        Punch = 3,
        Recover = 4,

        /// <summary>v3: короткая тяга P = 4…5 — низкий нырок вдоль короткой цепи (поза 9 листа B2).</summary>
        PullShort = 5,

        /// <summary>v3: Гейзер — апперкот из приседа на прибытии (поза 7 листа B2).</summary>
        Uppercut = 6,

        /// <summary>v3: Обвал — кулак в землю на прибытии (поза 8 листа B2).</summary>
        Slam = 7,
    }

    /// <summary>
    /// Чистые правила показа Абордажа — без Unity, проверяются тестами представления
    /// (tools/Combat.Presentation.Tests/AbordageClipRulesTests.cs). Источник чисел —
    /// ART/characters/pelag/abordage-2026-10-02/animation/timing.json: кадр = тик при самой
    /// длинной раскладке (W = 3, A = 6, P = 12), 30 к/с, корень клипа стоит (таз по XY постоянен),
    /// везёт Sim, поворачивает вид. Лист поз B: сабля за кушаком, рукоять цепи в ЛЕВОМ
    /// кулаке, якорь бросает и бьёт ПРАВАЯ.
    ///
    /// * Throw [0..9] (v3, 03.10) — замах ЧЕРЕЗ ПЛЕЧО 0→3 (3 тика, цель за спиной — 4; кадр 2 —
    ///   кисть над и за правым плечом, выпуск — кадр 3), полёт якоря [3..9] растянут на A = 1…6
    ///   тиков (throw_retime / throw_retime_turn), кадр 9 — натяг в тик зацепа = кадр 0 Pull/PullShort.
    /// * Pull [0..12] — тяга на P = 2…3 и 6…12 тиков (pull_retime): срыв 1 тик, полёт поровну,
    ///   последние три тика — к земле, кулак, КОНТАКТ (кадр 12 в тик удара) = кадр 0 Punch.
    /// * PullShort [0..5] (v3) — тяга на P = 4…5 (pull_short_retime): рывок, НЫРОК (кадр 2, всегда
    ///   на тике), взвод, кулак, контакт; кадры 3, 4, 5 — те же снимки, что 10, 11, 12 Pull.
    /// * Прибытие по форме (timing.json forms): база и Пробоина — Punch [0..3] с тика удара;
    ///   Гейзер — Uppercut, Обвал — Slam [0..4] с тика B+P−1 (кадр 0 = Pull 11 = PullShort 4),
    ///   контакт — кадр 1 в тик удара, кадр 4 = кадр 0 Recover.
    /// * Recover [0..6] — выход 6 тиков, 1:1; кадр 6 — та же стойка серии сабли, что конец рывка и Шквала.
    ///
    /// Стойка и риг — те же, что у Шквала: левая лодыжка, поворот вокруг неё и время
    /// показа берутся из PelagSquallClipRules.
    /// </summary>
    public static class PelagAbordageClipRules
    {
        public const string BindReference = "Pelag_AN_Abordage2Bind";

        /// <summary>Предел таза сборки: клипы дают до 0,433 длины корпуса (timing.json limits_v3.hip_offset).</summary>
        public const float HipLimit = .5f;

        /// <summary>Throw v3: выпуск — кадр 3 (после замаха через плечо), натяг — кадр 9.</summary>
        public const float ReleaseFrame = 3f;
        public const float BiteFrame = 9f;
        public const float SnapFrame = 1f;
        public const float FlightEndFrame = 9f;
        public const float ContactFrame = 12f;

        /// <summary>PullShort: контакт — кадр 5 (тот же снимок, что кадр 12 Pull).</summary>
        public const float ShortContactFrame = 5f;

        /// <summary>Uppercut/Slam: контакт — кадр 1 в тик удара; кадр 0 — на тик раньше (FormLeadTicks).</summary>
        public const float FormContactFrame = 1f;
        public const int FormLeadTicks = 1;

        /// <summary>Замах (Simulation.AbordageWindupTicks, v3 — 3): тик каста — кадр 0, выпуск — кадр 3.</summary>
        public const int WindupTicks = 3;

        /// <summary>
        /// Замах при цели за спиной (Simulation.AbordageWindupTicks + AbordageTurnWindupTicks):
        /// тот же Throw, кадры 0→3 растянуты на 4 тика (ThrowFrame), поворот корня — тоже на 4.
        /// </summary>
        public const int TurnWindupTicks = WindupTicks + 1;

        /// <summary>Удержание удара (Simulation.AbordageHoldTicks) — кадры 0–3 Punch.</summary>
        public const int HoldTicks = 3;

        /// <summary>Выход (Simulation.AbordageExitTicks) — кадры 0–6 Recover.</summary>
        public const int ExitTicks = 6;

        /// <summary>Якорь назад без цели (Simulation.AbordageRecallTicks).</summary>
        public const int RecallTicks = 4;

        /// <summary>Короткая тяга (timing.json pull_choice, N = 5): P = 4…5 — PullShort, иначе Pull.</summary>
        public const int ShortPullMinTicks = 4, ShortPullMaxTicks = 5;

        /// <summary>Все клипы: состояния, параметры, FBX.</summary>
        public static readonly PelagAbordageClip[] Clips =
        {
            PelagAbordageClip.Throw, PelagAbordageClip.Pull, PelagAbordageClip.Punch, PelagAbordageClip.Recover,
            PelagAbordageClip.PullShort, PelagAbordageClip.Uppercut, PelagAbordageClip.Slam,
        };

        /// <summary>
        /// Без этих Абордаж v2/v3 не показывается (прежний AnchorLeap_v5). PullShort, Uppercut и
        /// Slam — по наличию: нет состояния — вместо них Pull и Punch.
        /// </summary>
        public static readonly PelagAbordageClip[] RequiredClips =
        {
            PelagAbordageClip.Throw, PelagAbordageClip.Pull, PelagAbordageClip.Punch, PelagAbordageClip.Recover,
        };

        public static bool IsRequired(PelagAbordageClip clip)
            => clip == PelagAbordageClip.Throw || clip == PelagAbordageClip.Pull
               || clip == PelagAbordageClip.Punch || clip == PelagAbordageClip.Recover;

        public static string Suffix(PelagAbordageClip clip) => clip switch
        {
            PelagAbordageClip.Throw => "Throw",
            PelagAbordageClip.Pull => "Pull",
            PelagAbordageClip.Punch => "Punch",
            PelagAbordageClip.Recover => "Recover",
            PelagAbordageClip.PullShort => "PullShort",
            PelagAbordageClip.Uppercut => "Uppercut",
            PelagAbordageClip.Slam => "Slam",
            _ => "",
        };

        /// <summary>Имя FBX и клипа (.anim) — Assets/Resources/Characters/Pelag_v5/Mixamo/{имя}.fbx.</summary>
        public static string ClipName(PelagAbordageClip clip) => "Pelag_AN_Abordage2_" + Suffix(clip);

        public static string StateName(PelagAbordageClip clip) => "Abordage2_" + Suffix(clip) + "_v5";

        public static string StatePath(PelagAbordageClip clip) => "Base Layer." + StateName(clip);

        /// <summary>Своё время у каждого клипа: при смешивании уходящий не двигается чужим параметром.</summary>
        public static string PhaseParameter(PelagAbordageClip clip) => "Abordage2Phase" + Suffix(clip);

        /// <summary>Последний кадр клипа (кадров на один больше).</summary>
        public static int LastFrame(PelagAbordageClip clip) => clip switch
        {
            PelagAbordageClip.Throw => 9,
            PelagAbordageClip.Pull => 12,
            PelagAbordageClip.Punch => 3,
            PelagAbordageClip.Recover => 6,
            PelagAbordageClip.PullShort => 5,
            PelagAbordageClip.Uppercut => 4,
            PelagAbordageClip.Slam => 4,
            _ => 1,
        };

        // ---- выбор клипа ----

        /// <summary>Тяга P тиков: P = 4…5 — PullShort (если он есть в контроллере), иначе Pull.</summary>
        public static PelagAbordageClip PullClip(int pullTicks, bool shortAvailable = true)
            => shortAvailable && pullTicks >= ShortPullMinTicks && pullTicks <= ShortPullMaxTicks
                ? PelagAbordageClip.PullShort : PelagAbordageClip.Pull;

        /// <summary>Прибытие по форме: Гейзер — Uppercut, Обвал — Slam, Пробоина и база — Punch.</summary>
        public static PelagAbordageClip ArrivalClip(PelagForm form, bool formClipsAvailable = true)
        {
            if (!formClipsAvailable) return PelagAbordageClip.Punch;
            if (form == PelagForm.AbordageGeyser) return PelagAbordageClip.Uppercut;
            if (form == PelagForm.AbordageQuake) return PelagAbordageClip.Slam;
            return PelagAbordageClip.Punch;
        }

        public static bool IsPull(PelagAbordageClip clip) => clip == PelagAbordageClip.Pull || clip == PelagAbordageClip.PullShort;

        public static bool IsFormArrival(PelagAbordageClip clip) => clip == PelagAbordageClip.Uppercut || clip == PelagAbordageClip.Slam;

        /// <summary>Клип прибытия начинается на столько тиков раньше удара: Uppercut/Slam — 1, Punch — 0.</summary>
        public static int ArrivalLeadTicks(PelagAbordageClip clip) => IsFormArrival(clip) ? FormLeadTicks : 0;

        /// <summary>
        /// Стык без смешивания: конец (или кадр перехода) одного — та же поза, что начало другого
        /// (timing.json limits_v3.seams, 0°). Pull/PullShort → Uppercut/Slam — в тик B+P−1
        /// (кадр 11 Pull / 4 PullShort = кадр 0 формы).
        /// </summary>
        public static bool IsSeam(PelagAbordageClip from, PelagAbordageClip to)
        {
            if (from == PelagAbordageClip.Throw) return IsPull(to);
            if (IsPull(from)) return to == PelagAbordageClip.Punch || IsFormArrival(to);
            if (from == PelagAbordageClip.Punch || IsFormArrival(from)) return to == PelagAbordageClip.Recover;
            return false;
        }

        // ---- время: кадр клипа по тикам Sim ----

        /// <summary>
        /// Кадр Throw на k-м тике от каста: замах [0..3] на windupTicks тиков по прямой (3 —
        /// 0, 1, 2, 3; 4 — 0, .75, 1.5, 2.25, 3), полёт якоря [3..9] поровну на hookTicks тиков
        /// (throw_retime: тик C+W+j — кадр 3 + 6j/A). После зацепа — кадр 9 (натяг).
        /// </summary>
        public static float ThrowFrame(int windupTicks, int hookTicks, float k)
        {
            int windup = Math.Max(1, windupTicks), hook = Math.Max(1, hookTicks);
            if (k <= 0f) return 0f;
            if (k <= windup) return ReleaseFrame * k / windup;
            return ReleaseFrame + (BiteFrame - ReleaseFrame) * Math.Min(1f, (k - windup) / hook);
        }

        /// <summary>
        /// Кадр Pull на целом тике k (0…P) тяги длиной P (pull_retime): 0 — натяг, 1 — срыв
        /// (при P ≥ 3), полёт [1..9] поровну на P − 4 тиков, последние три — 10, 11, 12
        /// (при P = 3 без 10, при P = 2 и без срыва). P = 1 (тяги нет, удар тиком после
        /// зацепа) в timing.json нет: кадры 0 → 12 за тик.
        /// </summary>
        public static float PullKey(int pullTicks, int k)
        {
            int p = Math.Max(1, Math.Min(12, pullTicks));
            if (k <= 0) return 0f;
            if (k >= p) return ContactFrame;
            if (k == p - 1) return ContactFrame - 1f;
            if (k == p - 2 && p >= 4) return ContactFrame - 2f;
            if (k == 1) return SnapFrame;
            return SnapFrame + (FlightEndFrame - SnapFrame) * (k - 1) / (p - 4);
        }

        /// <summary>Кадр Pull в дробный тик k: по прямой между ключами PullKey.</summary>
        public static float PullFrame(int pullTicks, float k)
        {
            int p = Math.Max(1, Math.Min(12, pullTicks));
            if (k <= 0f) return 0f;
            if (k >= p) return ContactFrame;
            int a = (int)Math.Floor(k);
            float u = k - a;
            float from = PullKey(p, a), to = PullKey(p, a + 1);
            return from + (to - from) * u;
        }

        /// <summary>
        /// Кадр PullShort на целом тике k (0…P) тяги P = 4…5 (pull_short_retime): P = 5 — 0, 1, 2,
        /// 3, 4, 5; P = 4 — 0, 2, 3, 4, 5 (нырок всегда на тике, взвод — P−2, кулак — P−1, контакт — P).
        /// </summary>
        public static float PullShortKey(int pullTicks, int k)
        {
            int p = Math.Max(1, Math.Min(ShortPullMaxTicks, pullTicks));
            if (k <= 0) return 0f;
            if (k >= p) return ShortContactFrame;
            return ShortContactFrame - (p - k);
        }

        /// <summary>Кадр PullShort в дробный тик k: по прямой между ключами PullShortKey.</summary>
        public static float PullShortFrame(int pullTicks, float k)
        {
            int p = Math.Max(1, Math.Min(ShortPullMaxTicks, pullTicks));
            if (k <= 0f) return 0f;
            if (k >= p) return ShortContactFrame;
            int a = (int)Math.Floor(k);
            float u = k - a;
            float from = PullShortKey(p, a), to = PullShortKey(p, a + 1);
            return from + (to - from) * u;
        }

        /// <summary>Кадр тяги своим клипом: Pull или PullShort.</summary>
        public static float PullClipFrame(PelagAbordageClip clip, int pullTicks, float k)
            => clip == PelagAbordageClip.PullShort ? PullShortFrame(pullTicks, k) : PullFrame(pullTicks, k);

        public static float PunchFrame(float sinceArrive) => Clamp(sinceArrive, 0f, HoldTicks);

        /// <summary>Uppercut/Slam 1:1 от тика B+P−1: кадр 1 — в тик удара, 4 — в B+P+3 (= Recover 0).</summary>
        public static float FormFrame(float sinceFormStart) => Clamp(sinceFormStart, 0f, FormLeadTicks + HoldTicks);

        public static float RecoverFrame(float sinceExit) => Clamp(sinceExit, 0f, ExitTicks);

        /// <summary>
        /// Якорь назад без цели: клипа Recall в timing.json нет (лист B2) — ЗАГЛУШКА: Throw
        /// обратно от кадра, на котором цель пропала, к кадру 0 (стойка) за RecallTicks, S-кривой.
        /// </summary>
        public static float RecallFrame(float fromFrame, float sinceRecall)
            => fromFrame * (1f - PelagSquallClipRules.Smooth(sinceRecall / RecallTicks));

        // ---- опорная стопа ----

        /// <summary>
        /// Левая стопа стоит (timing.json planted_ankles: 0,476 вперёд и 0,163 влево — та же
        /// точка, что в опоре Шквала): весь Throw, Punch и Recover; кадры 0 и 12 Pull, 0 и 5
        /// PullShort; Uppercut и Slam — с контакта (кадр 1), кадр 0 ещё в воздухе.
        /// </summary>
        public static bool LeftPlanted(PelagAbordageClip clip, float frame) => clip switch
        {
            PelagAbordageClip.Throw or PelagAbordageClip.Punch or PelagAbordageClip.Recover => true,
            PelagAbordageClip.Pull => frame <= .01f || frame >= ContactFrame - .01f,
            PelagAbordageClip.PullShort => frame <= .01f || frame >= ShortContactFrame - .01f,
            PelagAbordageClip.Uppercut or PelagAbordageClip.Slam => frame >= FormContactFrame - .01f,
            _ => false,
        };

        // ---- поворот корня ----

        /// <summary>
        /// Замах: корень к цели S-кривой ровно за тики замаха — к выпуску (кадр 3) правая
        /// бросает точно в цель (timing.json view_requirements_v3). Обычно 3 тика: потолок Шквала
        /// 1200°/с держится до 80°, больший разворот — быстрее («резкое быстрое»). Цель за
        /// спиной — Sim даёт замах 4 тика (TurnWindupTicks), и поворот идёт на все 4.
        /// </summary>
        public static float ThrowTurnTicks(int windupTicks) => Math.Max(1, windupTicks);

        /// <summary>Зацеп: к направлению тяги (обычно доли градуса) за тик натяга.</summary>
        public const float HookTurnTicks = 1f, HookTurnMaxTicks = 2f;

        /// <summary>Удар: к взгляду Sim после удара (самонаведение сдвинуло цель) — последние 2 тика тяги.</summary>
        public const float PunchTurnTicks = 2f, PunchTurnMaxTicks = 3f, PunchTurnLeadTicks = 2f;

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
