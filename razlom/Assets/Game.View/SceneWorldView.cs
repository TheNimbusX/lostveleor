using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Переключает авторские пространства сцены. Лагерь и Полигон не строятся
    /// из сида, поэтому их геометрия принадлежит .unity; компонент лишь выбирает,
    /// какой корень должен быть виден при текущем состоянии сессии.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneWorldView : MonoBehaviour
    {
        [SerializeField] private GameObject _campRoot;
        [SerializeField] private GameObject _provingGroundRoot;

        private TickDriver _driver;
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
            if (_campRoot != null && _provingGroundRoot != null && _campRoot != _provingGroundRoot)
                return true;

            if (logErrors)
                Debug.LogError("[Разлом] SceneWorldView: назначь разные CampRoot и ProvingGroundRoot.", this);
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
            _provingGroundRoot.SetActive(onGround);
        }
    }
}
