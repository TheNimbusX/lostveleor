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
    ///
    /// ПЕРЕСНЯТО 03.10 (переделка Крушения, artifacts/wreck/plan/SPEC.md 2.5): индексы
    /// пула 4 (Удар якорем — влит в Крушение) и 8 («На вылет» — убран владельцем)
    /// больше не идут в броски наград (PelagKit.InRewardPool, две проверки RiftRun) —
    /// кандидатов меньше, карточки и дропы у всех сидов другие. Контроль: та же сборка
    /// с выключенным признаком дала прежние числа бит в бит (переделка Крушения, формы
    /// 11–13 и стат LaneLength награды не трогают); новые сняты двумя прогонами, совпали.
    /// Наборы и сборки (LoadoutHashes, BuildHashes) не сдвинулись.
    ///
    /// ПЕРЕСНЯТО 06.10 (экономика лагеря, artifacts/camp/spec-0610/plan.md S.9): только
    /// ArenaTrails — сценарий подмешивает run.Gold, а золото арен теперь 2×N (×2 «Сложно»,
    /// было 50 + 10·глубина только на «Сложно»), элита 25, разбор навыка 10 (было 15 + 5·глубина).
    /// Контроль: тот же сценарий с прежней формулой золота вместо run.Gold дал прежние 12
    /// чисел бит в бит (карточки, потоки Loot/Affix и наборы не сдвинулись); новые сняты
    /// двумя прогонами, совпали. PrototypeTrails, TalentTrail, LoadoutHashes, BuildHashes
    /// не менялись: прототип без маршрутов золото в хеш не кладёт.
    ///
    /// ПЕРЕСНЯТО 07.10 — «07.10 владелец: Ладно смазал и Отбой убраны из наград» (пул 2 и 9 вне
    /// PelagKit.InRewardPool, усилений убранных — PelagKit.OffersTalents) — ArenaTrails, PrototypeTrails,
    /// TalentTrail. Контроль: прежний признак дал прежние числа бит в бит; новые — два прогона, совпали.
    /// </summary>
    public sealed class FormPinTests
    {
        // Восемь арен Location(boss): карточки, потоки Loot/Affix и набор после каждого выбора.
        private static readonly ulong[] ArenaTrails =
        {
            0xD1BDCC48B82897C4UL, 0xA6670E33D6A530C9UL, 0x29472C105606C13FUL, 0x7B704FBA2147C190UL,
            0xADD8514F7B29654EUL, 0x04B09E1C2CCB14CCUL, 0x2074D07ED5EADFF1UL, 0x2ADDAB9FF7B053A5UL,
            0x0F0FCD4AFE7F2189UL, 0x511642B7DF4D0536UL, 0x8ACC0013E17AF030UL, 0x15A6E26811F503EEUL,
        };

        // Восемь прототипных Разломов подряд — то же без маршрутов.
        private static readonly ulong[] PrototypeTrails =
        {
            0xDE371A050AC4757BUL, 0x90637BC367D09A2AUL, 0xE108E4995C3723B5UL, 0x7A3B3B60DCA91B45UL,
            0xA62498D2CADC0E5BUL, 0x2A5E5C63C85A3F26UL, 0x78FD187B42BF2995UL, 0x0E55359A5455C212UL,
            0xAA46D7AAED4C986CUL, 0x557F47FAC57B114AUL, 0x75241F88BB6808BFUL, 0x5F2EE8F847B7FF86UL,
        };

        // 50 сидов × 2 набора × 2 экрана: карточки талантов у линий без форм.
        private const ulong TalentTrail = 0xFC031647CB270ADCUL;

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
