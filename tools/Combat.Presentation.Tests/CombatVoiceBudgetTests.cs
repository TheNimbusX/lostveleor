using Game.View;
using NUnit.Framework;

public sealed class CombatVoiceBudgetTests
{
    [Test]
    public void FootstepsCannotInterruptWarningOrDelayedImpact()
    {
        var pool = new CombatVoiceBudget(2);
        pool.Acquire(0, 1.5, 100);
        pool.Acquire(0, 0.8, 70);
        Assert.That(pool.Acquire(0.1, 0.2, 10), Is.EqualTo(-1));
        Assert.That(pool.Stolen, Is.Zero);
        Assert.That(pool.Acquire(0.9, 0.2, 10), Is.EqualTo(1));
    }

    [Test]
    public void WarningStealsLowestPriorityNotNextRingSlot()
    {
        var pool = new CombatVoiceBudget(3);
        pool.Acquire(0, 2, 70);
        pool.Acquire(0, 2, 10);
        pool.Acquire(0, 2, 50);
        Assert.That(pool.Acquire(0.1, 0.4, 100), Is.EqualTo(1));
        Assert.That(pool.Stolen, Is.EqualTo(1));
    }

    [Test]
    public void EqualPriorityDoesNotRestartAnAudibleContact()
    {
        var pool = new CombatVoiceBudget(1);
        pool.Acquire(0, 0.3, 70);
        Assert.That(pool.Acquire(0.01, 0.3, 70), Is.EqualTo(-1));
        Assert.That(pool.Acquire(0.3, 0.3, 70), Is.Zero);
    }

    [Test]
    public void CancelledChannelReleasesItsVoiceAndSceneChangeClearsReservations()
    {
        var pool = new CombatVoiceBudget(2);
        int channel = pool.Acquire(0, 5, 80);
        pool.Acquire(0, 5, 100);
        pool.Release(channel);
        Assert.That(pool.Acquire(0.1, 0.3, 10), Is.EqualTo(channel));
        pool.Clear();
        Assert.That(pool.Acquire(0, 1, 10), Is.Zero);
        Assert.That(pool.Acquire(0, 1, 10), Is.EqualTo(1));
    }
}
