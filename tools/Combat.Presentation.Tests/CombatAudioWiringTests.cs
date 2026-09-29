using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Провода боевого звука, которые ломаются молча (баг 29.09 «звук, которого тут быть не
/// должно»). Профиль Resources/Combat/CombatAudio.asset хранит звук НОМЕРОМ значения
/// CombatSound: вставка значения в середину перечисления сдвинула бы все записи ниже —
/// клипы, громкость и приоритет старого звука достались бы соседу. Проверяем по исходникам
/// и ассету (Unity здесь нет): номера старых значений не двигаются, у каждой записи профиля
/// клипы своего звука, приоритеты записей мобов — свои, а каждый слот MobSoundBank грузится
/// ровно в один звук CombatAudio.
/// </summary>
public sealed class CombatAudioWiringTests
{
    /// <summary>
    /// Порядок CombatSound на 29.09 (44111e2b): 0–37 — до мобов леса (8e513a38), дальше —
    /// поток K. Новые значения — только в конец перед Count; этот список лишь дописывают.
    /// </summary>
    private static readonly string[] PinnedOrder =
    {
        "Whoosh", "HitMetal", "HitBody", "Kill", "Whirlwind", "Cast", "Reward",
        "AnchorSweep", "ChainStep", "Footstep", "Dissolve",
        "GuardianFall", "RootSwarmHit", "RootSwarmKill", "RootSwarmFall", "RootSwarmDissolve",
        "EnemyWarning", "PlayerHurt", "CycloneRelease",
        "WhooshHeavy", "CycloneTurn", "WhirlwindEnd", "ChainStepHop", "ChainStepEnd",
        "PelagAttack", "Cleave", "Dash", "BlazePrepare", "BlazeFire", "Finisher",
        "WhirlwindPulse", "WhirlwindHit",
        "BudVolley", "BudPop", "BudFruitImpact", "BudHurt", "BudDeath", "GuardianSwing",
        "GuardianClawSwing", "GuardianClawImpact", "GuardianHurt", "GuardianDeath", "GuardianStep",
        "RootSwarmBite", "RootSwarmScuttle", "RootSwarmHurt", "RootSwarmDeath",
        "BudPuddle", "BudGurgle",
        "StonehoofSnort", "StonehoofCharge", "StonehoofCollision", "StonehoofTusk", "StonehoofHurt", "StonehoofDeath",
        "WendigoClaw", "WendigoLeap", "WendigoLand", "WendigoHowl", "WendigoSweep", "WendigoHurt", "WendigoDeath",
        "ThornSpike", "ThornBurst", "ThornShot", "ThorncasterHurt", "ThorncasterDeath",
        "SnarerSlam", "SnarerRoots", "SnarerMend", "SnarerHurt", "SnarerDeath",
        "SplitterBite", "SplitterRoll", "SplitterCrack", "SplitlingPop", "SplitterHurt", "SplitterDeath",
        "KillImpact", "HeroStunned", "HeroRooted",
    };

    /// <summary>Первый звук мобов леса: его записи профиля добавлены редактором с приоритетом по имени.</summary>
    private const string FirstMobSound = "GuardianClawSwing";

    [Test]
    public void CombatSoundNumbersOnlyGrowAtTheEnd()
    {
        List<string> names = EnumNames();
        Assert.That(names[names.Count - 1], Is.EqualTo("Count"), "Count — последним");
        Assert.That(names.Count - 1, Is.GreaterThanOrEqualTo(PinnedOrder.Length), "значения не удаляют");
        for (int i = 0; i < PinnedOrder.Length; i++)
            Assert.That(names[i], Is.EqualTo(PinnedOrder[i]),
                "CombatSound " + i + ": номер записан в CombatAudio.asset — новое значение только перед Count");
    }

    [Test]
    public void EverySoundHasOneProfileEntry()
    {
        List<string> names = EnumNames();
        var entries = ProfileEntries();
        int count = names.Count - 1;
        Assert.That(entries.Select(x => x.Sound), Is.Unique, "две записи на один звук: Find берёт первую");
        foreach (var entry in entries)
            Assert.That(entry.Sound, Is.InRange(0, count - 1), "запись за пределом перечисления");
        for (int i = 0; i < count; i++)
            Assert.That(entries.Any(x => x.Sound == i), names[i] + " (" + i + "): нет записи в профиле");
    }

    [Test]
    public void ProfileClipsBelongToTheirSound()
    {
        List<string> names = EnumNames();
        Dictionary<string, string> guids = AudioGuids();
        int checkedClips = 0;
        foreach (var entry in ProfileEntries())
            foreach (string guid in entry.Clips)
            {
                string name = names[entry.Sound];
                Assert.That(guids.TryGetValue(guid, out string path), Is.True, name + ": клип " + guid + " не найден");
                string folder = Path.GetFileName(Path.GetDirectoryName(path));
                string stem = Regex.Replace(Path.GetFileNameWithoutExtension(path), @"_\d+$", "");
                Assert.That(folder == name || stem == name || folder + stem == name, Is.True,
                    "запись " + entry.Sound + " (" + name + ") играет " + path + " — чужой звук");
                checkedClips++;
            }
        Assert.That(checkedClips, Is.GreaterThanOrEqualTo(40));
    }

    [Test]
    public void MobEntriesKeepThePriorityOfTheirName()
    {
        // Записи мобов добавил редактор (CombatPresentationSetup) с DefaultPriority(звук):
        // сдвиг номеров показал бы чужой приоритет у записи.
        List<string> names = EnumNames();
        Dictionary<string, int> priority = DefaultPriorities(out int fallback);
        int first = names.IndexOf(FirstMobSound);
        Assert.That(first, Is.GreaterThan(0));
        foreach (var entry in ProfileEntries().Where(x => x.Sound >= first && x.Sound < names.Count - 1))
        {
            string name = names[entry.Sound];
            int expected = priority.TryGetValue(name, out int p) ? p : fallback;
            Assert.That(entry.Priority, Is.EqualTo(expected), "запись " + entry.Sound + " (" + name + ")");
        }
    }

    [Test]
    public void EveryWiredMobSlotLoadsIntoOneSound()
    {
        List<string> names = EnumNames();
        string source = File.ReadAllText(Path.Combine(View, "CombatAudio.cs"));
        var loads = Regex.Matches(source, @"LoadMob\(Sound\.(\w+),\s*""(\w+)"",\s*""(\w+)""\)")
            .Select(m => (sound: m.Groups[1].Value, mob: m.Groups[2].Value, slot: m.Groups[3].Value)).ToList();
        Assert.That(loads.Select(x => x.sound), Is.Unique, "в один звук грузятся два слота");
        Assert.That(loads.Select(x => x.mob + "/" + x.slot), Is.Unique, "слот грузится в два звука");
        foreach (var load in loads)
        {
            Assert.That(names, Does.Contain(load.sound), load.sound);
            Assert.That(MobSoundBank.Slots.Any(s => s.Mob == load.mob && s.Name == load.slot && s.Wired), Is.True,
                load.mob + "/" + load.slot + ": нет подключённого слота в MobSoundBank");
        }
        foreach (var slot in MobSoundBank.Slots.Where(s => s.Wired))
            Assert.That(loads.Any(x => x.mob == slot.Mob && x.slot == slot.Name), Is.True,
                slot.Path + ": подключён в MobSoundBank, но CombatAudio его не грузит");
    }

    // ---------- разбор исходников и ассета ----------

    private sealed class Entry
    {
        public int Sound, Priority;
        public readonly List<string> Clips = new List<string>();
    }

    private static string View => Path.Combine(RepoRoot.Path, "razlom", "Assets", "Game.View");

    private static string Profile => Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "Combat", "CombatAudio.asset");

    private static List<string> EnumNames()
    {
        string source = File.ReadAllText(Path.Combine(View, "CombatAudioProfile.cs"));
        Match body = Regex.Match(source, @"public enum CombatSound\s*\{(?<body>[^}]*)\}");
        Assert.That(body.Success, "не найдено перечисление CombatSound");
        string text = Regex.Replace(body.Groups["body"].Value, @"//[^\r\n]*", "");
        return text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    private static List<Entry> ProfileEntries()
    {
        var entries = new List<Entry>();
        Entry current = null;
        foreach (string raw in File.ReadAllLines(Profile))
        {
            string line = raw.TrimEnd();
            Match sound = Regex.Match(line, @"^  - Sound: (\d+)$");
            if (sound.Success) { current = new Entry { Sound = int.Parse(sound.Groups[1].Value) }; entries.Add(current); continue; }
            if (current == null) continue;
            Match clip = Regex.Match(line, @"^    - \{fileID: \d+, guid: ([0-9a-f]{32}), type: \d+\}$");
            if (clip.Success) { current.Clips.Add(clip.Groups[1].Value); continue; }
            Match priority = Regex.Match(line, @"^    Priority: (\d+)$");
            if (priority.Success) current.Priority = int.Parse(priority.Groups[1].Value);
        }
        Assert.That(entries.Count, Is.GreaterThan(0), "в профиле нет записей");
        return entries;
    }

    /// <summary>guid → путь клипа относительно Resources/Audio по .meta.</summary>
    private static Dictionary<string, string> AudioGuids()
    {
        string root = Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "Audio");
        var map = new Dictionary<string, string>();
        foreach (string meta in Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories))
        {
            string asset = meta.Substring(0, meta.Length - ".meta".Length);
            if (!File.Exists(asset)) continue;
            foreach (string line in File.ReadLines(meta))
                if (line.StartsWith("guid: ", StringComparison.Ordinal))
                {
                    map[line.Substring(6).Trim()] = Path.GetRelativePath(root, asset);
                    break;
                }
        }
        return map;
    }

    /// <summary>Ветви CombatAudioProfile.DefaultPriority: имя звука → приоритет; fallback — default.</summary>
    private static Dictionary<string, int> DefaultPriorities(out int fallback)
    {
        string source = File.ReadAllText(Path.Combine(View, "CombatAudioProfile.cs"));
        int start = source.IndexOf("public static int DefaultPriority", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        var map = new Dictionary<string, int>();
        var pending = new List<string>();
        fallback = -1;
        foreach (string line in source.Substring(start).Split('\n').Skip(1))
        {
            foreach (Match name in Regex.Matches(line, @"case CombatSound\.(\w+):")) pending.Add(name.Groups[1].Value);
            Match ret = Regex.Match(line, @"return (\d+);");
            if (!ret.Success) continue;
            int value = int.Parse(ret.Groups[1].Value);
            if (line.Contains("default:")) { fallback = value; break; }
            foreach (string name in pending) map[name] = value;
            pending.Clear();
        }
        Assert.That(fallback, Is.GreaterThanOrEqualTo(0), "не найден default в DefaultPriority");
        return map;
    }
}
