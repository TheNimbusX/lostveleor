using UnityEngine;

namespace Game.View
{
    // Одна гарнитура для игрового текста; запечённые надписи в арте не подменяются.
    public static class GameTypography
    {
        /// <summary>
        /// Гарнитура проекта. Выбрана владельцем 15 сентября из листа шрифтов с
        /// родной кириллицей (Rubik отвергнут). Файлы Resources/UI/Fonts/{Family}-{Regular,SemiBold,Bold}.ttf;
        /// по этому же имени CombatHudBuilder создаёт SDF-шрифты для TextMeshPro.
        /// </summary>
        public const string Family = "Tektur";
        static Font _regular, _semibold, _bold;
        internal static Font Display => Semibold;
        internal static Font Regular => _regular != null ? _regular : (_regular = Load("Regular"));
        internal static Font Semibold => _semibold != null ? _semibold : (_semibold = Load("SemiBold"));
        internal static Font Bold => _bold != null ? _bold : (_bold = Load("Bold"));
        // Стили кешируются: раньше каждое обращение создавало новый GUIStyle,
        // а подписи манекенов и арки просили их на каждом событии IMGUI.
        // Вызывающие всегда копируют стиль (new GUIStyle(...)), так что общий
        // экземпляр никто не портит.
        static GUIStyle _label, _button;
        internal static GUIStyle Label => _label ?? (_label = new GUIStyle(GUI.skin.label) { font = Regular });
        internal static GUIStyle Button => _button ?? (_button = new GUIStyle(GUI.skin.button) { font = Semibold });
        static Font Load(string weight) => Resources.Load<Font>("UI/Fonts/" + Family + "-" + weight)
            ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
