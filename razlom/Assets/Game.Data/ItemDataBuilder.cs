using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.Data
{
    /// <summary>
    /// Граница между данными редактора и симуляцией.
    ///
    /// Единственное место, где ScriptableObject превращается в плоские
    /// структуры. Вызывается ОДИН РАЗ при загрузке: дальше симуляция работает
    /// с ItemDatabase и про Unity не знает.
    /// </summary>
    public static class ItemDataBuilder
    {
        public static ItemDatabase Build(IReadOnlyList<ItemBaseAsset> bases, IReadOnlyList<AffixAsset> affixes)
        {
            var baseDefs = new ItemBaseDefinition[bases.Count];
            for (int i = 0; i < bases.Count; i++)
            {
                if (bases[i] == null)
                {
                    Debug.LogError($"[Разлом] В списке баз предметов пустая ссылка на позиции {i}.");
                    continue;
                }
                baseDefs[i] = bases[i].ToDefinition();
            }

            var affixDefs = new AffixDefinition[affixes.Count];
            for (int i = 0; i < affixes.Count; i++)
            {
                if (affixes[i] == null)
                {
                    Debug.LogError($"[Разлом] В списке аффиксов пустая ссылка на позиции {i}.");
                    continue;
                }
                affixDefs[i] = affixes[i].ToDefinition();
            }

            // ItemDatabase сам отсортирует по id и проверит уникальность:
            // порядок, в котором Unity отдала ассеты, значения не имеет
            // и иметь не должен.
            return new ItemDatabase(baseDefs, affixDefs);
        }
    }
}
