using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Главное меню: утверждённая картинка, три кнопки из HUD-пака и тема.
    ///
    /// ВРЕМЕННОЕ РЕШЕНИЕ ДО ДЕМКИ, И ЭТО НЕ ОТГОВОРКА, А УСТРОЙСТВО. Фон здесь —
    /// одно изображение, потому что видео и параллакс решено делать после демо.
    /// Всё, что появится потом, заменяет РОВНО фон: разметка кнопок берётся из
    /// manifest.json пака в долях исходного холста, поэтому она переживёт смену
    /// подложки на видео или на слои без единой правки чисел.
    ///
    /// Рисуется на IMGUI, как PauseMenu и весь остальной HUD: заводить ради
    /// трёх кнопок Canvas и префабы значило бы завести вторую систему UI в
    /// проекте, где первая уже работает.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    [DefaultExecutionOrder(-300)]
    public sealed class MainMenuView : MonoBehaviour
    {
        /// <summary>Холст, в котором заданы координаты пака. См. manifest.json.</summary>
        private const float CanvasWidth = 1672f;
        private const float CanvasHeight = 941f;

        private const float MusicFadeSeconds = 0.6f;

        /// <summary>
        /// Открыто ли меню. Статика нужна PauseMenu: настройки из меню
        /// открываются поверх него, и закрытие настроек обязано вернуть паузу,
        /// а не запустить игру за спиной у игрока.
        /// </summary>
        public static bool IsOpen { get; private set; }

        // Разметка из manifest.json: центр в долях холста и размер в пикселях
        // холста. Доли, а не пиксели, потому что подложка ещё сменится.
        private static readonly Vector2 PlayCenter = new Vector2(0.5149522f, 0.5807651f);
        private static readonly Vector2 PlaySize = new Vector2(400f, 125f);
        private static readonly Vector2 SettingsCenter = new Vector2(0.9108852f, 0.9373007f);
        private static readonly Vector2 ExitCenter = new Vector2(0.9659091f, 0.9373007f);
        private static readonly Vector2 DiamondSize = new Vector2(84f, 84f);

        private TickDriver _driver;
        private PauseMenu _pause;
        private AudioSource _music;

        private Texture2D _background;
        private Texture2D[] _play;
        private Texture2D[] _settings;
        private Texture2D[] _exit;
        private Texture2D _focus;

        private int _hovered = -1;
        private int _pressed = -1;
        private bool _started;
        private float _musicFade = -1f;

        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        private void Awake()
        {
            // Съёмка не нажимает кнопок: с открытым меню capture.ps1 записал бы
            // заставку вместо игры. Поэтому под -razlom-capture меню не живёт.
            if (CaptureRig.Installed)
            {
                enabled = false;
                return;
            }

            _driver = GetComponent<TickDriver>();
            _pause = GetComponent<PauseMenu>();
            GameUserSettings.Load();

            _background = Load("UI/MainMenu/MainMenu_Background");
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

        private void Start()
        {
            if (!enabled) return;
            Open();
        }

        private void Open()
        {
            IsOpen = true;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
            if (_driver != null) _driver.SetGameplayPaused(true);
            if (_music != null) _music.Play();
        }

        private void StartGame()
        {
            if (_started) return;
            _started = true;
            IsOpen = false;
            Time.timeScale = 1f;
            if (_driver != null) _driver.SetGameplayPaused(false);
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            _musicFade = MusicFadeSeconds;
        }

        private void Update()
        {
            // Настройки открываются ПОВЕРХ меню, и их закрытие снимает паузу —
            // оно не знает, что под ним не игра, а заставка. Пока меню открыто,
            // пауза восстанавливается здесь.
            if (IsOpen && !_started && (_pause == null || !_pause.IsOpen))
            {
                if (Time.timeScale != 0f) Time.timeScale = 0f;
                if (_driver != null) _driver.SetGameplayPaused(true);
            }

            if (_musicFade > 0f && _music != null)
            {
                // Нескалированное время: на экране меню timeScale равен нулю, и
                // обычная дельта остановила бы затухание навсегда.
                _musicFade -= Time.unscaledDeltaTime;
                _music.volume = GameUserSettings.MusicGain * Mathf.Clamp01(_musicFade / MusicFadeSeconds);
                if (_musicFade <= 0f)
                {
                    _music.Stop();
                    _musicFade = -1f;
                }
            }
            else if (IsOpen && _music != null && !_started)
            {
                // Ползунок музыки в настройках слышен сразу, а не со следующего
                // запуска: меню — единственное место, где эту громкость и
                // проверяют на слух.
                _music.volume = GameUserSettings.MusicGain;
            }
        }

        private void OnGUI()
        {
            if (!IsOpen || _started) return;

            // Настройки открываются ПОВЕРХ меню и рисуются с той же глубиной
            // -1000. При равной глубине порядок решает очерёдность вызова
            // OnGUI — то есть случайность: панель настроек уходила под
            // подложку, и кнопка выглядела сломанной. Пока настройки открыты,
            // меню осознанно уступает им слой.
            bool settingsOpen = _pause != null && _pause.IsOpen;
            GUI.depth = settingsOpen ? -900 : -1000;

            // Подложка кроет экран целиком. Лишнее уходит за края, а не
            // растягивается: искажать утверждённую картинку нельзя.
            float scale = Mathf.Max(Screen.width / CanvasWidth, Screen.height / CanvasHeight);
            float width = CanvasWidth * scale;
            float height = CanvasHeight * scale;
            var canvas = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width, height);

            if (_background != null) GUI.DrawTexture(canvas, _background, ScaleMode.StretchToFill);
            else Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.06f, 0.08f, 1f));

            // Под открытыми настройками меню — только подложка. Кнопки под
            // затемнением ловили бы наведение и клики сквозь чужой экран.
            if (settingsOpen)
            {
                _hovered = -1;
                _pressed = -1;
                return;
            }

            Rect play = Place(canvas, scale, PlayCenter, PlaySize);
            Rect settings = Place(canvas, scale, SettingsCenter, DiamondSize);
            Rect exit = Place(canvas, scale, ExitCenter, DiamondSize);

            Vector2 pointer = new Vector2(Event.current.mousePosition.x, Event.current.mousePosition.y);
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

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            if (IsOpen) IsOpen = false;
        }
    }
}
