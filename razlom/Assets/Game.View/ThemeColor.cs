using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Цвет элемента по роли из <see cref="UiTheme"/>: «Panel», «Accent», «Rare»…
    /// Правка цвета в теме перекрашивает все такие элементы сразу, и в редакторе,
    /// и в игре. Прозрачность своя у каждого элемента (Alpha умножается на цвет темы):
    /// одна рамка ярче, другая тише, а оттенок общий.
    ///
    /// Кодом цвет меняется через <see cref="SetRole"/> (редкость ячейки, наведение),
    /// а не прямой записью Graphic.color — иначе следующая правка темы его затрёт.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Graphic))]
    public sealed class ThemeColor : MonoBehaviour
    {
        public UiTheme.Role Role = UiTheme.Role.Panel;
        [Range(0f, 1f)] public float Alpha = 1f;

        Graphic _graphic;

        public void SetRole(UiTheme.Role role, float alpha = -1f)
        {
            Role = role;
            if (alpha >= 0f) Alpha = alpha;
            Apply();
        }

        void OnEnable()
        {
            UiTheme.Changed += Apply;
            Apply();
        }

        void OnDisable() => UiTheme.Changed -= Apply;

        void OnValidate() => Apply();

        public void Apply()
        {
            if (this == null) return;
            if (_graphic == null) _graphic = GetComponent<Graphic>();
            if (_graphic == null) return;
            Color c = UiTheme.Current.Get(Role);
            c.a *= Alpha;
            _graphic.color = c;
        }
    }
}
