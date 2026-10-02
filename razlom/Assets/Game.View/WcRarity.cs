using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Редкость элемента пака «Ночная акварель» (ячейка, карточка улучшения, подсказка).
    /// Владелец 22 сентября: редкость должна читаться сразу — вся рамка в её цвете,
    /// камень на верхней кромке, плашка того же цвета, у редкой свечение. Боковую
    /// полоску он отверг.
    ///
    /// Tinted — красится в цвет редкости всегда (камень, плашка, рамка).
    /// TextTinted — обычный текст у обычной редкости, цвет редкости у редкой (название).
    /// CommonOnly / RareOnly — что видно только у обычной или у редкой и выше
    /// (у редкой жирная рамка и свечение, у обычной — тонкая рамка).
    /// С 23 сентября четыре уровня, как у вещей (владелец: «явнее для редких, эпических, уникальных»).
    /// Выбирается в инспекторе или кодом через <see cref="Set"/>.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WcRarity : MonoBehaviour
    {
        /// <summary>Номера совпадают с ItemRarity: обычная, редкая, эпическая, уникальная.</summary>
        public enum Tier { Common, Rare, Epic, Unique }

        public static UiTheme.Role RoleFor(Tier tier) => tier switch
        {
            Tier.Rare => UiTheme.Role.Rare,
            Tier.Epic => UiTheme.Role.Epic,
            Tier.Unique => UiTheme.Role.Unique,
            _ => UiTheme.Role.Common,
        };

        public static Tier FromItem(int rarity) => (Tier)Mathf.Clamp(rarity, 0, 3);

        /// <summary>Редкость словом, как в палатке: «Обычная», «Редкая», «Эпическая», «Уникальная».</summary>
        public static string Name(Tier tier)
        {
            switch (tier)
            {
                case Tier.Rare: return "Редкая";
                case Tier.Epic: return "Эпическая";
                case Tier.Unique: return "Уникальная";
                default: return "Обычная";
            }
        }

        public Tier Value = Tier.Common;
        [Tooltip("Красится в цвет редкости")] public ThemeColor[] Tinted = new ThemeColor[0];
        [Tooltip("Текст: обычный цвет у обычной редкости, цвет редкости у редкой")] public ThemeColor[] TextTinted = new ThemeColor[0];
        [Tooltip("Виден только у обычной")] public GameObject[] CommonOnly = new GameObject[0];
        [Tooltip("Виден только у редкой")] public GameObject[] RareOnly = new GameObject[0];

        [Header("Отметка (форма навыка, 02.10)")]
        [Tooltip("Отмечено единственным оранжевым акцентом, а не цветом редкости: у форм навыка редкости нет " +
                 "(лут 02.10), но награда особая. Ставит SetMarked; Set(редкость) отметку снимает")]
        public bool Marked;
        [Tooltip("Виден только у отмеченного (огненные нити карточки формы)")] public GameObject[] MarkedOnly = new GameObject[0];

        /// <summary>Роль краски сейчас: акцент у отмеченного, иначе цвет редкости.</summary>
        public UiTheme.Role Role => Marked ? UiTheme.Role.Accent : RoleFor(Value);

        /// <summary>Ни редкости, ни отметки — обычный элемент.</summary>
        public bool Plain => !Marked && Value == Tier.Common;

        public void Set(Tier tier)
        {
            Value = tier;
            Marked = false;
            Apply();
        }

        /// <summary>Отметка формы навыка: кольцо, сияние и строка вида — акцентом, название — обычным текстом.</summary>
        public void SetMarked(bool marked)
        {
            Value = Tier.Common;
            Marked = marked;
            Apply();
        }

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            if (this == null) return;
            bool rare = Value != Tier.Common || Marked;
            UiTheme.Role role = Role;
            foreach (ThemeColor t in Tinted)
                if (t != null) t.SetRole(role);
            // Название отмеченного — обычным текстом: акцент один, он уже на кольце, строке вида и нитях.
            foreach (ThemeColor t in TextTinted)
                if (t != null) t.SetRole(rare && !Marked ? role : UiTheme.Role.Text);
            foreach (GameObject go in CommonOnly)
                if (go != null && go.activeSelf == rare) go.SetActive(!rare);
            foreach (GameObject go in RareOnly)
                if (go != null && go.activeSelf != rare) go.SetActive(rare);
            if (MarkedOnly != null)
                foreach (GameObject go in MarkedOnly)
                    if (go != null && go.activeSelf != Marked) go.SetActive(Marked);
            // Ячейка с одной рамкой: фон и рамку красит WcSlotState (пустую не трогаем).
            var slot = GetComponent<WcSlotState>();
            if (slot != null && slot.Rarity != WcSlotState.Empty && slot.Rarity != WcSlotState.Plain) slot.Set((int)Value, slot.Selected);
        }
    }
}
