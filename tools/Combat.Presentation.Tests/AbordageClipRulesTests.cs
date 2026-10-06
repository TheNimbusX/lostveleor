using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Абордаж v2 (02.10) и v3 (03.10): кадр клипа по тикам Sim — как throw_retime/throw_retime_turn/
// pull_retime/pull_short_retime timing.json, клипы и стыки — как в Blender, левая лодыжка опоры —
// та же точка, что у Шквала (поворот корня вокруг неё общий), сроки замаха/удержания/выхода/возврата —
// числа Sim, клип прибытия по форме — timing.json forms/contact.
public sealed class AbordageClipRulesTests
{
    private static JsonElement Timing()
    {
        string path = Path.Combine(RepoRoot.Path, "ART", "characters", "pelag", "abordage-2026-10-02", "animation", "timing.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static void AssertThrowTables(string table, int windup)
    {
        int tables = 0;
        foreach (JsonProperty row in Timing().GetProperty(table).EnumerateObject())
        {
            int hook = int.Parse(row.Name);
            int k = 0;
            foreach (JsonElement frame in row.Value.EnumerateArray())
            {
                Assert.That(PelagAbordageClipRules.ThrowFrame(windup, hook, k),
                    Is.EqualTo(frame.GetSingle()).Within(.002f), $"{table} A={hook} k={k}");
                k++;
            }
            Assert.That(k, Is.EqualTo(windup + hook + 1), table + " A=" + hook);
            tables++;
        }
        Assert.That(tables, Is.EqualTo(Simulation.AbordageMaxHookTicks - Simulation.AbordageMinHookTicks + 1));
    }

    [Test]
    public void ThrowFrameMatchesTimingJsonRetime_WindupThree_TurnWindupFour()
    {
        AssertThrowTables("throw_retime", PelagAbordageClipRules.WindupTicks);
        AssertThrowTables("throw_retime_turn", PelagAbordageClipRules.TurnWindupTicks);
    }

    [Test]
    public void PullFrameMatchesTimingJsonRetime()
    {
        int tables = 0;
        foreach (JsonProperty table in Timing().GetProperty("pull_retime").EnumerateObject())
        {
            int pull = int.Parse(table.Name);
            int k = 0;
            foreach (JsonElement frame in table.Value.EnumerateArray())
            {
                Assert.That(PelagAbordageClipRules.PullKey(pull, k), Is.EqualTo(frame.GetSingle()).Within(.002f), $"P={pull} k={k}");
                Assert.That(PelagAbordageClipRules.PullFrame(pull, k), Is.EqualTo(frame.GetSingle()).Within(.002f), $"P={pull} k={k}");
                k++;
            }
            Assert.That(k, Is.EqualTo(pull + 1), "P=" + pull);
            tables++;
        }
        Assert.That(tables, Is.EqualTo(Simulation.AbordageMaxPullTicks - Simulation.AbordageMinPullTicks + 1));
        // Без тяги (P = 1, в timing.json нет): натяг → контакт за тик.
        Assert.That(PelagAbordageClipRules.PullFrame(1, 0f), Is.EqualTo(0f));
        Assert.That(PelagAbordageClipRules.PullFrame(1, .5f), Is.EqualTo(6f).Within(1e-4f));
        Assert.That(PelagAbordageClipRules.PullFrame(1, 1f), Is.EqualTo(PelagAbordageClipRules.ContactFrame));
    }

    [Test]
    public void PullShortFrameMatchesTimingJson_AndTheChoiceIsFourToFive()
    {
        JsonElement timing = Timing();
        int tables = 0;
        foreach (JsonProperty table in timing.GetProperty("pull_short_retime").EnumerateObject())
        {
            int pull = int.Parse(table.Name);
            int k = 0;
            foreach (JsonElement frame in table.Value.EnumerateArray())
            {
                Assert.That(PelagAbordageClipRules.PullShortKey(pull, k), Is.EqualTo(frame.GetSingle()).Within(.002f), $"P={pull} k={k}");
                Assert.That(PelagAbordageClipRules.PullShortFrame(pull, k), Is.EqualTo(frame.GetSingle()).Within(.002f), $"P={pull} k={k}");
                k++;
            }
            Assert.That(k, Is.EqualTo(pull + 1), "P=" + pull);
            Assert.That(PelagAbordageClipRules.PullClip(pull), Is.EqualTo(PelagAbordageClip.PullShort), "P=" + pull);
            tables++;
        }
        Assert.That(tables, Is.EqualTo(PelagAbordageClipRules.ShortPullMaxTicks - PelagAbordageClipRules.ShortPullMinTicks + 1));
        Assert.That(timing.GetProperty("pull_choice").GetProperty("N").GetInt32(), Is.EqualTo(PelagAbordageClipRules.ShortPullMaxTicks));
        for (int pull = 1; pull <= Simulation.AbordageMaxPullTicks; pull++)
        {
            bool shortPull = pull >= 4 && pull <= 5;
            Assert.That(PelagAbordageClipRules.PullClip(pull),
                Is.EqualTo(shortPull ? PelagAbordageClip.PullShort : PelagAbordageClip.Pull), "P=" + pull);
            Assert.That(PelagAbordageClipRules.PullClip(pull, shortAvailable: false), Is.EqualTo(PelagAbordageClip.Pull), "без PullShort, P=" + pull);
        }
        // Нырок (кадр 2, поза 9) — всегда на целом тике; кадры 3, 4, 5 — снимки 10, 11, 12 Pull (взвод, кулак, контакт).
        foreach (int pull in new[] { 4, 5 })
        {
            Assert.That(PelagAbordageClipRules.PullShortKey(pull, pull - 3), Is.EqualTo(2f), "нырок, P=" + pull);
            for (int back = 0; back < 3; back++)
                Assert.That(PelagAbordageClipRules.PullShortKey(pull, pull - back) + 7f,
                    Is.EqualTo(PelagAbordageClipRules.PullKey(12, 12 - back)), $"P={pull} k={pull - back}");
        }
    }

    [Test]
    public void FramesNeverRunBackwards_AndContactIsOnTheArrivalTick()
    {
        foreach (int windup in new[] { PelagAbordageClipRules.WindupTicks, PelagAbordageClipRules.TurnWindupTicks })
            for (int hook = 1; hook <= 6; hook++)
            {
                float last = -1f;
                for (float k = 0f; k <= windup + hook + 1; k += .125f)
                {
                    float frame = PelagAbordageClipRules.ThrowFrame(windup, hook, k);
                    Assert.That(frame, Is.GreaterThanOrEqualTo(last - 1e-5f), $"Throw W={windup} A={hook} k={k}");
                    last = frame;
                }
                Assert.That(PelagAbordageClipRules.ThrowFrame(windup, hook, windup), Is.EqualTo(PelagAbordageClipRules.ReleaseFrame),
                    "выпуск в тик каста + W");
                Assert.That(PelagAbordageClipRules.ThrowFrame(windup, hook, windup + hook), Is.EqualTo(PelagAbordageClipRules.BiteFrame),
                    "натяг в тик зацепа");
            }
        for (int pull = 1; pull <= 12; pull++)
        {
            PelagAbordageClip clip = PelagAbordageClipRules.PullClip(pull);
            float last = -1f;
            for (float k = 0f; k <= pull; k += .125f)
            {
                float frame = PelagAbordageClipRules.PullClipFrame(clip, pull, k);
                Assert.That(frame, Is.GreaterThanOrEqualTo(last - 1e-5f), $"{clip} P={pull} k={k}");
                last = frame;
            }
            Assert.That(PelagAbordageClipRules.PullClipFrame(clip, pull, pull), Is.EqualTo((float)PelagAbordageClipRules.LastFrame(clip)),
                $"контакт {clip} P={pull}");
            // Клип формы с тика B+P−1: стык с кадром 11 Pull / 4 PullShort.
            float before = PelagAbordageClipRules.PullClipFrame(clip, pull, pull - 1);
            if (pull >= 2) Assert.That(before, Is.EqualTo(PelagAbordageClipRules.LastFrame(clip) - 1f), $"{clip} P={pull}: кадр перед ударом");
        }
        Assert.That(PelagAbordageClipRules.FormFrame(0f), Is.EqualTo(0f));
        Assert.That(PelagAbordageClipRules.FormFrame(PelagAbordageClipRules.FormLeadTicks), Is.EqualTo(PelagAbordageClipRules.FormContactFrame));
        Assert.That(PelagAbordageClipRules.FormFrame(99f), Is.EqualTo((float)PelagAbordageClipRules.LastFrame(PelagAbordageClip.Uppercut)));
    }

    [Test]
    public void ClipsMatchTimingJson_AndSimTimings()
    {
        Assert.That(PelagAbordageClipRules.WindupTicks, Is.EqualTo(Simulation.AbordageWindupTicks));
        Assert.That(PelagAbordageClipRules.TurnWindupTicks, Is.EqualTo(Simulation.AbordageWindupTicks + Simulation.AbordageTurnWindupTicks));
        Assert.That(PelagAbordageClipRules.HoldTicks, Is.EqualTo(Simulation.AbordageHoldTicks));
        Assert.That(PelagAbordageClipRules.ExitTicks, Is.EqualTo(Simulation.AbordageExitTicks));
        Assert.That(PelagAbordageClipRules.RecallTicks, Is.EqualTo(Simulation.AbordageRecallTicks));
        JsonElement timing = Timing();
        JsonElement clips = timing.GetProperty("clips");
        string folder = Path.Combine(RepoRoot.Path, "ART", "characters", "pelag", "abordage-2026-10-02", "animation");
        var states = new HashSet<string>();
        foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
        {
            string name = PelagAbordageClipRules.ClipName(clip);
            JsonElement entry = clips.GetProperty(name);
            Assert.That(entry.GetProperty("frames").GetInt32() - 1, Is.EqualTo(PelagAbordageClipRules.LastFrame(clip)), name);
            Assert.That(File.Exists(Path.Combine(folder, name + ".fbx")), Is.True, name + ".fbx");
            Assert.That(PelagAbordageClipRules.StateName(clip), Does.StartWith("Abordage2_").And.EndWith("_v5"));
            Assert.That(states.Add(PelagAbordageClipRules.StateName(clip)), Is.True, "имя состояния одно на клип");
            Assert.That((int)clip, Is.LessThan(8), "массивы хешей вида — на 8 клипов");
        }
        Assert.That(PelagAbordageClipRules.RequiredClips, Is.SubsetOf(PelagAbordageClipRules.Clips));
        Assert.That(File.Exists(Path.Combine(folder, PelagAbordageClipRules.BindReference + ".fbx")), Is.True, "привязка");
        Assert.That(PelagAbordageClipRules.LastFrame(PelagAbordageClip.Punch), Is.EqualTo(PelagAbordageClipRules.HoldTicks));
        Assert.That(PelagAbordageClipRules.LastFrame(PelagAbordageClip.Recover), Is.EqualTo(PelagAbordageClipRules.ExitTicks));
        Assert.That(PelagAbordageClipRules.LastFrame(PelagAbordageClip.Uppercut),
            Is.EqualTo(PelagAbordageClipRules.FormLeadTicks + PelagAbordageClipRules.HoldTicks));
        JsonElement segments = clips.GetProperty(PelagAbordageClipRules.ClipName(PelagAbordageClip.Throw)).GetProperty("segments");
        Assert.That(segments.GetProperty("release")[1].GetInt32(), Is.EqualTo((int)PelagAbordageClipRules.ReleaseFrame), "выпуск");
        Assert.That(segments.GetProperty("bite").GetInt32(), Is.EqualTo((int)PelagAbordageClipRules.BiteFrame), "натяг");

        // Прибытие по форме (timing.json contact/forms): база и Пробоина — Punch 0, Гейзер — Uppercut 1, Обвал — Slam 1.
        JsonElement contact = timing.GetProperty("contact");
        var byForm = new (string key, PelagForm form, PelagAbordageClip clip, float frame)[]
        {
            ("base", PelagForm.None, PelagAbordageClip.Punch, 0f),
            ("breach", PelagForm.AbordageBreach, PelagAbordageClip.Punch, 0f),
            ("geyser", PelagForm.AbordageGeyser, PelagAbordageClip.Uppercut, PelagAbordageClipRules.FormContactFrame),
            ("quake", PelagForm.AbordageQuake, PelagAbordageClip.Slam, PelagAbordageClipRules.FormContactFrame),
        };
        foreach (var row in byForm)
        {
            JsonElement entry = contact.GetProperty(row.key);
            Assert.That(entry.GetProperty("clip").GetString(), Is.EqualTo(PelagAbordageClipRules.ClipName(row.clip)), row.key);
            Assert.That(entry.GetProperty("frame").GetSingle(), Is.EqualTo(row.frame), row.key);
            Assert.That(PelagAbordageClipRules.ArrivalClip(row.form), Is.EqualTo(row.clip), row.key);
            Assert.That(PelagAbordageClipRules.ArrivalClip(row.form, formClipsAvailable: false), Is.EqualTo(PelagAbordageClip.Punch), row.key);
            Assert.That(PelagAbordageClipRules.ArrivalLeadTicks(row.clip), Is.EqualTo((int)row.frame), row.key + ": контакт в тик удара");
        }
        Assert.That(PelagAbordageClipRules.ArrivalClip(PelagForm.WhirlwindStorm), Is.EqualTo(PelagAbordageClip.Punch), "чужая форма — база");
    }

    [Test]
    public void SeamsFollowTheAuthoredChain()
    {
        var expected = new HashSet<(PelagAbordageClip, PelagAbordageClip)>
        {
            (PelagAbordageClip.Throw, PelagAbordageClip.Pull), (PelagAbordageClip.Throw, PelagAbordageClip.PullShort),
            (PelagAbordageClip.Pull, PelagAbordageClip.Punch), (PelagAbordageClip.PullShort, PelagAbordageClip.Punch),
            (PelagAbordageClip.Pull, PelagAbordageClip.Uppercut), (PelagAbordageClip.Pull, PelagAbordageClip.Slam),
            (PelagAbordageClip.PullShort, PelagAbordageClip.Uppercut), (PelagAbordageClip.PullShort, PelagAbordageClip.Slam),
            (PelagAbordageClip.Punch, PelagAbordageClip.Recover), (PelagAbordageClip.Uppercut, PelagAbordageClip.Recover),
            (PelagAbordageClip.Slam, PelagAbordageClip.Recover),
        };
        foreach (PelagAbordageClip from in PelagAbordageClipRules.Clips)
            foreach (PelagAbordageClip to in PelagAbordageClipRules.Clips)
                Assert.That(PelagAbordageClipRules.IsSeam(from, to), Is.EqualTo(expected.Contains((from, to))), $"{from}->{to}");

        // timing.json limits_v3.seams: каждый стык вида измерен в Blender и равен 0°.
        var byName = new Dictionary<string, PelagAbordageClip>();
        foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips) byName[PelagAbordageClipRules.ClipName(clip)] = clip;
        var measured = new HashSet<(PelagAbordageClip, PelagAbordageClip)>();
        foreach (JsonElement seam in Timing().GetProperty("limits_v3").GetProperty("seams").EnumerateArray())
        {
            string from = seam.GetProperty("from_clip").GetString(), to = seam.GetProperty("to_clip").GetString();
            Assert.That(seam.GetProperty("max_bone_deg").GetSingle(), Is.EqualTo(0f).Within(.01f), from + "->" + to);
            if (byName.TryGetValue(from, out PelagAbordageClip a) && byName.TryGetValue(to, out PelagAbordageClip b)) measured.Add((a, b));
        }
        foreach (var pair in expected) Assert.That(measured, Does.Contain(pair), $"стык {pair.Item1}->{pair.Item2} измерен");
    }

    [Test]
    public void PlantedLeftAnkleIsTheSquallPivot()
    {
        JsonElement ankles = Timing().GetProperty("planted_ankles");
        int checkedFrames = 0;
        foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
            foreach (JsonElement row in ankles.GetProperty(PelagAbordageClipRules.Suffix(clip)).EnumerateArray())
            {
                int frame = row.GetProperty("frame").GetInt32();
                JsonElement left = row.GetProperty("Left");
                bool flat = left.GetProperty("contact").GetString() == "flat";
                Assert.That(PelagAbordageClipRules.LeftPlanted(clip, frame), Is.EqualTo(flat), $"{clip} {frame}");
                if (!flat) continue;
                // Та же точка, что в опоре Шквала: вокруг неё крутится корень (PelagSquallClipRules.LeftAnkle).
                Assert.That(left.GetProperty("f").GetSingle(), Is.EqualTo(PelagSquallClipRules.AuthoredAnkleForward).Within(.002f), $"{clip} {frame}");
                Assert.That(left.GetProperty("l").GetSingle(), Is.EqualTo(PelagSquallClipRules.AuthoredAnkleLeft).Within(.002f), $"{clip} {frame}");
                checkedFrames++;
            }
        // Throw 10, Pull 2, Punch 4, Recover 7, PullShort 2, Uppercut 4, Slam 4.
        Assert.That(checkedFrames, Is.EqualTo(10 + 2 + 4 + 7 + 2 + 4 + 4));
    }

    [Test]
    public void RecallRunsTheThrowBackToStance()
    {
        Assert.That(PelagAbordageClipRules.RecallFrame(5f, 0f), Is.EqualTo(5f));
        Assert.That(PelagAbordageClipRules.RecallFrame(5f, PelagAbordageClipRules.RecallTicks), Is.EqualTo(0f).Within(1e-5f));
        float last = 9f;
        for (float k = 0f; k <= PelagAbordageClipRules.RecallTicks; k += .25f)
        {
            float frame = PelagAbordageClipRules.RecallFrame(5f, k);
            Assert.That(frame, Is.LessThanOrEqualTo(last + 1e-5f));
            last = frame;
        }
    }
}
