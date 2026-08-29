namespace Game.Sim
{
    /// <summary>
    /// Слот снаряжения. Пока ровно один на категорию базы — категории уже есть
    /// в ItemCategory, и заводить слоты, которых не из чего заполнять, рано.
    ///
    /// ЭТО ЗАГЛУШКА, как и PrototypeContent: настоящий набор слотов (шлем,
    /// нагрудник, две руки, кольца) — решение владельца, и оно ещё не принято.
    /// Значения попадут в сейв, поэтому НОВЫЕ СЛОТЫ ДОБАВЛЯТЬ ТОЛЬКО ПЕРЕД Count,
    /// а существующие не переставлять.
    /// </summary>
    public enum EquipSlot : byte
    {
        Weapon = 0,
        Armor = 1,
        Jewellery = 2,

        /// <summary>Не слот. Размер массива.</summary>
        Count = 3,
    }

    /// <summary>
    /// Надетое на одну сущность.
    ///
    /// Хранит РЕЦЕПТЫ, а не посчитанные статы — по той же причине, по которой
    /// их хранит сейв: ребаланс аффиксов обязан применяться сам. Числа
    /// разворачиваются из рецепта в переиспользуемый буфер при каждом
    /// применении и никуда не запоминаются.
    ///
    /// СНАРЯЖЕНИЕ ПРИНАДЛЕЖИТ ПЕРСОНАЖУ, А НЕ СИМУЛЯЦИИ. Забег — единица жизни
    /// симуляции: каждый вход в Разлом создаёт новую, с новыми листами статов.
    /// Надетое переживает это, поэтому лист не задаётся в конструкторе, а
    /// привязывается через Bind на входе в забег и на Полигоне.
    /// </summary>
    public sealed class Equipment
    {
        private const int SlotCount = (int)EquipSlot.Count;

        private readonly ItemInstance[] _worn = new ItemInstance[SlotCount];
        private readonly ItemDatabase _items;
        private StatSheet _sheet;

        /// <summary>Буфер разворачивания. Один на всё снаряжение: аллокаций быть не должно.</summary>
        private readonly GeneratedItem _buffer = new GeneratedItem();

        public Equipment(ItemDatabase items)
        {
            _items = items;
        }

        /// <summary>
        /// Привязывает снаряжение к листу статов и сразу вешает на него всё надетое.
        ///
        /// Зовётся на входе в каждый забег и на Полигоне: лист принадлежит слоту
        /// сущности, а слот рождается заново вместе с симуляцией.
        /// </summary>
        public void Bind(StatSheet sheet)
        {
            _sheet = sheet;
            Reapply();
        }

        public ItemInstance Worn(EquipSlot slot) => _worn[(int)slot];
        public bool IsWorn(EquipSlot slot) => !_worn[(int)slot].IsEmpty;

        /// <summary>
        /// Слот, в который идёт база. Категория и слот пока в точности одно и то же,
        /// но приводит их друг к другу одно место: когда слотов станет больше,
        /// чем категорий, менять придётся здесь и только здесь.
        /// </summary>
        public static EquipSlot SlotOf(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Weapon: return EquipSlot.Weapon;
                case ItemCategory.Armor: return EquipSlot.Armor;
                default: return EquipSlot.Jewellery;
            }
        }

        /// <summary>
        /// Надевает предмет в слот его категории и возвращает то, что было надето
        /// там раньше (пустой ItemInstance, если слот был свободен).
        ///
        /// Возвращает false, если базы нет в справочнике: чужой предмет лучше
        /// не надеть, чем надеть пустым.
        /// </summary>
        public bool Equip(in ItemInstance item, out ItemInstance replaced)
        {
            replaced = default;

            int baseIndex = _items.IndexOfBase(item.BaseId);
            if (baseIndex < 0) return false;

            EquipSlot slot = SlotOf(_items.GetBase(baseIndex).Category);
            replaced = _worn[(int)slot];
            _worn[(int)slot] = item;

            ApplySlot(slot);
            return true;
        }

        /// <summary>Снимает предмет и возвращает его. Прибавки уходят ровно свои.</summary>
        public ItemInstance Unequip(EquipSlot slot)
        {
            ItemInstance was = _worn[(int)slot];
            _worn[(int)slot] = default;

            ApplySlot(slot);
            return was;
        }

        /// <summary>
        /// Вешает всё надетое на лист заново.
        ///
        /// Нужно после каждого Spawn: индекс сущности — это identity, лист
        /// принадлежит слоту сущности и при рождении сбрасывается целиком,
        /// в том числе и на входе в следующий Разлом.
        /// </summary>
        public void Reapply()
        {
            for (int slot = 0; slot < SlotCount; slot++) ApplySlot((EquipSlot)slot);
        }

        /// <summary>
        /// Приводит один слот в соответствие с тем, что в нём лежит.
        ///
        /// Сначала снимает СВОИ прошлые модификаторы по паре (Equipment, слот),
        /// потом вешает новые. Поэтому операция идемпотентна, а порядок надевания
        /// на результат не влияет: канонический порядок в списке держит сам
        /// StatSheet.
        /// </summary>
        private void ApplySlot(EquipSlot slot)
        {
            // Без привязанного листа снаряжение просто хранит рецепты: так оно
            // и лежит в лагере между забегами.
            if (_sheet == null) return;

            int id = (int)slot;
            _sheet.RemoveSource(ModifierSource.Equipment, id);

            if (_worn[id].IsEmpty) return;
            if (!ItemGenerator.Generate(in _worn[id], _items, _buffer)) return;

            _buffer.ApplyTo(_sheet, id);
        }

        /// <summary>
        /// Хешируются рецепты, а не прибавки: прибавки уже попадут в хеш через
        /// лист статов, а расхождение в самих надетых вещах так видно раньше.
        /// </summary>
        public void HashInto(ref ulong hash)
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                Hashing.Mix(ref hash, _worn[slot].IsEmpty ? 0 : 1);
                if (!_worn[slot].IsEmpty) _worn[slot].HashInto(ref hash);
            }
        }
    }
}
