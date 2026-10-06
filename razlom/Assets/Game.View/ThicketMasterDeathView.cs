using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — НАЕЗД КАМЕРЫ НА СМЕРТИ (владелец 02.10, вечер: «смерть надо доработать»). Добивающий
    /// удар — камера заметно подъезжает к боссу (CameraFollow.SetCinematic: 6,2 → 4,8, кадр на 80 % пути к
    /// телу, но не дальше 4,3 м от героя — герой в кадре), держится, пока тело валится на бок и бежит волна
    /// цветения (до land + 0,5 с), и плавно отпускает героя (ReleaseCinematic) — правила
    /// <see cref="ThicketMasterDeathRules"/>. Ввод не трогает: герой ходит как ходил, двигается только кадр.
    ///
    /// Плашка «АРЕНА ЗАЧИЩЕНА» (CombatHudView) на смерти босса ждёт, пока встанет холм
    /// (<see cref="HoldsClearedBanner"/>, ThicketMasterDeathRules.ClearedBannerAt): смерть помнится отдельно
    /// от наезда — до своей плашки.
    ///
    /// Время — тики Sim от события Death (пауза держит кадр и плашку). Наезд снимается сразу и отдаёт камеру:
    /// конец наезда, новая симуляция или поколение, смена арены (глубина), выход из боя Разлома, конец забега
    /// или выбор награды, гибель героя, выключение. Идёт кат-сцена вступления (её камера) — наезда нет.
    /// Ставит ThicketMasterCombatView (Awake) одной строкой <see cref="EnsureOn"/>.
    /// </summary>
    [DefaultExecutionOrder(675)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ThicketMasterDeathView : MonoBehaviour
    {
        private const int None = int.MinValue;

        private TickDriver _driver;
        private CameraFollow _follow;
        private ThicketMasterIntroView _intro;
        private Simulation _shown;
        private int _generation = -1, _depth = -1;

        /// <summary>Камера у нас (SetCinematic в этом кадре); наезд этой смерти уже отдан — второй раз не берём.</summary>
        private bool _active, _pushOver;
        private int _boss = -1, _deathTick = None;
        private float _land;
        private Vector3 _focus;

        public static ThicketMasterDeathView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<ThicketMasterDeathView>();
            return view != null ? view : host.AddComponent<ThicketMasterDeathView>();
        }

        /// <summary>Наезд идёт: камера сейчас у смерти босса.</summary>
        public bool Playing => _active;

        /// <summary>
        /// Плашка «АРЕНА ЗАЧИЩЕНА» ждёт: на арене этого драйвера убит Хозяин Чащи, а холм ещё не встал
        /// (ThicketMasterDeathRules.ClearedBannerAt, по тикам Sim — пауза держит). Нет вида смерти (арена
        /// без босса) — false. Смерть читается и здесь: HUD спрашивает раньше LateUpdate этого вида.
        /// </summary>
        public static bool HoldsClearedBanner(TickDriver driver)
            => driver != null && driver.TryGetComponent(out ThicketMasterDeathView view) && view.BannerWaits();

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _intro = GetComponent<ThicketMasterIntroView>();
        }

        private void OnDisable()
        {
            Release();
            Forget();
        }

        private void LateUpdate()
        {
            Simulation sim = Observe();
            if (sim == null || _deathTick == None || _pushOver) return;
            if (!Holds(sim)) { EndPush(); return; }

            float seconds = Seconds(sim);
            if (seconds < 0f || ThicketMasterDeathRules.PushDone(seconds, _land)) { EndPush(); return; }
            if (_intro == null) _intro = GetComponent<ThicketMasterIntroView>();
            // Кат-сцена вступления ведёт камеру сама (босс убит в её окне) — наезда нет.
            if (_intro != null && _intro.Playing) { EndPush(); return; }
            if (_follow == null) _follow = FindAnyObjectByType<CameraFollow>();
            if (_follow == null) return;

            float push = ThicketMasterDeathRules.Push(seconds, _land);
            Vector3 hero = _driver.GetRenderPosition(Simulation.PlayerId);
            Vector3 toFocus = _focus - hero;
            toFocus.y = 0f;
            float blend = ThicketMasterDeathRules.CameraBlend(push, toFocus.magnitude);
            _follow.SetCinematic(_focus, blend, ThicketMasterDeathRules.CameraZoom(push));
            _active = true;
        }

        /// <summary>
        /// Сверка с драйвером и чтение смерти этого кадра (повторный вызов в том же кадре ничего не меняет).
        /// Null — не бой Разлома: всё отдано и забыто.
        /// </summary>
        private Simulation Observe()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            int generation = _driver != null ? _driver.Generation : -1;
            int depth = _driver != null && _driver.Run != null ? _driver.Run.Depth : -1;
            if (!ReferenceEquals(sim, _shown) || generation != _generation || depth != _depth)
            {
                // Новый забег, «К боссу», лагерь, следующая арена: смерть прошлого босса уже не наша.
                Release();
                Forget();
                _shown = sim;
                _generation = generation;
                _depth = depth;
            }
            if (sim == null || _driver.Session == null || _driver.Session.Mode != GameMode.Rift)
            {
                Release();
                Forget();
                return null;
            }
            ReadDeath(sim);
            return sim;
        }

        /// <summary>Плашка ждёт холма: смерть есть и до ClearedBannerAt ещё не дошло.</summary>
        private bool BannerWaits()
        {
            Simulation sim = Observe();
            if (sim == null || _deathTick == None) return false;
            float seconds = Seconds(sim);
            return seconds >= 0f && seconds < ThicketMasterDeathRules.ClearedBannerAt(_land);
        }

        /// <summary>Секунды от тика смерти — по тикам Sim с долей кадра (пауза держит).</summary>
        private float Seconds(Simulation sim) => (sim.Tick - 1 + _driver.Alpha - _deathTick) / Simulation.TicksPerSecond;

        /// <summary>Событие смерти Хозяина Чащи в этом кадре: тик и точка кадра (тело на земле + подъём).</summary>
        private void ReadDeath(Simulation sim)
        {
            IReadOnlyList<FrameEventContext> events = _driver.FrameEventContexts;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i].Event;
                if (e.Type != SimEventType.Death) continue;
                if ((uint)e.Target >= (uint)sim.Entities.Count || sim.Entities.Kind[e.Target] != EnemyKind.ForestThicketMaster) continue;
                int tick = events[i].SimulationTick - 1;
                if (_deathTick == tick && _boss == e.Target) continue;
                Release();
                _pushOver = false;
                _boss = e.Target;
                _deathTick = tick;
                var beat = EnemyPresentationProfile.Kill(EnemyKind.ForestThicketMaster, false, false);
                _land = beat.LandsAt > 0f ? beat.LandsAt
                    : beat.HitStopSeconds + EnemyPresentationProfile.Death(EnemyKind.ForestThicketMaster).FallSeconds;
                _focus = _driver.GetRenderPosition(_boss) + Vector3.up * ThicketMasterDeathRules.FocusLift;
            }
        }

        /// <summary>Наезду есть место: герой жив и бой Разлома идёт (не итоги, не выбор награды).</summary>
        private bool Holds(Simulation sim)
        {
            if (!sim.Entities.Alive[Simulation.PlayerId]) return false;
            RiftRun run = _driver.Run;
            return run == null || run.Phase == RunPhase.Clearing || run.Phase == RunPhase.SeekingExit;
        }

        /// <summary>Наезд этой смерти кончился или прерван: камеру отдать, второй раз не брать (смерть помнится для плашки).</summary>
        private void EndPush()
        {
            Release();
            _pushOver = true;
        }

        /// <summary>Отдать камеру, если она у нас.</summary>
        private void Release()
        {
            if (_active && _follow != null) _follow.ReleaseCinematic();
            _active = false;
        }

        /// <summary>Забыть смерть (и наезд, и плашку).</summary>
        private void Forget()
        {
            _boss = -1;
            _deathTick = None;
            _pushOver = false;
        }
    }
}
