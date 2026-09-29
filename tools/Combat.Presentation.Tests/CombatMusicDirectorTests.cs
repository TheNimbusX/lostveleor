using System;
using Game.View;
using NUnit.Framework;
using Director = Game.View.CombatMusicDirector;
using Mood = Game.View.CombatMusicDirector.Mood;
using Place = Game.View.CombatMusicDirector.Place;
using Reason = Game.View.CombatMusicDirector.Reason;
using Status = Game.View.CombatMusicDirector.Status;
using Track = Game.View.CombatMusicDirector.Track;

public sealed class CombatMusicDirectorTests
{
    private const float Frame = 1f / 60f;
    private const float ClipSeconds = 150f;

    private static Director NewDirector(float clipSeconds = ClipSeconds)
    {
        var director = new Director();
        for (int t = 0; t < Director.TrackCount; t++) director.SetClipLength((Track)t, clipSeconds);
        return director;
    }

    private static Director.Signals Run(int depth, int awake = 0, int run = 1, bool elite = false, bool boss = false)
        => new Director.Signals
        {
            Place = Place.Run, RunNumber = run, Depth = depth, AwakeHostiles = awake, Elite = elite, Boss = boss,
        };

    /// <summary>Кадры по 1/60 с; игровое время идёт вместе с реальным, если не сказано иначе.</summary>
    private static void Play(Director director, Director.Signals signals, float seconds, bool gameTime = true)
    {
        int frames = (int)Math.Round(seconds / Frame);
        for (int i = 0; i < frames; i++) director.Update(in signals, Frame, gameTime ? Frame : 0f);
    }

    /// <summary>Забег 1, арена depth: затишье и бой с проснувшимися врагами.</summary>
    private static Director InCombat(int depth, float seconds = 5f)
    {
        Director director = NewDirector();
        Play(director, Run(depth), 3f);
        Play(director, Run(depth, awake: 3), seconds);
        return director;
    }

    [Test]
    public void TableRowsFollowTheTrackOrderAndResourceNames()
    {
        string[] names = { "Forest_Lull", "Forest_Normal_A", "Forest_Normal_B", "Forest_Hard", "Forest_Elite", "Forest_Boss" };
        Assert.That(Director.Tracks.Length, Is.EqualTo(Director.TrackCount));
        for (int t = 0; t < Director.TrackCount; t++)
        {
            Assert.That(Director.Tracks[t].Track, Is.EqualTo((Track)t));
            Assert.That(Director.Tracks[t].Resource, Is.EqualTo(names[t]));
            Assert.That(Director.Tracks[t].Gain, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
        }
        Assert.That(Director.Folder, Is.EqualTo("Audio/Music/Forest"));
    }

    [Test]
    public void LoopCrossfadesAreTheOnesTheSeamsWereMeasuredWith()
    {
        // Замер швов при нарезке (лес — 120 BPM, такт 2 с; босс — 8 долей по 0,4616 с).
        // Запасные 3 с сдвигают гармонию на полтакта, а у босса дают флэм в полдоли.
        float[] crossfade = { 2f, 2f, 4f, 4f, 2f, 3.6928f };
        for (int t = 0; t < Director.TrackCount; t++)
        {
            Assert.That(Director.Tracks[t].LoopCrossfade, Is.EqualTo(crossfade[t]), Director.Tracks[t].Resource);
            Assert.That(Director.Tracks[t].LoopStart, Is.Zero, Director.Tracks[t].Resource);
            Assert.That(Director.Tracks[t].LoopEnd, Is.Zero, Director.Tracks[t].Resource);
        }
    }

    [Test]
    public void ArenaTrackFollowsBossThenEliteThenHardThenAlternatingNormal()
    {
        Assert.That(Director.ChooseTrack(1, false, false), Is.EqualTo(Track.NormalA));
        Assert.That(Director.ChooseTrack(2, false, false), Is.EqualTo(Track.NormalB));
        Assert.That(Director.ChooseTrack(3, false, false), Is.EqualTo(Track.NormalA));
        Assert.That(Director.ChooseTrack(4, false, false), Is.EqualTo(Track.NormalB));
        Assert.That(Director.ChooseTrack(5, false, false), Is.EqualTo(Track.NormalA));
        Assert.That(Director.ChooseTrack(6, false, false), Is.EqualTo(Track.NormalB));
        Assert.That(Director.ChooseTrack(7, false, false), Is.EqualTo(Track.Hard));
        Assert.That(Director.ChooseTrack(8, false, false), Is.EqualTo(Track.Hard));
        Assert.That(Director.ChooseTrack(5, false, true), Is.EqualTo(Track.Elite), "элита на А5 — важнее обычной");
        Assert.That(Director.ChooseTrack(7, false, true), Is.EqualTo(Track.Elite), "элита важнее тяжёлой");
        Assert.That(Director.ChooseTrack(9, true, false), Is.EqualTo(Track.Boss));
        Assert.That(Director.ChooseTrack(9, true, true), Is.EqualTo(Track.Boss), "босс важнее всего");
    }

    [Test]
    public void OddAndEvenArenasAlternateTheNormalThemesInPlay()
    {
        Director director = NewDirector();
        for (int depth = 1; depth <= 6; depth++)
        {
            Play(director, Run(depth), 1f);
            Play(director, Run(depth, awake: 2), 2f);
            Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
            Assert.That(director.CurrentTrack, Is.EqualTo(depth % 2 == 1 ? Track.NormalA : Track.NormalB), "арена " + depth);
            Play(director, new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = depth, Cleared = true }, 4f);
        }
    }

    [Test]
    public void ArenaStartIsLullUntilTheFirstEnemyWakes()
    {
        Director director = NewDirector();
        Play(director, Run(1), 2.5f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Lull));
        Assert.That(director.LastTransition.Reason, Is.EqualTo(Reason.ArenaStart));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.SilenceToLullFade));

        Play(director, Run(1, awake: 1), Frame);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.NormalA));
        Assert.That(director.LastTransition.Reason, Is.EqualTo(Reason.HostileAwake));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.LullToCombatFade));
    }

    [Test]
    public void FreshTrackStartsItsSourceFromZeroWhileTheClockHasAlreadyMoved()
    {
        Director director = NewDirector();
        Play(director, Run(1), Frame);
        Assert.That(director.VoiceStartSerial(Track.Lull, 0), Is.EqualTo(1));
        Assert.That(director.Position(Track.Lull), Is.EqualTo(Frame).Within(1e-5f), "учёт уже шагнул кадр");
        Assert.That(director.VoiceStartPosition(Track.Lull, 0), Is.Zero, "а поток стартует с нуля, без перемотки");
    }

    [Test]
    public void ShortGapBetweenWavesKeepsTheCombatTrack()
    {
        Director director = InCombat(2);
        int transitions = director.TransitionCount;

        Play(director, Run(2), 1.5f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.NormalB));
        Play(director, Run(2, awake: 4), 2f);
        Assert.That(director.TransitionCount, Is.EqualTo(transitions), "полторы секунды между волнами не меняют музыку");
        Assert.That(director.LevelOf(Track.NormalB), Is.EqualTo(1f));
        Assert.That(director.LevelOf(Track.Lull), Is.EqualTo(0f));
    }

    [Test]
    public void LullComesOnlyAfterThreeQuietSecondsAndCombatReturnsWhenAnEnemyWakes()
    {
        Director director = InCombat(3);
        Play(director, Run(3), 2.9f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
        Play(director, Run(3), .2f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Lull));
        Assert.That(director.LastTransition.Reason, Is.EqualTo(Reason.QuietBetweenWaves));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.CombatToLullFade));

        Play(director, Run(3, awake: 2), Frame);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.NormalA), "снова бой этой же арены");
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.LullToCombatFade));
    }

    [Test]
    public void QuietIsMeasuredInGameTimeSoPauseDoesNotEndTheFight()
    {
        Director director = InCombat(1);
        Play(director, Run(1), 10f, gameTime: false);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
        Assert.That(director.QuietSeconds, Is.EqualTo(0f));
    }

    [Test]
    public void ClearedArenaAndRewardScreenGoToLullAtOnce()
    {
        Director director = InCombat(4);
        var cleared = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 4, Cleared = true };
        Play(director, cleared, Frame);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Lull));
        Assert.That(director.LastTransition.Reason, Is.EqualTo(Reason.ArenaCleared));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.CombatToLullFade));

        int transitions = director.TransitionCount;
        var reward = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 4, Choosing = true };
        Play(director, reward, 2f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Lull));
        Assert.That(director.TransitionCount, Is.EqualTo(transitions), "затишье не перезапускается экраном награды");
    }

    [Test]
    public void AwakeCacheGuardKeepsCombatOnTheWayToTheExit()
    {
        Director director = InCombat(2);
        var guard = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 2, Cleared = true, AwakeHostiles = 1 };
        Play(director, guard, 1f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
    }

    [Test]
    public void EachTransitionUsesItsOwnFadeLength()
    {
        Assert.That(Director.FadeSeconds(Mood.Lull, Mood.Combat, false), Is.EqualTo(1f));
        Assert.That(Director.FadeSeconds(Mood.Combat, Mood.Lull, false), Is.EqualTo(3f));
        Assert.That(Director.FadeSeconds(Mood.Combat, Mood.Combat, false), Is.EqualTo(2f));
        Assert.That(Director.FadeSeconds(Mood.Combat, Mood.Silence, false), Is.EqualTo(2f));
        Assert.That(Director.FadeSeconds(Mood.Lull, Mood.Silence, false), Is.EqualTo(2f));
        Assert.That(Director.FadeSeconds(Mood.Combat, Mood.Silence, true), Is.EqualTo(1.5f));
    }

    [Test]
    public void LullToCombatCrossfadeTakesOneSecond()
    {
        Director director = NewDirector();
        Play(director, Run(1), 3f);
        Play(director, Run(1, awake: 1), .5f);
        Assert.That(director.LevelOf(Track.NormalA), Is.EqualTo(.5f).Within(.03f));
        Assert.That(director.LevelOf(Track.Lull), Is.EqualTo(.5f).Within(.03f));
        Play(director, Run(1, awake: 1), .55f);
        Assert.That(director.LevelOf(Track.NormalA), Is.EqualTo(1f));
        Assert.That(director.LevelOf(Track.Lull), Is.EqualTo(0f));
        Assert.That(director.StatusOf(Track.Lull), Is.EqualTo(Status.Paused));
    }

    [Test]
    public void CombatToLullFadeTakesThreeSeconds()
    {
        Director director = InCombat(1);
        var cleared = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 1, Cleared = true };
        Play(director, cleared, 2.9f);
        Assert.That(director.LevelOf(Track.NormalA), Is.GreaterThan(0f));
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Playing));
        Play(director, cleared, .15f);
        Assert.That(director.LevelOf(Track.NormalA), Is.EqualTo(0f));
        Assert.That(director.LevelOf(Track.Lull), Is.EqualTo(1f));
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Paused));
    }

    [Test]
    public void CombatToAnotherCombatTrackTakesTwoSeconds()
    {
        Director director = InCombat(5);
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.NormalA));
        // Элита встречи узнаётся посреди драки (стенд без шаблона) — бой в бой.
        Play(director, Run(5, awake: 2, elite: true), Frame);
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.Elite));
        Assert.That(director.LastTransition.Reason, Is.EqualTo(Reason.TrackChanged));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.CombatToCombatFade));
        Play(director, Run(5, awake: 2, elite: true), 1.9f);
        Assert.That(director.LevelOf(Track.NormalA), Is.GreaterThan(0f));
        Play(director, Run(5, awake: 2, elite: true), .15f);
        Assert.That(director.LevelOf(Track.NormalA), Is.EqualTo(0f));
        Assert.That(director.LevelOf(Track.Elite), Is.EqualTo(1f));
    }

    [Test]
    public void BossArenaPlaysTheBossTrackAfterItsLull()
    {
        Director director = NewDirector();
        Play(director, Run(9, boss: true), 3f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Lull));
        Play(director, Run(9, awake: 1, boss: true), Frame);
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.Boss));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.LullToCombatFade));
    }

    [Test]
    public void EliteArenaPlaysTheEliteTrackFromItsFirstFightAndHardArenasTheHardOne()
    {
        Director director = NewDirector();
        Play(director, Run(6, elite: true), 3f);
        Play(director, Run(6, awake: 1, elite: true), Frame);
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.Elite));
        Play(director, new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 6, Cleared = true }, 4f);
        Play(director, Run(7), 1f);
        Play(director, Run(7, awake: 1), Frame);
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.Hard));
    }

    [Test]
    public void HeroDeathFadesToSilenceInOneAndAHalfSeconds()
    {
        Director director = InCombat(3);
        var death = new Director.Signals { Place = Place.Summary, HeroDead = true };
        Play(director, death, Frame);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Silence));
        Assert.That(director.LastTransition.Reason, Is.EqualTo(Reason.HeroDied));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.DeathToSilenceFade));
        Play(director, death, 1.4f);
        Assert.That(director.LevelOf(Track.NormalA), Is.GreaterThan(0f));
        Play(director, death, .1f);
        Assert.That(director.LevelOf(Track.NormalA), Is.EqualTo(0f));
        // Итоги — вне забега: замолчавший трек не держит поток на паузе.
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Stopped));
    }

    [Test]
    public void DeathInsideTheRunHoldsThePositionUntilTheRunIsOver()
    {
        Director director = InCombat(3, seconds: 12f);
        var dead = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 3, HeroDead = true, AwakeHostiles = 2 };
        Play(director, dead, 1.6f);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Paused));
        Assert.That(director.Position(Track.NormalA), Is.GreaterThan(12f));

        Play(director, new Director.Signals { Place = Place.Summary, HeroDead = true }, Frame);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Stopped));
        Assert.That(director.Position(Track.NormalA), Is.Zero);
        Assert.That(director.VoiceActive(Track.NormalA, 0), Is.False);
    }

    [Test]
    public void LeavingTheRunReleasesEveryFadedTrack()
    {
        Director director = InCombat(2);
        Play(director, new Director.Signals { Place = Place.Camp }, 2.1f);
        for (int t = 0; t < Director.TrackCount; t++)
        {
            Assert.That(director.StatusOf((Track)t), Is.EqualTo(Status.Stopped), ((Track)t).ToString());
            Assert.That(director.VoiceActive((Track)t, 0), Is.False);
            Assert.That(director.VoiceActive((Track)t, 1), Is.False);
        }
    }

    [Test]
    public void DeadHeroInsideTheRunIsSilenceToo()
    {
        Director director = InCombat(2);
        Play(director, new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 2, HeroDead = true, AwakeHostiles = 3 }, Frame);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Silence));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.DeathToSilenceFade));
    }

    [Test]
    public void LeavingTheRunForCampOrMenuFadesToSilenceInTwoSeconds()
    {
        Director director = InCombat(2);
        Play(director, new Director.Signals { Place = Place.Camp }, Frame);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Silence));
        Assert.That(director.LastTransition.Reason, Is.EqualTo(Reason.Camp));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.ToSilenceFade));
        Play(director, new Director.Signals { Place = Place.Camp }, 2.05f);
        for (int t = 0; t < Director.TrackCount; t++)
        {
            Assert.That(director.LevelOf((Track)t), Is.EqualTo(0f));
            Assert.That(director.VoiceVolume((Track)t, 0), Is.EqualTo(0f));
        }

        Director menu = NewDirector();
        Play(menu, new Director.Signals { Place = Place.Menu }, 1f);
        Assert.That(menu.CurrentMood, Is.EqualTo(Mood.Silence));
        Assert.That(menu.TransitionCount, Is.Zero, "меню и лагерь молчат с самого начала");
    }

    [Test]
    public void RunMusicWaitsUntilCampOrMenuThemeHasFaded()
    {
        Director director = NewDirector();
        var entering = Run(1);
        entering.OtherMusicAudible = true;
        Play(director, entering, 1f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Silence));
        Assert.That(director.TransitionCount, Is.Zero);
        Play(director, Run(1), Frame);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Lull));
        Assert.That(director.LastTransition.Fade, Is.EqualTo(Director.SilenceToLullFade));
    }

    [Test]
    public void FadedTrackPausesAndResumesWhereItLeftOff()
    {
        Director director = InCombat(1, seconds: 20f);
        float playedAt = director.Position(Track.NormalA);
        Assert.That(playedAt, Is.EqualTo(20f).Within(.1f));
        int serial = director.VoiceStartSerial(Track.NormalA, 0);

        // Зачистка: тема уходит за 3 с и замирает на паузе, позиция держится.
        var cleared = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 1, Cleared = true };
        Play(director, cleared, 3.1f);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Paused));
        float pausedAt = director.Position(Track.NormalA);
        Assert.That(pausedAt, Is.EqualTo(playedAt + 3f).Within(.1f));
        Play(director, cleared, 30f);
        Assert.That(director.Position(Track.NormalA), Is.EqualTo(pausedAt));
        Assert.That(director.VoiceVolume(Track.NormalA, 0), Is.EqualTo(0f));

        // А3 — снова тема A: продолжается с того же места, без нового старта.
        Play(director, Run(3), 1f);
        Play(director, Run(3, awake: 2), Frame);
        Assert.That(director.CurrentTrack, Is.EqualTo(Track.NormalA));
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Playing));
        Assert.That(director.VoiceStartSerial(Track.NormalA, 0), Is.EqualTo(serial), "снимается с паузы, а не стартует заново");
        Assert.That(director.Position(Track.NormalA), Is.EqualTo(pausedAt + Frame).Within(.001f));

        // Затишье тоже держало своё место, пока шёл бой.
        Assert.That(director.StatusOf(Track.Lull), Is.EqualTo(Status.Playing));
    }

    [Test]
    public void SourcePositionOverridesFrameClock()
    {
        Director director = InCombat(1, seconds: 10f);
        director.SyncVoicePosition(Track.NormalA, 0, 42f);
        Assert.That(director.Position(Track.NormalA), Is.EqualTo(42f));

        // Замолчавший трек чужие позиции не принимает: он стоит на паузе.
        Play(director, new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 1, Cleared = true }, 3.1f);
        float paused = director.Position(Track.NormalA);
        director.SyncVoicePosition(Track.NormalA, 0, 99f);
        Assert.That(director.Position(Track.NormalA), Is.EqualTo(paused));
    }

    [Test]
    public void NewRunStartsEveryTrackFromTheBeginning()
    {
        Director director = InCombat(1, seconds: 20f);
        // Смерть в забеге: трек на паузе с позицией. Новый забег сразу, без итогов (стенд).
        Play(director, new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 1, HeroDead = true }, 2f);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Paused));
        int serial = director.VoiceStartSerial(Track.NormalA, 0);

        Play(director, Run(1, run: 2), Frame);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Stopped));
        Assert.That(director.Position(Track.NormalA), Is.EqualTo(0f));

        Play(director, Run(1, run: 2), 1f);
        Play(director, Run(1, awake: 1, run: 2), Frame);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Playing));
        Assert.That(director.VoiceStartSerial(Track.NormalA, 0), Is.EqualTo(serial + 1), "новый старт с начала");
        Assert.That(director.Position(Track.NormalA), Is.EqualTo(Frame).Within(.001f));
    }

    [Test]
    public void QuickRepeatLetsAStillFadingTrackFinishBeforeItRewinds()
    {
        Director director = InCombat(1, seconds: 20f);
        Play(director, new Director.Signals { Place = Place.Summary }, .5f);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Playing));

        Play(director, Run(1, run: 2), .5f);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Playing), "догасает без щелчка");
        Assert.That(director.Position(Track.NormalA), Is.GreaterThan(20f));
        Play(director, Run(1, run: 2), 1.2f);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Stopped));
        Assert.That(director.Position(Track.NormalA), Is.EqualTo(0f));
    }

    [Test]
    public void LoopVoiceIsQueuedALeadAheadAndEntersAtTheLoopPoint()
    {
        const float clip = 30f;
        Director director = NewDirector(clip);
        Play(director, Run(1), 3f);
        Play(director, Run(1, awake: 1), 1f);
        Director.LoopWindow(Track.NormalA, clip, out float start, out float end, out float crossfade);
        float loopAt = Director.LoopPoint(Track.NormalA, clip);
        Assert.That(loopAt, Is.EqualTo(end - crossfade));

        // До очереди второй голос не заведён.
        float untilQueue = loopAt - Director.LoopScheduleLead - director.Position(Track.NormalA);
        Play(director, Run(1, awake: 1), untilQueue - .1f);
        Assert.That(director.VoiceActive(Track.NormalA, 1), Is.False);

        // В очереди: заведён с LoopStart, молчит, отсчёт до старта — сколько ведущему до точки петли.
        Play(director, Run(1, awake: 1), .2f);
        Assert.That(director.VoiceActive(Track.NormalA, 1), Is.True);
        Assert.That(director.VoicePending(Track.NormalA, 1), Is.True);
        Assert.That(director.VoiceStartSerial(Track.NormalA, 1), Is.EqualTo(1));
        Assert.That(director.VoiceStartPosition(Track.NormalA, 1), Is.EqualTo(start), "источник встаёт в очередь на LoopStart");
        Assert.That(director.VoiceVolume(Track.NormalA, 1), Is.Zero);
        Assert.That(director.PrimaryVoice(Track.NormalA), Is.Zero);
        Assert.That(director.LoopCount, Is.Zero);
        float countdown = start - director.VoicePosition(Track.NormalA, 1);
        Assert.That(countdown, Is.EqualTo(loopAt - director.Position(Track.NormalA)).Within(1e-3f));
        Assert.That(countdown, Is.GreaterThan(Director.LoopScheduleLead - .2f).And.LessThanOrEqualTo(Director.LoopScheduleLead));
        // Источник в очереди PlayScheduled стоит на LoopStart — отсчёт это не сбивает.
        director.SyncVoicePosition(Track.NormalA, 1, start);
        Assert.That(start - director.VoicePosition(Track.NormalA, 1), Is.EqualTo(countdown));

        Play(director, Run(1, awake: 1), countdown - .05f);
        Assert.That(director.VoicePending(Track.NormalA, 1), Is.True, "до точки петли голос ещё молчит");
        Assert.That(director.LoopCount, Is.Zero);

        Play(director, Run(1, awake: 1), .1f);
        Assert.That(director.VoicePending(Track.NormalA, 1), Is.False);
        Assert.That(director.LoopCount, Is.EqualTo(1));
        Assert.That(director.LastLoopTrack, Is.EqualTo(Track.NormalA));
        Assert.That(director.PrimaryVoice(Track.NormalA), Is.EqualTo(1));
        Assert.That(director.VoiceActive(Track.NormalA, 0), Is.True);
        Assert.That(director.VoiceActive(Track.NormalA, 1), Is.True);
        Assert.That(director.VoiceStartSerial(Track.NormalA, 1), Is.EqualTo(1), "очередь и вход — один старт");
        Assert.That(director.VoicePosition(Track.NormalA, 1), Is.EqualTo(start).Within(.1f));
        // Вход ровно в точке петли: старый голос там же, где новый, минус окно петли.
        Assert.That(director.VoicePosition(Track.NormalA, 0) - director.VoicePosition(Track.NormalA, 1),
            Is.EqualTo(loopAt - start).Within(1e-3f));

        // Середина петли: равная мощность, оба голоса на ~0.707.
        Play(director, Run(1, awake: 1), crossfade * .5f - .1f);
        float a = director.VoiceVolume(Track.NormalA, 0), b = director.VoiceVolume(Track.NormalA, 1);
        Assert.That(a, Is.EqualTo(b).Within(.03f));
        float gain = Director.Tracks[(int)Track.NormalA].Gain;
        Assert.That(a * a + b * b, Is.EqualTo(gain * gain).Within(.02f));

        Play(director, Run(1, awake: 1), crossfade * .5f + .1f);
        Assert.That(director.VoiceActive(Track.NormalA, 0), Is.False, "старый голос отыграл и остановлен");
        Assert.That(director.VoiceActive(Track.NormalA, 1), Is.True);
        Assert.That(director.VoiceVolume(Track.NormalA, 1), Is.EqualTo(gain).Within(.001f));
    }

    [Test]
    public void TrackThatFadesOutWhileItsLoopVoiceIsQueuedRequeuesAfterResume()
    {
        const float clip = 30f;
        Director director = NewDirector(clip);
        float loopAt = Director.LoopPoint(Track.NormalA, clip);
        Play(director, Run(1), 3f);
        // Зачистка за 3,5 с до точки петли: 3 с затухания кончаются внутри окна очереди.
        Play(director, Run(1, awake: 1), loopAt - 3.5f);
        var cleared = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 1, Cleared = true };
        Play(director, cleared, 2.8f);
        Assert.That(director.VoicePending(Track.NormalA, 1), Is.True, "в очереди, пока трек ещё гаснет");
        Play(director, cleared, .5f);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Paused));
        Assert.That(director.VoiceActive(Track.NormalA, 1), Is.False, "очередь снята: источник остановлен");
        Assert.That(director.VoicePending(Track.NormalA, 1), Is.False);
        Assert.That(director.LoopCount, Is.Zero);
        float paused = director.Position(Track.NormalA);
        Assert.That(loopAt - paused, Is.InRange(.1f, Director.LoopScheduleLead));

        // А3 — та же тема: с того же места, и петля снова в очереди с верным отсчётом.
        Play(director, Run(3), 1f);
        Play(director, Run(3, awake: 1), Frame);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Playing));
        Assert.That(director.VoicePending(Track.NormalA, 1), Is.True);
        Assert.That(director.VoiceStartSerial(Track.NormalA, 1), Is.EqualTo(2), "новая очередь — новый старт источника");
        Director.LoopWindow(Track.NormalA, clip, out float start, out _, out _);
        Assert.That(start - director.VoicePosition(Track.NormalA, 1),
            Is.EqualTo(loopAt - director.Position(Track.NormalA)).Within(1e-3f));
        Play(director, Run(3, awake: 1), loopAt - paused + .05f);
        Assert.That(director.LoopCount, Is.EqualTo(1));
        Assert.That(director.PrimaryVoice(Track.NormalA), Is.EqualTo(1));
    }

    [Test]
    public void FrameThatJumpsPastTheLoopPointStartsTheSecondVoiceOnTheSameBeat()
    {
        const float clip = 30f;
        Director director = NewDirector(clip);
        Play(director, Run(1), 3f);
        Play(director, Run(1, awake: 1), 1f);
        float loopAt = Director.LoopPoint(Track.NormalA, clip);
        // Провал кадра: с 26 с до точки петли + 0,25 с одним шагом (загрузка, сворачивание окна).
        director.SyncVoicePosition(Track.NormalA, 0, loopAt - 1.5f);
        var fight = Run(1, awake: 1);
        director.Update(in fight, 1.75f, 1.75f);
        Director.LoopWindow(Track.NormalA, clip, out float start, out _, out float crossfade);
        Assert.That(director.LoopCount, Is.EqualTo(1));
        Assert.That(director.VoicePending(Track.NormalA, 1), Is.False);
        Assert.That(director.VoicePosition(Track.NormalA, 1), Is.EqualTo(start + .25f).Within(1e-3f), "та же доля, что у старого голоса");
        Assert.That(director.VoiceStartPosition(Track.NormalA, 1), Is.EqualTo(start + .25f).Within(1e-3f), "источник — с той же доли");
        Assert.That(director.VoicePosition(Track.NormalA, 0) - director.VoicePosition(Track.NormalA, 1),
            Is.EqualTo(loopAt - start).Within(1e-3f));
        float a = director.VoiceVolume(Track.NormalA, 0), b = director.VoiceVolume(Track.NormalA, 1);
        float gain = Director.Tracks[(int)Track.NormalA].Gain;
        Assert.That(a * a + b * b, Is.EqualTo(gain * gain).Within(.01f), "перекрёстная петля уже на .25 / crossfade");
        Assert.That(b, Is.GreaterThan(0f).And.LessThan(a));
        Assert.That(crossfade, Is.GreaterThan(.25f));
    }

    [Test]
    public void TrackPausedMidLoopResumesBothVoices()
    {
        // Своя строка затишья: петля в 3 с дольше перехода в бой, что бы ни стояло в таблице.
        Director.TrackInfo authored = Director.Tracks[(int)Track.Lull];
        Director.Tracks[(int)Track.Lull] = new Director.TrackInfo(Track.Lull, authored.Resource, authored.Gain);
        try { PauseMidLoop(); }
        finally { Director.Tracks[(int)Track.Lull] = authored; }
    }

    private static void PauseMidLoop()
    {
        const float clip = 30f;
        Director director = NewDirector(clip);
        Director.LoopWindow(Track.Lull, clip, out _, out float end, out float crossfade);
        // Затишье доигрывает до петли и на её середине уходит в бой (1 с).
        Play(director, Run(1), end - crossfade + crossfade * .2f);
        Assert.That(director.LoopCount, Is.EqualTo(1));
        Play(director, Run(1, awake: 1), 1.05f);
        Assert.That(director.StatusOf(Track.Lull), Is.EqualTo(Status.Paused));
        Assert.That(director.VoiceActive(Track.Lull, 0), Is.True);
        Assert.That(director.VoiceActive(Track.Lull, 1), Is.True);
        float first = director.VoicePosition(Track.Lull, 0), second = director.VoicePosition(Track.Lull, 1);
        int firstSerial = director.VoiceStartSerial(Track.Lull, 0), secondSerial = director.VoiceStartSerial(Track.Lull, 1);

        var cleared = new Director.Signals { Place = Place.Run, RunNumber = 1, Depth = 1, Cleared = true };
        Play(director, cleared, .5f);
        Assert.That(director.StatusOf(Track.Lull), Is.EqualTo(Status.Playing));
        Assert.That(director.VoicePosition(Track.Lull, 0), Is.EqualTo(first + .5f).Within(.01f));
        Assert.That(director.VoicePosition(Track.Lull, 1), Is.EqualTo(second + .5f).Within(.01f));
        Assert.That(director.VoiceStartSerial(Track.Lull, 0), Is.EqualTo(firstSerial));
        Assert.That(director.VoiceStartSerial(Track.Lull, 1), Is.EqualTo(secondSerial));

        Play(director, cleared, crossfade);
        Assert.That(director.VoiceActive(Track.Lull, 0), Is.False, "петля доиграла после паузы");
        Assert.That(director.LoopCount, Is.EqualTo(1));
    }

    [Test]
    public void ShortClipClampsTheLoopCrossfade()
    {
        Director.LoopWindow(Track.Lull, 4f, out float start, out float end, out float crossfade);
        Assert.That(end, Is.EqualTo(4f));
        Assert.That(start, Is.LessThanOrEqualTo(end * .5f));
        Assert.That(crossfade, Is.LessThanOrEqualTo((end - start) * .5f));
        Director.LoopWindow(Track.Lull, 0f, out _, out _, out crossfade);
        Assert.That(crossfade, Is.GreaterThan(0f), "без клипа — не делить на ноль");
    }

    [Test]
    public void MissingClipLeavesTheTrackSilentButDecisionsGoOn()
    {
        var director = new Director();
        Play(director, Run(1), 3f);
        Play(director, Run(1, awake: 1), 1f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Stopped));
        Assert.That(director.VoiceVolume(Track.NormalA, 0), Is.EqualTo(0f));

        // Клип доехал позже — трек начинается в своей огибающей.
        director.SetClipLength(Track.NormalA, ClipSeconds);
        Assert.That(director.StatusOf(Track.NormalA), Is.EqualTo(Status.Playing));
    }

    [Test]
    public void PauseMenuDucksTheMusicAndReleasesIt()
    {
        Director director = InCombat(1);
        float gain = Director.Tracks[(int)Track.NormalA].Gain;
        Assert.That(director.VoiceVolume(Track.NormalA, 0), Is.EqualTo(gain).Within(.001f));

        var paused = Run(1, awake: 3);
        paused.Paused = true;
        int transitions = director.TransitionCount;
        Play(director, paused, 2f, gameTime: false);
        Assert.That(director.VoiceVolume(Track.NormalA, 0), Is.EqualTo(gain * Director.PauseDuck).Within(.01f));
        Assert.That(director.TransitionCount, Is.EqualTo(transitions), "пауза не меняет настроение");

        Play(director, Run(1, awake: 3), 2f);
        Assert.That(director.VoiceVolume(Track.NormalA, 0), Is.EqualTo(gain).Within(.01f));
    }

    [Test]
    public void TransitionsWorkWithNoFrameTimeAtAll()
    {
        Director director = NewDirector();
        var signals = Run(1, awake: 1);
        director.Update(in signals, 0f, 0f);
        Assert.That(director.CurrentMood, Is.EqualTo(Mood.Combat));
        Assert.That(float.IsNaN(director.LevelOf(Track.NormalA)), Is.False);
        Assert.That(float.IsNaN(director.VoiceVolume(Track.NormalA, 0)), Is.False);
    }

    [Test]
    public void UpdateDoesNotAllocate()
    {
        Director director = InCombat(1);
        var quiet = Run(1);
        var fight = Run(1, awake: 2);
        // Прогрев: те же ветки (бой, тишина между волнами, затишье, петли) до замера.
        Churn(director, quiet, fight, 12000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Churn(director, quiet, fight, 12000);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    private static void Churn(Director director, Director.Signals quiet, Director.Signals fight, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (i % 600 < 300) director.Update(in fight, Frame, Frame);
            else director.Update(in quiet, Frame, Frame);
            for (int t = 0; t < Director.TrackCount; t++) director.VoiceVolume((Track)t, i & 1);
        }
    }

    [Test]
    public void ReasonKeysAreStableForTheCaptureLog()
    {
        Assert.That(Director.KeyOf(Reason.QuietBetweenWaves), Is.EqualTo("quiet-3s"));
        Assert.That(Director.KeyOf(Reason.HostileAwake), Is.EqualTo("hostile-awake"));
        Assert.That(Director.KeyOf(Reason.HeroDied), Is.EqualTo("hero-died"));
        foreach (Reason reason in Enum.GetValues(typeof(Reason)))
            Assert.That(Director.KeyOf(reason), Is.Not.EqualTo("unknown"));
    }
}
