using System;

namespace Game.Sim
{
    /// <summary>
    /// Действие Эни в истории вещи. Числа лежат в сохранениях: не переставлять, только
    /// дописывать. 06.10 Refine стал закалкой (Temper), Replace — переплавкой (Remelt):
    /// значения те же, поэтому вещи сегодняшних профилей читаются без миграции.
    /// </summary>
    public enum ForgeOperation : byte
    {
        /// <summary>Закалка: Slot — свойство (BaseSlot — базовое свойство основы), Fraction — рост сессии в долях диапазона.</summary>
        Temper = 0,
        /// <summary>Переплавка: Slot — свойство, AffixId — новое свойство, Fraction — доля его диапазона.</summary>
        Remelt = 1,
        /// <summary>Добавление: Slot — новый индекс (= число свойств), AffixId и Fraction — как у переплавки.</summary>
        Add = 2,
        /// <summary>Перенос свойства удалён 06.10. Значение не принимается никогда: это порча файла.</summary>
        Transfer = 3,
        /// <summary>Трещина: Slot = BaseSlot, остальное пусто. Съедает попытку; три трещины — вещь расколота.</summary>
        Crack = 4,
        /// <summary>Шедевр рискованного удара: Slot = BaseSlot, остальное пусто. После него — только сердце.</summary>
        Masterpiece = 5,
        /// <summary>Сердце босса: AffixId — ключ босса (RunBossKeys), Slot — HeartFacet. На статы не влияет.</summary>
        Heart = 6,
    }
    public readonly struct CraftStep
    {
        public readonly ForgeOperation Operation;
        public readonly byte Slot;
        public readonly int AffixId;
        public readonly Fix64 Fraction;
        public CraftStep(ForgeOperation operation, byte slot, int affixId, Fix64 fraction)
        { Operation = operation; Slot = slot; AffixId = affixId; Fraction = fraction; }
    }
    /// <summary>Неизменяемая история действий: хранит долю диапазона, а не готовые прибавки.</summary>
    public sealed class CraftingRecipe
    {
        public const int MaximumSteps = 1024;
        /// <summary>Слот шага «базовое свойство основы», а не аффикс: закалка обычной вещи, трещина, шедевр.</summary>
        public const byte BaseSlot = 0xFF;
        /// <summary>Шедевр: +25% диапазона к каждому свойству, выше максимума (пробел плана №15).</summary>
        public static readonly Fix64 MasterpieceStep = Fix64.Ratio(25, 100);
        /// <summary>Потолок закалки базового свойства: +100% основы за всю жизнь вещи (пробел №15).</summary>
        public static readonly Fix64 ImplicitTemperCap = Fix64.One;

        readonly CraftStep[] _steps;
        public int Count => _steps.Length;
        public CraftStep Step(int index) => _steps[index];

        // Счётчики считаются один раз в конструкторе: рецепт неизменяем, а окна и цены
        // спрашивают их на каждой перерисовке.
        /// <summary>Потраченные попытки: закалка, трещина, переплавка, добавление. Сердце и шедевр попыток не тратят (пробел №13).</summary>
        public int AttemptsUsed { get; }
        public int Cracks { get; }
        /// <summary>Оплаченные действия над вещью, включая треснувшие: от них цена растёт ×1,5 (пробел №14).</summary>
        public int PaidActions { get; }
        public int TemperSteps { get; }
        public bool IsMasterpiece { get; }
        public int HeartCount { get; }

        public CraftingRecipe(CraftStep[] steps)
        {
            if (steps == null || steps.Length > MaximumSteps) throw new ArgumentException(nameof(steps));
            _steps = (CraftStep[])steps.Clone();
            foreach (var step in _steps)
                switch (step.Operation)
                {
                    case ForgeOperation.Temper: TemperSteps++; AttemptsUsed++; PaidActions++; break;
                    case ForgeOperation.Crack: Cracks++; AttemptsUsed++; PaidActions++; break;
                    case ForgeOperation.Remelt: case ForgeOperation.Add: AttemptsUsed++; PaidActions++; break;
                    case ForgeOperation.Masterpiece: IsMasterpiece = true; PaidActions++; break;
                    case ForgeOperation.Heart: HeartCount++; PaidActions++; break;
                }
        }
        public CraftingRecipe Append(CraftStep step)
        {
            if (Count >= MaximumSteps) throw new InvalidOperationException("История ковки заполнена");
            var steps = new CraftStep[Count + 1]; Array.Copy(_steps, steps, Count); steps[Count] = step;
            return new CraftingRecipe(steps);
        }

        /// <summary>Сердце номер index по порядку вплавления: ключ босса и грань.</summary>
        public void HeartAt(int index, out int bossKey, out HeartFacet facet)
        {
            bossKey = 0; facet = HeartFacet.None;
            foreach (var step in _steps)
            {
                if (step.Operation != ForgeOperation.Heart) continue;
                if (index-- != 0) continue;
                bossKey = step.AffixId; facet = (HeartFacet)step.Slot; return;
            }
        }

        /// <summary>
        /// Шаг устроен правильно — иначе сохранение повреждено (кодек v10 бросает
        /// InvalidDataException). Transfer = 3 не принимается никогда: перенос удалён 06.10.
        /// Новые действия Эни (4…6) дописаны сюда же, версия сохранения осталась 10.
        /// </summary>
        public static bool IsStructurallyValid(in CraftStep step)
        {
            var operation = step.Operation;
            bool known = operation == ForgeOperation.Temper || operation == ForgeOperation.Remelt || operation == ForgeOperation.Add
                || operation == ForgeOperation.Crack || operation == ForgeOperation.Masterpiece || operation == ForgeOperation.Heart;
            return known
               && (step.Slot < GeneratedItem.MaxAffixes || step.Slot == BaseSlot)
               && step.Fraction >= Fix64.Zero && step.Fraction <= Fix64.One;
        }

        /// <summary>
        /// Шаг допустим по нынешним правилам и справочнику. Нет — кодек срезает рецепт
        /// вещи целиком, а не отказывает в загрузке: справочник и правила меняются патчами.
        /// </summary>
        public static bool IsAllowed(in CraftStep step, ItemDatabase db, ItemRarity rarity, ItemCategory category)
        {
            // Артефакты Эни не трогает вовсе.
            if (category == ItemCategory.Artifact) return false;
            switch (step.Operation)
            {
                case ForgeOperation.Temper:
                    // Обычная вещь закаляет только базовое свойство, остальные — только аффиксы (пробел №20).
                    return step.AffixId == 0 && step.Fraction > Fix64.Zero
                        && (rarity == ItemRarity.Normal ? step.Slot == BaseSlot : step.Slot != BaseSlot);
                case ForgeOperation.Crack:
                case ForgeOperation.Masterpiece:
                    return step.Slot == BaseSlot && step.AffixId == 0 && step.Fraction == Fix64.Zero;
                case ForgeOperation.Heart:
                    return step.Fraction == Fix64.Zero && Camp.IsHeartFacetOf(step.AffixId, (HeartFacet)step.Slot);
                case ForgeOperation.Remelt:
                case ForgeOperation.Add:
                {
                    // Уникальная вещь не переплавляется и не дополняется: её набор свойств — её лицо.
                    if (rarity != ItemRarity.Magic && rarity != ItemRarity.Rare) return false;
                    int affix = db.IndexOfAffix(step.AffixId);
                    return affix >= 0 && step.Slot != BaseSlot && db.GetAffix(affix).AllowedOn(category);
                }
                default: return false;
            }
        }

        internal void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Count);
            foreach (var step in _steps)
            { Hashing.Mix(ref hash, (int)step.Operation); Hashing.Mix(ref hash, step.Slot); Hashing.Mix(ref hash, step.AffixId); Hashing.Mix(ref hash, step.Fraction); }
        }
        internal static bool Same(CraftingRecipe a, CraftingRecipe b)
        {
            int count = a?.Count ?? 0;
            if (count != (b?.Count ?? 0)) return false;
            for (int i = 0; i < count; i++)
            {
                var x = a.Step(i); var y = b.Step(i);
                if (x.Operation != y.Operation || x.Slot != y.Slot || x.AffixId != y.AffixId || x.Fraction != y.Fraction) return false;
            }
            return true;
        }
    }
}
