using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Вес маха Крушения для вида: лёгкий — мах 1, тяжёлый — мах 2 и «Четвёртый удар» (талант).</summary>
    public enum WreckSwingWeight : byte { None = 0, Light = 1, Heavy = 2 }

    /// <summary>
    /// Махи Крушения v4 «холодное железо», вид V2 (06.10; целевые кадры владельца
    /// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/swing1.png и swing2.png) — числа без Unity,
    /// тесты tools/Combat.Presentation.Tests/WreckSwingRulesTests.cs.
    ///
    /// Сабельный ритм v4: мах 1 справа налево (WreckState.Side +1), мах 2 слева направо (−1), выпад — удар оземь.
    /// Якорь — тяжёлая булава на КОРОТКОЙ цепи (0,45 м от кулака): голова ходит дугой 1,2–1,5 м вокруг героя.
    /// СЕРП (V1 была тонкая белёсая лента штрихами — путь брался раз на кадр/тик): сплошной серп от кулака до
    /// головы, ряды берутся в любой миг между кадрами (PelagWreckSwingSweep) — последние WindowSeconds хода головы;
    /// у головы толще, к хвосту сходит на нет внутренним краем, наружная кромка — по носу головы. Цвета — одна
    /// таблица PelagWreckSwingLook. ЗАДЕТЫЙ: короткий росчерк по ходу маха, сколы железа, пыль, отброс — без шара.
    /// Сроки — только из Sim: сектор маха (Simulation.TryGetWreckSweep: from…to), без него — сдвиги сектора Sim от
    /// тика удара. Тики серии здесь не зашиты. Время — тик показа sim.Tick − 2 + Alpha (PelagWreckVfxRules.ShownTick).
    /// </summary>
    public static class PelagWreckSwingRules
    {
        /// <summary>Этапы серии (WreckState.Stage, Amount события WreckStage).</summary>
        public const int StageSwing = 0, StageBackswing = 1, StageSlam = 2, StageFourth = 3;

        public static WreckSwingWeight WeightOf(int stage)
            => stage == StageSwing ? WreckSwingWeight.Light
                : stage == StageBackswing || stage == StageFourth ? WreckSwingWeight.Heavy : WreckSwingWeight.None;

        /// <summary>Этап рисуется серпом маха (выпад — своя земля, не серп).</summary>
        public static bool Draws(int stage) => WeightOf(stage) != WreckSwingWeight.None;

        /// <summary>Вес знака на задетом: мах — по своему этапу, «Четвёртый» — тяжёлый, прочее — не мах.</summary>
        public static WreckSwingWeight HitWeight(WreckVfxHit hit, int stage)
        {
            if (hit == WreckVfxHit.Fourth) return WreckSwingWeight.Heavy;
            if (hit != WreckVfxHit.Swing) return WreckSwingWeight.None;
            return stage == StageBackswing ? WreckSwingWeight.Heavy : WreckSwingWeight.Light;
        }

        private static bool Heavy(WreckSwingWeight w) => w == WreckSwingWeight.Heavy;

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        public static float Seconds(float ticks) => ticks / Simulation.TicksPerSecond;

        public static float Ticks(float seconds) => seconds * Simulation.TicksPerSecond;

        // ---------------------------------------------------------------- кто бит: мах по ходу головы

        /// <summary>
        /// Этап маха, чей сектор Sim (from…to, Simulation.TryGetWreckSweep — вид запоминает оба маха серии) содержит
        /// тик <paramref name="tick"/>. Стык (самый быстрый темп: мах 2 входит в сектор, пока мах 1 доигрывает, — Sim
        /// добивает остаток маха 1 разом): каждого мах бьёт один раз, поэтому кого мах 1 ещё не бил
        /// (<paramref name="hitBySwing1"/> false) — мах 1, иначе мах 2. −1 — ни один. Пустой сектор: from &gt; to.
        /// </summary>
        public static int SweepStageAt(int tick, int from0, int to0, int from1, int to1, bool hitBySwing1)
        {
            bool zero = tick >= from0 && tick <= to0, one = tick >= from1 && tick <= to1;
            if (zero && one) return hitBySwing1 ? StageBackswing : StageSwing;
            return one ? StageBackswing : zero ? StageSwing : -1;
        }

        /// <summary>
        /// Damage слота Крушения в ход маха. Sim бьёт каждого врага сектора в тик, когда голова проходит его угол
        /// (контакт − 3 … контакт + 4, Simulation.Wreck.Sweep), а WreckStage пишет только в контакт: удары вне тика
        /// контакта PelagWreckVfxRules.ClassifyHit не узнаёт (Other). Здесь: Other в секторе маха — мах; этап — этап
        /// этого маха (<paramref name="sweepStage"/> из SweepStageAt; −1 — не мах). Прочие удары не трогаются.
        /// </summary>
        public static WreckVfxHit SweepHit(WreckVfxHit hit, int sweepStage, out int stage)
        {
            stage = -1;
            if (sweepStage != StageSwing && sweepStage != StageBackswing) return hit;
            if (hit == WreckVfxHit.Other) hit = WreckVfxHit.Swing;
            if (hit == WreckVfxHit.Swing) stage = sweepStage;
            return hit;
        }

        /// <summary>Ход маха у задетого (x, z мира): касательная по стороне маха Sim этапа (мах 2 — зеркально).</summary>
        public static void SweepDirection(float radialX, float radialZ, int stage, out float x, out float z)
            => PelagWreckVfxRules.SwingTangent(radialX, radialZ, Simulation.WreckSwingSide(stage), out x, out z);

        // ---------------------------------------------------------------- сроки серпа

        /// <summary>Путь пишется с тика до входа головы в сектор (на столько тиков раньше from маха Sim).</summary>
        public const float LeadTicks = 1f;

        /// <summary>Начало записи: не раньше нажатия этапа и за LeadTicks до входа в сектор.</summary>
        public static float RecordFrom(int stageStart, int sweepFrom) => Math.Max(stageStart, sweepFrom - LeadTicks);

        /// <summary>Конец записи — голова уходит из сектора (to маха Sim); дальше серп замирает и рассыпается.</summary>
        public static float RecordUntil(int sweepTo) => sweepTo;

        /// <summary>Запас без снимка маха Sim: сектор от тика удара по сдвигам Sim (без сжатия темпом).</summary>
        public static int FallbackFrom(int contact) => contact + Simulation.WreckSweepFirstOffset;
        public static int FallbackTo(int contact) => contact + Simulation.WreckSweepLastOffset;

        public static bool Recording(float shown, float from, float until) => shown >= from && shown <= until;

        /// <summary>Серп — последние столько секунд хода головы: мах 1 — 0,12, мах 2 — 0,15 (тяжелее).</summary>
        public static float WindowSeconds(WreckSwingWeight w) => Heavy(w) ? .15f : .12f;
        public static float WindowTicks(WreckSwingWeight w) => Ticks(WindowSeconds(w));

        /// <summary>Замерший серп рассыпается за столько секунд (хвост первым).</summary>
        public static float ErodeSeconds(WreckSwingWeight w) => Heavy(w) ? .14f : .10f;

        /// <summary>Распад 0…1 от тика показа, когда серп замер.</summary>
        public static float Erode(float shown, float frozenAt, WreckSwingWeight w)
            => Smooth01(Seconds(shown - frozenAt) / ErodeSeconds(w));

        public static bool Done(float shown, float frozenAt, WreckSwingWeight w)
            => Seconds(shown - frozenAt) > ErodeSeconds(w) + .02f;

        /// <summary>
        /// Голова пошла против стороны маха Sim (угловая скорость вокруг героя, рад/с; +1 — справа налево = против
        /// часовой сверху): столько кадров подряд — серп замирает, обратный ход в него не пишется (при самом быстром
        /// темпе мах 2 разворачивает голову, пока мах 1 ещё в секторе).
        /// </summary>
        public const float ReverseRadPerSecond = 2.5f;
        public const int ReverseFrames = 2;

        public static bool Against(int side, float omega) => side != 0 && omega * side < -ReverseRadPerSecond;

        // ---------------------------------------------------------------- полоса серпа: от кулака до носа головы

        /// <summary>Внутренняя основа полосы — столько метров от кулака к голове (рукоять не заливается).</summary>
        public const float InnerFromGrip = .08f;

        /// <summary>Наружная кромка — за центром головы на столько метров (лапы якоря), мах 2 — шире.</summary>
        public static float OuterPastHead(WreckSwingWeight w) => Heavy(w) ? .26f : .22f;

        /// <summary>У головы внутренний край лежит на этой доле полосы от основы (0 — у самого кулака): мах 2 — полный.</summary>
        public static float InnerAtHead(WreckSwingWeight w) => Heavy(w) ? 0f : .12f;

        /// <summary>У самой головы полоса — эта доля полной ширины (скругление), полная — к доле HeadRound длины.</summary>
        public const float HeadFill = .8f, HeadRound = .1f;

        /// <summary>Сход к хвосту: ширина × (1 − u^TailPower) — мах 2 держит ширину дольше.</summary>
        public static float TailPower(WreckSwingWeight w) => Heavy(w) ? 1.7f : 1.4f;

        /// <summary>Толщина серпа 0…1 вдоль: <paramref name="u"/> 0 — голова сейчас, 1 — хвост (на нём ноль).</summary>
        public static float Thickness(float u, WreckSwingWeight w)
        {
            u = Clamp01(u);
            float head = HeadFill + (1f - HeadFill) * Smooth01(u / HeadRound);
            return head * (1f - (float)Math.Pow(u, TailPower(w))) * (1f - InnerAtHead(w));
        }

        /// <summary>Где лежит внутренний край: доля от основы у кулака (0) до наружной кромки (1).</summary>
        public static float InnerShare(float u, WreckSwingWeight w) => 1f - Thickness(u, w);

        /// <summary>След виден там, где голова хлещет: медленный ход прозрачен, м/с.</summary>
        public const float SpeedLow = 3f, SpeedFull = 8f;

        public static float SpeedAlpha(float speed) => Smooth01((speed - SpeedLow) / (SpeedFull - SpeedLow));

        /// <summary>Хвост слабеет к самому концу (рваный конец даёт маска пака).</summary>
        public static float TailAlpha(float u) => 1f - .6f * Smooth01((Clamp01(u) - .75f) / .25f);

        /// <summary>Яркость серпа: мах 2 ярче.</summary>
        public static float Glow(WreckSwingWeight w) => Heavy(w) ? 1.15f : 1f;

        // ---------------------------------------------------------------- звенья-призраки у наружной кромки

        private static readonly float[] LightLinks = { .26f, .6f };
        private static readonly float[] HeavyLinks = { .2f, .44f, .68f };

        /// <summary>Звеньев-призраков на серпе: мах 1 — два, мах 2 — три.</summary>
        public static int LinkCount(WreckSwingWeight w) => Heavy(w) ? HeavyLinks.Length : w == WreckSwingWeight.Light ? LightLinks.Length : 0;

        /// <summary>Где вдоль серпа (u) едет звено <paramref name="i"/>: едут вместе с головой.</summary>
        public static float LinkU(WreckSwingWeight w, int i)
        {
            float[] at = Heavy(w) ? HeavyLinks : LightLinks;
            return at[Math.Max(0, Math.Min(at.Length - 1, i))];
        }

        /// <summary>Длина звена-призрака, м (меш звена «железа» единичной длины), и отступ его оси от наружной кромки, м.</summary>
        public static float LinkLength(WreckSwingWeight w) => Heavy(w) ? .36f : .32f;
        public const float LinkInset = .12f;

        /// <summary>Ширина звена — доля длины (меш WreckIronLink: x ±0,26 при z ±0,5).</summary>
        public const float LinkWidthOfLength = .52f;

        /// <summary>Звено видно, где полоса вмещает его поперёк (на тонком хвосте — нет): 0…1 по ширине полосы, м.</summary>
        public static float LinkFit(float band, WreckSwingWeight w)
        {
            float need = LinkInset + .5f * LinkWidthOfLength * LinkLength(w);
            return Smooth01((band - need) / (.35f * need));
        }

        /// <summary>Звенья лежат плашмя в плоскости серпа, через одно чуть повёрнуты вокруг хода, градусы.</summary>
        public static float LinkRoll(int i) => (i & 1) == 0 ? 18f : -18f;
    }
}
