using UnityEngine;

namespace Game.View
{
    // Одна гарнитура для игрового текста; запечённые надписи в арте не подменяются.
    internal static class GameTypography
    {
        static Font _regular, _semibold, _bold;
        public static Font Display => Semibold;
        public static Font Regular => _regular != null ? _regular : (_regular = Load("Regular"));
        public static Font Semibold => _semibold != null ? _semibold : (_semibold = Load("SemiBold"));
        public static Font Bold => _bold != null ? _bold : (_bold = Load("Bold"));
        // Стили кешируются: раньше каждое обращение создавало новый GUIStyle,
        // а подписи манекенов и арки просили их на каждом событии IMGUI.
        // Вызывающие всегда копируют стиль (new GUIStyle(...)), так что общий
        // экземпляр никто не портит.
        static GUIStyle _label, _button;
        public static GUIStyle Label => _label ?? (_label = new GUIStyle(GUI.skin.label) { font = Regular });
        public static GUIStyle Button => _button ?? (_button = new GUIStyle(GUI.skin.button) { font = Semibold });
        static Font Load(string weight) => Resources.Load<Font>("UI/Fonts/Rubik-" + weight)
            ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
