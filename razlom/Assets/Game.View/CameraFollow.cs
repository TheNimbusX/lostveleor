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

        /// <summary>
        /// Сглаживание самой скорости, которой камера ведёт вперёд.
        ///
        /// Скорость считается разностью позиций за кадр и потому шумит: тело
        /// интерполируется между тиками симуляции, а кадры идут неравномерно.
        /// Без сглаживания этот шум ушёл бы прямо в положение камеры и заменил
        /// одну неровность другой. Заметно быстрее Smoothing, иначе компенсация
        /// сама начнёт отставать.
        /// </summary>
        private const float LeadSmoothing = 18f;

        private Vector3 _previousPlayer;
        private Vector3 _leadVelocity;

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
                _previousPlayer = player;
                _ready = true;
                return;
            }

            float dt = Time.deltaTime;

            // КОМПЕНСАЦИЯ ОТСТАВАНИЯ.
            //
            // Экспоненциальное сглаживание НИКОГДА не догоняет равномерно
            // движущуюся цель: оно застревает на постоянной ошибке v / Smoothing.
            // При обычной ходьбе это означало, что герой всё время смещён от
            // своего места в кадре, а мир под ним едет — и стоило остановиться,
            // камера медленно доползала. Именно это читается как «картинка
            // плывёт», и никакой настройкой Smoothing оно не лечится: жёстче —
            // меньше ошибка, но резче рывки на поворотах.
            //
            // Скорость цели, добавленная вперёд, гасит эту ошибку ровно в ноль:
            // при равномерном движении герой стоит в кадре неподвижно, а
            // мягкость на разгонах и остановках сохраняется.
            // Скорость берётся из симуляции: она точная и не зависит от того,
            // сколько длился кадр. Разность экранных позиций даёт то же число
            // с шумом, и этот шум ушёл бы прямо в камеру.
            Vector3 velocity = Driver.GetSimVelocity(Simulation.PlayerId);
            if (velocity.sqrMagnitude > 0.0001f)
            {
                // ТОЧНУЮ СКОРОСТЬ СГЛАЖИВАТЬ НЕЛЬЗЯ, и это не мелочь.
                //
                // Упреждение v/Smoothing держит героя неподвижно в кадре ровно
                // до тех пор, пока оно совпадает с настоящей скоростью. Сглаженное
                // значение ЗАПАЗДЫВАЕТ: герой уже тормозит, а упреждение ещё
                // прежнее, и камера уезжает вперёд, а потом возвращается — это
                // и читалось как покачивание при остановке.
                //
                // Скорость из симуляции точная и меняется раз в тик; ступеньки
                // такого размера камера со своим сглаживанием не показывает.
                _leadVelocity = velocity;
            }
            else if (dt > 0f)
            {
                // Запасной путь на случай, когда симуляции ещё нет. Разность
                // экранных позиций шумит, и вот её сглаживать обязательно.
                _leadVelocity = Vector3.Lerp(_leadVelocity, (player - _previousPlayer) / dt,
                    1f - Mathf.Exp(-LeadSmoothing * dt));
            }
            _previousPlayer = player;

            Vector3 wanted = _offset + player + framing + _leadVelocity / Mathf.Max(0.01f, Smoothing);
            Target.position = Vector3.Lerp(Target.position, wanted,
                1f - Mathf.Exp(-Smoothing * dt));
        }
    }
}


