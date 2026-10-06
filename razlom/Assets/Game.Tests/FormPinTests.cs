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
    /// </summary>
    public sealed class FormPinTests
    {
        // Восемь арен Location(boss): карточки, потоки Loot/Affix и набор после каждого выбора.
        private static readonly ulong[] ArenaTrails =
        {
            0x7DC8E5F490A761A3UL, 0x0420EA19230A0F6CUL, 0x831C7BC4EF631B81UL, 0xB71B4915AB8951ABUL,
            0x1A5F90935E718FF2UL, 0x77DD768AE0732C10UL, 0x72E3499538745EC9UL, 0x2C9E5E395D4C6A80UL,
            0xCBF70946E28F1486UL, 0x45A6A1B06A1C998CUL, 0x19EF6E7237002049UL, 0x76FC4139D79E0F1AUL,
        };

        // Восемь прототипных Разломов подряд — то же без маршрутов.
        private static readonly ulong[] PrototypeTrails =
        {
            0x4F96F705B75B44A3UL, 0x5170B0158F05DBBBUL, 0xE763B0AE849861E8UL, 0xAAC5440CEA1242D0UL,
            0xE44FF00F6C827B6DUL, 0x695C4718EED3C2C1UL, 0x9CCF30674D284CCFUL, 0x483B1B3C0EBC6761UL,
            0xDB89E9908B133671UL, 0x719AD91988272B4BUL, 0x315A89DC72A8D233UL, 0x85F54A46FA14E5ADUL,
        };

        // 50 сидов × 2 набора × 2 экрана: карточки талантов у линий без форм.
        private const ulong TalentTrail = 0x7C2B3415CD5A4C70UL;

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
