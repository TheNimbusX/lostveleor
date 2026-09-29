using Game.Sim;
using Game.View;
using NUnit.Framework;
using Change = Game.View.CombatSoundScope.Change;

/// <summary>
/// Чей бой звучит (CombatAudio → CombatSoundScope). Баг владельца 29.09: «иногда
/// включается звук, который не должен тут быть». Кадр смены симуляции звучал
/// событиями не новой Sim (на возврате в лагерь — давний список лагерной), а смену
/// арены внутри одной Sim никто не замечал: ожидания мобов, хозяева длинных звуков
/// и отложенное прошлой арены доживали до новой, где номера начинаются заново.
/// Кадры идут, как в игре: лагерь → Разлом → арены → смерть → итоги → «повторить» → лагерь.
/// </summary>
public sealed class CombatSoundScopeTests
{
    private const int NoRun = -1;

    [Test]
    public void FollowsARunFromCampThroughArenasToSummaryAndBack()
    {
        var scope = new CombatSoundScope();
        // Первый кадр: лагерь собран (ConfigureCampWorld уже подняла поколение).
        Assert.That(scope.Update(GameMode.Camp, 1, NoRun), Is.EqualTo(Change.Simulation));
        Assert.That(scope.Update(GameMode.Camp, 1, NoRun), Is.EqualTo(Change.None));
        // Вход в Разлом: новая Sim, первая арена.
        Assert.That(scope.Update(GameMode.Rift, 2, 1), Is.EqualTo(Change.Simulation));
        Assert.That(scope.Update(GameMode.Rift, 2, 1), Is.EqualTo(Change.None));
        // Следующая арена: та же Sim и то же поколение, глубина другая.
        Assert.That(scope.Update(GameMode.Rift, 2, 2), Is.EqualTo(Change.Arena), "смену арены раньше не видел никто");
        Assert.That(scope.Update(GameMode.Rift, 2, 2), Is.EqualTo(Change.None));
        Assert.That(scope.Update(GameMode.Rift, 2, 3), Is.EqualTo(Change.Arena));
        // Смерть: тот же тик кончает забег — итоги при той же Sim.
        Assert.That(scope.Update(GameMode.Summary, 2, 3), Is.EqualTo(Change.Mode));
        Assert.That(scope.Update(GameMode.Summary, 2, 3), Is.EqualTo(Change.None));
        // «Повторить»: новая Sim, снова первая арена.
        Assert.That(scope.Update(GameMode.Rift, 3, 1), Is.EqualTo(Change.Simulation));
        // Уход в лагерь из паузы: забега больше нет.
        Assert.That(scope.Update(GameMode.Camp, 4, NoRun), Is.EqualTo(Change.Simulation));
        Assert.That(scope.Update(GameMode.Camp, 4, NoRun), Is.EqualTo(Change.None));
    }

    [Test]
    public void NewSimulationWinsOverTheSameDepth()
    {
        // Смерть на первой арене и «повторить»: глубина та же, Sim новая — это не
        // «та же арена», а другой бой с другими номерами сущностей.
        var scope = new CombatSoundScope();
        scope.Update(GameMode.Rift, 5, 1);
        scope.Update(GameMode.Summary, 5, 1);
        Assert.That(scope.Update(GameMode.Rift, 6, 1), Is.EqualTo(Change.Simulation));
    }

    [Test]
    public void ProvingGroundInCampIsASimulationSwap()
    {
        var scope = new CombatSoundScope();
        scope.Update(GameMode.Camp, 1, NoRun);
        Assert.That(scope.Update(GameMode.Camp, 2, NoRun), Is.EqualTo(Change.Simulation), "встать на Полигон");
        Assert.That(scope.Update(GameMode.Camp, 3, NoRun), Is.EqualTo(Change.Simulation), "сойти с Полигона");
    }

    [Test]
    public void OnlyTheSimulationSwapFrameIsSilent()
    {
        Assert.That(CombatSoundScope.HearsFrameEvents(Change.Simulation), Is.False,
            "события кадра смены — прежней Sim или давний список новой");
        Assert.That(CombatSoundScope.HearsFrameEvents(Change.None), Is.True);
        Assert.That(CombatSoundScope.HearsFrameEvents(Change.Arena), Is.True, "расстановка новой арены — её события");
        Assert.That(CombatSoundScope.HearsFrameEvents(Change.Mode), Is.True, "последний удар перед итогами звучит");
    }

    [Test]
    public void ModeOrSimulationChangeEndsTheFightButTheNextArenaDoesNot()
    {
        // Итоги и лагерь: голоса боя и сердцебиение низкого здоровья гаснут.
        Assert.That(CombatSoundScope.EndsFight(Change.Mode), Is.True);
        Assert.That(CombatSoundScope.EndsFight(Change.Simulation), Is.True);
        // Смена арены идёт под дымной завесой: голоса доигрывают, сбрасываются ожидания.
        Assert.That(CombatSoundScope.EndsFight(Change.Arena), Is.False);
        Assert.That(CombatSoundScope.EndsFight(Change.None), Is.False);
    }
}
