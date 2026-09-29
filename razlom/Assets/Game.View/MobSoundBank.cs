namespace Game.View
{
    /// <summary>
    /// Звуки мобов леса (поток K, выбор владельца 29.09). Клипы лежат в
    /// Resources/Audio/Combat/Mobs/&lt;Моб&gt;/&lt;слот&gt;_NN.wav, их режет и сводит
    /// ART/SFX/epidemic-2026-09-29/process.py из записей Epidemic Sound.
    ///
    /// Здесь — таблица слотов: как выровнен клип и какой он громкости. По ней
    /// CombatAudio запускает выровненные по пику звуки (взмах, прыжок, вой) за
    /// PeakSeconds до контакта, а тесты tools/Combat.Presentation.Tests сверяют с
    /// ней сами клипы. Чистый C# без UnityEngine — его собирает и тестовый проект.
    /// Числа пиков те же, что SWING_LEAD и соседи в process.py.
    /// </summary>
    public static class MobSoundBank
    {
        public const string Folder = "Audio/Combat/Mobs";

        /// <summary>Взмах когтей, клыков: пик свиста — через столько секунд от начала клипа.</summary>
        public const float SwingPeakSeconds = .20f;

        /// <summary>Круг когтей Вендиго: низкий свист нарастает дольше.</summary>
        public const float SweepPeakSeconds = .50f;

        /// <summary>Взлёт Вендиго: обратный свист, пик — на тик отрыва.</summary>
        public const float LeapPeakSeconds = .25f;

        /// <summary>Вой Вендиго: пик крика — на удар кольца.</summary>
        public const float HowlPeakSeconds = .80f;

        public enum Align : byte
        {
            /// <summary>Контакт: удар в первые миллисекунды клипа, играется в тик события.</summary>
            Attack,
            /// <summary>Голос или скрип: звук начинается сразу, пик может быть позже.</summary>
            Onset,
            /// <summary>Пик на PeakSeconds: запускается заранее, чтобы пик лёг на контакт.</summary>
            Peak,
            /// <summary>Фон: кусок записи как есть, только края.</summary>
            Window,
        }

        public readonly struct Slot
        {
            public readonly string Mob, Name;
            public readonly Align Align;

            /// <summary>Цель громкости, LUFS-M (макс. окна 400 мс; короче клип — окно по клипу, не короче 100 мс).</summary>
            public readonly float Loudness;

            /// <summary>Где пик (только Align.Peak), секунды.</summary>
            public readonly float PeakSeconds;

            /// <summary>Не длиннее, секунды.</summary>
            public readonly float MaxSeconds;

            /// <summary>Подключён ли к событиям. Шаги Хранителя лежат про запас: хука шагов врагов нет.</summary>
            public readonly bool Wired;

            public Slot(string mob, string name, Align align, float loudness, float maxSeconds,
                float peakSeconds = 0f, bool wired = true)
            {
                Mob = mob; Name = name; Align = align; Loudness = loudness; MaxSeconds = maxSeconds;
                PeakSeconds = peakSeconds; Wired = wired;
            }

            public string Path => Folder + "/" + Mob + "/" + Name;
        }

        private const float OneShot = -18f, Big = -16f, Kill = -15f, Bed = -20f;

        public static readonly Slot[] Slots =
        {
            new Slot("Guardian", "swing", Align.Peak, OneShot, .55f, SwingPeakSeconds),
            new Slot("Guardian", "impact", Align.Attack, Big, .75f),
            new Slot("Guardian", "hurt", Align.Onset, OneShot, .50f),
            new Slot("Guardian", "death", Align.Onset, Big, 1.90f),
            new Slot("Guardian", "step", Align.Attack, OneShot, .45f, wired: false),

            new Slot("RootSwarm", "bite", Align.Attack, OneShot, .28f),
            new Slot("RootSwarm", "scuttle", Align.Window, Bed, .75f),
            new Slot("RootSwarm", "hurt", Align.Onset, OneShot, .45f),
            new Slot("RootSwarm", "death", Align.Attack, OneShot, .40f),

            // Плоды залпа летят через 0,2 с: хвост выстрела не длиннее 0,24 с.
            new Slot("Bud", "spit", Align.Attack, OneShot, .24f),
            new Slot("Bud", "splat", Align.Attack, OneShot, .35f),
            new Slot("Bud", "puddle", Align.Window, Bed, 2.2f),
            new Slot("Bud", "hurt", Align.Onset, OneShot, .45f),
            new Slot("Bud", "death", Align.Attack, OneShot, .40f),

            new Slot("Stonehoof", "snort", Align.Onset, OneShot, .95f),
            new Slot("Stonehoof", "charge", Align.Window, Bed, 1.5f),
            new Slot("Stonehoof", "collision", Align.Attack, Big, 1.0f),
            new Slot("Stonehoof", "tusk", Align.Peak, OneShot, .55f, SwingPeakSeconds),
            new Slot("Stonehoof", "hurt", Align.Onset, OneShot, .70f),
            new Slot("Stonehoof", "death", Align.Onset, OneShot, 1.40f),

            new Slot("Wendigo", "claw", Align.Peak, OneShot, .55f, SwingPeakSeconds),
            new Slot("Wendigo", "leap", Align.Peak, OneShot, .42f, LeapPeakSeconds),
            new Slot("Wendigo", "land", Align.Attack, Big, .60f),
            new Slot("Wendigo", "howl", Align.Peak, Big, 2.0f, HowlPeakSeconds),
            new Slot("Wendigo", "sweep", Align.Peak, Big, 1.10f, SweepPeakSeconds),
            new Slot("Wendigo", "hurt", Align.Onset, OneShot, .70f),
            new Slot("Wendigo", "death", Align.Onset, OneShot, 2.0f),

            new Slot("Thorncaster", "spike", Align.Attack, OneShot, .35f),
            new Slot("Thorncaster", "burst", Align.Attack, Big, .60f),
            new Slot("Thorncaster", "shot", Align.Onset, OneShot, .45f),
            new Slot("Thorncaster", "hurt", Align.Onset, OneShot, .45f),
            new Slot("Thorncaster", "death", Align.Onset, OneShot, 1.60f),

            new Slot("RootSnarer", "slam", Align.Attack, Big, 1.10f),
            new Slot("RootSnarer", "roots", Align.Window, OneShot, 1.0f),
            new Slot("RootSnarer", "mend", Align.Window, Bed, 1.6f),
            new Slot("RootSnarer", "hurt", Align.Onset, OneShot, .45f),
            new Slot("RootSnarer", "death", Align.Onset, OneShot, 2.20f),

            new Slot("Splitter", "bite", Align.Attack, OneShot, .40f),
            new Slot("Splitter", "roll", Align.Window, OneShot, .90f),
            new Slot("Splitter", "crack", Align.Attack, OneShot, .35f),
            new Slot("Splitter", "pop", Align.Onset, OneShot, .28f),
            new Slot("Splitter", "hurt", Align.Onset, OneShot, .40f),
            new Slot("Splitter", "death", Align.Attack, OneShot, .45f),

            new Slot("Generic", "kill", Align.Attack, Kill, .90f),
            new Slot("Generic", "stun", Align.Attack, OneShot, 1.30f),
            new Slot("Generic", "rooted", Align.Window, OneShot, 1.0f),
        };
    }
}
