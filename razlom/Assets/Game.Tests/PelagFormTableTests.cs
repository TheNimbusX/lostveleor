using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Таблица форм Пелага (план форм 02.10, тест 1): номера навсегда, формы своих
    /// линий, ключи узлов не пересекаются, пул → линия талантов — явная таблица.
    /// </summary>
    public sealed class PelagFormTableTests
    {
        /// <summary>Номера прибиты: только дописывать (как бит «Печати» и RewardKind.AbilityNode).</summary>
        [Test]
        public void FormNumbers_AreForever()
        {
            Assert.AreEqual(0, (int)PelagForm.None);
            Assert.AreEqual(1, (int)PelagForm.WhirlwindStorm);
            Assert.AreEqual(2, (int)PelagForm.WhirlwindMaelstrom);
            Assert.AreEqual(3, (int)PelagForm.WhirlwindFoamWaves);
            Assert.AreEqual(4, (int)PelagForm.WhirlwindOnTheMove);
            Assert.AreEqual(3, (int)NodeKind.Form);
            Assert.AreEqual(4, (int)NodeKind.Trait);
            Assert.AreEqual(7, (int)RewardKind.Form);
            Assert.AreEqual(8, (int)RewardKind.Link, "8 — связка, не занимать");
            Assert.AreEqual(16, PelagForms.FormTalentBase);
            Assert.AreEqual(100, PelagKit.SabreLine);
        }

        [Test]
        public void EveryForm_BelongsToItsLine_InNumberOrder()
        {
            int seen = 0;
            for (int i = 0; i < PelagKit.LineCount; i++)
            {
                int line = PelagKit.LineAt(i);
                int count = PelagForms.FormCount(line, readyOnly: false);
                Assert.LessOrEqual(PelagForms.ReadyFormCount(line), count);
                for (int k = 0; k < count; k++)
                {
                    PelagForm form = PelagForms.FormAt(line, k, readyOnly: false);
                    Assert.AreEqual(line, PelagForms.LineOf(form), form.ToString());
                    if (k > 0) Assert.Greater((int)form, (int)PelagForms.FormAt(line, k - 1, readyOnly: false));
                }
                for (int k = 0; k < PelagForms.ReadyFormCount(line); k++)
                {
                    Assert.IsTrue(PelagForms.IsReady(PelagForms.ReadyFormAt(line, k)));
                    Assert.AreEqual(line, PelagForms.LineOf(PelagForms.ReadyFormAt(line, k)));
                }
                Assert.AreEqual(PelagForm.None, PelagForms.FormAt(line, count, readyOnly: false));
                seen += count;
            }
            // Убранная форма держит номер, но ни в какой линии её нет.
            for (int f = 1; f <= PelagForms.Count; f++)
                if (PelagForms.IsRetired((PelagForm)f))
                {
                    Assert.IsFalse(PelagForms.IsValid((PelagForm)f));
                    Assert.AreEqual(-1, PelagForms.LineOf((PelagForm)f));
                    seen++;
                }
            Assert.AreEqual(PelagForms.Count, seen, "форма без линии");
            Assert.AreEqual(-1, PelagForms.LineOf(PelagForm.None));
            Assert.IsFalse(PelagForms.IsValid((PelagForm)(PelagForms.Count + 1)));
        }

        /// <summary>«Вихрь на ходу» убран 02.10 («на ходу убираем»): три формы, номер 4 занят навсегда.</summary>
        [Test]
        public void WhirlwindForms_AreTheThreeApproved_OnTheMoveIsRetired()
        {
            int line = PelagKit.PoolIndexOf(AbilityDefinition.WhirlwindId);
            Assert.AreEqual(3, PelagForms.FormCount(line, readyOnly: false));
            Assert.AreEqual(3, PelagForms.ReadyFormCount(line), "механика всех трёх написана");
            Assert.AreEqual(PelagForm.WhirlwindStorm, PelagForms.FormAt(line, 0, false));
            Assert.AreEqual(PelagForm.WhirlwindMaelstrom, PelagForms.FormAt(line, 1, false));
            Assert.AreEqual(PelagForm.WhirlwindFoamWaves, PelagForms.FormAt(line, 2, false));
            Assert.AreEqual(PelagForm.None, PelagForms.FormAt(line, 3, false));
            Assert.IsTrue(PelagForms.IsRetired(PelagForm.WhirlwindOnTheMove));
            Assert.IsFalse(PelagForms.IsValid(PelagForm.WhirlwindOnTheMove));
            Assert.IsNull(PelagForms.KeyOf(PelagForm.WhirlwindOnTheMove));
            Assert.AreEqual(0, PelagForms.FormTalentCount(PelagForm.WhirlwindOnTheMove));
            // 13: номер 4 убранной формы не освобождается, 5–7 — формы Шквала, 8–10 — Абордажа (02.10),
            // 11–13 — Крушения, 14–16 — Броска якоря (03.10).
            Assert.AreEqual(16, PelagForms.Count, "номер убранной формы не освобождается");
            Assert.AreEqual(AbilityDefinition.WhirlwindId, PelagKit.PoolDefinition(PelagForms.LineOf(PelagForm.WhirlwindStorm)).Id);
            // Формы сабли и остальных навыков владелец ещё не утвердил.
            Assert.AreEqual(0, PelagForms.FormCount(PelagKit.SabreLine, readyOnly: false));
        }

        /// <summary>Ключи узлов форм, талантов форм и прежних талантов не совпадают.</summary>
        [Test]
        public void NodeKeys_DoNotCollide()
        {
            var ids = new HashSet<int>();
            void Add(string key)
            {
                Assert.IsNotNull(key);
                Assert.IsTrue(ids.Add(StableId.Of(key)), "совпал Id ключа " + key);
            }

            string[] lines = { "whirlwind", "cleave", "blaze", "squall", "anchor_slam", "wreck", "boarding", "flask" };
            foreach (string line in lines)
                for (int index = 1; index <= SabreTalents.TalentsPerLine; index++) Add("talent.sabre." + line + "." + index);
            Add("form.whirlwind.on_the_move");   // ключ убранной формы — занят навсегда
            Add(PelagForms.StormMoveKey);
            for (int f = 1; f <= PelagForms.Count; f++)
            {
                var form = (PelagForm)f;
                if (PelagForms.IsRetired(form)) continue;
                Add(PelagForms.KeyOf(form));
                for (int k = 0; k < PelagForms.FormTalentCount(form); k++) Add(PelagForms.TalentKeyOf(form, k));
                Assert.IsNull(PelagForms.TalentKeyOf(form, PelagForms.FormTalentCount(form)));
            }
            Assert.LessOrEqual(PelagForms.PlaceholderTalentsPerForm, PelagForms.MaxFormTalents);
        }

        [Test]
        public void FormNodes_SetTheFormAndTalentNodesAreTraits()
        {
            var buffer = new AbilityNode[RunLoadout.MaxNodesPerSlot];
            int count = PelagForms.AppendFormNodes(PelagForm.WhirlwindMaelstrom, buffer, 0);
            Assert.AreEqual(1, count);
            Assert.AreEqual(NodeKind.Form, buffer[0].Kind);
            Assert.AreEqual(PelagForm.WhirlwindMaelstrom, buffer[0].FormId);
            Assert.AreEqual(StableId.Of("form.whirlwind.maelstrom"), buffer[0].Id);
            Assert.AreEqual(0, PelagForms.AppendFormNodes(PelagForm.None, buffer, 0));

            count = PelagForms.AppendFormTalentNode(PelagForm.WhirlwindMaelstrom, 1, buffer, 0);
            Assert.AreEqual(1, count);
            Assert.AreEqual(NodeKind.Trait, buffer[0].Kind);
            Assert.AreEqual(StableId.Of("form.whirlwind.maelstrom.t2"), buffer[0].Id);
            Assert.AreEqual(0, PelagForms.AppendFormTalentNode(PelagForm.WhirlwindMaelstrom, 99, buffer, 0));
        }

        /// <summary>Пул → линия талантов больше не тождество: пул 8–9 и сабля без линии.</summary>
        [Test]
        public void TalentLines_AreAnExplicitTable()
        {
            for (int pool = 0; pool < SabreTalents.LineCount; pool++)
            {
                Assert.IsTrue(SabreTalents.TryLineOf(pool, out SabreTalentLine line), "пул " + pool);
                Assert.AreEqual(pool, SabreTalents.PoolIndexOf(line));
            }
            Assert.IsFalse(SabreTalents.TryLineOf(8, out _), "«На вылет» получил чужие таланты");
            Assert.IsFalse(SabreTalents.TryLineOf(9, out _));
            Assert.IsFalse(SabreTalents.TryLineOf(PelagKit.SabreLine, out _));
            Assert.IsFalse(SabreTalents.TryLineOf(-1, out _));
            Assert.IsFalse(SabreTalents.TryLineOf(PelagKit.PoolSize, out _));
        }

        [Test]
        public void SabreLine_IsALineButNeverAPoolIndex()
        {
            Assert.Greater(PelagKit.SabreLine, PelagKit.PoolSize);
            Assert.IsNull(PelagKit.PoolDefinition(PelagKit.SabreLine));
            Assert.AreEqual(PelagKit.PoolSize + 1, PelagKit.LineCount);
            for (int i = 0; i < PelagKit.PoolSize; i++) Assert.AreEqual(i, PelagKit.LineAt(i));
            Assert.AreEqual(PelagKit.SabreLine, PelagKit.LineAt(PelagKit.PoolSize), "сабля — последней");
        }
    }
}
