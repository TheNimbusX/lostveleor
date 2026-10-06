using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Цвета махов Крушения V2 — ОДНА ТАБЛИЦА форм (06.10). Через неё идут все цвета маха: ядро серпа (Tint, у
    /// внутреннего края) → светлое ядро (Light, к наружной кромке) → белая кромка; звенья-призраки, росчерк задетого,
    /// голубая кромка сколов и искры с головы. Тон формы — тот же, что у звеньев HUD серии
    /// (HudWreckSeriesRules.FormHex; тест сверяет): база #4FA8FF, Волнорез #1FB37E, Девятый вал #4B3FD0,
    /// Якорная броня #E4EEF6. Без красного, оранжевого и золота; контур и железо — общие тёмные.
    /// </summary>
    public static class PelagWreckSwingLook
    {
        public struct Row
        {
            /// <summary>Ядро серпа у внутреннего края, тон формы (#RRGGBB).</summary>
            public int Tint;
            /// <summary>Светлое ядро к наружной кромке; им же — звенья, росчерк, кромка сколов.</summary>
            public int Light;
        }

        // ТАБЛИЦА ДЛЯ ПРАВКИ.
        private static readonly Row Base = new Row { Tint = 0x4FA8FF, Light = 0x9CD8FF };          // холодный голубой
        private static readonly Row Breakwater = new Row { Tint = 0x1FB37E, Light = 0x8FE6C6 };    // морская зелень
        private static readonly Row NinthWave = new Row { Tint = 0x4B3FD0, Light = 0x9C94F2 };     // индиго
        private static readonly Row Armor = new Row { Tint = 0xE4EEF6, Light = 0xF7FBFE };         // жемчуг «Якорной брони»

        /// <summary>Тёмный контур серпа, звеньев и росчерка (чернила, не чистый чёрный).</summary>
        public const int OutlineHex = 0x0E0A14;

        /// <summary>Тело скола — тёмное железо (кромка — Light формы).</summary>
        public const int IronHex = 0x262A33;

        /// <summary>Сердцевина росчерка — к белому.</summary>
        public const int HotHex = 0xF4FAFF;

        public static Row For(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WreckBreakwater: return Breakwater;
                case PelagForm.WreckNinthWave: return NinthWave;
                case PelagForm.WreckShell: return Armor;
                default: return Base;
            }
        }

        public static int Tint(PelagForm form) => For(form).Tint;
        public static int Light(PelagForm form) => For(form).Light;

        /// <summary>Каналы 0…1.</summary>
        public static void Rgb(int hex, out float r, out float g, out float b)
        {
            r = ((hex >> 16) & 255) / 255f;
            g = ((hex >> 8) & 255) / 255f;
            b = (hex & 255) / 255f;
        }

        /// <summary>Тон (0…360) и насыщенность (0…1) — для проверки «без красного, оранжевого и золота».</summary>
        public static void HueSat(int hex, out float hue, out float sat)
        {
            Rgb(hex, out float r, out float g, out float b);
            float max = r > g ? (r > b ? r : b) : (g > b ? g : b);
            float min = r < g ? (r < b ? r : b) : (g < b ? g : b);
            float d = max - min;
            sat = max <= 1e-5f ? 0f : d / max;
            if (d <= 1e-5f) { hue = 0f; return; }
            if (max == r) hue = 60f * (((g - b) / d) % 6f);
            else if (max == g) hue = 60f * ((b - r) / d + 2f);
            else hue = 60f * ((r - g) / d + 4f);
            if (hue < 0f) hue += 360f;
        }
    }
}
