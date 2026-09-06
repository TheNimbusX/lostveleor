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
    public sealed class CameraFollow : MonoBehaviour
    {
        public TickDriver Driver;
        public Transform Target;

        [Tooltip("Метров в секунду на метр отставания. Больше — жёстче привязка.")]
        public float Smoothing = 8f;

        [Tooltip("Сдвиг центра боевого кадра в плоскости камеры: X — вправо, Y — вверх. Нужен, чтобы герой оставался ниже центра и впереди оставалось место для толпы.")]
        // Отрицательный Y поднимает героя в кадре и оставляет больше места
        // для движения вниз по арене.
        public Vector2 ScreenOffset = new Vector2(0f, -1.05f);

        private Vector3 _offset;
        private bool _ready;
        private bool _initialized;
        private int _generation = -1;

        /// <summary>Явно связывает авторскую камеру с runtime-драйвером.</summary>
        public void Initialize(TickDriver driver, Transform target)
        {
            Driver = driver;
            Target = target;
            _offset = Target != null ? Target.position : Vector3.zero;
            _generation = -1;
            _ready = false;
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

            if (Driver.Sim == null)
            {
                // Лагерь авторский и стоит в мировом нуле. После выхода из
                // Разлома камера обязана вернуться к сохранённой сценой точке,
                // иначе она продолжила бы смотреть на последнюю комнату забега.
                Target.position = _offset;
                _ready = false;
                return;
            }

            Vector3 player = Driver.GetRenderPosition(Simulation.PlayerId);
            // Смещаем именно кадр, а не симуляционного игрока: так впереди
            // остаётся место для врагов, а лагерь и ввод не получают скрытого
            // мирового сдвига.
            Vector3 framing = transform.right * ScreenOffset.x + transform.up * ScreenOffset.y;

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


