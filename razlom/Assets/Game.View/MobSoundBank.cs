using Game.Sim;

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

    /// <summary>
    /// Фон топота роя (CombatAudio.UpdateSwarmScuttle): когда шорох звучит и под
    /// сколько бегущих. Чистый C#, как и таблица выше, — его гоняют тесты.
    ///
    /// Баг 29.09 («иногда включается звук, который не должен тут быть — при входе
    /// на арену»): топот смотрел только на скорость корнеползов, а звук не
    /// пространственный. Поэтому его было слышно
    ///  1) от роя за краем кадра — стартовая волна бежит к входу с 12–20 м (замер
    ///     на стенде, 107 арен: каждый четвёртый шорох первых 3 с арены — от роя за
    ///     краем кадра, все в выживании E12, где волна бодрствует с первого тика);
    ///  2) над стоящей симуляцией — итоги после смерти или ухода, награда, маршрут,
    ///     дымная завеса: Sim не шагает, скорость у бегущих так и остаётся, и шорох
    ///     шёл раз в 0,55 с, пока игрок не уйдёт с экрана.
    /// </summary>
    public sealed class SwarmScuttleClock
    {
        /// <summary>Шорох не чаще, с.</summary>
        public const float Spacing = .55f;

        /// <summary>Никто не бежит или мир стоит — снова смотрим через, с.</summary>
        public const float IdleRecheck = .2f;

        /// <summary>
        /// Тик Sim не менялся дольше этого по часам кадра (Time.time, как у TickDriver) —
        /// мир стоит. Тик — 1/30 с; запас на просевший кадр.
        /// </summary>
        public const float FrozenSeconds = .2f;

        /// <summary>
        /// Бегущих слышно не дальше, м: край кадра боевой камеры (ортографический
        /// размер 6,2 при 16:9 — ±11 м вбок, ±8,3 м вглубь). Рой за кадром молчит.
        /// </summary>
        public const float HearingRadius = 11f;

        private float _readyAt, _tickSeenAt;
        private int _tickSeen = int.MinValue;

        /// <summary>
        /// Новый бой: срок шороха снимается. Когда Sim шагала последний раз, помним —
        /// новая симуляция, ещё не шагнувшая, для топота тоже стоит.
        /// </summary>
        public void Reset() => _readyAt = 0f;

        /// <summary>
        /// Звать каждый кадр: здесь же отмечается, шагает ли Sim. true — пора сосчитать
        /// бегущих (CountRunning) и отдать счёт в Counted.
        /// </summary>
        public bool Due(int simTick, float now)
        {
            if (simTick != _tickSeen) { _tickSeen = simTick; _tickSeenAt = now; }
            if (now < _readyAt) return false;
            if (Frozen(now)) { _readyAt = now + IdleRecheck; return false; }
            return true;
        }

        /// <summary>Sim не шагает дольше FrozenSeconds.</summary>
        public bool Frozen(float now) => now - _tickSeenAt > FrozenSeconds;

        /// <summary>Сосчитали бегущих в пределах слышимости. true — играть шорох под running.</summary>
        public bool Counted(int running, float now)
        {
            _readyAt = now + (running > 0 ? Spacing : IdleRecheck);
            return running > 0;
        }

        /// <summary>
        /// Живые корнеползы в движении не дальше radius от героя. radius &lt; 0 — на
        /// любом расстоянии (журнал съёмки: сколько бегущих отсекла дальность).
        /// </summary>
        public static int CountRunning(EntityStore e, int hero, float radius)
        {
            if (e == null || (uint)hero >= (uint)e.Count) return 0;
            float hx = e.Position[hero].X.ToFloat(), hy = e.Position[hero].Y.ToFloat();
            float limit = radius * radius;
            int running = 0;
            for (int i = 0; i < e.Count; i++)
            {
                if (i == hero || !e.Alive[i] || e.Kind[i] != EnemyKind.ForestRootSwarm
                    || e.Velocity[i].LengthSq.Raw == 0) continue;
                float dx = e.Position[i].X.ToFloat() - hx, dy = e.Position[i].Y.ToFloat() - hy;
                if (radius >= 0f && dx * dx + dy * dy > limit) continue;
                running++;
            }
            return running;
        }
    }

    /// <summary>
    /// Чей сейчас бой у CombatAudio: режим игры, симуляция (поколение сессии) и арена
    /// забега. Звать раз в кадр до событий кадра; ответ — что сменилось. Чистый C# —
    /// его гоняют тесты.
    ///
    /// Баг 29.09 («звук, которого тут быть не должно»):
    ///  1) кадр смены симуляции звучал событиями, которые новой Sim не принадлежат:
    ///     TickDriver.SyncGeneration кладёт в кадр список событий новой Sim как есть, а
    ///     у лагерной Sim он не чистился с её последнего шага перед забегом — на
    ///     возврате в лагерь звучали удары и замахи, которых давно нет;
    ///  2) следующая арена — та же Sim и то же поколение (RiftRun.EnterNextRift), и её
    ///     смену никто не замечал: ожидания мобов, хозяева длинных звуков и отложенные
    ///     звуки прошлой арены доживали до новой, где номера сущностей и действий
    ///     начинаются заново.
    /// </summary>
    public sealed class CombatSoundScope
    {
        public enum Change : byte
        {
            /// <summary>Тот же бой.</summary>
            None,

            /// <summary>
            /// Следующая арена забега: Sim та же, сущности расставлены заново. Ожидания,
            /// хозяева голосов и отложенное — прочь; голоса доигрывают под дымной завесой.
            /// </summary>
            Arena,

            /// <summary>
            /// Сменился режим при той же Sim (смерть или конец забега — итоги): бой оборван,
            /// голоса гаснут. События кадра — этой же Sim (последний удар) и звучат.
            /// </summary>
            Mode,

            /// <summary>
            /// Сменилась сама Sim: вход в Разлом, «повторить», возврат в лагерь, Полигон.
            /// Всё гаснет, а события кадра смены не звучат — они прежней Sim или повтор
            /// давнего списка новой.
            /// </summary>
            Simulation,
        }

        private GameMode _mode = GameMode.Camp;
        private int _generation = -1, _depth = -1;

        /// <summary>depth — глубина забега (RiftRun.Depth), −1 — забега нет.</summary>
        public Change Update(GameMode mode, int generation, int depth)
        {
            if (generation != _generation)
            {
                _generation = generation; _mode = mode; _depth = depth;
                return Change.Simulation;
            }
            if (mode != _mode)
            {
                _mode = mode; _depth = depth;
                return Change.Mode;
            }
            if (depth != _depth)
            {
                _depth = depth;
                return Change.Arena;
            }
            return Change.None;
        }

        /// <summary>Звучат ли события кадра с этой сменой.</summary>
        public static bool HearsFrameEvents(Change change) => change != Change.Simulation;

        /// <summary>Бой оборван целиком: голоса гаснут, сердцебиение низкого здоровья тоже.</summary>
        public static bool EndsFight(Change change) => change == Change.Mode || change == Change.Simulation;
    }
}
