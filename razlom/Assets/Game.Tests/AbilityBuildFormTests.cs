using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Форма в сборке способности (план форм 02.10, тест 3): узел формы ставит
    /// Form, определение и его Id прежние, порядок узлов не важен, сборка без
    /// формы хешируется как до форм (пин — FormPinTests.BuildHash_…).
    /// </summary>
    public sealed class AbilityBuildFormTests
    {
        private static ulong Hash(AbilityBuild build) => FormBaselineScenarios.BuildHash(build);

        private static AbilityNode RadiusNode()
            => AbilityNode.StatMod("test.forms.radius", AbilityStatType.Radius, ModifierOp.Increased, Fix64.Ratio(1, 4));

        [Test]
        public void FormNode_SetsTheForm_WithoutIt_None()
        {
            var build = new AbilityBuild();
            var nodes = new[] { RadiusNode(), AbilityNode.Form("form.whirlwind.storm", PelagForm.WhirlwindStorm) };
            build.Rebuild(AbilityDefinition.Whirlwind(), nodes, 2);
            Assert.AreEqual(PelagForm.WhirlwindStorm, build.Form);
            Assert.AreEqual(AbilityDefinition.WhirlwindId, build.DefinitionId);

            build.Rebuild(AbilityDefinition.Whirlwind(), new[] { RadiusNode() }, 1);
            Assert.AreEqual(PelagForm.None, build.Form, "пересборка без узла оставила форму");
        }

        [Test]
        public void NodeOrder_DoesNotMatter()
        {
            var a = new AbilityBuild();
            var b = new AbilityBuild();
            var trait = (AbilityTrait)(1UL << 40);
            a.Rebuild(AbilityDefinition.Whirlwind(), new[]
            {
                RadiusNode(), AbilityNode.Form("form.whirlwind.maelstrom", PelagForm.WhirlwindMaelstrom),
                AbilityNode.Trait("test.forms.trait", trait),
            }, 3);
            b.Rebuild(AbilityDefinition.Whirlwind(), new[]
            {
                AbilityNode.Trait("test.forms.trait", trait),
                AbilityNode.Form("form.whirlwind.maelstrom", PelagForm.WhirlwindMaelstrom), RadiusNode(),
            }, 3);
            Assert.AreEqual(Hash(a), Hash(b));
            Assert.AreEqual(PelagForm.WhirlwindMaelstrom, b.Form);
            Assert.IsTrue(b.Has(trait));
            Assert.IsFalse(b.Has((AbilityTrait)1UL));
        }

        [Test]
        public void Hash_SeesFormAndTraits_OnlyWhenSet()
        {
            var plain = new AbilityBuild();
            plain.Rebuild(AbilityDefinition.Whirlwind(), new[] { RadiusNode() }, 1);
            var placeholder = new AbilityBuild();
            placeholder.Rebuild(AbilityDefinition.Whirlwind(),
                new[] { RadiusNode(), AbilityNode.Trait("test.forms.empty", AbilityTrait.None) }, 2);
            Assert.AreEqual(Hash(plain), Hash(placeholder), "пустая черта (заглушка таланта формы) сдвинула хеш");

            var storm = new AbilityBuild();
            storm.Rebuild(AbilityDefinition.Whirlwind(),
                new[] { RadiusNode(), AbilityNode.Form("form.whirlwind.storm", PelagForm.WhirlwindStorm) }, 2);
            var waves = new AbilityBuild();
            waves.Rebuild(AbilityDefinition.Whirlwind(),
                new[] { RadiusNode(), AbilityNode.Form("form.whirlwind.foam_waves", PelagForm.WhirlwindFoamWaves) }, 2);
            var traits = new AbilityBuild();
            traits.Rebuild(AbilityDefinition.Whirlwind(),
                new[] { RadiusNode(), AbilityNode.Trait("test.forms.trait", (AbilityTrait)(1UL << 3)) }, 2);
            Assert.AreNotEqual(Hash(plain), Hash(storm));
            Assert.AreNotEqual(Hash(storm), Hash(waves));
            Assert.AreNotEqual(Hash(plain), Hash(traits));
            Assert.AreEqual(plain.Get(AbilityStatType.Radius), storm.Get(AbilityStatType.Radius), "форма без чисел сдвинула стат");
        }
    }
}
