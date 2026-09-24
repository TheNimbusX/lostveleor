using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Ячейка умения пака «Ночная акварель» (лист kit-sheet-4-hud): состояния
    /// «готово» (бирюзовая рамка со свечением), «нажата» (оранжевая), «перезарядка»
    /// (значок гаснет, вокруг секунд загорается кольцо делений), «нет лавидия»
    /// (значок притемнён, камень лавидия на углу).
    /// Состояние и доля перезарядки выставляются кодом HUD или в инспекторе.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WcAbilitySlot : MonoBehaviour
    {
        public enum State { Ready, Pressed, Cooldown, NoLavidium }

        public State Value = State.Ready;
        [Tooltip("Сколько перезарядки осталось: 1 — только началась, 0 — готово")]
        [Range(0f, 1f)] public float Cooldown = .6f;
        public string Seconds = "4,8";

        public ThemeColor Frame;
        public ThemeColor Glow;
        public RawImage Icon;
        [Tooltip("Узел перезарядки: кольцо делений и секунды, виден только в «перезарядке»")] public GameObject CooldownRing;
        [Tooltip("Загоревшиеся деления (Image.Filled): доля прошедшей перезарядки")] public Image CooldownShade;
        public TMP_Text CooldownText;
        public GameObject NoLavidiumMark;

        static readonly Color CoolingIcon = new Color(.13f, .14f, .17f, 1f);
        static readonly Color StarvingIcon = new Color(.42f, .40f, .40f, 1f);

        public void Set(State state, float cooldown = -1f, string seconds = null)
        {
            Value = state;
            if (cooldown >= 0f) Cooldown = cooldown;
            if (seconds != null) Seconds = seconds;
            Apply();
        }

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            if (this == null) return;
            bool cooling = Value == State.Cooldown;
            bool starving = Value == State.NoLavidium;
            UiTheme.Role frame = Value switch
            {
                State.Ready => UiTheme.Role.Rare,
                State.Pressed => UiTheme.Role.Accent,
                State.NoLavidium => UiTheme.Role.Lavidium,
                _ => UiTheme.Role.PanelLine,
            };
            if (Frame != null) Frame.SetRole(frame, starving ? .6f : 1f);
            if (Glow != null)
            {
                Glow.SetRole(frame, Value == State.Pressed ? .7f : .45f);
                Glow.gameObject.SetActive(Value == State.Ready || Value == State.Pressed);
            }
            // Прозрачность на ярких значках почти не видна, поэтому значок притемняется цветом:
            // при перезарядке почти до фона (читаются кольцо и секунды), без лавидия — наполовину.
            if (Icon != null) Icon.color = cooling ? CoolingIcon : starving ? StarvingIcon : Color.white;
            if (CooldownRing != null && CooldownRing.activeSelf != cooling) CooldownRing.SetActive(cooling);
            if (CooldownShade != null) CooldownShade.fillAmount = 1f - Cooldown;
            if (CooldownText != null) CooldownText.text = Seconds;
            if (NoLavidiumMark != null && NoLavidiumMark.activeSelf != starving) NoLavidiumMark.SetActive(starving);
        }
    }
}
