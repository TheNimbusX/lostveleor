using System;
using System.IO;

namespace Game.Sim
{
    /// <summary>
    /// Закалка Эни (решение 06.10): попытки по редкости, трещины, рост цены, броски,
    /// открытая сессия кузнеца и её хранение, грани сердец на надетых вещах.
    ///
    /// СЕССИЯ ПИШЕТ РОСТ В ВЕЩЬ СРАЗУ. Каждый удачный удар заменяет шаг закалки этой
    /// сессии в рецепте, трещина — заменяет его трещиной. Поэтому вещь в любой момент
    /// такая, какой её надо унести: вход в Разлом, выход из игры и перекладывание не
    /// требуют «забрать». Сама запись сессии нужна только для номера удара (риск растёт)
    /// и для оплаченного выбора переплавки/добавления. Закалка закрывается окном, другим
    /// действием и концом реального забега (RecordRealAttemptEnded); переплавка ждёт выбора.
    /// </summary>
    public sealed partial class Camp
    {
        public const int CracksToShatter = 3, RiskySuccessPercent = 50, AddOverflowCrackPercent = 50;
        /// <summary>Бонус разбора за каждое оплаченное действие без трещины (пробел №18).</summary>
        public const int SalvageBonusPerAction = 5;

        // Потоки бросков. Константы навсегда: смена меняет исход у всех сохранённых сессий.
        const ulong TemperStream = 0x54454D504552UL, OverflowStream = 0x4F56455246UL, RiskyStream = 0x5249534B59UL;

        /// <summary>Рост удара strike (с нуля) в долях диапазона: 10 / 15 / 20 / 25% (пробел №15).</summary>
        public static Fix64 TemperGrowth(int strike)
            => strike <= 0 ? Fix64.Ratio(10, 100) : strike == 1 ? Fix64.Ratio(15, 100) : strike == 2 ? Fix64.Ratio(20, 100) : Fix64.Ratio(25, 100);

        /// <summary>Шанс трещины удара strike: первый без риска, дальше 15 / 30 / 50%.</summary>
        public static int CrackPercent(int strike) => strike <= 0 ? 0 : strike == 1 ? 15 : strike == 2 ? 30 : 50;

        /// <summary>Рост сессии из strikes удачных ударов, не больше всего диапазона.</summary>
        public static Fix64 SessionGrowth(int strikes)
        {
            Fix64 sum = Fix64.Zero;
            for (int k = 0; k < strikes && sum < Fix64.One; k++) sum += TemperGrowth(k);
            return Fix64.Min(sum, Fix64.One);
        }

        /// <summary>Сколько попыток закалки вещь уже потратила: закалка, трещина, переплавка, добавление.</summary>
        public int AttemptsUsed(in ItemInstance i) => i.Crafting?.AttemptsUsed ?? 0;

        /// <summary>Попыток закалки по редкости: обычная 2, редкая 3, эпическая 4, уникальная 2.</summary>
        public static int TemperAttempts(ItemRarity r)
        {
            switch (r)
            {
                case ItemRarity.Magic: return 3;
                case ItemRarity.Rare: return 4;
                default: return 2;
            }
        }

        public int AttemptsLeft(in ItemInstance i) => Math.Max(0, TemperAttempts(i.Rarity) - AttemptsUsed(i));

        /// <summary>Трещины на вещи. Не чинятся; три — вещь расколота.</summary>
        public int CrackCount(in ItemInstance i) => i.Crafting?.Cracks ?? 0;

        public bool IsShattered(in ItemInstance i) => CrackCount(i) >= CracksToShatter;

        public bool IsMasterpiece(in ItemInstance i) => i.Crafting?.IsMasterpiece == true;

        /// <summary>
        /// Цена после paidActions оплаченных действий над вещью: ×1,5 за каждое, вверх
        /// от точной дроби base·3ⁿ/2ⁿ (80 → 120 → 180, 5 → 8 → 12).
        /// </summary>
        public static int Escalate(int basePrice, int paidActions)
        {
            if (basePrice <= 0) return 0;
            // 3²⁰·int.MaxValue ещё помещается в long; столько действий над вещью не бывает.
            int n = Math.Min(Math.Max(paidActions, 0), 20);
            long numerator = basePrice, denominator = 1;
            for (int i = 0; i < n; i++) { numerator *= 3; denominator *= 2; }
            long price = (numerator + denominator - 1) / denominator;
            return price > int.MaxValue ? int.MaxValue : (int)price;
        }

        /// <summary>Бросок 0..99 действия index над вещью до оплаты: перезагрузка даёт тот же исход.</summary>
        static int Roll(in ItemInstance snapshot, int index, ulong stream)
        {
            ulong seed = Hashing.Offset; snapshot.HashInto(ref seed); Hashing.Mix(ref seed, index);
            var rng = new Pcg32(seed, stream);
            return rng.NextInt(0, 100);
        }

        // ---- сердца ----

        /// <summary>Сколько сердец можно вплавить в одну вещь: одно, на ранге 3 — два (пробел №19).</summary>
        public int HeartSlots => CampRank >= MaxCampRank ? 2 : 1;

        /// <summary>Граней у сердца босса. Пока они есть только у Хозяина Чащи; у боссов 2–3 появятся с ними.</summary>
        public static int HeartFacetCount(int bossKey) => bossKey != 0 && bossKey == RunBossKeys.ThicketMaster ? 3 : 0;

        /// <summary>Грань index сердца босса (выбор 1 из 3 при вплавлении); вне диапазона — None.</summary>
        public static HeartFacet HeartFacetAt(int bossKey, int index)
            => (uint)index < (uint)HeartFacetCount(bossKey) ? (HeartFacet)((int)HeartFacet.ThicketRoots + index) : HeartFacet.None;

        public static bool IsHeartFacetOf(int bossKey, HeartFacet facet)
        {
            if (facet == HeartFacet.None) return false;
            int count = HeartFacetCount(bossKey);
            for (int i = 0; i < count; i++) if (HeartFacetAt(bossKey, i) == facet) return true;
            return false;
        }

        /// <summary>
        /// Биты (1 &lt;&lt; HeartFacet) всех граней сердец на надетых вещах — для снимка
        /// забега (CreateRunBoons). Одна грань на двух вещах не складывается: это маска.
        /// </summary>
        public uint WornHeartFacetMask()
        {
            uint mask = 0;
            for (int s = 0; s < (int)EquipSlot.Count; s++)
            {
                var crafting = Worn.Worn((EquipSlot)s).Crafting;
                int hearts = crafting?.HeartCount ?? 0;
                for (int h = 0; h < hearts; h++)
                {
                    crafting.HeartAt(h, out int bossKey, out var facet);
                    if (IsHeartFacetOf(bossKey, facet)) mask |= 1u << (int)facet;
                }
            }
            return mask;
        }

        // ---- открытая сессия ----

        ForgeSessionKind _sessionKind;
        ForgeTarget _sessionTarget;
        // Вещь до оплаты (от неё броски и кандидаты) и вещь сейчас (снимок + рост сессии).
        ItemInstance _sessionSnapshot, _sessionItem;
        int _sessionProperty, _sessionStrikes;
        RolledAffix[] _sessionCandidates = Array.Empty<RolledAffix>();

        /// <summary>Открытая сессия: место — там, где вещь лежит сейчас. Нет — default.</summary>
        public ForgeSession Session => LocateSession(out var at)
            ? new ForgeSession(_sessionKind, at, _sessionSnapshot, _sessionProperty, _sessionStrikes) : default;

        void OpenSession(ForgeSessionKind kind, ForgeTarget target, in ItemInstance snapshot, int property)
        {
            _sessionKind = kind; _sessionTarget = target; _sessionSnapshot = snapshot; _sessionItem = snapshot;
            _sessionProperty = property; _sessionStrikes = 0; _sessionCandidates = Array.Empty<RolledAffix>();
            if ((kind == ForgeSessionKind.Remelt || kind == ForgeSessionKind.Add) && ItemGenerator.Generate(snapshot, Items, _forgeRoll))
                _sessionCandidates = ForgeCandidates(snapshot, _forgeRoll, kind == ForgeSessionKind.Add ? -1 : property,
                    kind == ForgeSessionKind.Add ? ForgeOperation.Add : ForgeOperation.Remelt);
        }

        void CloseSession()
        {
            _sessionKind = ForgeSessionKind.None; _sessionTarget = default; _sessionSnapshot = _sessionItem = default;
            _sessionProperty = _sessionStrikes = 0; _sessionCandidates = Array.Empty<RolledAffix>();
        }

        /// <summary>
        /// Где сейчас вещь сессии, без изменений состояния. Вещь переложили или надели —
        /// ищется тот же рецепт по сумке и надетому: сессия следует за вещью.
        /// </summary>
        bool LocateSession(out ForgeTarget at)
        {
            at = _sessionTarget;
            if (_sessionKind == ForgeSessionKind.None) return false;
            if (TryForgeTarget(at, out var item) && item.SameRecipe(_sessionItem)) return true;
            for (int i = 0; i < Bag.Capacity; i++)
                if (!Bag.IsEmpty(i) && Bag.At(i).SameRecipe(_sessionItem)) { at = ForgeTarget.Bag(i); return true; }
            for (int s = 0; s < (int)EquipSlot.Count; s++)
            {
                var worn = Worn.Worn((EquipSlot)s);
                if (!worn.IsEmpty && worn.SameRecipe(_sessionItem)) { at = ForgeTarget.Worn((EquipSlot)s); return true; }
            }
            return false;
        }

        /// <summary>Переносит сессию за вещью; вещи нет (продана, разобрана) — сессия сброшена, оплата пропала.</summary>
        bool ResolveSession(out ItemInstance current)
        {
            current = default;
            if (!LocateSession(out var at)) { CloseSession(); return false; }
            _sessionTarget = at;
            return TryForgeTarget(at, out current);
        }

        /// <summary>Оплаченная переплавка или добавление ждёт выбора.</summary>
        bool PendingChoice() => (_sessionKind == ForgeSessionKind.Remelt || _sessionKind == ForgeSessionKind.Add) && ResolveSession(out _);

        bool ContinuesTemper(ForgeTarget target, int property)
            => _sessionKind == ForgeSessionKind.Temper && _sessionProperty == property && LocateSession(out var at) && at.Same(target);

        /// <summary>Вещь в этом месте принадлежит открытой сессии: продажа и разбор её не трогают.</summary>
        internal bool SessionHolds(ForgeTarget target) => LocateSession(out var at) && at.Same(target);

        /// <summary>Слот сумки с вещью открытой сессии или −1: автоматический разбор его пропускает.</summary>
        internal int SessionBagSlot() => LocateSession(out var at) && !at.IsWorn ? at.Slot : -1;

        void HashForgeSession(ref ulong hash)
        {
            // Место берётся текущее: хеш до и после круга сохранения совпадает.
            if (!LocateSession(out var at)) { Hashing.Mix(ref hash, 0); return; }
            Hashing.Mix(ref hash, (int)_sessionKind); Hashing.Mix(ref hash, at.IsWorn ? 1 : 0); Hashing.Mix(ref hash, at.Slot);
            Hashing.Mix(ref hash, _sessionProperty); Hashing.Mix(ref hash, _sessionStrikes);
            _sessionSnapshot.HashInto(ref hash);
        }

        // ---- сохранение v10, секция 10 ----

        /// <summary>
        /// Секция сессии: byte вид, bool надето, int32 слот, int32 свойство, byte ударов.
        /// Вещь не пишется: она уже лежит в сумке или на герое, а снимок до оплаты — это
        /// она же без шага закалки этой сессии. Пустая сессия — пустая секция (не пишется).
        /// </summary>
        internal void WriteForgeSession(BinaryWriter w)
        {
            if (!LocateSession(out var at)) return;
            w.Write((byte)_sessionKind); w.Write(at.IsWorn); w.Write(at.Slot); w.Write(_sessionProperty); w.Write((byte)Math.Min(_sessionStrikes, 255));
        }

        /// <summary>
        /// Читает секцию сессии (после сумки и надетого). Байты вне диапазона — порча файла;
        /// вещь не нашлась или не сходится с записью — сессия сброшена, профиль живёт дальше.
        /// </summary>
        internal void ReadForgeSession(BinaryReader r, int length)
        {
            CloseSession();
            if (length <= 0) return;
            int kind = r.ReadByte();
            if (kind > (int)ForgeSessionKind.Add) throw new InvalidDataException("Некорректная сессия кузнеца");
            if (kind == 0 || !CampSaveCodec.Has(r, 1 + 4 + 4 + 1)) return;
            bool worn = r.ReadBoolean(); int slot = r.ReadInt32(), property = r.ReadInt32(), strikes = r.ReadByte();
            if (property < -1 || property >= GeneratedItem.MaxAffixes || slot < 0 || slot >= (worn ? (int)EquipSlot.Count : Bag.Capacity))
                throw new InvalidDataException("Некорректная сессия кузнеца");
            var target = worn ? ForgeTarget.Worn((EquipSlot)slot) : ForgeTarget.Bag(slot);
            if (!TryForgeTarget(target, out var item)) return;
            var snapshot = item;
            if ((ForgeSessionKind)kind == ForgeSessionKind.Temper)
            {
                // Рост уже в вещи: последний шаг обязан быть закалкой этой сессии.
                var crafting = item.Crafting;
                if (strikes < 1 || crafting == null) return;
                var last = crafting.Step(crafting.Count - 1); var expected = TemperStep(property, strikes);
                if (last.Operation != expected.Operation || last.Slot != expected.Slot || last.AffixId != 0 || last.Fraction != expected.Fraction) return;
                snapshot = WithoutLastStep(item);
            }
            OpenSession((ForgeSessionKind)kind, target, snapshot, property);
            _sessionItem = item; _sessionStrikes = (ForgeSessionKind)kind == ForgeSessionKind.Temper ? strikes : 0;
            if ((ForgeSessionKind)kind == ForgeSessionKind.Temper) return;
            // Переплавка и добавление: кандидаты выводятся заново; по нынешним правилам их нет
            // или свойство не на месте — сессия сброшена (оплата пропала, вещь цела).
            bool valid = _sessionCandidates.Length > 0 && ItemGenerator.Generate(snapshot, Items, _forgeRoll)
                && ((ForgeSessionKind)kind == ForgeSessionKind.Add ? property == _forgeRoll.AffixCount : property >= 0 && property < _forgeRoll.AffixCount);
            if (!valid) CloseSession();
        }

        static ItemInstance WithoutLastStep(in ItemInstance item)
        {
            int count = item.Crafting.Count - 1;
            CraftingRecipe recipe = null;
            if (count > 0)
            {
                var steps = new CraftStep[count];
                for (int i = 0; i < count; i++) steps[i] = item.Crafting.Step(i);
                recipe = new CraftingRecipe(steps);
            }
            return new ItemInstance(item.BaseId, item.ItemLevel, item.Rarity, item.Seed, item.ForgeRecipe, recipe);
        }
    }
}
