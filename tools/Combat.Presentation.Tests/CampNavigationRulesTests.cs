using Game.View;
using NUnit.Framework;

public sealed class CampNavigationRulesTests
{
    [TestCase("Тень wooden+fence+3d+model", 2f)]
    [TestCase("Тень Куст изгороди 69", .02f)]
    [TestCase("Shadow stylized+tree+3d+model", 8f)]
    public void ContactShadowCannotBecomeItsSourceObstacle(string name, float height)
        => Assert.That(CampNavigationRules.LegacyRole(name, height, 0), Is.EqualTo(CampObstacleRole.Passable));

    [Test]
    public void IndividualBoundaryBushBlocksButAggregateHedgeIsNotAModelRoot()
    {
        Assert.That(CampNavigationRules.LegacyRole("Куст изгороди 1", 1.8f, 0), Is.EqualTo(CampObstacleRole.Boundary));
        Assert.That(CampNavigationRules.IsModelRootName("Куст изгороди 1", true), Is.True);
        Assert.That(CampNavigationRules.IsModelRootName("Живая изгородь", false), Is.False);
        Assert.That(CampNavigationRules.LegacyRole("Молодая ель 1", 1.4f, 0), Is.EqualTo(CampObstacleRole.Trunk));
    }

    [TestCase(4f, 1.6f, .4f)]
    [TestCase(.8f, .2f, .12f)]
    [TestCase(1f, 1f, .2f)]
    public void LegacyTrunkUsesWorldRadiusIndependentOfModelScale(float extent, float scale, float expected)
        => Assert.That(CampNavigationRules.LegacyTrunkRadius(extent, scale) * scale, Is.EqualTo(expected).Within(.00001f));
}
