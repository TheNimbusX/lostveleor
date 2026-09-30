using System;

namespace Game.View
{
    /// <summary>
    /// Блок ввода на экранах выбора (владелец 29.09: «блок ввода 1–2 с при открытии — против случайных
    /// кликов»; кадр 3a — кольцо блокировки на кейкапе). Экран награды встаёт сразу после последнего
    /// удара, а клавиши выбора — те же, что способности: удар, пришедшийся на открытие, молча брал
    /// карточку. Пока блок идёт, ни клик, ни клавиша карточку не берут, кольцо на кейкапах наполняется;
    /// дошло — кейкапы загораются. Тот же блок — у итогов забега (RunEndBeat).
    ///
    /// Чистая логика без Unity: часы — снаружи (UiMotion.Now), проверяется в Combat.Presentation.Tests.
    /// </summary>
    public sealed class RunHudChoiceLock
    {
        /// <summary>Сколько длится блок, с: как проявление карточек экрана.</summary>
        public const float DefaultDuration = 1.2f;

        public float Duration = DefaultDuration;
        float _start;
        bool _armed, _released;

        /// <summary>Экран открылся (или карточки сменились): блок с этого мгновения.</summary>
        public void Start(float now)
        {
            _start = now;
            _armed = true;
            _released = false;
        }

        /// <summary>Экран закрыт: блока нет.</summary>
        public void Clear()
        {
            _armed = false;
            _released = true;
        }

        /// <summary>Блок идёт: выбор не принимается.</summary>
        public bool Locked(float now) => _armed && now - _start < Duration;

        /// <summary>Наполнение кольца 0…1; без блока — 1.</summary>
        public float Progress(float now)
        {
            if (!_armed) return 1f;
            if (Duration <= 0f) return 1f;
            return Math.Max(0f, Math.Min(1f, (now - _start) / Duration));
        }

        /// <summary>
        /// true ровно один раз — в первый вызов после конца блока: кейкапы загораются. Экран закрыли раньше
        /// (<see cref="Clear"/>) — не загораются.
        /// </summary>
        public bool TakeRelease(float now)
        {
            if (_released || !_armed || Locked(now)) return false;
            _released = true;
            return true;
        }
    }
}
