using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Фон топота роя (CombatAudio.UpdateSwarmScuttle → SwarmScuttleClock). Баг владельца
/// 29.09: «иногда включается звук, который не должен тут быть — при входе на арену
/// например, ну и ещё где-то». Топот смотрел только на скорость корнеползов, а звук не
/// пространственный: он шёл над стоящей симуляцией (итоги после смерти или ухода,
/// награда, маршрут, дымная завеса — у бегущих скорость так и остаётся) и от стартовой
/// волны, бегущей к входу из-за края кадра. Кадры — 60 в секунду, тики Sim — 30, как в игре.
/// </summary>
public sealed class SwarmScuttleTests
{
    private const float Frame = 1f / 60f;

    [Test]
    public void ScuttlesWhileTheSwarmRunsInFrame()
    {
        var clock = new SwarmScuttleClock();
        var plays = Frames(clock, 0f, 3f, _ => true, _ => 3);
        Assert.That(plays.Count, Is.InRange(5, 6), "раз в 0,55 с за 3 с боя");
        for (int i = 1; i < plays.Count; i++)
            Assert.That(plays[i] - plays[i - 1], Is.GreaterThanOrEqualTo(SwarmScuttleClock.Spacing - .001f));
    }

    [Test]
    public void SilentOverAFrozenSimulation()
    {
        // Герой умер посреди роя: Sim больше не шагает, у бегущих скорость осталась.
        // Итоги висят, пока игрок не нажмёт — прежде шорох шёл весь этот экран.
        var clock = new SwarmScuttleClock();
        const float death = 2f;
        var plays = Frames(clock, 0f, 12f, t => t < death, _ => 3);
        Assert.That(plays.Count, Is.GreaterThan(0), "в бою топот есть");
        foreach (float at in plays)
            Assert.That(at, Is.LessThanOrEqualTo(death + SwarmScuttleClock.FrozenSeconds + Frame),
                "шорох над стоящей Sim на " + at.ToString("0.00") + " с");
    }

    [Test]
    public void ReturnsAsSoonAsTheSimulationStepsAgain()
    {
        // Награда и маршрут: Sim стоит; тайник охраняют корнеползы, и бой продолжается.
        var clock = new SwarmScuttleClock();
        var plays = Frames(clock, 0f, 7f, t => t < 2f || t >= 5f, _ => 2);
        Assert.That(plays.FindAll(t => t > 2f + SwarmScuttleClock.FrozenSeconds + Frame && t < 5f), Is.Empty);
        Assert.That(plays.Find(t => t >= 5f), Is.InRange(5f, 5f + SwarmScuttleClock.IdleRecheck + 2 * Frame),
            "бой пошёл — топот вернулся не позже, чем через проверку простоя");
    }

    [Test]
    public void NewArenaUnderTheSmokeStaysSilentUntilItsFirstTick()
    {
        // Смена арены: та же Sim, тики стоят под завесой, у CombatAudio — сброс (PlayArenaChange).
        var clock = new SwarmScuttleClock();
        const int tick = 100;
        float now = 0f;
        for (; now < 1f; now += Frame) clock.Due(tick, now);
        clock.Reset();
        for (; now < 2f; now += Frame) Assert.That(clock.Due(tick, now), Is.False, "Sim ещё не шагнула");
        float due = -1f;
        for (int next = tick + 1; now < 3f && due < 0f; now += Frame, next++)
            if (clock.Due(next, now)) due = now;
        Assert.That(due, Is.InRange(2f, 2f + SwarmScuttleClock.IdleRecheck + Frame), "первый тик новой арены");
    }

    [Test]
    public void SilentWithoutRunners()
    {
        var clock = new SwarmScuttleClock();
        Assert.That(Frames(clock, 0f, 3f, _ => true, _ => 0), Is.Empty);
    }

    [Test]
    public void CountsOnlyLivingRunningRootSwarmWithinEarshot()
    {
        var e = new EntityStore(16);
        int hero = e.Spawn(At(0f, 0f), 100, Faction.Wole);
        int near = Swarm(e, 5f, 0f, running: true);
        int edge = Swarm(e, 0f, -SwarmScuttleClock.HearingRadius + .5f, running: true);
        Swarm(e, 15f, 0f, running: true);               // за краем кадра
        Swarm(e, 3f, 3f, running: false);               // стоит и кусает
        int dead = Swarm(e, -4f, 0f, running: true);
        e.Alive[dead] = false;
        int guardian = Swarm(e, 2f, 0f, running: true);
        e.Kind[guardian] = EnemyKind.ForestGuardian;    // его шаги — не топот роя
        int child = Swarm(e, -2f, 0f, running: true);
        e.Kind[child] = EnemyKind.ForestSplitling;

        Assert.That(SwarmScuttleClock.CountRunning(e, hero, SwarmScuttleClock.HearingRadius), Is.EqualTo(2),
            "в кадре бегут #" + near + " и #" + edge);
        Assert.That(SwarmScuttleClock.CountRunning(e, hero, -1f), Is.EqualTo(3), "без предела — и дальний");
        Assert.That(SwarmScuttleClock.CountRunning(e, 99, SwarmScuttleClock.HearingRadius), Is.Zero, "нет героя — нет счёта");
    }

    [Test]
    public void ArenaEntryWaveRushingFromOffscreenIsHeardOnlyOnceInFrame()
    {
        // Вход на арену: стартовая волна корнеползов заметила героя с 18 м и бежит к
        // нему (на стенде — треть тиков топота в первые 3 с). Шорох — когда рой в кадре.
        var e = new EntityStore(8);
        int hero = e.Spawn(At(0f, 0f), 100, Faction.Wole);
        var pack = new[] { Swarm(e, 18f, 0f, true), Swarm(e, 18.5f, 1f, true), Swarm(e, 19f, -1f, true) };
        const float speed = .12f;   // м за тик
        var clock = new SwarmScuttleClock();
        float firstPlay = -1f, enteredFrame = -1f;
        int tick = 0;
        for (float now = 0f; now < 4f && firstPlay < 0f; now += Frame)
        {
            int due = (int)(now * Simulation.TicksPerSecond);
            for (; tick < due; tick++)
                foreach (int id in pack)
                    e.Position[id] = new FixVec2(e.Position[id].X - Fix64.FromDouble(speed), e.Position[id].Y);
            if (enteredFrame < 0f && e.Position[pack[0]].X.ToFloat() <= SwarmScuttleClock.HearingRadius) enteredFrame = now;
            if (!clock.Due(tick, now)) continue;
            if (clock.Counted(SwarmScuttleClock.CountRunning(e, hero, SwarmScuttleClock.HearingRadius), now)) firstPlay = now;
        }
        Assert.That(enteredFrame, Is.GreaterThan(1f), "рой добегает до края кадра не сразу");
        Assert.That(firstPlay, Is.GreaterThanOrEqualTo(enteredFrame), "шорох раньше, чем рой в кадре");
        Assert.That(firstPlay, Is.LessThanOrEqualTo(enteredFrame + SwarmScuttleClock.IdleRecheck + Frame));
    }

    /// <summary>Кадры от from до to: тик растёт, пока ticking(t); возвращает моменты шороха.</summary>
    private static List<float> Frames(SwarmScuttleClock clock, float from, float to, Func<float, bool> ticking,
        Func<float, int> running)
    {
        var plays = new List<float>();
        int tick = 0;
        float simClock = 0f;
        for (float now = from; now < to; now += Frame)
        {
            if (ticking(now))
            {
                simClock += Frame;
                tick = (int)(simClock * Simulation.TicksPerSecond);
            }
            if (!clock.Due(tick, now)) continue;
            if (clock.Counted(running(now), now)) plays.Add(now);
        }
        return plays;
    }

    private static FixVec2 At(float x, float y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

    private static int Swarm(EntityStore e, float x, float y, bool running)
    {
        int id = e.Spawn(At(x, y), 10, Faction.Orvill);
        e.Kind[id] = EnemyKind.ForestRootSwarm;
        e.Velocity[id] = running ? At(-.1f, 0f) : FixVec2.Zero;
        return id;
    }
}
