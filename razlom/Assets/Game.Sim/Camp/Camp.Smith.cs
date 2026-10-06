using System;
using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>Ответ Эни. Не сохраняется, но окна сравнивают значения: только дописывать.</summary>
    public enum SmithResult
    {
        Success, InvalidItem, Protected, NoAffix, AtMaximum, Exhausted, InsufficientFunds, Locked, InvalidDonor, Incompatible, NoSpace, StalePreview,
        /// <summary>Три трещины: вещь больше не куётся, но носится и разбирается.</summary>
        Shattered,
        /// <summary>Нет сердца этого босса (или у босса пока нет граней).</summary>
        NoHeart,
        /// <summary>В вещи уже столько сердец, сколько позволяет ранг (1, на ранге 3 — 2).</summary>
        HeartsFull,
        /// <summary>Оплаченная переплавка или добавление ждёт выбора — другое действие нельзя.</summary>
        SessionOpen,
        NoSession,
        /// <summary>Шедевр: дальше только сердце.</summary>
        Masterpiece,
        /// <summary>Рискованный удар — только когда попытки закалки кончились.</summary>
        NotTempered,
    }
    public readonly struct ForgeTarget
    {
        public readonly bool IsWorn;
        public readonly int Slot;
        ForgeTarget(bool worn, int slot) { IsWorn = worn; Slot = slot; }
        public static ForgeTarget Bag(int slot) => new ForgeTarget(false, slot);
        public static ForgeTarget Worn(EquipSlot slot) => new ForgeTarget(true, (int)slot);
        public bool Same(ForgeTarget other) => IsWorn == other.IsWorn && Slot == other.Slot;
    }

    /// <summary>Действия Эни (06.10). Порядок — порядок кнопок окна: только дописывать.</summary>
    public enum EniAction : byte { Temper, Remelt, Add, Heart }

    /// <summary>Открытая сессия кузнеца. Лежит в сохранении (секция 10): только дописывать.</summary>
    public enum ForgeSessionKind : byte { None = 0, Temper = 1, Remelt = 2, Add = 3 }

    public enum StrikeOutcome : byte { None, Grew, Cracked, Shattered }

    /// <summary>Снимок открытой сессии для окна: что куётся и сколько ударов уже было.</summary>
    public readonly struct ForgeSession
    {
        public readonly ForgeSessionKind Kind;
        public readonly ForgeTarget Target;
        /// <summary>Вещь до оплаты сессии: от неё считаются броски и кандидаты.</summary>
        public readonly ItemInstance Snapshot;
        /// <summary>Свойство: −1 — базовое свойство основы; у добавления — индекс нового свойства.</summary>
        public readonly int Property;
        /// <summary>Удачных ударов закалки в этой сессии.</summary>
        public readonly int Strikes;
        public bool IsOpen => Kind != ForgeSessionKind.None;
        internal ForgeSession(ForgeSessionKind kind, ForgeTarget target, ItemInstance snapshot, int property, int strikes)
        { Kind = kind; Target = target; Snapshot = snapshot; Property = property; Strikes = strikes; }
    }

    /// <summary>
    /// Что будет стоить действие и чем оно рискует — до клика. Исход броска сюда не
    /// попадает никогда: только шанс, рост и число ударов (защита от подглядывания).
    /// </summary>
    public readonly struct EniQuote
    {
        public readonly SmithResult Status;
        public readonly int Gold, Shards, Steel, Hearts;
        /// <summary>Закалка — шанс трещины этого удара; рискованный удар — шанс рассыпаться; перелив — шанс трещины.</summary>
        public readonly int RiskPercent;
        /// <summary>Рост этого удара закалки в долях диапазона свойства.</summary>
        public readonly Fix64 NextGrowth;
        public readonly int AttemptsLeft, Cracks, Strikes;
        /// <summary>Попытки кончились: закалка становится рискованным ударом (шедевр или осколки).</summary>
        public readonly bool Risky;
        /// <summary>Добавление сверх лимита редкости: свойство или трещина.</summary>
        public readonly bool Overflow;
        public readonly bool Affordable;
        public bool Allowed => Status == SmithResult.Success && Affordable;
        internal EniQuote(SmithResult status, int gold, int shards, int steel, int hearts, int risk, Fix64 growth,
            int left, int cracks, int strikes, bool risky, bool overflow, bool affordable)
        {
            Status = status; Gold = gold; Shards = shards; Steel = steel; Hearts = hearts; RiskPercent = risk; NextGrowth = growth;
            AttemptsLeft = left; Cracks = cracks; Strikes = strikes; Risky = risky; Overflow = overflow; Affordable = affordable;
        }
    }

    /// <summary>
    /// Эни без мини-игр (решение 06.10): закалка «Ещё удар?», переплавка, добавление,
    /// сердце босса, рискованный удар и разбор. Правило «перековка не ухудшает» отменено:
    /// промах — трещина. Все броски детерминированы от вещи до оплаты и номера действия,
    /// поэтому перезагрузка не меняет исход. Сессия и её хранение — в Camp.Temper.
    /// </summary>
    public sealed partial class Camp
    {
        // Цены 06.10 (суровая экономика: одно действие ≈ удачный лес). Золото и осколки
        // дорожают ×1,5 за каждое оплаченное действие над вещью, сталь и сердце — нет.
        public const int TemperGold = 80, TemperShards = 5, RemeltGold = 120, RemeltSteel = 1, AddGold = 150, AddSteel = 2, HeartGold = 100;

        // Буфер разворачивания для проверок Эни: окна спрашивают цену на каждой перерисовке.
        readonly GeneratedItem _forgeRoll = new GeneratedItem();

        /// <summary>
        /// Действия по рангу лагеря (06.10): закалка и разбор — с начала, переплавка и
        /// сердце — ранг 1, добавление — ранг 2; второе сердце в вещь — ранг 3 (HeartSlots).
        /// </summary>
        public bool EniActionUnlocked(EniAction action)
        {
            if (!HasResident(CampResident.Smith)) return false;
            switch (action)
            {
                case EniAction.Temper: return true;
                case EniAction.Remelt: case EniAction.Heart: return Rank(CampResident.Smith) >= 1;
                case EniAction.Add: return Rank(CampResident.Smith) >= 2;
                default: return false;
            }
        }

        bool TryForgeTarget(ForgeTarget target, out ItemInstance item)
        {
            item = default;
            if (target.IsWorn) { if ((uint)target.Slot >= (uint)EquipSlot.Count) return false; item = Worn.Worn((EquipSlot)target.Slot); }
            else { if ((uint)target.Slot >= Bag.Capacity) return false; item = Bag.At(target.Slot); }
            return !item.IsEmpty;
        }

        /// <summary>Эни берёт любую известную вещь, кроме артефактов; редкость ограничивает действия, а не допуск.</summary>
        bool Forgeable(in ItemInstance item)
        {
            int index = Items.IndexOfBase(item.BaseId);
            return !item.IsEmpty && index >= 0 && Items.GetBase(index).Category != ItemCategory.Artifact;
        }

        EniQuote Refuse(SmithResult status, int left = 0, int cracks = 0)
            => new EniQuote(status, 0, 0, 0, 0, 0, Fix64.Zero, left, cracks, 0, false, false, false);

        EniQuote Priced(SmithResult status, int gold, int shards, int steel, int hearts, int bossKey, int risk, Fix64 growth,
            int left, int cracks, bool risky = false, bool overflow = false)
        {
            bool affordable = Money(CurrencyType.Gold) >= gold && Money(CurrencyType.Shards) >= shards
                && Money(CurrencyType.Steel) >= steel && HeartCount(bossKey) >= hearts;
            return new EniQuote(status, gold, shards, steel, hearts, risk, growth, left, cracks, 0, risky, overflow, affordable);
        }

        /// <summary>
        /// Цена, допуск и риск действия до клика. property — свойство (−1 — базовое
        /// свойство обычной вещи), bossKey и facet — для сердца. Без бросков и без трат.
        /// </summary>
        public EniQuote Quote(EniAction action, ForgeTarget target, int property = 0, int bossKey = 0, HeartFacet facet = HeartFacet.None)
        {
            if (!EniActionUnlocked(action)) return Refuse(SmithResult.Locked);
            if (!TryForgeTarget(target, out var item) || !Forgeable(item)) return Refuse(SmithResult.InvalidItem);
            var crafting = item.Crafting;
            int left = AttemptsLeft(item), cracks = CrackCount(item), paid = crafting?.PaidActions ?? 0;
            if (cracks >= CracksToShatter) return Refuse(SmithResult.Shattered, left, cracks);
            if ((crafting?.Count ?? 0) >= CraftingRecipe.MaximumSteps) return Refuse(SmithResult.Exhausted, left, cracks);
            bool pending = PendingChoice();
            switch (action)
            {
                case EniAction.Temper:
                {
                    if (ContinuesTemper(target, property))
                    {
                        // Сессия уже оплачена: следующий удар бесплатный, растёт только риск.
                        int strike = _sessionStrikes;
                        TemperRoom(_sessionSnapshot, property, out var sessionRoom);
                        var status = SessionGrowth(strike) >= sessionRoom ? SmithResult.AtMaximum : SmithResult.Success;
                        return new EniQuote(status, 0, 0, 0, 0, CrackPercent(strike), TemperGrowth(strike), left, cracks, strike, false, false, true);
                    }
                    if (crafting?.IsMasterpiece == true) return Refuse(SmithResult.Masterpiece, left, cracks);
                    if (pending) return Refuse(SmithResult.SessionOpen, left, cracks);
                    if (left == 0) return QuoteRisky(item, left, cracks);
                    if (!TemperRoom(item, property, out var room)) return Refuse(SmithResult.NoAffix, left, cracks);
                    return Priced(room <= Fix64.Zero ? SmithResult.AtMaximum : SmithResult.Success, Escalate(TemperGold, paid), Escalate(TemperShards, paid),
                        0, 0, 0, CrackPercent(0), TemperGrowth(0), left, cracks);
                }
                case EniAction.Remelt:
                case EniAction.Add:
                {
                    bool add = action == EniAction.Add;
                    // Уникальная и обычная вещь не переплавляются и не дополняются (пробел №20).
                    if (item.Rarity != ItemRarity.Magic && item.Rarity != ItemRarity.Rare) return Refuse(SmithResult.InvalidItem, left, cracks);
                    if (crafting?.IsMasterpiece == true) return Refuse(SmithResult.Masterpiece, left, cracks);
                    if (pending) return Refuse(SmithResult.SessionOpen, left, cracks);
                    if (left == 0) return Refuse(SmithResult.Exhausted, left, cracks);
                    if (!ItemGenerator.Generate(item, Items, _forgeRoll)) return Refuse(SmithResult.InvalidItem, left, cracks);
                    bool overflow = false;
                    if (add)
                    {
                        int cap = ItemGenerator.AddCap(item.Rarity);
                        // Лимит редкости — безопасно, +1 сверх — перелив с трещиной, дальше нельзя (пробел №16).
                        if (_forgeRoll.AffixCount > cap) return Refuse(SmithResult.NoSpace, left, cracks);
                        overflow = _forgeRoll.AffixCount == cap;
                    }
                    else if ((uint)property >= (uint)_forgeRoll.AffixCount) return Refuse(SmithResult.NoAffix, left, cracks);
                    if (ForgeCandidates(item, _forgeRoll, add ? -1 : property, add ? ForgeOperation.Add : ForgeOperation.Remelt).Length == 0)
                        return Refuse(SmithResult.Incompatible, left, cracks);
                    return Priced(SmithResult.Success, Escalate(add ? AddGold : RemeltGold, paid), 0, add ? AddSteel : RemeltSteel, 0, 0,
                        overflow ? AddOverflowCrackPercent : 0, Fix64.Zero, left, cracks, overflow: overflow);
                }
                case EniAction.Heart:
                {
                    if (pending) return Refuse(SmithResult.SessionOpen, left, cracks);
                    if ((crafting?.HeartCount ?? 0) >= HeartSlots) return Refuse(SmithResult.HeartsFull, left, cracks);
                    if (HeartFacetCount(bossKey) == 0) return Refuse(SmithResult.NoHeart, left, cracks);
                    // Второе сердце — другая пара (босс, грань) (пробел №19).
                    if (facet != HeartFacet.None && (!IsHeartFacetOf(bossKey, facet) || HasHeart(crafting, bossKey, facet)))
                        return Refuse(SmithResult.Incompatible, left, cracks);
                    return Priced(SmithResult.Success, Escalate(HeartGold, paid), 0, 0, 1, bossKey, 0, Fix64.Zero, left, cracks);
                }
                default: return Refuse(SmithResult.Locked);
            }
        }

        EniQuote QuoteRisky(in ItemInstance item, int left, int cracks)
        {
            int paid = item.Crafting?.PaidActions ?? 0;
            // Цена — как у закалки (пробел №15); риск — шанс, что вещь рассыпется.
            return Priced(SmithResult.Success, Escalate(TemperGold, paid), Escalate(TemperShards, paid), 0, 0, 0,
                100 - RiskySuccessPercent, Fix64.Zero, left, cracks, risky: true);
        }

        static bool HasHeart(CraftingRecipe crafting, int bossKey, HeartFacet facet)
        {
            int count = crafting?.HeartCount ?? 0;
            for (int i = 0; i < count; i++) { crafting.HeartAt(i, out int key, out var f); if (key == bossKey && f == facet) return true; }
            return false;
        }

        void Pay(in EniQuote quote, int bossKey)
        {
            // Всё проверено Quote до первого изменения: списание не бывает частичным.
            Spend(CurrencyType.Gold, quote.Gold); Spend(CurrencyType.Shards, quote.Shards); Spend(CurrencyType.Steel, quote.Steel);
            for (int i = 0; i < quote.Hearts; i++) SpendHeart(bossKey);
        }

        static ItemInstance WithStep(in ItemInstance item, CraftStep step)
            => new ItemInstance(item.BaseId, item.ItemLevel, item.Rarity, item.Seed, item.ForgeRecipe,
                (item.Crafting ?? new CraftingRecipe(Array.Empty<CraftStep>())).Append(step));

        static CraftStep CrackStep => new CraftStep(ForgeOperation.Crack, CraftingRecipe.BaseSlot, 0, Fix64.Zero);

        void Place(ForgeTarget target, in ItemInstance item)
        {
            // Надетая вещь закаляется на месте: Equip пересобирает статы героя.
            if (target.IsWorn) Worn.Equip(item, out _);
            else Bag.Put(target.Slot, item, Bag.IsKept(target.Slot));
        }

        void RemoveAt(ForgeTarget target)
        {
            if (target.IsWorn) Worn.Unequip((EquipSlot)target.Slot);
            else Bag.Remove(target.Slot);
        }

        // ---- закалка ----

        /// <summary>
        /// Удар закалки. Без сессии — проверки, оплата и первый удар (без риска); в сессии
        /// — следующий удар того же свойства, бесплатно, с растущим шансом трещины. Рост
        /// пишется в вещь сразу: вход в Разлом уносит набранное без отдельного «забрать».
        /// </summary>
        public SmithResult Strike(ForgeTarget target, int property, out StrikeOutcome outcome)
        {
            outcome = StrikeOutcome.None;
            // Другая вещь или другое свойство: прежняя закалка закрывается с тем, что набрано.
            if (_sessionKind == ForgeSessionKind.Temper && !ContinuesTemper(target, property)) SettleForgeSession();
            if (_sessionKind == ForgeSessionKind.None)
            {
                var quote = Quote(EniAction.Temper, target, property);
                if (quote.Status != SmithResult.Success) return quote.Status;
                // Попытки кончились: рискованный удар — отдельное решение игрока (RiskyStrike).
                if (quote.Risky) return SmithResult.Exhausted;
                if (!quote.Affordable) return SmithResult.InsufficientFunds;
                TryForgeTarget(target, out var item);
                Pay(quote, 0);
                OpenSession(ForgeSessionKind.Temper, target, item, property);
            }
            else if (_sessionKind != ForgeSessionKind.Temper) return SmithResult.SessionOpen;
            else if (!ResolveSession(out _)) return SmithResult.NoSession;

            int strike = _sessionStrikes;
            if (!TemperRoom(_sessionSnapshot, _sessionProperty, out var room)) { CloseSession(); return SmithResult.InvalidItem; }
            // Свойство уже на пределе с учётом роста сессии: броска нет, окно предлагает забрать.
            if (SessionGrowth(strike) >= room) return SmithResult.AtMaximum;
            if (Roll(_sessionSnapshot, strike, TemperStream) < CrackPercent(strike))
            {
                // Трещина сжигает рост всей сессии (пробел №12) и съедает попытку.
                var cracked = WithStep(_sessionSnapshot, CrackStep);
                Place(_sessionTarget, cracked); CloseSession();
                outcome = IsShattered(cracked) ? StrikeOutcome.Shattered : StrikeOutcome.Cracked;
                return SmithResult.Success;
            }
            _sessionStrikes = strike + 1;
            _sessionItem = TemperedBy(_sessionSnapshot, _sessionProperty, _sessionStrikes);
            Place(_sessionTarget, _sessionItem);
            outcome = StrikeOutcome.Grew;
            return SmithResult.Success;
        }

        /// <summary>«Забрать»: рост уже в вещи, сессия просто закрывается.</summary>
        public SmithResult TakeTemper()
        {
            if (_sessionKind != ForgeSessionKind.Temper || !ResolveSession(out _)) return SmithResult.NoSession;
            CloseSession();
            return SmithResult.Success;
        }

        /// <summary>
        /// Закрывает закалку с тем, что набрано (окно закрыто, другое действие, конец забега).
        /// Оплаченные переплавка и добавление остаются: выбор обязателен и ждёт (пробел №17).
        /// </summary>
        public void SettleForgeSession()
        {
            if (_sessionKind == ForgeSessionKind.Temper) CloseSession();
        }

        /// <summary>Вещь, если следующий удар закалки будет удачным. Без бросков — для сравнения в окне.</summary>
        public ItemInstance PreviewTemperStrike(ForgeTarget target, int property)
        {
            if (ContinuesTemper(target, property)) return TemperedBy(_sessionSnapshot, property, _sessionStrikes + 1);
            return TryForgeTarget(target, out var item) ? TemperedBy(item, property, 1) : default;
        }

        static ItemInstance TemperedBy(in ItemInstance snapshot, int property, int strikes)
            => WithStep(snapshot, TemperStep(property, strikes));

        static CraftStep TemperStep(int property, int strikes)
            => new CraftStep(ForgeOperation.Temper, property < 0 ? CraftingRecipe.BaseSlot : (byte)property, 0, SessionGrowth(strikes));

        /// <summary>
        /// Сколько ещё может вырасти свойство, в долях диапазона: аффикс — до максимума,
        /// базовое свойство обычной вещи — до +100% основы за всю жизнь вещи. false — свойства нет.
        /// </summary>
        bool TemperRoom(in ItemInstance item, int property, out Fix64 room)
        {
            room = Fix64.Zero;
            if (!ItemGenerator.Generate(item, Items, _forgeRoll)) return false;
            if (item.Rarity == ItemRarity.Normal)
            {
                if (property != -1 || !_forgeRoll.HasImplicit) return false;
                Fix64 grown = Fix64.Zero; var crafting = item.Crafting;
                for (int i = 0; i < (crafting?.Count ?? 0); i++)
                {
                    var step = crafting.Step(i);
                    if (step.Operation == ForgeOperation.Temper && step.Slot == CraftingRecipe.BaseSlot) grown += step.Fraction;
                }
                room = Fix64.Max(Fix64.Zero, CraftingRecipe.ImplicitTemperCap - grown);
                return true;
            }
            if ((uint)property >= (uint)_forgeRoll.AffixCount) return false;
            var current = _forgeRoll.GetAffix(property); int index = Items.IndexOfAffix(current.AffixId);
            if (index < 0) return false;
            var definition = Items.GetAffix(index); Fix64 range = definition.MaxValue - definition.MinValue;
            room = range <= Fix64.Zero ? Fix64.Zero : Fix64.Max(Fix64.Zero, (definition.MaxValue - current.Value) / range);
            return true;
        }

        /// <summary>
        /// Рискованный удар (только когда попытки кончились): 50% — шедевр, иначе вещь
        /// рассыпается в осколки (с героя — снимается). Цена — как у закалки.
        /// </summary>
        public SmithResult RiskyStrike(ForgeTarget target, out bool masterpiece, out int shards)
        {
            masterpiece = false; shards = 0;
            if (_sessionKind == ForgeSessionKind.Temper) SettleForgeSession();
            if (!EniActionUnlocked(EniAction.Temper)) return SmithResult.Locked;
            if (!TryForgeTarget(target, out var item) || !Forgeable(item)) return SmithResult.InvalidItem;
            if (AttemptsLeft(item) > 0) return SmithResult.NotTempered;
            var quote = Quote(EniAction.Temper, target, -1);
            if (quote.Status != SmithResult.Success) return quote.Status;
            if (!quote.Risky) return SmithResult.NotTempered;
            if (!quote.Affordable) return SmithResult.InsufficientFunds;
            Pay(quote, 0);
            if (Roll(item, item.Crafting?.PaidActions ?? 0, RiskyStream) < RiskySuccessPercent)
            {
                Place(target, WithStep(item, new CraftStep(ForgeOperation.Masterpiece, CraftingRecipe.BaseSlot, 0, Fix64.Zero)));
                masterpiece = true;
                return SmithResult.Success;
            }
            // Неудача: осколки с бонусом за прежнюю закалку; сам удар бонуса не даёт — он не удался.
            shards = SalvageShards(item); RemoveAt(target); Earn(CurrencyType.Shards, shards);
            return SmithResult.Success;
        }

        // ---- переплавка и добавление ----

        /// <summary>Оплата переплавки: три кандидата без старого свойства, выбор обязателен и ждёт (пробел №17).</summary>
        public SmithResult BeginRemelt(ForgeTarget target, int property) => BeginChoice(EniAction.Remelt, target, property, out _);

        /// <summary>
        /// Оплата добавления. В пределах лимита редкости — сессия выбора; перелив (+1 сверх
        /// лимита) сначала бросает трещину 50%: cracked — оплата и попытка сгорели.
        /// </summary>
        public SmithResult BeginAdd(ForgeTarget target, out bool cracked) => BeginChoice(EniAction.Add, target, 0, out cracked);

        SmithResult BeginChoice(EniAction action, ForgeTarget target, int property, out bool cracked)
        {
            cracked = false;
            if (_sessionKind == ForgeSessionKind.Temper) SettleForgeSession();
            var quote = Quote(action, target, property);
            if (quote.Status != SmithResult.Success) return quote.Status;
            if (!quote.Affordable) return SmithResult.InsufficientFunds;
            TryForgeTarget(target, out var item);
            Pay(quote, 0);
            if (quote.Overflow && Roll(item, item.Crafting?.PaidActions ?? 0, OverflowStream) < AddOverflowCrackPercent)
            {
                Place(target, WithStep(item, CrackStep));
                cracked = true;
                return SmithResult.Success;
            }
            ItemGenerator.Generate(item, Items, _forgeRoll);
            bool add = action == EniAction.Add;
            OpenSession(add ? ForgeSessionKind.Add : ForgeSessionKind.Remelt, target, item, add ? _forgeRoll.AffixCount : property);
            return SmithResult.Success;
        }

        public int SessionCandidateCount => PendingChoice() ? _sessionCandidates.Length : 0;

        public RolledAffix SessionCandidate(int index) => (uint)index < (uint)SessionCandidateCount ? _sessionCandidates[index] : default;

        /// <summary>Вещь, если выбрать кандидата index. Без изменений — для сравнения в окне.</summary>
        public ItemInstance SessionCandidateItem(int index)
        {
            if ((uint)index >= (uint)SessionCandidateCount) return default;
            var candidate = _sessionCandidates[index]; var definition = Items.GetAffix(Items.IndexOfAffix(candidate.AffixId));
            var operation = _sessionKind == ForgeSessionKind.Add ? ForgeOperation.Add : ForgeOperation.Remelt;
            return WithStep(_sessionSnapshot, new CraftStep(operation, (byte)_sessionProperty, candidate.AffixId, NormalizeAffix(candidate.Value, definition)));
        }

        /// <summary>Выбор оплаченного кандидата: вещь меняется, сессия закрывается.</summary>
        public SmithResult ChooseSessionCandidate(int index)
        {
            if (!PendingChoice()) return SmithResult.NoSession;
            if ((uint)index >= (uint)_sessionCandidates.Length) return SmithResult.NoAffix;
            var after = SessionCandidateItem(index);
            if (!ItemGenerator.Generate(after, Items, _forgeRoll)) return SmithResult.Incompatible;
            Place(_sessionTarget, after);
            CloseSession();
            return SmithResult.Success;
        }

        // ---- сердце ----

        /// <summary>Вплавить сердце босса гранью facet: без риска и без траты попытки (пробел №13).</summary>
        public SmithResult InlayHeart(ForgeTarget target, int bossKey, HeartFacet facet)
        {
            if (_sessionKind == ForgeSessionKind.Temper) SettleForgeSession();
            var quote = Quote(EniAction.Heart, target, 0, bossKey, facet);
            if (quote.Status != SmithResult.Success) return quote.Status;
            if (facet == HeartFacet.None) return SmithResult.Incompatible;
            if (HeartCount(bossKey) < 1) return SmithResult.NoHeart;
            if (!quote.Affordable) return SmithResult.InsufficientFunds;
            TryForgeTarget(target, out var item);
            Pay(quote, bossKey);
            Place(target, WithStep(item, new CraftStep(ForgeOperation.Heart, (byte)facet, bossKey, Fix64.Zero)));
            return SmithResult.Success;
        }

        // ---- разбор ----

        /// <summary>
        /// Осколки за разбор: основа по редкости и уровню, +5 за каждое оплаченное действие
        /// без трещины (Inventory.ShardsFor), «Знаток рун» ×1,25 — вниз.
        /// </summary>
        public int SalvageShards(in ItemInstance item) => (int)((long)Inventory.ShardsFor(item) * SalvagePercent / 100);

        /// <summary>
        /// Процент осколков разбора. «Знаток рун» — включённая клятва (снимок CreateRunBoons
        /// доски T2): пока доски нет, снимок пуст и процент 100.
        /// </summary>
        internal int SalvagePercent => CreateRunBoons().Rank(OathId.RuneSage) > 0 ? 125 : 100;

        public SmithResult Dismantle(int slot, out int shards)
        {
            shards = 0;
            if (!Has(CampService.Smith) || (uint)slot >= Bag.Capacity || Bag.IsEmpty(slot)) return SmithResult.InvalidItem;
            if (Bag.IsKept(slot)) return SmithResult.Protected;
            // Вещь открытой сессии ждёт выбора или удара: разбор не съест оплаченное.
            if (SessionHolds(ForgeTarget.Bag(slot))) return SmithResult.SessionOpen;
            shards = SalvageShards(Bag.At(slot)); Bag.Remove(slot); Earn(CurrencyType.Shards, shards); return SmithResult.Success;
        }

        // ---- кандидаты ----

        static Fix64 NormalizeAffix(Fix64 value, AffixDefinition definition) => definition.MaxValue <= definition.MinValue ? Fix64.Zero
            : Fix64.Clamp((value - definition.MinValue) / (definition.MaxValue - definition.MinValue), Fix64.Zero, Fix64.One);
        bool CanPlaceAffix(AffixDefinition definition, ItemInstance item, GeneratedItem rolled, int replacedSlot)
        {
            if (definition.Weight <= 0 || definition.MinItemLevel > item.ItemLevel || !definition.AllowedOn(rolled.Category)) return false;
            for (int i = 0; i < rolled.AffixCount; i++)
            {
                // Переплавка: старое свойство не возвращается ни тем же, ни другим тиром своей группы.
                int index = Items.IndexOfAffix(rolled.GetAffix(i).AffixId);
                if (index >= 0 && Items.GetAffix(index).Group == definition.Group) return false;
            }
            return true;
        }
        /// <summary>
        /// Три кандидата разных групп. Детерминированы от вещи до оплаты: после закрытия
        /// окна и перезагрузки те же — они не хранятся, а выводятся заново.
        /// </summary>
        RolledAffix[] ForgeCandidates(ItemInstance item, GeneratedItem rolled, int replacedSlot, ForgeOperation operation)
        {
            var eligible = new List<int>();
            for (int i = 0; i < Items.AffixCount; i++)
                if (CanPlaceAffix(Items.GetAffix(i), item, rolled, replacedSlot)) eligible.Add(i);
            ulong seed = Hashing.Offset; item.HashInto(ref seed); Hashing.Mix(ref seed, (int)operation); Hashing.Mix(ref seed, replacedSlot);
            var rng = new Pcg32(seed, 0x43414E4449444154UL); var candidates = new List<RolledAffix>(3);
            while (eligible.Count > 0 && candidates.Count < 3)
            {
                int pick = rng.NextInt(0, eligible.Count); var definition = Items.GetAffix(eligible[pick]);
                Fix64 fraction = rng.NextFix(Fix64.Zero, Fix64.One);
                candidates.Add(new RolledAffix(definition.Id, definition.Stat, definition.Op, definition.MinValue + (definition.MaxValue - definition.MinValue) * fraction));
                // Три варианта относятся к разным группам, а не к трём тирами одной прибавки.
                for (int i = eligible.Count - 1; i >= 0; i--) if (Items.GetAffix(eligible[i]).Group == definition.Group) eligible.RemoveAt(i);
            }
            return candidates.ToArray();
        }
    }
}
