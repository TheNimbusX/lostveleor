using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Индикатор серии Крушения на плитке (06.10, целевой кадр ART/characters/pelag/wreck-look-2026-10-06/
    /// chatgpt-results/series-ui.png; владелец — «и там, и там»): под плиткой со Крушением — ряд железных звеньев,
    /// удар зажигает следующее цветом формы; кольцо плитки светится и гаснет по кругу, пока открыто окно
    /// следующего нажатия; после последнего удара звенья вспыхивают, плитка уходит в перезарядку (как всегда).
    /// Звенья над героем — PelagWreckSeriesView. Дуга точек над плиткой — усиления, её не трогаем.
    ///
    /// Данные — только снимок WreckState и сборка слота (форма, «Четвёртый удар»): сроков окна здесь нет.
    /// Узлы — HudWreckSeries на каждой плитке способности (CombatHudWcBuilder, миграция v5); в старом префабе без
    /// них — ничего.
    /// </summary>
    public sealed partial class CombatHudView
    {
        [UnityEngine.Header("Крушение — серия (06.10)")]
        [UnityEngine.Tooltip("Индикатор серии по плиткам способностей (звенья и кольцо окна); пусто — старый префаб")]
        public HudWreckSeries[] WreckSeries = new HudWreckSeries[4];

        static readonly int WreckDefinitionId = AbilityDefinition.WreckId;

        void RefreshWreckSeries(Simulation sim, TickDriver driver)
        {
            if (WreckSeries == null) return;
            int tick = sim.Tick - 1;
            float alpha = driver != null ? driver.Alpha : 0f;
            WreckState state = sim.Wreck;
            for (int slot = 0; slot < WreckSeries.Length && slot < DashSlot; slot++)
            {
                HudWreckSeries series = WreckSeries[slot];
                if (series == null) continue;
                AbilityBuild build = sim.GetAbility(slot);
                if (build == null || build.DefinitionId != WreckDefinitionId)
                {
                    series.Hide();
                    continue;
                }
                int links = HudWreckSeriesRules.LinkCount(build.Has(AbilityFlag.WreckFourthStrike));
                series.Show(state, slot, links, tick, alpha, build.Form);
            }
        }
    }
}
