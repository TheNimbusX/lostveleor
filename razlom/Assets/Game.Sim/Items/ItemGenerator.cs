namespace Game.Sim
{
    /// <summary>
    /// Разворачивает рецепт предмета в конкретные числа.
    ///
    /// ЧИСТАЯ ФУНКЦИЯ ОТ (рецепт + данные). Никакого состояния забега здесь нет:
    /// генератор берёт СВОЙ локальный Pcg32, засеянный сидом предмета, и не
    /// трогает потоки RngStreams. Иначе один и тот же предмет разворачивался бы
    /// по-разному в зависимости от того, сколько до него бросали кубик в бою,
    /// и «рецепт в сейве» перестал бы работать.
    ///
    /// Сид предмета берётся из потока Rng.Affix при дропе — см. ItemDrop.
    /// </summary>
    public static class ItemGenerator
    {
        /// <summary>
        /// Номер последовательности локального генератора. Константа: меняя её,
        /// вы меняете КАЖДЫЙ предмет у КАЖДОГО игрока.
        /// </summary>
        private const ulong AffixSequence = 0x9E3779B97F4A7C15UL;

        /// <summary>
        /// Сколько аффиксов даёт редкость. Числа — заглушка баланса.
        ///
        /// Бросок делается ровно один и ВСЕГДА, даже для Normal, которому
        /// он не нужен. Если пропускать его для Normal, то добавление в патче
        /// новой редкости или смена правила сдвинет всю последующую
        /// последовательность, и все сохранённые предметы станут другими.
        /// </summary>
        private static void RollAffixCount(ItemRarity rarity, ref Pcg32 rng, out int count)
        {
            int roll = rng.NextInt(0, 100);

            switch (rarity)
            {
                case ItemRarity.Magic: count = 1 + (roll % 2); break;   // 1..2
                case ItemRarity.Rare: count = 3 + (roll % 2); break;    // 3..4
                default: count = 0; break;                              // Normal
            }
        }

        /// <summary>
        /// Разворачивает предмет в переданный буфер.
        /// Возвращает false, если базы с таким id в данных нет.
        /// </summary>
        public static bool Generate(in ItemInstance item, ItemDatabase db, GeneratedItem into)
        {
            into.Clear();
            into.Source = item;

            int baseIndex = db.IndexOfBase(item.BaseId);
            if (baseIndex < 0) return false;

            ItemBaseDefinition baseDef = db.GetBase(baseIndex);
            into.Category = baseDef.Category;

            if (baseDef.HasImplicit)
            {
                into.HasImplicit = true;
                into.ImplicitStat = baseDef.ImplicitStat;
                into.ImplicitOp = baseDef.ImplicitOp;
                into.ImplicitValue = baseDef.ImplicitValue;
            }

            var rng = new Pcg32(item.Seed, AffixSequence);
            RollAffixCount(item.Rarity, ref rng, out int wanted);
            if (wanted > GeneratedItem.MaxAffixes) wanted = GeneratedItem.MaxAffixes;

            // Группы уже занятых аффиксов. Массив, а не множество: их максимум
            // шесть, линейный поиск дешевле хеширования, а порядок обхода
            // гарантирован — в отличие от HashSet. Буфер лежит в GeneratedItem,
            // чтобы разворачивание предмета не стоило ни одной аллокации.
            int usedGroupCount = 0;
            int[] usedGroups = into.UsedGroups;

            for (int slot = 0; slot < wanted; slot++)
            {
                int picked = PickAffix(db, baseDef.Category, item.ItemLevel,
                    usedGroups, usedGroupCount, ref rng);

                // Кандидаты кончились: на низком уровне предмета подходящих
                // аффиксов может просто не быть. Это нормально, а не ошибка.
                if (picked < 0) break;

                AffixDefinition affix = db.GetAffix(picked);
                Fix64 value = rng.NextFix(affix.MinValue, affix.MaxValue);

                into.Add(new RolledAffix(affix.Id, affix.Stat, affix.Op, value));
                usedGroups[usedGroupCount++] = affix.Group;
            }

            return true;
        }

        /// <summary>
        /// Взвешенный выбор аффикса среди подходящих.
        ///
        /// Массив базы отсортирован по id, обход идёт строго по возрастанию
        /// индекса — от этого зависит, какой аффикс достанется какому броску,
        /// и порядок обязан быть одним и тем же всегда.
        ///
        /// Два прохода вместо буфера кандидатов: первый считает сумму весов,
        /// второй отматывает выбранный вес. Так не нужен ни список, ни его
        /// вместимость, ни аллокация.
        /// </summary>
        private static int PickAffix(ItemDatabase db, ItemCategory category, short itemLevel,
            int[] usedGroups, int usedGroupCount, ref Pcg32 rng)
        {
            int totalWeight = 0;
            for (int i = 0; i < db.AffixCount; i++)
            {
                if (!IsEligible(db.GetAffix(i), category, itemLevel, usedGroups, usedGroupCount)) continue;
                totalWeight += db.GetAffix(i).Weight;
            }

            if (totalWeight <= 0) return -1;

            int roll = rng.NextInt(0, totalWeight);
            for (int i = 0; i < db.AffixCount; i++)
            {
                AffixDefinition affix = db.GetAffix(i);
                if (!IsEligible(affix, category, itemLevel, usedGroups, usedGroupCount)) continue;

                roll -= affix.Weight;
                if (roll < 0) return i;
            }

            return -1;
        }

        private static bool IsEligible(in AffixDefinition affix, ItemCategory category, short itemLevel,
            int[] usedGroups, int usedGroupCount)
        {
            if (affix.Weight <= 0) return false;
            if (affix.MinItemLevel > itemLevel) return false;
            if (!affix.AllowedOn(category)) return false;

            for (int g = 0; g < usedGroupCount; g++)
                if (usedGroups[g] == affix.Group) return false;

            return true;
        }
    }
}
