using NUnit.Framework;
using Game.Sim;
using Game.View;

public sealed class HudAbilityAvailabilityTests
{
    [Test]
    public void ExactCostIsAvailable()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,0,30,30,false);
        Assert.That(state.Ready,Is.True);
        Assert.That(state.Text,Is.Empty,"у готовой способности нет текста отказа");
    }
    [Test]
    public void ResourceDenialReportsTheMissingAmount()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,0,18,30,false);
        Assert.That(state.Block,Is.EqualTo(HudAbilityBlock.Resource));
        Assert.That(state.MissingResource,Is.EqualTo(12));
    }
    [Test]
    public void CooldownHasPriorityOverResourceDenial()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,21,0,30,false);
        Assert.That(state.Block,Is.EqualTo(HudAbilityBlock.Cooldown));
        Assert.That(state.RemainingTicks,Is.EqualTo(21));
    }
    [Test]
    public void ComboContinuationDoesNotChargeOrWaitAgain() => Assert.That(HudAbilityAvailability.Evaluate(true,true,60,0,30,false).Ready,Is.True);
    [Test]
    public void DeadHeroCannotContinueCombo() => Assert.That(HudAbilityAvailability.Evaluate(false,true,0,100,30,false).Block,Is.EqualTo(HudAbilityBlock.Dead));
    [Test]
    public void CooldownExpiryRestoresAvailability() => Assert.That(HudAbilityAvailability.Evaluate(true,false,-1,100,30,false).Ready,Is.True);

    // Корни (решение владельца 29.09): кнопка, что двигает героя, не готова, пока он в корнях.
    [Test]
    public void RootsHoldAnOtherwiseReadyAbility()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,0,30,30,true);
        Assert.That(state.Block,Is.EqualTo(HudAbilityBlock.Rooted));
        Assert.That(state.Ready,Is.False);
        Assert.That(state.Text,Is.EqualTo("Корни держат"));
    }
    [Test]
    public void CooldownDialStaysUnderRoots()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,21,100,30,true);
        Assert.That(state.Block,Is.EqualTo(HudAbilityBlock.Cooldown));
        Assert.That(state.RemainingTicks,Is.EqualTo(21));
    }
    [Test]
    public void ResourceBadgeStaysUnderRoots()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,0,18,30,true);
        Assert.That(state.Block,Is.EqualTo(HudAbilityBlock.Resource));
        Assert.That(state.MissingResource,Is.EqualTo(12));
    }
    [Test]
    public void DeadHeroOutranksRoots() => Assert.That(HudAbilityAvailability.Evaluate(false,false,0,100,30,true).Block,Is.EqualTo(HudAbilityBlock.Dead));
    [Test]
    public void RootsHoldComboContinuationToo() => Assert.That(HudAbilityAvailability.Evaluate(true,true,60,0,30,true).Block,Is.EqualTo(HudAbilityBlock.Rooted));

    // Флаг берётся из Sim.AbilityHeldByRoots: кувырок в корнях — «Корни держат», Вихрь на месте — нет.
    [Test]
    public void RootedHeroSeesDashHeldButWhirlwindFree()
    {
        var sim=new Simulation(1234,64);
        sim.SetupTestArena(0);
        new RunLoadout().ApplyTo(sim);
        int dash=PelagKit.DashSlot;
        Assert.That(HudAbilityAvailability.Of(sim,dash,sim.GetAbility(dash)).Block,Is.Not.EqualTo(HudAbilityBlock.Rooted));
        Assert.That(sim.ApplyHeroRoot(150),Is.True);
        Assert.That(HudAbilityAvailability.Of(sim,dash,sim.GetAbility(dash)).Block,Is.EqualTo(HudAbilityBlock.Rooted));
        Assert.That(HudAbilityAvailability.Of(sim,0,sim.GetAbility(0)).Block,Is.Not.EqualTo(HudAbilityBlock.Rooted));
    }
}
