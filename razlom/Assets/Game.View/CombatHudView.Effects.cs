using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Строка эффектов героя над портретом (этап 4, выбор владельца 30.09 — кадр 1a; из 1b — секунды и
    /// стаки в углу значка). Здесь — откуда берутся эффекты и подсказка; вид и движение — в
    /// <see cref="HudEffectRow"/>, порядок, секунды и «+N» — в <see cref="HudEffectList"/>.
    ///
    /// Источники (Game.Sim, только чтение): корни, оглушение, защита от контроля и замедление —
    /// Simulation.HeroSlow; Живица, Порыв и Ясный настой — Simulation.PotionEffects; артефакт —
    /// ArtifactActiveUntil, Обет Хранителя — событие ArtifactUsed; Blaze — BlazeActive и
    /// BlazeIgniteTick. Стаков сим пока ни у одного эффекта не даёт — «×N» появится сам, как только
    /// источник передаст число больше 1.
    ///
    /// Строки нет в префабе (старый CombatHud) — работают прежние значки зелий HudBuffChip.
    /// </summary>
    public sealed partial class CombatHudView
    {
        [Header("Эффекты над портретом (этап 4, кадр 1a)")]
        [Tooltip("Необязательно: строка кругов-таймеров эффектов героя над портретом. Нет — прежние значки зелий")]
        public HudEffectRow EffectRow;

        readonly HudEffectList _effects = new HudEffectList();
        readonly System.Text.StringBuilder _effectTip = new System.Text.StringBuilder(160);
        Simulation _effectSim;
        RunArtifact _effectArtifact = (RunArtifact)255;
        int _blazeIgnite = int.MinValue, _blazeStoked, _vowUntil;
        int _effectTipIndex = -1, _effectTipSeconds = -1, _effectTipSlow = -1, _effectTipOverflow = -1;
        RunArtifact _effectTipArtifact = (RunArtifact)255;

        // «Подбросить дров» (усиление Blaze): каждое убийство под огнём +0,5 с, всего не больше 3 с.
        // Числа — как в Simulation.Upgrades (там они закрытые): вид повторяет их, чтобы кольцо не стояло.
        const int BlazeStokeTicks = Simulation.TicksPerSecond / 2, BlazeStokeMaxTicks = 3 * Simulation.TicksPerSecond;

        void RefreshEffects(Simulation sim, TickDriver driver, Vector2 pointer)
        {
            if (EffectRow == null)
            {
                // Префаб без строки эффектов: прежние значки зелий над героем.
                if (ResinChip != null) ResinChip.Set(sim.ResinTicksLeft, Simulation.PotionEffectTicks, Simulation.TicksPerSecond);
                if (SurgeChip != null) SurgeChip.Set(sim.SurgeTicksLeft, Simulation.PotionEffectTicks, Simulation.TicksPerSecond);
                return;
            }
            // Новая симуляция (лагерь ↔ забег): круги прошлой уходят сразу, без вспышек.
            if (sim != _effectSim)
            {
                _effectSim = sim;
                _effects.Clear();
                EffectRow.HideAll();
                _effectArtifact = (RunArtifact)255;
                _blazeIgnite = int.MinValue;
                _blazeStoked = _vowUntil = 0;
                _effectTipIndex = -1;
            }
            _effects.MaxVisible = HudEffectsDemo.Enabled ? HudEffectsDemo.MaxVisible : EffectRow.MaxVisible;
            _effects.Begin();
            if (HudEffectsDemo.Enabled) HudEffectsDemo.Report(_effects, UiMotion.Now);
            else CollectEffects(sim, driver);
            _effects.Commit();
            RefreshEffectArtifactIcon(sim);
            EffectRow.Apply(_effects, UiMotion.Now);
            RefreshEffectTooltip(sim, pointer);
        }

        void CollectEffects(Simulation sim, TickDriver driver)
        {
            // Контроль: корни и оглушение не ложатся вместе; защита — после них (иммунитет 1,5 с).
            int root = sim.HeroRootTicksLeft, stun = sim.HeroStunTicksLeft;
            _effects.Report(HudEffectKind.Root, root);
            _effects.Report(HudEffectKind.Stun, stun);
            if (root <= 0 && stun <= 0)
                _effects.Report(HudEffectKind.ControlImmune, sim.HeroControlImmuneTicksLeft, Simulation.HeroControlImmunityTicks);
            _effects.Report(HudEffectKind.Slow, sim.HeroSlowTicksLeft);
            _effects.Report(HudEffectKind.Resin, sim.ResinTicksLeft, Simulation.PotionEffectTicks);
            _effects.Report(HudEffectKind.Surge, sim.SurgeTicksLeft, Simulation.PotionEffectTicks);
            _effects.Report(HudEffectKind.Clear, sim.ClearTicksLeft);

            // Артефакт: включённый действует до ArtifactActiveUntil; Обет Хранителя — 3 с неуязвимости
            // после спасения (срок сим не отдаёт — берём из события).
            var events = driver != null ? driver.FrameEvents : null;
            var contexts = driver != null ? driver.FrameEventContexts : null;
            AbilityBuild blaze = BlazeBuild(sim);
            bool stokes = sim.BlazeActive && blaze != null && blaze.Has(AbilityFlag.BlazeStoke);
            if (events != null)
                for (int i = 0; i < events.Count; i++)
                {
                    SimEvent e = events[i];
                    if (e.Type == SimEventType.ArtifactUsed && e.ActionVariant == (int)RunArtifact.GuardianVow)
                    {
                        int at = contexts != null && i < contexts.Count ? contexts[i].SimulationTick : sim.Tick;
                        _vowUntil = at + e.Amount;
                    }
                    else if (stokes && e.Type == SimEventType.Death && e.Source == Simulation.PlayerId && e.Target != Simulation.PlayerId)
                        _blazeStoked = Mathf.Min(BlazeStokeMaxTicks, _blazeStoked + BlazeStokeTicks);
                }
            RunArtifact artifact = sim.Artifact;
            int artifactLeft = Mathf.Max(0, sim.ArtifactActiveUntil - sim.Tick);
            int artifactFull = Simulation.ArtifactDurationTicks(artifact);
            int vowLeft = artifact == RunArtifact.GuardianVow ? Mathf.Max(0, _vowUntil - sim.Tick) : 0;
            if (vowLeft > artifactLeft)
            {
                artifactLeft = vowLeft;
                artifactFull = Simulation.VowImmuneTicks;
            }
            _effects.Report(HudEffectKind.Artifact, artifact != RunArtifact.None ? artifactLeft : 0, artifactFull);

            // Blaze: огонь идёт от поджига (BlazeIgniteTick) длительностью способности и доливается
            // убийствами у «Подбросить дров». Пока сабля горит, круг не кончается раньше огня.
            int blazeLeft = 0, blazeFull = 0;
            if (sim.BlazeActive)
            {
                // Новый поджиг — только когда он уже случился: повторное нажатие под огнём ставит поджиг
                // в будущее (жест бутылки), а отменённый жест — в −1; старый огонь до тех пор горит свой срок.
                int ignite = sim.BlazeIgniteTick;
                if (ignite >= 0 && ignite <= sim.Tick && ignite != _blazeIgnite)
                {
                    _blazeIgnite = ignite;
                    _blazeStoked = 0;
                }
                int duration = blaze != null ? Mathf.Max(1, blaze.Get(AbilityStatType.DurationTicks).ToInt()) : 3 * Simulation.TicksPerSecond;
                blazeFull = duration + _blazeStoked;
                blazeLeft = _blazeIgnite == int.MinValue ? 1 : Mathf.Max(1, _blazeIgnite + blazeFull - sim.Tick);
            }
            _effects.Report(HudEffectKind.Blaze, blazeLeft, blazeFull);
        }

        static AbilityBuild BlazeBuild(Simulation sim)
        {
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
            {
                AbilityBuild build = sim.GetAbility(slot);
                if (build != null && build.DefinitionId == AbilityDefinition.BlazeId) return build;
            }
            return null;
        }

        /// <summary>Значок круга артефакта — картинка включённого артефакта; меняется только со сменой артефакта.</summary>
        void RefreshEffectArtifactIcon(Simulation sim)
        {
            RunArtifact artifact = HudEffectsDemo.Enabled ? RunArtifact.SunSeal : sim.Artifact;
            if (artifact == _effectArtifact) return;
            _effectArtifact = artifact;
            EffectRow.SetIcon(HudEffectKind.Artifact, artifact != RunArtifact.None ? RunArtifactTexts.Icon(artifact) : null);
        }

        /// <summary>
        /// Подсказка над кругом под мышью: имя, одна строка «что делает» и секунды. Текст собирается
        /// заново только при смене круга, секунды или замедления. Спрятанная в лагере строка подсказку не открывает.
        /// </summary>
        void RefreshEffectTooltip(Simulation sim, Vector2 pointer)
        {
            int index = CampCombatHidden ? -1
                : HudEffectsDemo.TooltipKind >= 0 ? HudEffectsDemo.TooltipKind
                : EffectRow.HitChip(pointer);
            if (index >= 0 && index < HudEffectList.KindCount && !_effects.Shown((HudEffectKind)index)) index = -1;
            if (index == HudEffectList.KindCount && _effects.Overflow <= 0) index = -1;
            if (index < 0)
            {
                _effectTipIndex = -1;
                EffectRow.HideTooltip();
                return;
            }
            var kind = (HudEffectKind)Mathf.Min(index, HudEffectList.KindCount - 1);
            int seconds = index < HudEffectList.KindCount ? _effects.Seconds(kind) : 0;
            int slow = sim.HeroSlowPercent;
            bool changed = index != _effectTipIndex || seconds != _effectTipSeconds || slow != _effectTipSlow
                || _effects.Overflow != _effectTipOverflow || _effectArtifact != _effectTipArtifact;
            string text = null;
            if (changed)
            {
                _effectTipIndex = index;
                _effectTipSeconds = seconds;
                _effectTipSlow = slow;
                _effectTipOverflow = _effects.Overflow;
                _effectTipArtifact = _effectArtifact;
                text = EffectTooltipText(index, kind, seconds, slow);
            }
            EffectRow.ShowTooltip(index, text, Scale);
        }

        string EffectTooltipText(int index, HudEffectKind kind, int seconds, int slow)
        {
            string muted = Hex(UiTheme.Role.TextMuted);
            _effectTip.Clear();
            if (index == HudEffectList.KindCount)
            {
                // Круг «+N»: какие эффекты не поместились.
                _effectTip.Append("<b>Ещё ").Append(_effects.Overflow).Append("</b>\n");
                bool first = true;
                for (int i = 0; i < HudEffectList.KindCount; i++)
                {
                    var hidden = (HudEffectKind)i;
                    if (!_effects.Active(hidden) || _effects.Shown(hidden)) continue;
                    if (!first) _effectTip.Append(" · ");
                    first = false;
                    _effectTip.Append(EffectName(hidden));
                }
                return _effectTip.ToString();
            }
            string line = kind == HudEffectKind.Artifact ? HudEffectTexts.ArtifactLine(_effectArtifact) : HudEffectTexts.Line(kind, slow);
            _effectTip.Append("<b>").Append(EffectName(kind)).Append("</b>");
            if (!string.IsNullOrEmpty(line)) _effectTip.Append('\n').Append(line);
            _effectTip.Append("\n<size=88%><color=#").Append(muted).Append('>').Append(HudEffectTexts.Remaining(seconds)).Append("</color></size>");
            return _effectTip.ToString();
        }

        string EffectName(HudEffectKind kind)
            => kind == HudEffectKind.Artifact && _effectArtifact != RunArtifact.None && _effectArtifact != (RunArtifact)255
                ? RunArtifactTexts.Name(_effectArtifact)
                : HudEffectTexts.Name(kind);
    }

    /// <summary>
    /// Съёмка строки эффектов без боя (-ExtraArgs '-capture-hud-effects'): все виды по кругу —
    /// появляются, тикают и кончаются, у Blaze «×2», чтобы на кадре были и секунды, и стаки, и
    /// «+N» (одновременно больше, чем помещается). '-capture-hud-effect-tooltip N' держит подсказку
    /// над кругом вида N (HudEffectKind; 9 — «+N»). В обычной игре выключено.
    /// </summary>
    static class HudEffectsDemo
    {
        public static readonly bool Enabled = Has("-capture-hud-effects");
        public static readonly int TooltipKind = Enabled ? Number("-capture-hud-effect-tooltip") : -1;

        /// <summary>На съёмке в строке 6 мест: на 5–5,6 с эффектов 7 — пять кругов и «+2».</summary>
        public const int MaxVisible = 6;

        const float Cycle = 16f;
        // Вид, начало и длительность в секундах внутри цикла, полная длина (0 — по остатку), стаки.
        static readonly (HudEffectKind Kind, float Start, float Length, int Full, int Stacks)[] Script =
        {
            (HudEffectKind.Resin, 0f, 6f, 6, 1),
            (HudEffectKind.Blaze, .6f, 5f, 5, 2),
            (HudEffectKind.Root, 1.4f, 2f, 0, 1),
            (HudEffectKind.ControlImmune, 3.4f, 2.2f, 0, 1),
            (HudEffectKind.Slow, 2.2f, 4f, 0, 1),
            (HudEffectKind.Surge, 3f, 6f, 6, 1),
            (HudEffectKind.Artifact, 4f, 4f, 4, 1),
            (HudEffectKind.Clear, 5f, 2f, 0, 1),
            (HudEffectKind.Stun, 9.5f, 1f, 0, 1),
            (HudEffectKind.Resin, 10.5f, 3f, 6, 1),
        };

        public static void Report(HudEffectList list, float now)
        {
            float t = Mathf.Repeat(now, Cycle);
            // Подсказка на кадре: строка стоит на 4,5 с (шесть кругов, все на месте), не по кругу.
            if (TooltipKind >= 0) t = 4.5f;
            foreach (var step in Script)
            {
                float left = step.Start + step.Length - t;
                if (t < step.Start || left <= 0f) continue;
                int ticks = Mathf.CeilToInt(left * Simulation.TicksPerSecond);
                list.Report(step.Kind, ticks, step.Full * Simulation.TicksPerSecond, step.Stacks);
            }
        }

        static bool Has(string flag) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), flag) >= 0;

        static int Number(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int at = System.Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int value) ? value : -1;
        }
    }
}
