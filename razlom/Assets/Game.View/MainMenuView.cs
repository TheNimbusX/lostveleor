using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Главное меню: пауза до PLAY, три кнопки из HUD-пака и тема.
    ///
    /// Сама сцена — параллакс из слоёв — живёт в <see cref="MainMenuScene"/>
    /// на спрайтах и своей камере. Здесь остаётся то, что IMGUI делает хорошо:
    /// неподвижные кнопки, которым округление до пикселя как раз нужно.
    /// Разметка кнопок — из manifest.json пака, в долях холста 1672×941.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(-300)]
    public sealed class MainMenuView : MonoBehaviour
    {
        private const float CanvasWidth = MainMenuScene.CanvasWidth;
        private const float CanvasHeight = MainMenuScene.CanvasHeight;
        private const float MusicFadeSeconds = 0.6f;
        /// <summary>Сглаживание курсора, 1/с: сцена догоняет мышь, а не прилипает к ней.</summary>
        private const float PointerSmoothing = 4f;

        /// <summary>
        /// Открыто ли меню. Статика нужна PauseMenu: настройки из меню
        /// открываются поверх него, и их закрытие обязано вернуть паузу, а не
        /// запустить игру за спиной у игрока.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private static readonly Vector2 PlayCenter = new Vector2(0.5149522f, 0.5807651f);
        private static readonly Vector2 PlaySize = new Vector2(400f, 125f);
        private static readonly Vector2 SettingsCenter = new Vector2(0.9108852f, 0.9373007f);
        private static readonly Vector2 ExitCenter = new Vector2(0.9659091f, 0.9373007f);
        private static readonly Vector2 DiamondSize = new Vector2(84f, 84f);

        private TickDriver _driver;
        private PauseMenu _pause;
        private AudioSource _music;
        private MainMenuScene _scene;
        private Texture2D _flatBackground;
        private Texture2D[] _play;
        private Texture2D[] _settings;
        private Texture2D[] _exit;
        private Texture2D _focus;

        private Vector2 _pointer;

        private int _hovered = -1;
        private int _pressed = -1;
        private bool _started;
        private bool _released;
        private float _musicFade = -1f;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        private void Awake()
        {
            // Съёмка не нажимает кнопок: с открытым меню capture.ps1 записал бы
            // заставку вместо игры. Поэтому под -razlom-capture меню не живёт.
            if (CaptureRig.Installed && !CaptureRig.MainMenuCapture)
            {
                enabled = false;
                return;
            }

            _driver = GetComponent<TickDriver>();
            _pause = GetComponent<PauseMenu>();
            GameUserSettings.Load();

            // Плоская картинка — запасной путь, если слои не доехали: меню не
            // должно превращаться в пустой экран из-за одного файла.
            _scene = MainMenuScene.TryCreate();
            if (_scene == null) _flatBackground = Load("UI/MainMenu/MainMenu_Background");

            _play = LoadStates("play");
            _settings = LoadStates("settings");
            _exit = LoadStates("exit");
            _focus = Load("UI/MainMenu/focus_primary");

            AudioClip theme = Resources.Load<AudioClip>("Audio/Music/MainMenuTheme");
            if (theme != null)
            {
                _music = gameObject.AddComponent<AudioSource>();
                _music.clip = theme;
                _music.loop = true;
                _music.playOnAwake = false;
                _music.spatialBlend = 0f;
                // Мастер-громкость живёт на AudioListener, здесь только музыка.
                _music.volume = GameUserSettings.MusicGain;
            }
        }

        /// <summary>
        /// После перекомпиляции во время Play ссылка на сцену меню теряется —
        /// это обычное поле, Unity его не переносит, — а сами объекты сцены
        /// остаются. Без этой уборки их камера рисовала бы голубым поверх игры
        /// до конца сессии.
        /// </summary>
        private void OnEnable()
        {
            if (_scene == null) MainMenuScene.DestroyLeftovers();
        }

        private void Start()
        {
            if (!enabled) return;
            IsOpen = true;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
            if (_driver != null) _driver.SetGameplayPaused(true);
            if (_music != null) _music.Play();

            // Игровую камеру меню НЕ трогает. Раньше ей обнуляли маску, чтобы
            // лагерь не рисовался под сценой впустую, и возвращали её после
            // PLAY. Возврат стоял после снятия паузы, и если что-то между ними
            // падало, игра шла вслепую: HUD есть, мира нет, голубой экран.
            // Экономия на экране меню копеечная, а камера меню и так кроет кадр.
        }

        private void StartGame()
        {
            if (_started) return;
            _started = true;
            IsOpen = false;

            // Сцена гаснет ПЕРВОЙ: что бы ни случилось дальше, камера меню не
            // останется рисовать поверх игры.
            _scene?.Hide();

            Time.timeScale = 1f;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            _musicFade = MusicFadeSeconds;
            if (_driver != null) _driver.SetGameplayPaused(false);
        }

        private void Update()
        {
            // Всё время меню — нескалированное: на этом экране timeScale равен
            // нулю, и обычная дельта остановила бы и сцену, и затухание темы.
            float dt = Time.unscaledDeltaTime;

            // Съёмка меню: стенд кнопок не нажимает, поэтому PLAY — сам.
            if (CaptureRig.MainMenuCapture && IsOpen && !_started && Time.unscaledTime >= 3f) StartGame();

            // Состояние камер после PLAY — в лог, в редакторе и под съёмкой.
            // Появилось из-за голубого экрана после PLAY, который по коду не
            // объяснялся: нужно видеть, какая камера что рисует на самом деле.
            if (_started && _diagnosticFrames <= 120 && (CaptureRig.MainMenuCapture || Application.isEditor))
            {
                _diagnosticFrames++;
                if (_diagnosticFrames == 2 || _diagnosticFrames == 30 || _diagnosticFrames == 120)
                    LogCameras(_diagnosticFrames);
            }

            if (IsOpen && !_started)
            {
                // Настройки открываются ПОВЕРХ меню, и их закрытие снимает
                // паузу. Пока меню открыто, пауза восстанавливается здесь.
                if (_pause == null || !_pause.IsOpen)
                {
                    if (Time.timeScale != 0f) Time.timeScale = 0f;
                    if (_driver != null) _driver.SetGameplayPaused(true);
                    _pointer = Vector2.Lerp(_pointer, ReadPointer(), 1f - Mathf.Exp(-PointerSmoothing * dt));
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Просмотр тёмной версии замка, пока не решено, когда её
                // показывать. В релизной сборке клавиши нет.
                if (_scene != null && VersionKeyPressed()) _scene.SetDark(!_scene.Dark);
#endif
                _scene?.Tick(_pointer, dt, Time.unscaledTime);
            }

            // Сцена уходит в первом же кадре игры: сразу после PLAY игровая
            // камера снова рисует мир.
            if (_started && _scene != null)
            {
                _scene.Dispose();
                _scene = null;
            }

            if (_musicFade > 0f && _music != null)
            {
                _musicFade -= dt;
                _music.volume = GameUserSettings.MusicGain * Mathf.Clamp01(_musicFade / MusicFadeSeconds);
                if (_musicFade <= 0f)
                {
                    _music.Stop();
                    _musicFade = -1f;
                }
            }
            else if (IsOpen && _music != null && !_started)
            {
                // Ползунок музыки в настройках слышен сразу: меню — единственное
                // место, где эту громкость и проверяют на слух.
                _music.volume = GameUserSettings.MusicGain;
            }

            if (_started && !_released && _musicFade < 0f) ReleaseArt();
        }

        /// <summary>
        /// Курсор в долях экрана от −1 до 1, ось Y вниз. Читается из ввода
        /// напрямую, а не из событий IMGUI: те приходят рывками, по событию.
        /// </summary>
        private static Vector2 ReadPointer()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return Vector2.zero;
            Vector2 mouse = Mouse.current.position.ReadValue();
#else
            Vector2 mouse = Input.mousePosition;
#endif
            float x = mouse.x / Mathf.Max(1f, Screen.width) * 2f - 1f;
            float y = 1f - mouse.y / Mathf.Max(1f, Screen.height) * 2f;
            return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
        }

        private void OnGUI()
        {
            if (!IsOpen || _started) return;

            // Под открытыми настройками только сцена: кнопки под затемнением
            // ловили бы наведение и клики сквозь чужой экран.
            if (_pause != null && _pause.IsOpen)
            {
                _hovered = -1;
                _pressed = -1;
                return;
            }
            GUI.depth = -1000;

            // Холст кроет экран целиком — так же камера сцены кадрирует слои,
            // поэтому кнопки совпадают с артом.
            float scale = Mathf.Max(Screen.width / CanvasWidth, Screen.height / CanvasHeight);
            float width = CanvasWidth * scale;
            float height = CanvasHeight * scale;
            var canvas = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            if (_scene == null && _flatBackground != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(canvas, _flatBackground, ScaleMode.StretchToFill);

            Rect play = Place(canvas, scale, PlayCenter, PlaySize);
            Rect settings = Place(canvas, scale, SettingsCenter, DiamondSize);
            Rect exit = Place(canvas, scale, ExitCenter, DiamondSize);

            Vector2 pointer = Event.current.mousePosition;
            _hovered = play.Contains(pointer) ? 0
                : settings.Contains(pointer) ? 1
                : exit.Contains(pointer) ? 2 : -1;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                _pressed = _hovered;
            else if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (_pressed >= 0 && _pressed == _hovered) Activate(_pressed);
                _pressed = -1;
            }

            // Свечение под PLAY только на наведении: кнопка и так самая крупная
            // на экране, постоянный ореол превратил бы её в мигающий баннер.
            if (_focus != null && _hovered == 0)
            {
                Rect glow = play;
                glow.x -= play.width * 0.18f;
                glow.y -= play.height * 0.45f;
                glow.width += play.width * 0.36f;
                glow.height += play.height * 0.9f;
                GUI.DrawTexture(glow, _focus, ScaleMode.StretchToFill, true);
            }

            DrawButton(play, _play, 0);
            DrawButton(settings, _settings, 1);
            DrawButton(exit, _exit, 2);
        }

        private void Activate(int index)
        {
            switch (index)
            {
                case 0:
                    StartGame();
                    break;
                case 1:
                    if (_pause != null) _pause.OpenSettings();
                    break;
                case 2:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        private void DrawButton(Rect rect, Texture2D[] states, int index)
        {
            if (states == null) return;
            Texture2D texture = _pressed == index ? states[2]
                : _hovered == index ? states[1]
                : states[0];
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }

        /// <summary>Прямоугольник элемента пака в координатах экрана.</summary>
        private static Rect Place(Rect canvas, float scale, Vector2 center, Vector2 size)
        {
            float w = size.x * scale;
            float h = size.y * scale;
            return new Rect(
                canvas.x + canvas.width * center.x - w * 0.5f,
                canvas.y + canvas.height * center.y - h * 0.5f,
                w, h);
        }

        private void ReleaseArt()
        {
            _released = true;
            Unload(_flatBackground);
            Unload(_focus);
            foreach (Texture2D[] states in new[] { _play, _settings, _exit })
                if (states != null)
                    foreach (Texture2D state in states) Unload(state);
        }

        private static void Unload(Texture2D texture)
        {
            if (texture != null) Resources.UnloadAsset(texture);
        }

        private static Texture2D[] LoadStates(string id) => new[]
        {
            Load($"UI/MainMenu/btn_{id}_normal"),
            Load($"UI/MainMenu/btn_{id}_hover"),
            Load($"UI/MainMenu/btn_{id}_pressed"),
        };

        private static Texture2D Load(string path)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"[Разлом] Главное меню: нет ассета {path}.");
            return texture;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool VersionKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F2);
#endif
        }
#endif

        private int _diagnosticFrames;

        private void LogCameras(int frame)
        {
            var log = new System.Text.StringBuilder();
            log.Append($"[main-menu] +{frame} кадров после PLAY: timeScale={Time.timeScale} ")
               .Append($"paused={(_driver != null && _driver.GameplayPaused)} ")
               .Append($"mode={_driver?.Session?.Mode} main={(Camera.main != null ? Camera.main.name : "null")}");
            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                Transform tr = camera.transform;
                log.Append($"\n  «{camera.name}» enabled={camera.enabled} active={camera.gameObject.activeInHierarchy} ")
                   .Append($"depth={camera.depth} mask=0x{camera.cullingMask:X} clear={camera.clearFlags} ")
                   .Append($"bg={camera.backgroundColor} pos={tr.position} rot={tr.eulerAngles} ")
                   .Append($"ortho={camera.orthographic}/{camera.orthographicSize} near={camera.nearClipPlane} far={camera.farClipPlane} ")
                   .Append($"rt={(camera.targetTexture != null ? camera.targetTexture.name : "-")}");
            }
            Debug.Log(log.ToString());
        }

        private void OnDestroy()
        {
            if (IsOpen) IsOpen = false;
            _scene?.Dispose();
            _scene = null;
        }
    }
}
