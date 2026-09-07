using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Камера едет за игроком.
    ///
    /// Понадобилась вместе с Разломом: тестовая арена помещалась в кадр целиком,
    /// а собранная из модулей локация — нет.
    ///
    /// Сглаживание здесь ЧИСТО КОСМЕТИЧЕСКОЕ и живёт в представлении: положение
    /// камеры ни на что в симуляции не влияет, поэтому ему можно быть плавным
    /// и кадрозависимым.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class CameraFollow : MonoBehaviour
    {
        public TickDriver Driver;
        public Transform Target;

        [Tooltip("Метров в секунду на метр отставания. Больше — жёстче привязка.")]
        public float Smoothing = 8f;

        [Tooltip("Общий сдвиг игрового кадра в плоскости камеры: X — вправо, Y — вверх. Один профиль используется в лагере и забеге, чтобы герой не менял размер и положение при переходе.")]
        // Отрицательный Y поднимает героя в кадре и оставляет больше места
        // для движения вниз по арене.
        public Vector2 CombatFraming = new Vector2(0f, .35f);
        [Range(35f, 65f)] public float CombatPitch = 48f;
        [Range(4f, 10f)] public float CombatSize = 6.2f;

        private Vector3 _offset;
        private bool _ready;
        private bool _initialized;
        private int _generation = -1;
        private bool _wasCamp;
        private CombatCameraJuice _juice;

        /// <summary>Явно связывает авторскую камеру с runtime-драйвером.</summary>
        public void Initialize(TickDriver driver, Transform target)
        {
            Driver = driver;
            Target = target;
            Camera camera = GetComponent<Camera>();
            _juice = GetComponent<CombatCameraJuice>();
            if (camera != null)
            {
                transform.rotation = Quaternion.Euler(CombatPitch, transform.eulerAngles.y, 0f);
                transform.position = -transform.forward * 60f;
                camera.orthographicSize = CombatSize;
                _juice?.SetBaseOrthographicSize(CombatSize);
            }
            _offset = Target != null ? Target.position : Vector3.zero;
            _generation = -1;
            _ready = false;
            _wasCamp = false;
            _initialized = Driver != null && Target != null;
            enabled = _initialized;
        }

        private void Start()
        {
            if (!_initialized && Target != null && Driver != null)
                Initialize(Driver, Target);

            if (!_initialized)
            {
                enabled = false;
                return;
            }
        }

        private void LateUpdate()
        {
            if (!_initialized) return;

            // Сменилась симуляция — игрок стоит в другом месте карты, и наезд
            // из прошлой точки был бы полётом через полкарты.
            if (_generation != Driver.Generation)
            {
                _generation = Driver.Generation;
                _ready = false;
            }

            bool camp = Driver.Sim == null;
            if (camp != _wasCamp)
            {
                // При смене Camp/Rift не тянем камеру от последней позиции
                // другой сцены, но сохраняем один и тот же ракурс и масштаб.
                _wasCamp = camp;
                _ready = false;
            }

            Vector3 player;
            if (camp)
            {
                CampPlayerView campPlayer = CampPlayerView.Instance;
                if (campPlayer == null || !campPlayer.Active) return;
                player = campPlayer.Position;

                // CombatCameraJuice мог получить импульс в последнем забеге.
                // В лагере его базовый размер возвращается к тому же профилю,
                // иначе герой менял бы размер только после перехода в меню.
                transform.rotation = Quaternion.Euler(CombatPitch, transform.eulerAngles.y, 0f);
                Camera camera = GetComponent<Camera>();
                if (camera != null) camera.orthographicSize = CombatSize;
                _juice?.SetBaseOrthographicSize(CombatSize);
            }
            else
            {
                player = Driver.GetRenderPosition(Simulation.PlayerId);
            }

            // Смещаем именно кадр, а не игрока: одинаковое кадрирование в
            // лагере и забеге оставляет место впереди и не меняет управление.
            Vector3 framing = transform.right * CombatFraming.x + transform.up * CombatFraming.y;

            if (!_ready)
            {
                // Первый кадр — встаём сразу, без наезда из начала координат.
                Target.position = _offset + player + framing;
                _ready = true;
                return;
            }

            Vector3 wanted = _offset + player + framing;
            Target.position = Vector3.Lerp(Target.position, wanted,
                1f - Mathf.Exp(-Smoothing * Time.deltaTime));
        }
    }
}


