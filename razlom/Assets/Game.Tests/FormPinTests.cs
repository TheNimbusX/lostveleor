using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// «Формы выключены — забег прежний» (план форм Пелага 02.10, тесты 2–5).
    ///
    /// Значения сняты 02.10 с кода ДО форм: сценарии FormBaselineScenarios собраны
    /// отдельно против снимка Game.Sim и прибиты здесь. Сдвинулся пин — сдвинулись
    /// награды, броски карточек или хеш обычного забега: искать причину в правке,
    /// а не переснимать число. Пересъёмка — только когда владелец включит формы,
    /// с записью причины здесь же.
    /// </summary>
    public sealed class FormPinTests
    {
        // Восемь арен Location(boss): карточки, потоки Loot/Affix и набор после каждого выбора.
        private static readonly ulong[] ArenaTrails =
        {
            0xC56721031C49ED73UL, 0x7ED15147ECE6341BUL, 0x0E9C85705DD75B77UL, 0x17A7A005B7BFF6DAUL,
            0x83740094412AE298UL, 0x49086190A2E7F1F6UL, 0xD7DC59DD948F81C1UL, 0x08B2033F4A89DB37UL,
            0x34DBBDE88A620372UL, 0x2F3180B23AE5E4E9UL, 0x94B61CC56C19A6A2UL, 0x5C4E3A733A4875B9UL,
        };

        // Восемь прототипных Разломов подряд — то же без маршрутов.
        private static readonly ulong[] PrototypeTrails =
        {
            0xC2B2E2467E861C62UL, 0x1DF32CE8AF9FE0B0UL, 0xF8D2F49E05AA6864UL, 0x900C6024D274B76DUL,
            0xDDA19F8F13B7BA8DUL, 0xBCA0A76A183094FDUL, 0xF59112F87ADA52FEUL, 0x77AA6AB069534CEDUL,
            0xE411FB2F3D72F432UL, 0xBC69A08BF05CB340UL, 0x8B072597BFCE6979UL, 0xE477B846A2B89A11UL,
        };

        // 50 сидов × 2 набора × 2 экрана: карточки талантов у линий без форм.
        private const ulong TalentTrail = 0x8A764ADFAB2BF05CUL;

        // Стартовый, A, B, A после замены слота с усилениями.
        private static readonly ulong[] LoadoutHashes =
            { 0x7B44EC52768AFEB9UL, 0xF81C512EB89D0140UL, 0xE34E34F930345F22UL, 0x07918B61A10BEB9BUL };

        // Своё определение без узлов; с узлами StatMod и флагами обеих половин.
        private static readonly ulong[] BuildHashes = { 0x599D8818AB388595UL, 0x53F3273770686568UL };

        [Test]
        public void SkillForms_AreOffByDefault()
        {
            Assert.IsFalse(FormRewardRules.UseSkillForms, "формы включены до приёмки Вихря владельцем");
            RiftRun run = FormBaselineScenarios.NewArenaRun(1);
            Assert.IsFalse(run.SkillFormsEnabled);
            Assert.IsFalse(run.DeveloperFormsUnlocked);
        }

        [Test]
        public void ArenaRewards_AreTheSameAsBeforeForms()
        {
            for (ulong seed = 1; seed <= (ulong)ArenaTrails.Length; seed++)
                Assert.AreEqual(ArenaTrails[seed - 1], FormBaselineScenarios.ArenaTrail(seed), "сид " + seed);
        }

        /// <summary>Разблокировка форм для F8 без включателя ничего не меняет.</summary>
        [Test]
        public void ArenaRewards_UnlockedFormsWithoutTheSwitch_ChangeNothing()
        {
            for (ulong seed = 1; seed <= 4; seed++)
                Assert.AreEqual(ArenaTrails[seed - 1],
                    FormBaselineScenarios.ArenaTrail(seed, r => r.DeveloperFormsUnlocked = true), "сид " + seed);
        }

        /// <summary>
        /// Включатель без готовых форм тоже ничего не меняет: обычный забег предлагает
        /// только готовые формы. Когда первая форма станет готовой, проверка уходит
        /// в FormRewardTests (там формы разблокированы явно).
        /// </summary>
        [Test]
        public void ArenaRewards_SwitchOnWithoutReadyForms_ChangeNothing()
        {
            if (PelagForms.ReadyFormCount(PelagKit.StarterPoolIndex) > 0)
                Assert.Ignore("у Вихря есть готовые формы — включатель меняет награды намеренно");
            for (ulong seed = 1; seed <= 4; seed++)
                Assert.AreEqual(ArenaTrails[seed - 1],
                    FormBaselineScenarios.ArenaTrail(seed, r => r.SkillFormsEnabled = true), "сид " + seed);
        }

        [Test]
        public void PrototypeRewards_AreTheSameAsBeforeForms()
        {
            for (ulong seed = 1; seed <= (ulong)PrototypeTrails.Length; seed++)
                Assert.AreEqual(PrototypeTrails[seed - 1], FormBaselineScenarios.PrototypeTrail(seed), "сид " + seed);
        }

        [Test]
        public void TalentCards_OnLinesWithoutForms_AreTheSameAsBeforeForms()
            => Assert.AreEqual(TalentTrail, FormBaselineScenarios.TalentCardTrail());

        [Test]
        public void LoadoutHash_WithoutForms_IsTheSameAsBefore()
            => CollectionAssert.AreEqual(LoadoutHashes, FormBaselineScenarios.LoadoutHashes());

        [Test]
        public void BuildHash_WithoutForm_IsTheSameAsBefore()
            => CollectionAssert.AreEqual(BuildHashes, FormBaselineScenarios.BuildHashes());
    }
}
