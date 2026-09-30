using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public enum SmithResult { Success, InvalidItem, Protected, NoAffix, AtMaximum, Exhausted, InsufficientFunds, Locked, InvalidDonor, Incompatible, NoSpace, StalePreview }
    public readonly struct ForgeTarget
    {
        public readonly bool IsWorn;
        public readonly int Slot;
        ForgeTarget(bool worn, int slot) { IsWorn = worn; Slot = slot; }
        public static ForgeTarget Bag(int slot) => new ForgeTarget(false, slot);
        public static ForgeTarget Worn(EquipSlot slot) => new ForgeTarget(true, (int)slot);
    }
    public sealed class ForgePreview
    {
        internal readonly Camp Owner;
        readonly RolledAffix[] _candidates;
        public SmithResult Status { get; }
        public ForgeTarget Target { get; }
        public ForgeOperation Operation { get; }
        public ItemInstance Before { get; }
        public ItemInstance After { get; }
        public ItemInstance DonorItem { get; }
        public int AffixIndex { get; }
        public int Choice { get; }
        public int DonorSlot { get; }
        public int DonorAffix { get; }
        public int Gold { get; }
        public int Shards { get; }
        public int Steel { get; }
        public int Cores { get; }
        public int CandidateCount => _candidates?.Length ?? 0;
        public RolledAffix Candidate(int index) => (uint)index < CandidateCount ? _candidates[index] : default;
        internal ForgePreview(Camp owner, SmithResult status, ForgeTarget target, ForgeOperation operation, ItemInstance before,
            ItemInstance after, int affix, int choice, int donorSlot, int donorAffix, ItemInstance donor,
            int gold, int shards, int steel, int cores, RolledAffix[] candidates)
        {
            Owner = owner; Status = status; Target = target; Operation = operation; Before = before; After = after;
            AffixIndex = affix; Choice = choice; DonorSlot = donorSlot; DonorAffix = donorAffix; DonorItem = donor;
            Gold = gold; Shards = shards; Steel = steel; Cores = cores; _candidates = candidates;
        }
    }
    public sealed partial class Camp
    {
        public static int ReforgeGold(in ItemInstance item) => 30 * (item.ReforgeCount + 1);
        public static int ReforgeShards(in ItemInstance item) => 3 * (item.ReforgeCount + 1);
        public bool ForgeOperationUnlocked(ForgeOperation operation) => HasResident(CampResident.Smith)
            && (uint)operation <= (uint)ForgeOperation.Transfer && (operation == ForgeOperation.Refine || Rank(CampResident.Smith) >= (int)operation);
        bool TryForgeTarget(ForgeTarget target, out ItemInstance item)
        {
            item = default;
            if (target.IsWorn) { if ((uint)target.Slot >= (uint)EquipSlot.Count) return false; item = Worn.Worn((EquipSlot)target.Slot); }
            else { if ((uint)target.Slot >= Bag.Capacity) return false; item = Bag.At(target.Slot); }
            return !item.IsEmpty;
        }
        bool Forgeable(in ItemInstance item)
        {
            int index = Items.IndexOfBase(item.BaseId);
            return !item.IsEmpty && index >= 0 && item.Rarity < ItemRarity.Unique && Items.GetBase(index).Category != ItemCategory.Artifact;
        }
        public SmithResult ReforgeRange(int slot, int affix, out Fix64 lower, out Fix64 upper)
            => ReforgeRange(ForgeTarget.Bag(slot), affix, out lower, out upper);
        public SmithResult ReforgeRange(EquipSlot slot, int affix, out Fix64 lower, out Fix64 upper)
            => ReforgeRange(ForgeTarget.Worn(slot), affix, out lower, out upper);
        SmithResult ReforgeRange(ForgeTarget target, int affix, out Fix64 lower, out Fix64 upper)
        {
            lower = upper = Fix64.Zero;
            if (!Has(CampService.Smith) || !TryForgeTarget(target, out var item) || !Forgeable(item) || item.ItemLevel > short.MaxValue - 4) return SmithResult.InvalidItem;
            if (item.ReforgeCount >= 3) return SmithResult.Exhausted;
            var rolled = new GeneratedItem(); if (!ItemGenerator.Generate(item, Items, rolled)) return SmithResult.InvalidItem;
            if ((uint)affix >= rolled.AffixCount) return SmithResult.NoAffix;
            var current = rolled.GetAffix(affix); int index = Items.IndexOfAffix(current.AffixId);
            upper = index < 0 ? current.Value : Fix64.Max(current.Value, Items.GetAffix(index).MaxValue);
            lower = current.Value + (upper - current.Value) * Fix64.Ratio(1, 4);
            return upper <= current.Value ? SmithResult.AtMaximum : SmithResult.Success;
        }
        public ForgePreview PreviewForge(ForgeTarget target, ForgeOperation operation, int affix = 0, int choice = 0, int donorSlot = -1, int donorAffix = 0)
        {
            ItemInstance before = default, after = default, donor = default;
            RolledAffix[] candidates = null;
            int gold = 0, shards = 0, steel = 0, cores = 0;
            SmithResult status = SmithResult.Success;
            if (!ForgeOperationUnlocked(operation)) status = SmithResult.Locked;
            else if (!TryForgeTarget(target, out before) || !Forgeable(before)) status = SmithResult.InvalidItem;
            else if ((before.Crafting?.Count ?? 0) >= CraftingRecipe.MaximumSteps) status = SmithResult.Exhausted;
            else
            {
                var rolled = new GeneratedItem();
                if (!ItemGenerator.Generate(before, Items, rolled)) status = SmithResult.InvalidItem;
                else
                {
                    CraftStep step = default;
                    if (operation == ForgeOperation.Refine)
                    {
                        gold = ReforgeGold(before); shards = ReforgeShards(before);
                        status = ReforgeRange(target, affix, out _, out _);
                        if (status == SmithResult.Success)
                        {
                            var rng = new Pcg32(before.Seed, 0x534D495448UL); Fix64 fraction = Fix64.Zero;
                            for (int i = 0; i <= before.ReforgeCount; i++) fraction = rng.NextFix(Fix64.Ratio(1, 4), Fix64.One);
                            step = new CraftStep(operation, (byte)affix, 0, fraction);
                        }
                    }
                    else if (operation == ForgeOperation.Replace || operation == ForgeOperation.Add)
                    {
                        bool add = operation == ForgeOperation.Add;
                        gold = add ? 120 : 60; shards = add ? 15 : 8; steel = add ? 2 : 1;
                        int cap = before.Rarity == ItemRarity.Magic ? 2 : before.Rarity == ItemRarity.Rare ? 4 : 0;
                        if (add && rolled.AffixCount >= cap) status = SmithResult.NoSpace;
                        else if (!add && (uint)affix >= rolled.AffixCount) status = SmithResult.NoAffix;
                        else
                        {
                            if (add) affix = rolled.AffixCount;
                            candidates = ForgeCandidates(before, rolled, add ? -1 : affix, operation);
                            if (candidates.Length == 0) status = SmithResult.Incompatible;
                            else if ((uint)choice >= candidates.Length) status = SmithResult.NoAffix;
                            else
                            {
                                var selected = candidates[choice]; var definition = Items.GetAffix(Items.IndexOfAffix(selected.AffixId));
                                step = new CraftStep(operation, (byte)affix, selected.AffixId, NormalizeAffix(selected.Value, definition));
                            }
                        }
                    }
                    else
                    {
                        gold = 180; shards = 20; cores = 1;
                        if ((uint)affix >= rolled.AffixCount) status = SmithResult.NoAffix;
                        else if ((uint)donorSlot >= Bag.Capacity || !target.IsWorn && donorSlot == target.Slot || !Forgeable(donor = Bag.At(donorSlot))) status = SmithResult.InvalidDonor;
                        else if (Bag.IsKept(donorSlot)) status = SmithResult.Protected;
                        else
                        {
                            var donorRolled = new GeneratedItem();
                            if (!ItemGenerator.Generate(donor, Items, donorRolled) || (uint)donorAffix >= donorRolled.AffixCount) status = SmithResult.InvalidDonor;
                            else
                            {
                                var selected = donorRolled.GetAffix(donorAffix); int index = Items.IndexOfAffix(selected.AffixId);
                                if (index < 0 || !CanPlaceAffix(Items.GetAffix(index), before, rolled, affix)) status = SmithResult.Incompatible;
                                else step = new CraftStep(operation, (byte)affix, selected.AffixId, NormalizeAffix(selected.Value, Items.GetAffix(index)));
                            }
                        }
                    }
                    if (status == SmithResult.Success)
                    {
                        var recipe = (before.Crafting ?? new CraftingRecipe(Array.Empty<CraftStep>())).Append(step);
                        short level = (short)(before.ItemLevel + (operation == ForgeOperation.Refine ? 1 + (int)before.Rarity : 0));
                        after = new ItemInstance(before.BaseId, level, before.Rarity, before.Seed, before.ForgeRecipe, recipe);
                    }
                }
            }
            return new ForgePreview(this, status, target, operation, before, after, affix, choice, donorSlot, donorAffix, donor, gold, shards, steel, cores, candidates);
        }
        static Fix64 NormalizeAffix(Fix64 value, AffixDefinition definition) => definition.MaxValue <= definition.MinValue ? Fix64.Zero
            : Fix64.Clamp((value - definition.MinValue) / (definition.MaxValue - definition.MinValue), Fix64.Zero, Fix64.One);
        bool CanPlaceAffix(AffixDefinition definition, ItemInstance item, GeneratedItem rolled, int replacedSlot)
        {
            if (definition.Weight <= 0 || definition.MinItemLevel > item.ItemLevel || !definition.AllowedOn(rolled.Category)) return false;
            for (int i = 0; i < rolled.AffixCount; i++)
            {
                if (i == replacedSlot) continue;
                int index = Items.IndexOfAffix(rolled.GetAffix(i).AffixId);
                if (index >= 0 && Items.GetAffix(index).Group == definition.Group) return false;
            }
            return true;
        }
        RolledAffix[] ForgeCandidates(ItemInstance item, GeneratedItem rolled, int replacedSlot, ForgeOperation operation)
        {
            var eligible = new List<int>();
            for (int i = 0; i < Items.AffixCount; i++)
                if (CanPlaceAffix(Items.GetAffix(i), item, rolled, replacedSlot)
                    && (replacedSlot < 0 || Items.GetAffix(i).Id != rolled.GetAffix(replacedSlot).AffixId)) eligible.Add(i);
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
        public bool CanAffordForge(ForgePreview preview) => preview != null && preview.Owner == this && preview.Status == SmithResult.Success
            && Money(CurrencyType.Gold) >= preview.Gold && Money(CurrencyType.Shards) >= preview.Shards
            && MaterialCount(ForgeMaterial.Steel) >= preview.Steel && MaterialCount(ForgeMaterial.Core) >= preview.Cores;
        public SmithResult CommitForge(ForgePreview preview)
        {
            if (preview == null || preview.Owner != this) return SmithResult.StalePreview;
            if (preview.Status != SmithResult.Success) return preview.Status;
            if (!TryForgeTarget(preview.Target, out var current) || !current.SameRecipe(preview.Before)) return SmithResult.StalePreview;
            if (preview.Operation == ForgeOperation.Transfer && ((uint)preview.DonorSlot >= Bag.Capacity || !Bag.At(preview.DonorSlot).SameRecipe(preview.DonorItem))) return SmithResult.StalePreview;
            var reread = PreviewForge(preview.Target, preview.Operation, preview.AffixIndex, preview.Choice, preview.DonorSlot, preview.DonorAffix);
            if (reread.Status != SmithResult.Success) return reread.Status;
            if (!reread.After.SameRecipe(preview.After)) return SmithResult.StalePreview;
            if (!CanAffordForge(reread)) return SmithResult.InsufficientFunds;
            // Всё проверено до первого изменения; расход происходит только после подтверждения.
            if (preview.Target.IsWorn) Worn.Equip(preview.After, out _);
            else Bag.Put(preview.Target.Slot, preview.After, Bag.IsKept(preview.Target.Slot));
            if (preview.Operation == ForgeOperation.Transfer) Bag.Remove(preview.DonorSlot);
            Spend(CurrencyType.Gold, reread.Gold); Spend(CurrencyType.Shards, reread.Shards);
            SpendForgeMaterial(ForgeMaterial.Steel, reread.Steel); SpendForgeMaterial(ForgeMaterial.Core, reread.Cores);
            return SmithResult.Success;
        }
        public SmithResult Reforge(int slot, int affix) => CommitForge(PreviewForge(ForgeTarget.Bag(slot), ForgeOperation.Refine, affix));
        public SmithResult Reforge(EquipSlot slot, int affix) => CommitForge(PreviewForge(ForgeTarget.Worn(slot), ForgeOperation.Refine, affix));
        public SmithResult Dismantle(int slot, out int shards)
        {
            shards = 0;
            if (!Has(CampService.Smith) || (uint)slot >= Bag.Capacity || Bag.IsEmpty(slot)) return SmithResult.InvalidItem;
            if (Bag.IsKept(slot)) return SmithResult.Protected;
            shards = Inventory.ShardsFor(Bag.At(slot)); Bag.Remove(slot); Earn(CurrencyType.Shards, shards); return SmithResult.Success;
        }
    }
}
