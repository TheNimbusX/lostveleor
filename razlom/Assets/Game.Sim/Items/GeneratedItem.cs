namespace Game.Sim
{
    /// <summary>Один развёрнутый аффикс: какой стат, в каком слое, с каким числом.</summary>
    public readonly struct RolledAffix
    {
        public readonly int AffixId;
        public readonly StatType Stat;
        public readonly ModifierOp Op;
        public readonly Fix64 Value;

        public RolledAffix(int affixId, StatType stat, ModifierOp op, Fix64 value)
        {
            AffixId = affixId;
            Stat = stat;
            Op = op;
            Value = value;
        }
    }

    /// <summary>
    /// Предмет, развёрнутый из рецепта. ПЕРЕИСПОЛЬЗУЕМЫЙ БУФЕР, а не результат:
    /// генератор пишет сюда, вызывающий читает и забывает.
    ///
    /// Так сделано затем, что в гриндилке предметы разворачиваются пачками —
    /// при загрузке сейва, при открытии инвентаря, при каждом дропе. Возвращать
    /// новый объект на каждый значило бы кормить сборщик мусора.
    /// </summary>
    public sealed class GeneratedItem
    {
        /// <summary>Больше шести аффиксов ни одна редкость не даёт.</summary>
        public const int MaxAffixes = 6;

        public ItemInstance Source { get; internal set; }
        public ItemCategory Category { get; internal set; }

        public bool HasImplicit { get; internal set; }
        public StatType ImplicitStat { get; internal set; }
        public ModifierOp ImplicitOp { get; internal set; }
        public Fix64 ImplicitValue { get; internal set; }

        private readonly RolledAffix[] _affixes = new RolledAffix[MaxAffixes];
        public int AffixCount { get; internal set; }

        /// <summary>
        /// Черновик генератора: группы уже занятых аффиксов. Живёт здесь,
        /// а не в самом генераторе, потому что буфер обязан выделяться один раз
        /// на буфер предмета, а не на каждый разворачиваемый предмет.
        /// </summary>
        internal readonly int[] UsedGroups = new int[MaxAffixes];

        public RolledAffix GetAffix(int index) => _affixes[index];

        internal void Clear()
        {
            AffixCount = 0;
            HasImplicit = false;
        }

        internal void Add(in RolledAffix affix)
        {
            if (AffixCount >= MaxAffixes) return;
            _affixes[AffixCount++] = affix;
        }

        /// <summary>
        /// Вешает предмет на лист статов. sourceId — слот снаряжения:
        /// по нему же предмет потом снимается целиком через RemoveSource.
        ///
        /// Порядок добавления значения не имеет: StatSheet держит модификаторы
        /// в каноническом порядке сам.
        /// </summary>
        public void ApplyTo(StatSheet sheet, int sourceId)
        {
            if (HasImplicit)
                sheet.Add(new StatModifier(ImplicitStat, ImplicitOp, ImplicitValue,
                    ModifierSource.Equipment, sourceId));

            for (int i = 0; i < AffixCount; i++)
                sheet.Add(new StatModifier(_affixes[i].Stat, _affixes[i].Op, _affixes[i].Value,
                    ModifierSource.Equipment, sourceId));
        }

        public void HashInto(ref ulong hash)
        {
            Source.HashInto(ref hash);
            Hashing.Mix(ref hash, (int)Category);
            Hashing.Mix(ref hash, HasImplicit ? 1 : 0);
            if (HasImplicit) Hashing.Mix(ref hash, ImplicitValue);

            Hashing.Mix(ref hash, AffixCount);
            for (int i = 0; i < AffixCount; i++)
            {
                Hashing.Mix(ref hash, _affixes[i].AffixId);
                Hashing.Mix(ref hash, (int)_affixes[i].Stat);
                Hashing.Mix(ref hash, (int)_affixes[i].Op);
                Hashing.Mix(ref hash, _affixes[i].Value);
            }
        }
    }
}
