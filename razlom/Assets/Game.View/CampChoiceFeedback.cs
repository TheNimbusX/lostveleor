using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.View
{
    // Только новые окна лагеря: выбор остаётся оранжевым, фокус управления — светлая рамка.
    [DisallowMultipleComponent]
    public sealed class CampChoiceFeedback : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        public RectTransform FocusFrame;
        public ThemeStates States;
        public UiTheme.Role RestRole;
        public bool Configured;
        Button _button;
        bool _chosen;
        public static void Install(GameObject panel)
        {
            foreach (var button in panel.GetComponentsInChildren<Button>(true))
                (button.GetComponent<CampChoiceFeedback>() ?? button.gameObject.AddComponent<CampChoiceFeedback>()).Configure();
        }
        public static void Choose(Button button, bool chosen)
        {
            if (button == null) return;
            var feedback = button.GetComponent<CampChoiceFeedback>() ?? button.gameObject.AddComponent<CampChoiceFeedback>();
            feedback.Configure(); feedback._chosen = chosen; feedback.Apply();
        }
        public void Configure()
        {
            _button = GetComponent<Button>();
            if (!Configured) { States = GetComponent<ThemeStates>(); RestRole = States != null ? States.Normal : UiTheme.Role.Text; Configured = true; }
            if (FocusFrame != null) return;
            FocusFrame = new GameObject("Фокус управления", typeof(RectTransform)).GetComponent<RectTransform>();
            FocusFrame.SetParent(transform, false); FocusFrame.anchorMin = Vector2.zero; FocusFrame.anchorMax = Vector2.one;
            FocusFrame.offsetMin = new Vector2(3, 3); FocusFrame.offsetMax = new Vector2(-3, -3);
            Edge("Верх", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), Vector2.zero);
            Edge("Низ", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 2));
            Edge("Лево", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(2, 0));
            Edge("Право", new Vector2(1, 0), Vector2.one, new Vector2(-2, 0), Vector2.zero);
            FocusFrame.gameObject.SetActive(false);
        }
        void Edge(string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(FocusFrame, false); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            var graphic = rect.GetComponent<Image>(); graphic.sprite = UiTheme.Current?.Pixel; graphic.raycastTarget = false;
            graphic.color = UiTheme.Current != null ? UiTheme.Current.Text : Color.white;
        }
        void OnEnable() { Configure(); UiTheme.Changed += Apply; Apply(); }
        void OnDisable() { UiTheme.Changed -= Apply; if (FocusFrame != null) FocusFrame.gameObject.SetActive(false); }
        void Apply()
        {
            if (States != null) { States.Normal = _chosen ? UiTheme.Role.Accent : RestRole; States.Apply(); }
            if (FocusFrame != null && UiTheme.Current != null)
                foreach (var graphic in FocusFrame.GetComponentsInChildren<Image>(true)) graphic.color = UiTheme.Current.Text;
        }
        void LateUpdate() => Focus();
        public void OnSelect(BaseEventData _) => Focus();
        public void OnDeselect(BaseEventData _) { if (FocusFrame != null) FocusFrame.gameObject.SetActive(false); }
        void Focus()
        {
            if (FocusFrame == null || _button == null) return;
            bool visible = _button.IsInteractable() && EventSystem.current?.currentSelectedGameObject == gameObject;
            if (FocusFrame.gameObject.activeSelf != visible) FocusFrame.gameObject.SetActive(visible);
        }
    }
}
