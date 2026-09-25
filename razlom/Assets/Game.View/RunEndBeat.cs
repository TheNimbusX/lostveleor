using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// Пауза между концом забега и экраном итогов (аудит UI, этап 2 — «моменты»). Раньше итоги
    /// вставали в тот же тик, что и смерть, со звоном награды, как у победы, — а R четвёртой
    /// способности, нажатая в бою, тут же запускала забег заново.
    ///
    /// Смерть: мир замедляется и теряет цвет, звучит своя фраза, итоги — через 1,2 с.
    /// Победа: короткое замедление последнего удара и своя фраза, итоги — через 0,8 с.
    /// Уход с добычей: без замедления, итоги — через 0,45 с. Пока пауза идёт, команды экрана
    /// итогов не принимаются (<see cref="Holding"/>). Sim здесь ни при чём: забег уже кончился,
    /// меняются только время кадра, цвет и момент показа.
    /// </summary>
    public sealed class RunEndBeat : MonoBehaviour
    {
        [Header("Когда показать итоги, секунды")]
        public float DeathDelay = 1.2f;
        public float VictoryDelay = .8f;
        public float LeaveDelay = .45f;

        [Header("Замедление мира")]
        [Range(.05f, 1f)] public float DeathSlow = .3f;
        public float DeathSlowFor = .7f;
        [Range(.05f, 1f)] public float VictorySlow = .5f;
        public float VictorySlowFor = .35f;
        [Tooltip("За сколько секунд время возвращается к обычному")] public float SlowRelease = .25f;

        [Header("Смерть: мир теряет цвет")]
        public float DeathSaturation = -70f;
        public float DeathExposure = -.35f;
        [Tooltip("За сколько секунд цвет уходит")] public float DeathFade = .8f;

        static RunEndBeat _instance;

        /// <summary>Забег уже кончился, а экран итогов ещё не показан.</summary>
        public static bool Holding => _instance != null && _instance._holding;

        TickDriver _driver;
        GameMode _mode = GameMode.Camp;
        RunOutcome _outcome;
        float _start = -1f;
        bool _holding, _slowing;
        Volume _volume;
        VolumeProfile _profile;

        void Awake()
        {
            _instance = this;
            _driver = GetComponent<TickDriver>();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            Finish();
            if (_volume != null) Destroy(_volume.gameObject);
            if (_profile != null) Destroy(_profile);
        }

        void LateUpdate()
        {
            GameSession session = _driver != null ? _driver.Session : null;
            GameMode mode = session != null ? session.Mode : GameMode.Camp;
            if (mode != _mode)
            {
                if (mode == GameMode.Summary && _mode == GameMode.Rift) Begin(session.LastRun.Outcome);
                else if (mode != GameMode.Summary) Finish();
                _mode = mode;
            }
            if (_start < 0f) return;

            float t = Time.unscaledTime - _start;
            bool died = _outcome == RunOutcome.Died, won = _outcome == RunOutcome.Completed;
            float delay = died ? DeathDelay : won ? VictoryDelay : LeaveDelay;
            if (_holding && t >= delay) _holding = false;

            if (_slowing)
            {
                float slow = died ? DeathSlow : VictorySlow, hold = died ? DeathSlowFor : VictorySlowFor;
                float scale = t < hold ? slow : Mathf.Lerp(slow, 1f, Mathf.SmoothStep(0f, 1f, (t - hold) / Mathf.Max(.01f, SlowRelease)));
                if (t >= hold + SlowRelease) { scale = 1f; _slowing = false; }
                // Пауза и меню ставят время в ноль и сами его вернут — не спорим с ними.
                if (!_driver.GameplayPaused && Time.timeScale > 0f) Time.timeScale = scale;
            }
            if (died && _volume != null) _volume.weight = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(.01f, DeathFade));
        }

        void Begin(RunOutcome outcome)
        {
            _outcome = outcome;
            _start = Time.unscaledTime;
            _holding = true;
            bool died = outcome == RunOutcome.Died, won = outcome == RunOutcome.Completed;
            _slowing = died || won;
            if (died)
            {
                EnsureVolume();
                if (_volume != null) { _volume.weight = 0f; _volume.gameObject.SetActive(true); }
            }
            GameSound.Play(died ? "run_death" : won ? "run_victory" : "run_leave", died || won ? .9f : .8f, 0f, .5f);
        }

        /// <summary>Итоги закрыты (лагерь или новый забег): время и цвет — обычные.</summary>
        void Finish()
        {
            _start = -1f;
            _holding = false;
            if (_slowing && !(_driver != null && _driver.GameplayPaused) && Time.timeScale > 0f) Time.timeScale = 1f;
            _slowing = false;
            if (_volume != null) { _volume.weight = 0f; _volume.gameObject.SetActive(false); }
        }

        void EnsureVolume()
        {
            if (_volume != null) return;
            // Слой — как у Volume сцены: его видит маска камеры.
            Volume scene = FindAnyObjectByType<Volume>();
            // Без DontSave: объект живёт под TickDriver и уходит вместе с ним при выходе из Play.
            var root = new GameObject("Конец забега — обесцвечивание");
            root.layer = scene != null ? scene.gameObject.layer : 0;
            root.transform.SetParent(transform, false);
            _volume = root.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Конец забега";
            var colour = _profile.Add<ColorAdjustments>(true);
            colour.saturation.Override(DeathSaturation);
            colour.postExposure.Override(DeathExposure);
            _volume.sharedProfile = _profile;
        }
    }
}
