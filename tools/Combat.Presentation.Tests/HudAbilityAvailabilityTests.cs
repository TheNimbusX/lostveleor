using NUnit.Framework;
using Game.View;

public sealed class HudAbilityAvailabilityTests
{
    [Test]
    public void ExactCostIsAvailable() => Assert.That(HudAbilityAvailability.Evaluate(true,false,0,30,30).Ready,Is.True);
    [Test]
    public void ResourceDenialReportsTheMissingAmount()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,0,18,30);
        Assert.That(state.Block,Is.EqualTo(HudAbilityBlock.Resource));
        Assert.That(state.MissingResource,Is.EqualTo(12));
    }
    [Test]
    public void CooldownHasPriorityOverResourceDenial()
    {
        var state=HudAbilityAvailability.Evaluate(true,false,21,0,30);
        Assert.That(state.Block,Is.EqualTo(HudAbilityBlock.Cooldown));
        Assert.That(state.RemainingTicks,Is.EqualTo(21));
    }
    [Test]
    public void ComboContinuationDoesNotChargeOrWaitAgain() => Assert.That(HudAbilityAvailability.Evaluate(true,true,60,0,30).Ready,Is.True);
    [Test]
    public void DeadHeroCannotContinueCombo() => Assert.That(HudAbilityAvailability.Evaluate(false,true,0,100,30).Block,Is.EqualTo(HudAbilityBlock.Dead));
    [Test]
    public void CooldownExpiryRestoresAvailability() => Assert.That(HudAbilityAvailability.Evaluate(true,false,-1,100,30).Ready,Is.True);
}
