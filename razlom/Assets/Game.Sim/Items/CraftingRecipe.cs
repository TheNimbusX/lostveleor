using System;

namespace Game.Sim
{
    public enum ForgeOperation : byte { Refine, Replace, Add, Transfer }
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
        readonly CraftStep[] _steps;
        public int Count => _steps.Length;
        public int RefineCount { get; }
        public CraftStep Step(int index) => _steps[index];
        public CraftingRecipe(CraftStep[] steps)
        {
            if (steps == null || steps.Length > MaximumSteps) throw new ArgumentException(nameof(steps));
            _steps = (CraftStep[])steps.Clone();
            foreach (var step in _steps) if (step.Operation == ForgeOperation.Refine) RefineCount++;
        }
        public CraftingRecipe Append(CraftStep step)
        {
            if (Count >= MaximumSteps) throw new InvalidOperationException("История ковки заполнена");
            var steps = new CraftStep[Count + 1]; Array.Copy(_steps, steps, Count); steps[Count] = step;
            return new CraftingRecipe(steps);
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
