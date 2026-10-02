using Game.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Полоса босса (выбор владельца 30.09: кадр 1a и подпись «Босс · фаза 2» из 1b). Появление: имя пишется
    /// тушью (группа проявления полосы), потом полоса наливается до здоровья босса. На самой полосе — засечки
    /// 66% и 33% (подмога) и 50% (ярость): кружки загораются, когда их проходят. Заливка кончается кругом, за
    /// ней — светлый след недавнего урона. Под именем — «Босс · фаза 2», на смене фазы — короткий толчок, под
    /// полосой — «2900 / 5000». Правила засечек и фаз — RunHudBossMarks.
    ///
    /// HUD проявляется раз: после паузы полоса встаёт сразу, без повторного появления и его звука.
    /// </summary>
    public sealed partial class RunHud
    {
        private float _bossTrail = -1f, _bossTrailHoldUntil;
        /// <summary>Глубина, на которой босс уже показан; −1 — ещё нет.</summary>
        private int _bossMet = -1;
        /// <summary>Забег, для которого считается «уже показан»: пауза его не сбрасывает.</summary>
        private RiftRun _bossRun;
        private LayoutView _layout;
        private Color _bossBaseColour;
        private static readonly Color RageColour = new Color(1f, .36f, .16f, 1f);

        private float _bossIntroAt = -100f;
        private int _bossPhaseShown = -1, _bossHealthShown = -1, _bossMaxShown = -1;
        private bool _bossRageShown;
        private readonly bool[] _bossMarkLit = new bool[RunHudBossMarks.Count];

        const int ChannelSubtitle = 41;

        private void RefreshBoss(RiftRun run, bool status)
        {
            if (run != _bossRun)
            {
                _bossRun = run;
                _bossMet = -1;
            }
            bool boss = status && run != null && run.BossId >= 0 && run.Sim.Entities.Alive[run.BossId];
            bool intro = false;
            if (boss && _bossMet != run.Depth)
            {
                // Появление босса: полоса встаёт, когда босс вышел из тумана, — толчком и своим звуком,
                // а не с первого кадра арены за пеленой (аудит UI, этап 2).
                var at = run.Sim.Entities.Position[run.BossId];
                if (_layout == null) _layout = FindAnyObjectByType<LayoutView>();
                boss = _layout == null || _layout.IsRevealed(at.X.ToFloat(), at.Y.ToFloat())
                    || run.Sim.Entities.Health[run.BossId] < run.Sim.Entities.MaxHealth[run.BossId];
                // Хозяин Чащи на поляне (02.10): полоса встаёт концом кат-сцены вступления, в тик первой атаки.
                if (boss && ThicketMasterIntroRules.HoldsBossBar(run.Sim, run.BossId)) boss = false;
                if (boss)
                {
                    _bossMet = run.Depth;
                    intro = true;
                }
            }
            bool wasShown = _view.Boss != null && _view.Boss.gameObject.activeSelf;
            RunHudView.SetActive(_view.Boss, boss);
            if (!boss)
            {
                _bossTrail = -1f;
                return;
            }

            int id = run.BossId;
            int health = run.Sim.Entities.Health[id], max = Mathf.Max(1, run.Sim.Entities.MaxHealth[id]);
            if (intro) BeginBossIntro(health, max, run.BossEnraged);
            // Вернулись из паузы или меню: полоса уже знакома — сразу, без второго появления и вспышек засечек.
            else if (!wasShown)
            {
                ShowBossInstant();
                RepaintBossMarks(health, max, run.BossEnraged);
            }

            RunHudView.SetText(_view.BossName, EnemyTexts.BossName(run.Sim.Entities.Kind[id]));
            float since = UiMotion.Now - _bossIntroAt;
            bool introRunning = !RunHudBossMarks.IntroDone(since);
            float value = health / (float)max;
            if (_view.BossBar != null)
                BossBar(introRunning ? RunHudBossMarks.IntroFill(value, since) : value, run.BossEnraged, introRunning);
            BossMarks(health, max, run.BossEnraged, !introRunning);
            BossSubtitle(health, max, run.BossEnraged, !intro);
            BossNumbers(health, max);
        }

        /// <summary>Первое появление за арену: толчок, звук выхода, полоса с нуля, засечки и фаза — как есть, без вспышек.</summary>
        private void BeginBossIntro(int health, int max, bool enraged)
        {
            _bossIntroAt = UiMotion.Now;
            _bossTrail = -1f;
            HudFx.Punch(_view.Boss, 1.22f, .55f);
            GameSound.Play("boss_intro", .85f, 0f, 1f);
            RepaintBossMarks(health, max, enraged);
            _bossPhaseShown = -1;
            _bossHealthShown = _bossMaxShown = -1;
        }

        /// <summary>Засечки заново в свои цвета, без вспышек (появление, возврат из паузы).</summary>
        private void RepaintBossMarks(int health, int max, bool enraged)
        {
            for (int i = 0; i < _bossMarkLit.Length; i++) _bossMarkLit[i] = !Lit(i, health, max, enraged);
            BossMarks(health, max, enraged, false);
        }

        private void ShowBossInstant()
        {
            var ink = _view.Boss.GetComponent<UiInkGroup>();
            if (ink != null) ink.ShowInstant();
            _view.Boss.localScale = Vector3.one;
        }

        private static bool Lit(int index, int health, int max, bool enraged)
            => RunHudBossMarks.KindAt(index) == RunHudBossMarks.Kind.Rage ? enraged : RunHudBossMarks.Passed(index, health, max);

        /// <summary>Кружки засечек: пройденная загорается (толчок и вспышка по FlashScale); на появлении — без вспышек.</summary>
        private void BossMarks(int health, int max, bool enraged, bool animate)
        {
            RunHudView.BossMark[] marks = _view.BossMarks;
            if (marks == null) return;
            for (int i = 0; i < marks.Length && i < _bossMarkLit.Length; i++)
            {
                RunHudView.BossMark mark = marks[i];
                if (mark == null || mark.Rect == null) continue;
                bool lit = Lit(i, health, max, enraged);
                if (lit == _bossMarkLit[i]) continue;
                _bossMarkLit[i] = lit;
                bool rage = RunHudBossMarks.KindAt(i) == RunHudBossMarks.Kind.Rage;
                UiTheme theme = UiTheme.Current;
                if (mark.Dot != null)
                {
                    Color dot = lit ? (rage ? RageColour : theme.Get(UiTheme.Role.Accent)) : theme.Get(UiTheme.Role.SmokeDeep);
                    mark.Dot.color = dot;
                }
                if (mark.Ring != null)
                {
                    Color ring = lit ? new Color(1f, .9f, .74f, .95f) : theme.Get(UiTheme.Role.PanelLine);
                    if (!lit) ring.a = .55f;
                    mark.Ring.color = ring;
                }
                if (!lit || !animate) continue;
                HudFx.Punch(mark.Rect, 1.6f, .45f);
                if (mark.Flash != null)
                {
                    Color flash = rage ? RageColour : theme.Get(UiTheme.Role.Accent);
                    flash.a = 0f;
                    mark.Flash.color = flash;
                    HudFx.Burst(mark.Flash, .9f, .6f, 2.6f, .7f);
                }
            }
        }

        /// <summary>«Босс · фаза 2»: на смене фазы или ярости — толчок и оранжевый, остывающий к приглушённому.</summary>
        private void BossSubtitle(int health, int max, bool enraged, bool animate)
        {
            TMPro.TMP_Text label = _view.BossSubtitle;
            if (label == null) return;
            int phase = RunHudBossMarks.Phase(health, max);
            if (phase == _bossPhaseShown && enraged == _bossRageShown) return;
            bool changed = _bossPhaseShown >= 0;
            _bossPhaseShown = phase;
            _bossRageShown = enraged;
            RunHudView.SetText(label, RunHudBossMarks.Subtitle(phase, enraged));
            UiTheme theme = UiTheme.Current;
            Color rest = theme.Get(UiTheme.Role.TextMuted);
            if (!changed || !animate)
            {
                UiMotion.Stop(label);
                label.color = rest;
                return;
            }
            Color hot = theme.Get(UiTheme.Role.Accent);
            HudFx.Punch(label.transform, 1.3f, .5f);
            UiMotion.Play(label, ChannelSubtitle, 1.4f, k => label.color = Color.Lerp(hot, rest, k * k));
        }

        /// <summary>«2900 / 5000» — строка только при смене чисел.</summary>
        private void BossNumbers(int health, int max)
        {
            if (_view.BossNumbers == null || (health == _bossHealthShown && max == _bossMaxShown)) return;
            _bossHealthShown = health;
            _bossMaxShown = max;
            RunHudView.SetText(_view.BossNumbers, health + " / " + max);
        }

        /// <summary>
        /// Полоса босса: светлый след недавнего урона догоняет заполнение с задержкой, ярость —
        /// горячим цветом заливки и пульсом, а не словом в имени. На появлении следа нет.
        /// </summary>
        private void BossBar(float value, bool enraged, bool intro)
        {
            WcBar bar = _view.BossBar;
            float now = UiMotion.Now;
            if (intro) _bossTrail = value;
            if (_bossTrail < 0f || value > _bossTrail) _bossTrail = value;
            else if (value < bar.Value) _bossTrailHoldUntil = now + .45f;
            if (now >= _bossTrailHoldUntil)
                _bossTrail = Mathf.MoveTowards(_bossTrail, value, Time.unscaledDeltaTime * .7f);
            bar.TrailValue = _bossTrail;
            bar.Set(value);
            // Круглая «голова» на конце заливки (ромб концепта заменён кругом): при пустой полосе не видна.
            if (bar.Head != null) RunHudView.SetActive(bar.Head, value > .004f);
            // Заливка «Дыма и света» — маска с мазком внутри: красится первый мазок; у полосы пака — сама заливка.
            var fill = bar.Fill != null ? bar.Fill.GetComponentInChildren<Graphic>(true) : null;
            if (fill == null) return;
            if (_bossBaseColour.a <= 0f) _bossBaseColour = fill.color;
            float pulse = enraged ? .5f + .5f * Mathf.Sin(now * 6f) : 0f;
            fill.color = enraged ? Color.Lerp(RageColour, Color.white, pulse * .25f) : _bossBaseColour;
        }
    }
}
