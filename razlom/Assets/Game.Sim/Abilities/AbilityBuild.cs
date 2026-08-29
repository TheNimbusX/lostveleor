using System;

namespace Game.Sim
{
    /// <summary>
    /// Способность со всеми применёнными узлами — то, чем пользуется бой.
    ///
    /// Пересобирается только при смене взятых узлов, а не каждый каст:
    /// та же логика грязного флага, что и в StatSheet, и по той же причине.
    /// </summary>
    public sealed class AbilityBuild
    {
        private const int StatCount = (int)AbilityStatType.Count;
        private const int StageCount = (int)AbilityStage.Count;

        /// <summary>Больше восьми эффектов на одну стадию узлы одной способности не дадут.</summary>
        public const int MaxEffectsPerStage = 8;

        private readonly Fix64[] _stats = new Fix64[StatCount];

        // Эффекты по стадиям, плоским массивом: [стадия * MaxEffectsPerStage + номер].
        // Массив, а не список списков — обход подряд и ноль аллокаций при пересборке.
        private readonly AbilityEffect[] _effects = new AbilityEffect[StageCount * MaxEffectsPerStage];
        private readonly int[] _effectCount = new int[StageCount];

        public AbilityFlag Flags { get; private set; }

        /// <summary>Сколько раз пересобирался билд. Только для тестов и профилировки.</summary>
        public int RebuildCount { get; private set; }

        public Fix64 Get(AbilityStatType stat) => _stats[(int)stat];

        /// <summary>Кулдаун в тиках. Округление вниз: дробных тиков не бывает.</summary>
        public int CooldownTicks
        {
            get
            {
                int ticks = Get(AbilityStatType.CooldownTicks).ToInt();
                return ticks < 1 ? 1 : ticks;
            }
        }

        public bool Has(AbilityFlag flag) => (Flags & flag) != 0;

        public int EffectCount(AbilityStage stage) => _effectCount[(int)stage];

        public AbilityEffect GetEffect(AbilityStage stage, int index)
            => _effects[(int)stage * MaxEffectsPerStage + index];

        /// <summary>
        /// Собирает билд из базовых статов и взятых узлов.
        ///
        /// УЗЛЫ ПРИМЕНЯЮТСЯ ПО ВОЗРАСТАНИЮ Id. Массив узлов сортируется здесь же,
        /// а не доверяется вызывающему: порядок взятия узлов игроком не должен
        /// влиять на итог, а умножение в Fix64 округляет и порядок замечает.
        ///
        /// Эффекты в стадию тоже встают в порядке Id — от него зависит,
        /// какой из двух вставленных эффектов отработает первым.
        /// </summary>
        public void Rebuild(AbilityDefinition definition, AbilityNode[] nodes, int nodeCount)
        {
            Array.Sort(nodes, 0, nodeCount, NodeIdOrder.Instance);

            Span<Fix64> flat = stackalloc Fix64[StatCount];
            Span<Fix64> increased = stackalloc Fix64[StatCount];
            Span<Fix64> more = stackalloc Fix64[StatCount];

            for (int i = 0; i < StatCount; i++)
            {
                flat[i] = Fix64.Zero;
                increased[i] = Fix64.Zero;
                more[i] = Fix64.One;
            }

            Flags = definition.BaseFlags;
            for (int i = 0; i < StageCount; i++) _effectCount[i] = 0;

            for (int n = 0; n < nodeCount; n++)
            {
                AbilityNode node = nodes[n];
                switch (node.Kind)
                {
                    case NodeKind.StatMod:
                        int s = (int)node.Stat;
                        if (node.Op == ModifierOp.Flat) flat[s] += node.Value;
                        else if (node.Op == ModifierOp.Increased) increased[s] += node.Value;
                        else more[s] *= Fix64.One + node.Value;
                        break;

                    case NodeKind.Flag:
                        Flags |= node.EnabledFlag;
                        break;

                    case NodeKind.EffectInsert:
                        InsertEffect(node.Stage, node.Effect);
                        break;
                }
            }

            for (int i = 0; i < StatCount; i++)
                _stats[i] = StatMath.Combine(definition.GetBase((AbilityStatType)i), flat[i], increased[i], more[i]);

            RebuildCount++;
        }

        private void InsertEffect(AbilityStage stage, AbilityEffect effect)
        {
            int stageIndex = (int)stage;
            if (_effectCount[stageIndex] >= MaxEffectsPerStage) return;

            _effects[stageIndex * MaxEffectsPerStage + _effectCount[stageIndex]] = effect;
            _effectCount[stageIndex]++;
        }

        public void HashInto(ref ulong hash)
        {
            for (int i = 0; i < StatCount; i++) Hashing.Mix(ref hash, _stats[i]);
            Hashing.Mix(ref hash, (int)Flags);

            for (int s = 0; s < StageCount; s++)
            {
                Hashing.Mix(ref hash, _effectCount[s]);
                for (int e = 0; e < _effectCount[s]; e++)
                    Hashing.Mix(ref hash, (int)_effects[s * MaxEffectsPerStage + e]);
            }
        }

        /// <summary>
        /// Сравнение узлов по Id. Отдельный класс-одиночка, а не лямбда:
        /// лямбда в Array.Sort порождает делегат на каждый вызов, а пересборка
        /// билда случается при каждой правке дерева.
        /// </summary>
        private sealed class NodeIdOrder : System.Collections.Generic.IComparer<AbilityNode>
        {
            public static readonly NodeIdOrder Instance = new NodeIdOrder();
            public int Compare(AbilityNode a, AbilityNode b) => a.Id.CompareTo(b.Id);
        }
    }
}
