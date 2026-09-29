using System;

namespace Game.Sim
{
    /// <summary>
    /// Навигация и восприятие лесных врагов. Качество боя оценивается по
    /// выбору полезной позиции и доступной атаки. Намеренного уклонения
    /// от способностей героя нет; каждый вид сохраняет свою роль.
    /// Решения используют наблюдаемое состояние Sim, Fix64 и устойчивый
    /// порядок обхода; состояние поведения включено в HashEnemyBrain.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- путь и застревание ----

        /// <summary>Сколько тиков моб идёт по полю пути после того, как упёрся.</summary>
        public const int NavHoldTicks = 45;

        /// <summary>Раз в столько тиков проверяется, сдвинулся ли моб, который хотел идти.</summary>
        public const int StuckProbeTicks = 20;

        /// <summary>Меньше этого за проверку — застрял.</summary>
        public static readonly Fix64 StuckDistance = Fix64.Ratio(15, 100);

        /// <summary>Шаг вбок вдоль препятствия после двух застреваний подряд.</summary>
        public const int UnstickTicks = 30;

        // ---- агро пачки ----

        /// <summary>Заметивший героя зовёт своих в этом радиусе.</summary>
        public static readonly Fix64 PackAlertRadius = Fix64.FromInt(6);

        private EnemyNavGrid _navGrid;
        private int[] _navUntil, _stuckProbeTick, _stuckCount, _unstickUntil, _unstickSide, _alertTick;
        private FixVec2[] _stuckProbePos;
        private bool[] _wantedMove;

        private void AllocateEnemyBrain(int capacity)
        {
            _navUntil = new int[capacity];
            _stuckProbeTick = new int[capacity];
            _stuckCount = new int[capacity];
            _unstickUntil = new int[capacity];
            _unstickSide = new int[capacity];
            _alertTick = new int[capacity];
            _stuckProbePos = new FixVec2[capacity];
            _wantedMove = new bool[capacity];
            _surroundSlot = new int[capacity];
            _surroundNextPick = new int[capacity];
            _surroundWaitSince = new int[capacity];
            _rangedGoal = new FixVec2[capacity];
            _rangedGoalUntil = new int[capacity];
            _rangedGoalMoving = new bool[capacity];
            _engaged = new bool[capacity];
            _bearingFrom = new FixVec2[capacity];
            _bearing = new FixVec2[capacity];
        }

        private void ResetEnemyBrain()
        {
            Array.Clear(_navUntil, 0, _navUntil.Length);
            Array.Clear(_stuckProbeTick, 0, _stuckProbeTick.Length);
            Array.Clear(_stuckCount, 0, _stuckCount.Length);
            Array.Clear(_unstickUntil, 0, _unstickUntil.Length);
            Array.Clear(_unstickSide, 0, _unstickSide.Length);
            Array.Clear(_alertTick, 0, _alertTick.Length);
            Array.Clear(_stuckProbePos, 0, _stuckProbePos.Length);
            Array.Clear(_wantedMove, 0, _wantedMove.Length);
            Array.Clear(_engaged, 0, _engaged.Length);
            Array.Clear(_rangedGoal, 0, _rangedGoal.Length);
            Array.Clear(_rangedGoalUntil, 0, _rangedGoalUntil.Length);
            Array.Clear(_rangedGoalMoving, 0, _rangedGoalMoving.Length);
            _lastStandTick = int.MinValue;
            for (int i = 0; i < _surroundSlot.Length; i++)
            { _surroundSlot[i] = -1; _surroundWaitSince[i] = -1; }
            ResetSurround();
        }

        // ---------- путь ----------

        /// <summary>Идёт ли моб по полю пути: недавно упёрся или застрял.</summary>
        private bool NavActive(int id) => Tick < _navUntil[id];

        private EnemyNavGrid NavGrid()
        {
            if (_layout == null || _layout.Routes == null) return null;
            if (_navGrid == null || !_navGrid.Matches(_layout)) _navGrid = new EnemyNavGrid(_layout);
            return _navGrid;
        }

        /// <summary>
        /// Локальное желаемое направление с обходом к герою. toGoal может быть
        /// коротким steering-вектором, поэтому не считается дальней целью пути.
        /// </summary>
        private FixVec2 SteerHeading(int id, FixVec2 toGoal)
            => SteerAlongRoute(id, toGoal, Entities.Position[PlayerId]);

        /// <summary>
        /// Путь к выбранной мировой точке: союзнику, месту атаки или ожидания.
        /// Прямой проход телом позволяет сразу выйти из устаревшего обхода.
        /// </summary>
        private FixVec2 SteerToward(int id, FixVec2 goal)
        {
            FixVec2 from = Entities.Position[id];
            FixVec2 toGoal = goal - from;
            if (toGoal.LengthSq.Raw == 0) return FixVec2.Zero;
            // Без активного обхода обе ветки ниже возвращают прямое направление.
            // Проверка всего отрезка нужна лишь для выхода из обхода/застревания.
            if (!NavActive(id) && Tick >= _unstickUntil[id]) return toGoal.Normalized();
            if (_layout == null || _layout.CanTravel(from, goal, Entities.BodyRadius[id]))
                return toGoal.Normalized();
            return SteerAlongRoute(id, toGoal, goal);
        }

        private FixVec2 SteerAlongRoute(int id, FixVec2 toGoal, FixVec2 routeGoal)
        {
            if (toGoal.LengthSq.Raw == 0) return FixVec2.Zero;
            FixVec2 straight = toGoal.Normalized();
            if (Tick < _unstickUntil[id])
            {
                var side = new FixVec2(-straight.Y, straight.X);
                if (_unstickSide[id] < 0) side = -side;
                return (side * Fix64.Ratio(4, 5) + straight * Fix64.Ratio(1, 5)).Normalized();
            }
            if (!NavActive(id)) return straight;
            var grid = NavGrid();
            if (grid != null && grid.TryHeading(Entities.Position[id], routeGoal,
                    Entities.BodyRadius[id], out var heading))
                return heading;
            return straight;
        }

        /// <summary>
        /// Шаг моба своим ходом: скольжение вдоль стены, как у всех
        /// (MoveInsideLayout), плюс учёт для пути: не прошёл полный шаг —
        /// упёрся, и ближайшие NavHoldTicks моб идёт по полю пути.
        /// </summary>
        private FixVec2 EnemyStep(int id, FixVec2 from, FixVec2 delta)
        {
            FixVec2 moved = MoveInsideLayout(id, from, delta);
            if (delta.LengthSq.Raw != 0)
            {
                _wantedMove[id] = true;
                if (!moved.Equals(from + delta)) _navUntil[id] = Tick + NavHoldTicks;
            }
            return moved;
        }

        /// <summary>
        /// Проверка застревания раз в StuckProbeTicks (со сдвигом по индексу,
        /// чтобы проверки не падали на один тик). Хотел идти и сдвинулся меньше
        /// StuckDistance — застрял: поле пути, а со второго раза подряд — шаг
        /// вбок на ту сторону, где свободно.
        /// </summary>
        private void ProbeStuck(int id)
        {
            if ((Tick + id) % StuckProbeTicks != 0) return;
            FixVec2 position = Entities.Position[id];
            bool wanted = _wantedMove[id];
            _wantedMove[id] = false;
            bool stuck = wanted && _stuckProbeTick[id] > 0
                && FixVec2.DistanceSq(position, _stuckProbePos[id]) < StuckDistance * StuckDistance;
            _stuckProbePos[id] = position;
            _stuckProbeTick[id] = Tick;
            if (!stuck) { _stuckCount[id] = 0; return; }
            _stuckCount[id]++;
            _navUntil[id] = Tick + NavHoldTicks + StuckProbeTicks;
            if (_stuckCount[id] < 2) return;
            // Сторона — та, где в полутора метрах вбок от взгляда на героя проходимо.
            FixVec2 toHero = Entities.Position[PlayerId] - position;
            FixVec2 look = toHero.LengthSq.Raw != 0 ? toHero.Normalized() : Entities.Facing[id];
            var left = new FixVec2(-look.Y, look.X);
            Fix64 radius = Entities.BodyRadius[id];
            bool leftOpen = _layout == null || _layout.IsWalkable(position + left * Fix64.Ratio(3, 2), radius);
            bool rightOpen = _layout == null || _layout.IsWalkable(position - left * Fix64.Ratio(3, 2), radius);
            int side = leftOpen && !rightOpen ? 1 : rightOpen && !leftOpen ? -1 : (_stuckCount[id] & 1) == 0 ? 1 : -1;
            _unstickSide[id] = side;
            _unstickUntil[id] = Tick + UnstickTicks;
        }

        // ---------- агро пачки ----------

        /// <summary>
        /// Моб заметил героя — соседи в PackAlertRadius заметят через
        /// 6 + (id % 4) × 3 тиков, даже если сами героя ещё не видят. Раньше
        /// член пачки в 7,5 м стоял, пока его сосед дрался.
        /// </summary>
        private void AlertPack(int source)
        {
            FixVec2 at = Entities.Position[source];
            Fix64 radiusSq = PackAlertRadius * PackAlertRadius;
            for (int j = 1; j < Entities.Count; j++)
            {
                if (j == source || !Entities.Alive[j] || Entities.Aggro[j]) continue;
                if (Entities.Side[j] != Entities.Side[source]) continue;
                if (FixVec2.DistanceSq(at, Entities.Position[j]) > radiusSq) continue;
                int when = Tick + 6 + (j % 4) * 3;
                if (_alertTick[j] == 0 || when < _alertTick[j]) _alertTick[j] = when;
            }
        }

        /// <summary>Позвала ли пачка: срок подошёл — агро без обнаружения.</summary>
        private bool PackAlerted(int id) => _alertTick[id] > 0 && Tick >= _alertTick[id];

        // ---- последние идут сами ----

        /// <summary>
        /// Последних врагов арены не ищут по всей поляне (поток D, 29.09): когда
        /// живых врагов встречи столько или меньше, а бой уже идёт (кто-то
        /// заметил героя или пал), оставшиеся замечают героя сами и идут к нему.
        /// Стенд ощущения: последний Корнехват в 35–40 м так и не видел героя,
        /// герой шёл к нему 8 с, пауза без атак — до 16,6 с.
        /// </summary>
        public const int LastStandEnemies = 2;

        // Ответ на тик: живых врагов мало и бой идёт. Считается из состояния
        // при первом вопросе в тике — не состояние, в хеш не идёт.
        private int _lastStandTick = int.MinValue;
        private bool _lastStand;

        /// <summary>Встреча по шаблону, бой начался, живых врагов не больше LastStandEnemies.</summary>
        private bool LastStandAlert()
        {
            if (_encounter == null) return false;
            if (_lastStandTick == Tick) return _lastStand;
            _lastStandTick = Tick;
            int alive = 0;
            bool fighting = false;
            for (int i = 1; i < Entities.Count; i++)
            {
                if (Entities.Side[i] == Faction.Wole) continue;
                if (!Entities.Alive[i]) { fighting = true; continue; }
                alive++;
                if (Entities.Aggro[i]) fighting = true;
            }
            _lastStand = fighting && alive <= LastStandEnemies;
            return _lastStand;
        }

        private void HashEnemyBrain(ref ulong hash)
        {
            bool any = false;
            for (int i = 1; i < Entities.Count && !any; i++)
                any = _navUntil[i] != 0 || _alertTick[i] != 0 || _surroundSlot[i] >= 0
                    || _stuckProbeTick[i] != 0 || _rangedGoalUntil[i] != 0;
            if (!any) return;
            Hashing.Mix(ref hash, 0x4252414E);
            HashSurround(ref hash);
            for (int i = 1; i < Entities.Count; i++)
            {
                Hashing.Mix(ref hash, _navUntil[i]); Hashing.Mix(ref hash, _stuckProbeTick[i]);
                Hashing.Mix(ref hash, _stuckCount[i]); Hashing.Mix(ref hash, _unstickUntil[i]);
                Hashing.Mix(ref hash, _unstickSide[i]); Hashing.Mix(ref hash, _alertTick[i]);
                Hashing.Mix(ref hash, _stuckProbePos[i].X); Hashing.Mix(ref hash, _stuckProbePos[i].Y);
                Hashing.Mix(ref hash, _wantedMove[i] ? 1 : 0);
                Hashing.Mix(ref hash, _surroundSlot[i]); Hashing.Mix(ref hash, _engaged[i] ? 1 : 0);
            }
        }
    }
}
