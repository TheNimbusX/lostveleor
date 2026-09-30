using Game.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Монеты в строку добычи (выбор владельца 30.09, кадр 1a): золото забега прибавилось — из места, где оно
    /// взялось, вылетает горсть монет (3–6) и мягкой дугой летит в знак золота строки; число досчитывает, когда
    /// монеты садятся, знак толкается на каждой. Источник — последний убитый враг (награда опасной арены
    /// приходит с последним ударом), иначе герой (разобранная способность — у его ног). Монет в воздухе не
    /// больше пула; под завесой перехода, в паузе и без строки добычи не летят — число досчитывает само.
    /// Правила — RunHudCoins, пул — миграция v3 (RunHudWcBuilder.Polish).
    /// </summary>
    public sealed partial class RunHud
    {
        struct CoinFlight
        {
            public bool Active;
            public float Start;
            public int Index, Share;
            public float FromX, FromY;
            public RawImage Art;
        }

        readonly CoinFlight[] _coins = new CoinFlight[RunHudCoins.Pool];
        /// <summary>Золото в воздухе: число строки его ещё не показывает.</summary>
        int _coinPending;
        /// <summary>Золото забега, уже разложенное по монетам или отданное числу; меньше нуля — не начато.</summary>
        int _coinSeen = -1;
        Vector3 _coinSourceWorld;
        float _coinSourceAt = -100f;
        bool _coinsBound;

        /// <summary>Новый забег (или золото убавилось): монет нет, число — как есть.</summary>
        private void ResetCoins(int gold)
        {
            _coinSeen = gold;
            SettleCoins();
        }

        /// <summary>Все монеты садятся разом: завеса, пауза, строка скрыта.</summary>
        private void SettleCoins()
        {
            _coinPending = 0;
            for (int i = 0; i < _coins.Length; i++)
            {
                if (!_coins[i].Active) continue;
                _coins[i].Active = false;
                if (_coins[i].Art != null) RunHudView.SetActive(_coins[i].Art, false);
            }
        }

        /// <summary>
        /// Сколько золота показывать строке: золото забега без летящего. Новое золото раскладывается на
        /// монеты (если есть откуда и куда лететь); монеты летят, севшие отдают свою долю числу.
        /// </summary>
        private int CoinGold(RiftRun run)
        {
            if (_coinSeen < 0 || run.Gold < _coinSeen) ResetCoins(run.Gold);
            int delta = run.Gold - _coinSeen;
            if (delta > 0)
            {
                _coinSeen = run.Gold;
                LaunchCoins(run, delta);
            }
            AdvanceCoins();
            return run.Gold - _coinPending;
        }

        private void BindCoins()
        {
            if (_coinsBound) return;
            _coinsBound = true;
            RectTransform[] pool = _view.Coins;
            for (int i = 0; i < _coins.Length; i++)
            {
                RawImage art = pool != null && i < pool.Length && pool[i] != null ? pool[i].GetComponent<RawImage>() : null;
                _coins[i].Art = art;
                if (art != null) RunHudView.SetActive(art, false);
            }
        }

        private void LaunchCoins(RiftRun run, int amount)
        {
            if (_view.CoinLayer == null || _view.LootCoin == null || !_view.LootCoin.isActiveAndEnabled) return;
            BindCoins();
            int free = 0;
            for (int i = 0; i < _coins.Length; i++) if (!_coins[i].Active && _coins[i].Art != null) free++;
            int count = RunHudCoins.CountFor(amount, free);
            if (count <= 0 || !CoinSource(run, out Vector2 screen)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_view.CoinLayer, screen, null, out Vector2 from)) return;
            float now = UiMotion.Now;
            int launched = 0;
            for (int i = 0; i < _coins.Length && launched < count; i++)
            {
                if (_coins[i].Active || _coins[i].Art == null) continue;
                int share = RunHudCoins.Share(amount, count, launched);
                _coins[i].Active = true;
                _coins[i].Start = now;
                _coins[i].Index = launched;
                _coins[i].Share = share;
                _coins[i].FromX = from.x;
                _coins[i].FromY = from.y;
                _coinPending += share;
                RectTransform rect = _coins[i].Art.rectTransform;
                rect.localPosition = new Vector3(from.x, from.y, 0f);
                rect.localScale = Vector3.zero;
                RunHudView.SetActive(_coins[i].Art, true);
                launched++;
            }
        }

        /// <summary>Откуда летят: свежий убитый враг, иначе герой. Точка — на экране (пиксели), за камерой — нет.</summary>
        private bool CoinSource(RiftRun run, out Vector2 screen)
        {
            screen = default;
            Camera camera = Camera.main;
            if (camera == null) return false;
            Vector3 world = _coinSourceWorld;
            if (UiMotion.Now - _coinSourceAt > RunHudCoins.SourceFreshness)
            {
                FixVec2 hero = run.Sim.Entities.Position[Simulation.PlayerId];
                world = new Vector3(hero.X.ToFloat(), 1.2f, hero.Y.ToFloat());
            }
            Vector3 projected = camera.WorldToScreenPoint(world);
            if (projected.z <= 0f) return false;
            screen = new Vector2(Mathf.Clamp(projected.x, 0f, Screen.width), Mathf.Clamp(projected.y, 0f, Screen.height));
            return true;
        }

        /// <summary>Шаг монет: дуга, размер, прозрачность; севшая отдаёт долю числу и толкает знак золота.</summary>
        private void AdvanceCoins()
        {
            if (_coinPending <= 0 && !AnyCoin()) return;
            Vector3 target = _view.CoinLayer != null && _view.LootCoin != null
                ? _view.CoinLayer.InverseTransformPoint(_view.LootCoin.rectTransform.position)
                : Vector3.zero;
            float now = UiMotion.Now;
            for (int i = 0; i < _coins.Length; i++)
            {
                if (!_coins[i].Active) continue;
                ref CoinFlight coin = ref _coins[i];
                float t = RunHudCoins.Progress(coin.Index, now - coin.Start);
                if (t >= 1f)
                {
                    coin.Active = false;
                    _coinPending = Mathf.Max(0, _coinPending - coin.Share);
                    RunHudView.SetActive(coin.Art, false);
                    continue;
                }
                RectTransform rect = coin.Art.rectTransform;
                if (t < 0f)
                {
                    rect.localScale = Vector3.zero;
                    continue;
                }
                RunHudCoins.Arc(coin.FromX, coin.FromY, target.x, target.y, coin.Index, t, out float x, out float y);
                rect.localPosition = new Vector3(x, y, 0f);
                rect.localScale = Vector3.one * RunHudCoins.Scale(t);
                // Монета чуть крутится в полёте: чётные — по часовой, нечётные — против.
                rect.localRotation = Quaternion.Euler(0f, 0f, (coin.Index % 2 == 0 ? -1f : 1f) * 220f * t);
                Color colour = coin.Art.color;
                colour.a = RunHudCoins.Alpha(t);
                coin.Art.color = colour;
            }
        }

        private bool AnyCoin()
        {
            for (int i = 0; i < _coins.Length; i++) if (_coins[i].Active) return true;
            return false;
        }

        // ---------------------------------------------------------------- события кадра

        /// <summary>
        /// События кадра для монет и итогов: где умер последний враг (оттуда вылетит золото опасной арены) и
        /// урон по телам — портрет вида для «Убито» снимается со спокойного живого тела (RunHud.Portraits).
        /// </summary>
        private void TrackFrameEvents(RiftRun run)
        {
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type != SimEventType.Death || e.Target == Simulation.PlayerId) continue;
                if ((uint)e.Target >= (uint)run.Sim.Entities.Count) continue;
                FixVec2 at = run.Sim.Entities.Position[e.Target];
                _coinSourceWorld = new Vector3(at.X.ToFloat(), 1.2f, at.Y.ToFloat());
                _coinSourceAt = UiMotion.Now;
            }
            // Портреты видов для «Убито» — с живого спокойного тела, не с убийства (RunHud.Portraits).
            WatchPortraits(run, events);
        }
    }
}
