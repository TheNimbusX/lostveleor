namespace Game.Sim
{
    /// <summary>
    /// Стабильные ключи боссов акта I: под ними лагерь хранит победы и сердца.
    ///
    /// Ключ — строка, свёрнутая StableId, а не EnemyKind: вид босса может смениться
    /// (временный Хранитель вместо Хозяина Чащи), а победа в сохранении должна
    /// остаться победой. СТРОКИ КЛЮЧЕЙ ПОСЛЕ ВЛИТИЯ НЕ МЕНЯЮТСЯ НИКОГДА: сменишь —
    /// и все сохранённые победы и сердца станут чужими. Имя босса для игрока —
    /// отдельно, во View.
    ///
    /// Порядок индексов — порядок боссов акта: индекс r−1 открывает ранг лагеря r.
    /// </summary>
    public static class RunBossKeys
    {
        /// <summary>Боссов в акте I.</summary>
        public const int Count = 3;

        /// <summary>Хозяин Чащи, босс первой локации.</summary>
        public static readonly int ThicketMaster = StableId.Of("boss.act1.thicket-master");

        /// <summary>Второй босс акта I. Ключ-заглушка навсегда, имя появится потом.</summary>
        public static readonly int Second = StableId.Of("boss.act1.second");

        /// <summary>Третий босс акта I. Ключ-заглушка навсегда, имя появится потом.</summary>
        public static readonly int Third = StableId.Of("boss.act1.third");

        /// <summary>Ключ босса по порядку в акте; вне 0..Count−1 — 0.</summary>
        public static int At(int index)
        {
            switch (index)
            {
                case 0: return ThicketMaster;
                case 1: return Second;
                case 2: return Third;
                default: return 0;
            }
        }

        /// <summary>Порядок босса в акте; −1, если ключ неизвестен (снятый босс, битое сохранение).</summary>
        public static int IndexOf(int key)
        {
            // Ноль — «нет босса», а не хеш какой-то строки: его не путаем с настоящим ключом.
            if (key == 0) return -1;
            for (int i = 0; i < Count; i++) if (At(i) == key) return i;
            return -1;
        }

        /// <summary>
        /// Ключ по виду убитого босса. В акте I босс один: и Хозяин Чащи, и временный
        /// Хранитель (ThicketMasterBossEnabled = false) — это босс леса. Боссы 2–3
        /// получат свои ветки по EnemyKind, когда появятся.
        /// </summary>
        public static int Of(EnemyKind kind) => ThicketMaster;
    }
}
