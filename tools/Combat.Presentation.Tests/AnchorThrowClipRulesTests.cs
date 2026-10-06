using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Бросок якоря (03.10): кадр клипа по тикам Sim — как retime/layouts timing.json клипов
// (ART/characters/pelag/anchor-throw-2026-10-03/animation), клипы и стыки — как в Blender: между
// тиками кадр идёт по цепочке Throw→Fly→Yank→Haul→Catch, смена клипа — только на шве (та же поза).
// Сроки удержания и выхода — числа Sim.
public sealed class AnchorThrowClipRulesTests
{
    private static JsonElement Timing()
    {
        string path = Path.Combine(RepoRoot.Path, "ART", "characters", "pelag", "anchor-throw-2026-10-03", "animation", "timing.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    /// <summary>(клип, кадр) из timing.json → точка цепочки при возврате R.</summary>
    private static float Chain(string clip, float frame, int returnTicks)
    {
        switch (clip)
        {
            case "Throw": return frame;
            case "Fly": return PelagAnchorThrowClipRules.FlyStart + frame;
            case "Yank": return PelagAnchorThrowClipRules.YankStart + frame;
            case "Haul": return PelagAnchorThrowClipRules.HaulStart + frame - PelagAnchorThrowClipRules.HaulEntry(returnTicks);
            case "Catch": return PelagAnchorThrowClipRules.CatchStart(returnTicks) + frame;
            default: throw new ArgumentException(clip);
        }
    }

    [Test]
    public void WindupMatchesTimingJsonRetime()
    {
        int tables = 0;
        foreach (JsonProperty table in Timing().GetProperty("retime").GetProperty("windup").EnumerateObject())
        {
            int w = int.Parse(table.Name);
            int j = 0;
            foreach (JsonElement frame in table.Value.EnumerateArray())
                Assert.That(PelagAnchorThrowClipRules.WindupChain(w, j), Is.EqualTo(frame.GetSingle()).Within(.002f), $"W={w} j={j++}");
            Assert.That(j, Is.EqualTo(w + 1), "W=" + w);
            tables++;
        }
        Assert.That(tables, Is.EqualTo(2), "замах 2 и 3 (курсор за спиной)");
    }

    [Test]
    public void FlightMatchesTimingJsonTable()
    {
        int tables = 0;
        foreach (JsonProperty table in Timing().GetProperty("retime").GetProperty("flight").GetProperty("table").EnumerateObject())
        {
            int f = int.Parse(table.Name);
            int u = 0;
            foreach (JsonElement key in table.Value.EnumerateArray())
            {
                float expected = Chain(key[0].GetString(), key[1].GetSingle(), 8);
                Assert.That(PelagAnchorThrowClipRules.FlightChain(f, u), Is.EqualTo(expected).Within(.002f), $"F={f} u={u}");
                u++;
            }
            Assert.That(u, Is.EqualTo(f + 2), "F=" + f);
            tables++;
        }
        Assert.That(tables, Is.EqualTo(Simulation.AnchorThrowMaxFlightTicks - Simulation.AnchorThrowMinFlightTicks + 1));
    }

    [Test]
    public void HaulMatchesTimingJsonTable()
    {
        int tables = 0;
        foreach (JsonProperty table in Timing().GetProperty("retime").GetProperty("haul").GetProperty("table").EnumerateObject())
        {
            int r = int.Parse(table.Name);
            int k = 2;
            foreach (JsonElement frame in table.Value.EnumerateArray())
                Assert.That(PelagAnchorThrowClipRules.HaulKey(r, k), Is.EqualTo(frame.GetSingle()).Within(.002f), $"R={r} k={k++}");
            Assert.That(k, Is.EqualTo(r + 1), "R=" + r);
            Assert.That(PelagAnchorThrowClipRules.HaulKey(r, 2), Is.EqualTo((float)PelagAnchorThrowClipRules.HaulEntry(r)),
                "вход в Haul — та же поза, что Yank 2");
            tables++;
        }
        Assert.That(tables, Is.EqualTo(Simulation.AnchorThrowMaxReturnTicks - Simulation.AnchorThrowMinReturnTicks + 1));
    }

    [Test]
    public void LayoutsMatchTimingJsonExamples()
    {
        var name = new Regex(@"W(\d+) F(\d+) R(\d+)");
        int layouts = 0;
        foreach (JsonProperty layout in Timing().GetProperty("layouts").GetProperty("examples").EnumerateObject())
        {
            Match m = name.Match(layout.Name);
            Assert.That(m.Success, Is.True, layout.Name);
            int w = int.Parse(m.Groups[1].Value), f = int.Parse(m.Groups[2].Value), r = int.Parse(m.Groups[3].Value);
            int release = w, taut = w + f + 1, catchTick = taut + r;
            foreach (JsonElement row in layout.Value.EnumerateArray())
            {
                int tick = row[0].GetInt32();
                float expected = Chain(row[1].GetString(), row[2].GetSingle(), r);
                float chain = PelagAnchorThrowClipRules.KeyChain(0, w, release, f, taut, r, catchTick, tick);
                Assert.That(chain, Is.EqualTo(expected).Within(.002f), $"{layout.Name} тик {tick}");
            }
            layouts++;
        }
        Assert.That(layouts, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void ClipsNamesFramesAndKeysMatchTimingJson()
    {
        JsonElement timing = Timing();
        JsonElement clips = timing.GetProperty("clips");
        foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
        {
            string file = PelagAnchorThrowClipRules.ClipName(clip);
            Assert.That(clips.TryGetProperty(file, out JsonElement entry), Is.True, file);
            Assert.That(PelagAnchorThrowClipRules.LastFrame(clip), Is.EqualTo(entry.GetProperty("frames").GetInt32() - 1), file);
            Assert.That(PelagAnchorThrowClipRules.StateName(clip), Is.EqualTo("AnchorThrow_" + PelagAnchorThrowClipRules.Suffix(clip) + "_v5"));
            Assert.That(PelagAnchorThrowClipRules.PhaseParameter(clip), Is.EqualTo("AnchorThrowPhase" + PelagAnchorThrowClipRules.Suffix(clip)));
        }
        JsonElement keys = timing.GetProperty("keys");
        Assert.That(keys.GetProperty("release").GetProperty("frame").GetSingle(), Is.EqualTo(PelagAnchorThrowClipRules.ReleaseFrame));
        Assert.That(keys.GetProperty("stow_handoff").GetProperty("frame").GetSingle(), Is.EqualTo(PelagAnchorThrowClipRules.StowHandoffFrame));
        Assert.That(keys.GetProperty("walk_from").GetProperty("frame").GetSingle(), Is.EqualTo(PelagAnchorThrowClipRules.WalkFromFrame));
        Assert.That(PelagAnchorThrowClipRules.HoldTicks, Is.EqualTo(Simulation.AnchorThrowHoldTicks));
        Assert.That(PelagAnchorThrowClipRules.ExitTicks, Is.EqualTo(Simulation.AnchorThrowExitTicks));
        Assert.That(PelagAnchorThrowClipRules.WalkFromFrame, Is.EqualTo(Simulation.AnchorThrowHoldTicks + Simulation.AnchorThrowExitLockedTicks));
        Assert.That(PelagAnchorThrowClipRules.BindReference, Is.EqualTo("Pelag_AN_AnchorThrowBind"));
    }

    [Test]
    public void ClipAt_SeamGoesToTheNextClip()
    {
        float frame;
        Assert.That(PelagAnchorThrowClipRules.ClipAt(0f, 8, out frame), Is.EqualTo(PelagAnchorThrowClip.Throw));
        Assert.That(PelagAnchorThrowClipRules.ClipAt(5f, 8, out frame), Is.EqualTo(PelagAnchorThrowClip.Fly));
        Assert.That(frame, Is.EqualTo(0f));
        Assert.That(PelagAnchorThrowClipRules.ClipAt(8f, 8, out frame), Is.EqualTo(PelagAnchorThrowClip.Yank));
        Assert.That(frame, Is.EqualTo(0f));
        Assert.That(PelagAnchorThrowClipRules.ClipAt(10f, 8, out frame), Is.EqualTo(PelagAnchorThrowClip.Haul));
        Assert.That(frame, Is.EqualTo(0f));
        Assert.That(PelagAnchorThrowClipRules.ClipAt(10f, 4, out frame), Is.EqualTo(PelagAnchorThrowClip.Haul));
        Assert.That(frame, Is.EqualTo(3f), "короткий возврат входит с кадра 3");
        Assert.That(PelagAnchorThrowClipRules.ClipAt(16f, 8, out frame), Is.EqualTo(PelagAnchorThrowClip.Catch));
        Assert.That(frame, Is.EqualTo(0f));
        Assert.That(PelagAnchorThrowClipRules.ClipAt(13f, 5, out frame), Is.EqualTo(PelagAnchorThrowClip.Catch));
        Assert.That(PelagAnchorThrowClipRules.ClipAt(99f, 8, out frame), Is.EqualTo(PelagAnchorThrowClip.Catch));
        Assert.That(frame, Is.EqualTo(9f));
    }

    /// <summary>
    /// Все раскладки Sim (W 2–3 × F 1–10 × R 4–8): ключи в тики выпуска, натяга, ловли и конца; цепочка не идёт
    /// назад; при показе 60 к/с (пол-тика) каждая смена клипа — шов (та же поза), без смешивания и скачка позы.
    /// </summary>
    [Test]
    public void AllSimLayouts_KeysMonotoneAndOnlySeams()
    {
        for (int w = 2; w <= 3; w++)
        for (int f = Simulation.AnchorThrowMinFlightTicks; f <= Simulation.AnchorThrowMaxFlightTicks; f++)
        for (int r = Simulation.AnchorThrowMinReturnTicks; r <= Simulation.AnchorThrowMaxReturnTicks; r++)
        {
            int release = w, taut = w + f + 1, catchTick = taut + r;
            string at = $"W{w} F{f} R{r}";
            float Key(int n) => PelagAnchorThrowClipRules.KeyChain(0, w, release, f, taut, r, catchTick, n);
            Assert.That(Key(0), Is.EqualTo(0f), at);
            Assert.That(Key(release), Is.EqualTo(PelagAnchorThrowClipRules.ReleaseFrame), at + " выпуск — Throw 2");
            Assert.That(Key(taut), Is.EqualTo(PelagAnchorThrowClipRules.YankStart), at + " натяг — Yank 0");
            Assert.That(Key(catchTick), Is.EqualTo(PelagAnchorThrowClipRules.CatchStart(r)), at + " ловля — Catch 0");
            Assert.That(Key(catchTick + 9), Is.EqualTo(PelagAnchorThrowClipRules.ChainEnd(r)), at + " конец — Catch 9");
            PelagAnchorThrowClip previous = PelagAnchorThrowClip.Throw;
            for (int half = 0; half <= 2 * (catchTick + 9); half++)
            {
                int n = half / 2;
                float c = half % 2 == 0 ? Key(n) : (Key(n) + Key(n + 1)) / 2f;
                Assert.That(Key(n + 1), Is.GreaterThanOrEqualTo(Key(n)), $"{at} тик {n}");
                PelagAnchorThrowClip clip = PelagAnchorThrowClipRules.ClipAt(c, r, out float frame);
                Assert.That(frame, Is.InRange(0f, (float)PelagAnchorThrowClipRules.LastFrame(clip)), $"{at} тик {n}");
                if (clip != previous)
                    Assert.That(PelagAnchorThrowClipRules.IsSeam(previous, clip), Is.True, $"{at} тик {n}: {previous}→{clip}");
                previous = clip;
            }
        }
    }

    [Test]
    public void ExitGlide_ScalesWithTheShift()
    {
        Assert.That(PelagAnchorThrowClipRules.ExitGlideSeconds(0f), Is.EqualTo(PelagAnchorThrowClipRules.ExitGlideMin));
        Assert.That(PelagAnchorThrowClipRules.ExitGlideSeconds(.66f), Is.EqualTo(.22f).Within(.001f));
        Assert.That(PelagAnchorThrowClipRules.ExitGlideSeconds(3f), Is.EqualTo(PelagAnchorThrowClipRules.ExitGlideMax));
    }
}
