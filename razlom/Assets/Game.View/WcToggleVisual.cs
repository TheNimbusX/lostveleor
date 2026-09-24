using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Вид переключателя пака (тумблер, флажок, радио): при включении красит цель
    /// в роль «вкл», при выключении — в «выкл», и сдвигает ручку тумблера.
    /// Роли и ход ручки правятся в инспекторе; состояние берётся из Toggle.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Toggle))]
    public sealed class WcToggleVisual : MonoBehaviour
    {
        [Tooltip("Что перекрашивается (дорожка тумблера, квадрат флажка, кольцо радио)")] public ThemeColor Target;
        public UiTheme.Role On = UiTheme.Role.Accent;
        public UiTheme.Role Off = UiTheme.Role.Track;
        [Tooltip("Ручка тумблера; у флажка и радио пусто")] public RectTransform Knob;
        [Tooltip("Смещение ручки от центра, единицы Canvas")] public float KnobTravel = 14f;
        public ThemeColor KnobColor;
        public UiTheme.Role KnobOn = UiTheme.Role.Text;
        public UiTheme.Role KnobOff = UiTheme.Role.TextMuted;
        [Tooltip("Видно только во включённом состоянии (галочка, точка радио)")] public GameObject OnlyWhenOn;

        Toggle _toggle;

        void OnEnable()
        {
            _toggle = GetComponent<Toggle>();
            _toggle.onValueChanged.AddListener(OnChanged);
            Apply();
        }

        void OnDisable()
        {
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnChanged);
        }

        void OnValidate() => Apply();

        void OnChanged(bool _) => Apply();

        public void Apply()
        {
            if (this == null) return;
            if (_toggle == null) _toggle = GetComponent<Toggle>();
            bool on = _toggle != null && _toggle.isOn;
            if (Target != null) Target.SetRole(on ? On : Off);
            if (Knob != null) Knob.anchoredPosition = new Vector2(on ? KnobTravel : -KnobTravel, Knob.anchoredPosition.y);
            if (KnobColor != null) KnobColor.SetRole(on ? KnobOn : KnobOff);
            if (OnlyWhenOn != null && OnlyWhenOn.activeSelf != on) OnlyWhenOn.SetActive(on);
        }
    }
}
