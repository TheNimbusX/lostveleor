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
    ///
    /// Итоги со статистикой (выбор владельца 30.09, кадр 4): в миг конца снимается стоп-кадр мира —
    /// он встаёт в тлеющий круг итогов (<see cref="FreezeFrame"/>) с подписью последнего удара; после
    /// показа итогов ввод ещё <see cref="InputLock"/> с закрыт, как у экрана награды (кольцо на кейкапах
    /// «Повторить» и «В лагерь» — <see cref="LockProgress"/>).
    /// </summary>
    public sealed class RunEndBeat : MonoBehaviour
    {
        [Header("Когда показать итоги, секунды")]
        public float DeathDelay = 1.2f;
        public float VictoryDelay = .8f;
        public float LeaveDelay = .45f;

        [Header("Итоги: блок ввода после показа")]
        [Tooltip("Сколько секунд после показа итогов кнопки и клавиши не принимаются: кольцо на кейкапах наполняется")]
        public float InputLock = RunHudChoiceLock.DefaultDuration;

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

        [Header("Стоп-кадр для итогов")]
        [Tooltip("Размер снимка мира в миг конца забега (16:9)")] public Vector2Int FreezeSize = new Vector2Int(960, 540);
        [Tooltip("Какую долю высоты кадра видно в круге итогов: меньше — ближе к герою")]
        [Range(.3f, 1f)] public float FreezeZoom = .72f;

        static RunEndBeat _instance;

        /// <summary>Забег уже кончился, а ввод экрана итогов ещё закрыт: пауза до показа и блок после.</summary>
        public static bool Holding => _instance != null && _instance._holding;

        /// <summary>Итоги пора показывать: пауза конца забега прошла (или её нет).</summary>
        public static bool ScreenDue => _instance == null || _instance._start < 0f || _instance._due;

        /// <summary>Кольцо блокировки на кейкапах итогов: 0 — только показаны, 1 — ввод открыт.</summary>
        public static float LockProgress => _instance == null ? 1f : _instance.Lock();

        /// <summary>Стоп-кадр мира в миг конца забега; null — не снят (нет камеры).</summary>
        public static Texture FreezeFrame => _instance != null && _instance._freezeTaken ? _instance._freeze : null;

        /// <summary>Квадратная часть стоп-кадра вокруг героя — uvRect круга итогов.</summary>
        public static Rect FreezeRect => _instance != null ? _instance._freezeRect : new Rect(0f, 0f, 1f, 1f);

        /// <summary>Последний удар: по герою (гибель) или героя (победа); 0 — не видели.</summary>
        public static int LastBlow => _instance != null ? _instance._blow : 0;

        /// <summary>Кто нанёс последний удар по герою или по кому пришёлся победный; None — не враг.</summary>
        public static EnemyKind LastBlowFoe => _instance != null ? _instance._blowFoe : EnemyKind.None;

        /// <summary>Последний удар пришёлся на босса или нанесён им.</summary>
        public static bool LastBlowBoss => _instance != null && _instance._blowBoss;

        TickDriver _driver;
        GameMode _mode = GameMode.Camp;
        RunOutcome _outcome;
        float _start = -1f;
        bool _holding, _slowing, _due;
        Volume _volume;
        VolumeProfile _profile;
        RenderTexture _freeze;
        bool _freezeTaken;
        Rect _freezeRect = new Rect(0f, 0f, 1f, 1f);
        int _blow;
        EnemyKind _blowFoe;
        bool _blowBoss;
        /// <summary>Забег, в котором стоп-кадр уже снят на смерти босса: победа приходит позже, на последней награде.</summary>
        RiftRun _victoryRun;

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
            if (_freeze != null)
            {
                _freeze.Release();
                Destroy(_freeze);
            }
        }

        float Delay => _outcome == RunOutcome.Died ? DeathDelay : _outcome == RunOutcome.Completed ? VictoryDelay : LeaveDelay;

        float Lock()
        {
            if (_start < 0f || !_holding) return 1f;
            if (!_due) return 0f;
            float t = Time.unscaledTime - _start - Delay;
            return InputLock <= 0f ? 1f : Mathf.Clamp01(t / InputLock);
        }

        void LateUpdate()
        {
            GameSession session = _driver != null ? _driver.Session : null;
            GameMode mode = session != null ? session.Mode : GameMode.Camp;
            if (mode == GameMode.Rift) WatchBossDeath(session.Run);
            if (mode != _mode)
            {
                if (mode == GameMode.Summary && _mode == GameMode.Rift) Begin(session);
                else if (mode != GameMode.Summary) Finish();
                _mode = mode;
            }
            if (_start < 0f) return;

            float t = Time.unscaledTime - _start;
            bool died = _outcome == RunOutcome.Died;
            float delay = Delay;
            if (!_due && t >= delay) _due = true;
            if (_holding && t >= delay + Mathf.Max(0f, InputLock)) _holding = false;

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

        void Begin(GameSession session)
        {
            RunOutcome outcome = session.LastRun.Outcome;
            _outcome = outcome;
            _start = Time.unscaledTime;
            _holding = true;
            _due = false;
            bool died = outcome == RunOutcome.Died, won = outcome == RunOutcome.Completed;
            _slowing = died || won;
            // Стоп-кадр — до обесцвечивания: в круге итогов мир в миг удара, в своём цвете. Победа приходит на
            // последней награде, когда босс давно лежит, — её кадр снят раньше, в миг смерти босса.
            bool shotAtBoss = won && _victoryRun != null && _victoryRun == session.Run && _freezeTaken;
            if (!shotAtBoss)
            {
                ReadLastBlow(session);
                CaptureFreeze(session);
            }
            _victoryRun = null;
            if (died)
            {
                EnsureVolume();
                if (_volume != null) { _volume.weight = 0f; _volume.gameObject.SetActive(true); }
            }
            GameSound.Play(died ? "run_death" : won ? "run_victory" : "run_leave", died || won ? .9f : .8f, 0f, .5f);
        }

        /// <summary>
        /// Смерть босса — победный миг: стоп-кадр и удар снимаются сейчас (героя по боссу), итоги покажут их после
        /// последней награды. Один раз за забег.
        /// </summary>
        void WatchBossDeath(RiftRun run)
        {
            if (run == null || run.BossId < 0 || _victoryRun == run || _driver == null) return;
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type != SimEventType.Death || e.Target != run.BossId) continue;
                _victoryRun = run;
                _blow = 0;
                _blowFoe = run.Sim.Entities.Kind[run.BossId];
                _blowBoss = true;
                for (int j = events.Count - 1; j >= 0; j--)
                {
                    SimEvent hit = events[j];
                    if ((hit.Type == SimEventType.Damage || hit.Type == SimEventType.DamageOverTime)
                        && hit.Source == Simulation.PlayerId && hit.Target == run.BossId) { _blow = hit.Amount; break; }
                }
                CaptureFreeze(_driver.Session);
                return;
            }
        }

        /// <summary>
        /// Последний удар из событий этого кадра (конец забега наступает в том же тике, что и удар): по герою —
        /// при гибели, героя по врагу — при победе. Число и кто — для подписи стоп-кадра.
        /// </summary>
        void ReadLastBlow(GameSession session)
        {
            _blow = 0;
            _blowFoe = EnemyKind.None;
            _blowBoss = false;
            RiftRun run = session.Run;
            if (_driver == null || run == null) return;
            var events = _driver.FrameEvents;
            bool died = _outcome == RunOutcome.Died;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                SimEvent e = events[i];
                if (e.Type != SimEventType.Damage && e.Type != SimEventType.DamageOverTime) continue;
                int foe = died ? e.Source : e.Target;
                if (died ? e.Target != Simulation.PlayerId : e.Source != Simulation.PlayerId) continue;
                _blow = e.Amount;
                if (foe > 0 && foe < run.Sim.Entities.Count)
                {
                    _blowFoe = run.Sim.Entities.Kind[foe];
                    _blowBoss = foe == run.BossId;
                }
                return;
            }
        }

        /// <summary>
        /// Снимок мира игровой камерой в свою текстуру (без интерфейса: холсты поверх экрана в неё не
        /// рисуются). Один лишний проход рендера в миг конца забега — там мир и так замедлен.
        /// </summary>
        void CaptureFreeze(GameSession session)
        {
            _freezeTaken = false;
            Camera camera = Camera.main;
            if (camera == null || FreezeSize.x < 16 || FreezeSize.y < 16) return;
            if (_freeze == null || _freeze.width != FreezeSize.x || _freeze.height != FreezeSize.y)
            {
                if (_freeze != null) { _freeze.Release(); Destroy(_freeze); }
                _freeze = new RenderTexture(FreezeSize.x, FreezeSize.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "Стоп-кадр конца забега" };
                _freeze.Create();
            }
            RenderTexture previous = camera.targetTexture;
            float aspect = FreezeSize.x / (float)FreezeSize.y;
            try
            {
                camera.targetTexture = _freeze;
                camera.aspect = aspect;
                _freezeRect = FocusRect(camera, session, aspect);
                camera.Render();
                _freezeTaken = true;
            }
            finally
            {
                camera.targetTexture = previous;
                camera.ResetAspect();
            }
        }

        /// <summary>Квадрат вокруг героя в долях кадра: круг итогов показывает его, а не угол арены.</summary>
        Rect FocusRect(Camera camera, GameSession session, float aspect)
        {
            float h = FreezeZoom, w = h / aspect;
            Vector2 focus = new Vector2(.5f, .5f);
            RiftRun run = session.Run;
            if (run != null && Simulation.PlayerId < run.Sim.Entities.Count)
            {
                FixVec2 at = run.Sim.Entities.Position[Simulation.PlayerId];
                Vector3 viewport = camera.WorldToViewportPoint(new Vector3(at.X.ToFloat(), 1f, at.Y.ToFloat()));
                if (viewport.z > 0f) focus = new Vector2(viewport.x, viewport.y);
            }
            float x = Mathf.Clamp(focus.x - w * .5f, 0f, 1f - w);
            float y = Mathf.Clamp(focus.y - h * .5f, 0f, 1f - h);
            return new Rect(x, y, w, h);
        }

        /// <summary>Итоги закрыты (лагерь или новый забег): время и цвет — обычные.</summary>
        void Finish()
        {
            _start = -1f;
            _holding = false;
            _due = false;
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
