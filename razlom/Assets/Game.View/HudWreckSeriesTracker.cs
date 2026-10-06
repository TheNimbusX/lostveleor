using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Память индикатора серии одной плитки (без Unity, тесты — HudWreckSeriesRulesTests): снимок WreckState
    /// помнит только последний удар, а звену нужен тик СВОЕГО удара (вскакивание), а ряду — тик конца серии
    /// (звенья гаснут не разом). Кормится снимком каждый кадр — HUD не пул эффектов, опрос снимка здесь честен.
    ///
    /// Тик удара: пока этап ещё этот (после контакта, до следующего нажатия) — ContactTick снимка; если кадр
    /// пропустил контакт и следующий замах уже идёт — тик перед его нажатием. Серия, которую HUD застал уже
    /// кончившейся (открыли HUD, сменили расстановку), ничего не зажигает.
    /// </summary>
    public sealed class HudWreckSeriesTracker
    {
        private const int NotEnded = int.MaxValue, LongAgo = int.MinValue / 2;

        private readonly int[] _strikeTick = new int[HudWreckSeriesRules.MaxLinks];
        private int _serial = -1, _known, _endTick = NotEnded;

        /// <summary>Забыть серию (у плитки больше нет Крушения или сменилась симуляция).</summary>
        public void Reset()
        {
            _serial = -1;
            _known = 0;
            _endTick = NotEnded;
        }

        /// <summary>
        /// Кадр плитки <paramref name="slot"/> с <paramref name="links"/> звеньями. <paramref name="tick"/> — тик
        /// показа (sim.Tick − 1), <paramref name="alpha"/> — доля кадра между тиками.
        /// </summary>
        public HudWreckSeriesShow Observe(in WreckState s, int slot, int links, int tick, float alpha)
        {
            var show = new HudWreckSeriesShow { Links = links, Newest = -1, NewestAge = float.MaxValue, Window = -1f };
            if (links <= 0) return show;
            // Серии этой плитки нет (ни одной за расстановку или она в другом слоте): тёмные звенья.
            if (s.Serial == 0 || s.Slot != slot)
            {
                Reset();
                return show;
            }
            if (s.Serial != _serial)
            {
                _serial = s.Serial;
                _known = 0;
                _endTick = s.Phase == WreckPhase.None ? LongAgo : NotEnded;
            }
            if (s.Phase == WreckPhase.None)
            {
                if (_endTick == NotEnded) _endTick = tick;
            }
            else _endTick = NotEnded;

            int strikes = s.Strikes < 0 ? 0 : s.Strikes > links ? links : s.Strikes;
            if (strikes > HudWreckSeriesRules.MaxLinks) strikes = HudWreckSeriesRules.MaxLinks;
            bool afterContact = s.Phase != WreckPhase.Windup && s.Phase != WreckPhase.Charge;
            for (; _known < strikes; _known++)
            {
                bool exact = afterContact && s.Stage == _known && s.ContactTick <= tick;
                int at = exact ? s.ContactTick : afterContact ? tick : s.StageStartTick - 1;
                // Серия, застанная кончившейся, — удары давно в прошлом: без вскакивания и вспышки.
                _strikeTick[_known] = _endTick == LongAgo ? LongAgo : at;
            }
            if (_known > strikes) _known = strikes;

            float now = tick + alpha;
            show.Lit = strikes;
            show.Glow = _endTick == NotEnded ? 1f
                : _endTick == LongAgo ? 0f
                : 1f - HudWreckSeriesRules.Smooth01((now - _endTick) / Simulation.TicksPerSecond / HudWreckSeriesRules.EndFadeSeconds);
            if (strikes > 0)
            {
                show.Newest = strikes - 1;
                int at = _strikeTick[strikes - 1];
                show.NewestAge = at == LongAgo ? float.MaxValue : (now - at) / Simulation.TicksPerSecond;
            }
            show.Final = strikes >= links;
            show.Window = s.Slot == slot ? HudWreckSeriesRules.WindowLeft(s, links, now) : -1f;
            return show;
        }
    }
}
