using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Переключает лагерь и забег. Манекены входят в авторскую сцену лагеря;
    /// необязательный старый корень оставлен для совместимости прежних сцен.
    /// </summary>
    [DisallowMultipleComponent]
    // Выход из лагеря возвращает общий свет до того, как LayoutView настроит следующий Разлом.
    [DefaultExecutionOrder(-100)]
    public sealed class SceneWorldView : MonoBehaviour
    {
        [SerializeField] private GameObject _campRoot;
        [SerializeField] private GameObject _provingGroundRoot;

        private TickDriver _driver;
        public GameObject CampRoot => _campRoot;
        private GameMode _shownMode = (GameMode)byte.MaxValue;
        private bool _shownGround;

        public void Initialize(TickDriver driver)
        {
            _driver = driver;
            _shownMode = (GameMode)byte.MaxValue;
            ApplyState();
        }

        public bool ValidateContract(bool logErrors)
        {
            if (_campRoot != null && _campRoot != _provingGroundRoot)
                return true;

            if (logErrors)
                Debug.LogError("[Разлом] SceneWorldView: назначь CampRoot.", this);
            return false;
        }

        private void LateUpdate()
        {
            ApplyState();
        }

        private void ApplyState()
        {
            GameSession session = _driver != null ? _driver.Session : null;
            if (session == null) return;

            bool onGround = session.Mode == GameMode.Camp && session.OnProvingGround;
            if (_shownMode == session.Mode && _shownGround == onGround) return;

            _shownMode = session.Mode;
            _shownGround = onGround;

            // Summary оставляет за интерфейсом последний Разлом; авторские
            // корни там выключены, чтобы не проступить сквозь поле боя.
            _campRoot.SetActive(session.Mode == GameMode.Camp && !onGround);
            if (_provingGroundRoot != null) _provingGroundRoot.SetActive(onGround);
        }
    }
}
