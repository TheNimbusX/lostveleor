using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Звуки Плюй-плода и сигналов врагов (26.09): игровые клипы лежат в Resources под
/// именами, которые читает CombatAudio, а кандидаты для выбора на слух — в
/// ART/SFX/candidates-2026-09-26. Проверяем то, что ломается незаметно на слух в
/// редакторе: формат, клиппинг, длину и то, что удар стоит в начале клипа — иначе
/// звук отстаёт от события симуляции, по которому играет.
/// </summary>
public sealed class EnemyAudioClipTests
{
    // ForestBudSettings.ShotCount по умолчанию: хлопок на каждый плод залпа.
    private const int BudShots = 5;
    // Замах залпа 24 тика: раскрытие должно уложиться до первого плода с небольшим хвостом.
    private const double BudWindupSeconds = 24 / 30.0;

    private static readonly string[] CandidateSlots =
        { "bud_volley", "bud_fruit", "bud_hurt", "bud_death", "enemy_warning", "guardian_swing" };

    [Test]
    public void BudClipsAreInstalledUnderTheNamesCombatAudioLoads()
    {
        string bud = Path.Combine(Audio, "Bud");
        foreach (string name in new[] { "BudVolley", "BudFruitImpact", "BudHurt", "BudDeath" })
            Assert.That(File.Exists(Path.Combine(bud, name + ".wav")), Is.True, name);
        for (int i = 1; i <= BudShots; i++)
            Assert.That(File.Exists(Path.Combine(bud, $"BudPop_{i:00}.wav")), Is.True, $"BudPop_{i:00}");
        Assert.That(Directory.GetFiles(Path.Combine(Audio, "EnemyWarning"), "*.wav").Length, Is.EqualTo(1));
    }

    [Test]
    public void InstalledClipsAreCleanMonoPcmWithoutClipping()
    {
        foreach (string path in InstalledClips())
        {
            var clip = Wav.Read(path);
            Assert.That(clip.Channels, Is.EqualTo(1), path);
            Assert.That(clip.Peak, Is.LessThan(0.95).And.GreaterThan(0.05), path);
            Assert.That(Math.Abs(clip.Samples[0]), Is.LessThan(0.02), path + ": щелчок в начале");
            Assert.That(Math.Abs(clip.Samples[clip.Samples.Length - 1]), Is.LessThan(0.02), path + ": щелчок в конце");
        }
    }

    [Test]
    public void ContactClipsHitAtTheirEvent()
    {
        // Хлопок, падение плода и боль играют по событию контакта без задержки.
        string bud = Path.Combine(Audio, "Bud");
        for (int i = 1; i <= BudShots; i++)
            Assert.That(Wav.Read(Path.Combine(bud, $"BudPop_{i:00}.wav")).PeakSeconds, Is.LessThan(0.04), $"BudPop_{i:00}");
        Assert.That(Wav.Read(Path.Combine(bud, "BudFruitImpact.wav")).PeakSeconds, Is.LessThan(0.04));
        Assert.That(Wav.Read(Path.Combine(bud, "BudHurt.wav")).PeakSeconds, Is.LessThan(0.04));
        Assert.That(Wav.Read(Path.Combine(bud, "BudDeath.wav")).PeakSeconds, Is.LessThan(0.04));
    }

    [Test]
    public void TelegraphCuesStayShortAndSubtle()
    {
        var opening = Wav.Read(Path.Combine(Audio, "Bud", "BudVolley.wav"));
        Assert.That(opening.Seconds, Is.LessThan(BudWindupSeconds + 0.1), "раскрытие не должно тянуться за первый плод");
        foreach (string pop in Directory.GetFiles(Path.Combine(Audio, "Bud"), "BudPop_*.wav"))
            Assert.That(Wav.Read(pop).Seconds, Is.LessThan(0.25), pop + ": хлопки идут через 0,2 с, хвост не длиннее следующего");
        foreach (string warning in Directory.GetFiles(Path.Combine(Audio, "EnemyWarning"), "*.wav"))
            Assert.That(Wav.Read(warning).Seconds, Is.LessThan(0.6), warning);
        // Замах хранителя может быть выбран «тишиной» — тогда папка пуста.
        string swing = Path.Combine(Audio, "GuardianSwing");
        if (Directory.Exists(swing))
            foreach (string clip in Directory.GetFiles(swing, "*.wav"))
                Assert.That(Wav.Read(clip).Seconds, Is.LessThan(1.0), clip);
    }

    [Test]
    public void ListeningPageOffersThreeCandidatesPerSlot()
    {
        string folder = Path.Combine(Root, "ART", "SFX", "candidates-2026-09-26");
        string page = File.ReadAllText(Path.Combine(folder, "index.html"));
        foreach (string slot in CandidateSlots)
            foreach (char kind in "abc")
            {
                string file = $"{slot}_{kind}.wav";
                Assert.That(File.Exists(Path.Combine(folder, file)), Is.True, file);
                Assert.That(page, Does.Contain($"src=\"{file}\""), file);
            }
        // Кандидаты залпа по частям — то, что ставится в игру.
        foreach (char kind in "abc")
        {
            Assert.That(File.Exists(Path.Combine(folder, "parts", $"bud_volley_{kind}_open.wav")), Is.True);
            for (int i = 1; i <= BudShots; i++)
                Assert.That(File.Exists(Path.Combine(folder, "parts", $"bud_volley_{kind}_pop{i}.wav")), Is.True);
        }
    }

    private static string[] InstalledClips()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (string folder in new[] { "Bud", "EnemyWarning", "GuardianSwing" })
        {
            string path = Path.Combine(Audio, folder);
            if (Directory.Exists(path)) list.AddRange(Directory.GetFiles(path, "*.wav"));
        }
        return list.ToArray();
    }

    private static string Audio => Path.Combine(Root, "razlom", "Assets", "Resources", "Audio", "Combat");

    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "razlom", "Assets"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "не найден корень репозитория");
            return dir.FullName;
        }
    }

    private sealed class Wav
    {
        public int Channels, SampleRate;
        public float[] Samples = Array.Empty<float>();
        public double Seconds => Samples.Length / (double)Math.Max(1, SampleRate);
        public float Peak { get; private set; }
        public double PeakSeconds { get; private set; }

        public static Wav Read(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            Assert.That(System.Text.Encoding.ASCII.GetString(data, 0, 4), Is.EqualTo("RIFF"), path);
            Assert.That(System.Text.Encoding.ASCII.GetString(data, 8, 4), Is.EqualTo("WAVE"), path);
            var wav = new Wav();
            int bits = 0, format = 0;
            for (int at = 12; at + 8 <= data.Length;)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, at, 4);
                int size = BitConverter.ToInt32(data, at + 4);
                int body = at + 8;
                if (id == "fmt ")
                {
                    format = BitConverter.ToInt16(data, body);
                    wav.Channels = BitConverter.ToInt16(data, body + 2);
                    wav.SampleRate = BitConverter.ToInt32(data, body + 4);
                    bits = BitConverter.ToInt16(data, body + 14);
                }
                else if (id == "data")
                {
                    Assert.That(format, Is.EqualTo(1), path + ": ожидается PCM");
                    Assert.That(bits, Is.EqualTo(16), path);
                    int count = Math.Min(size, data.Length - body) / 2 / Math.Max(1, wav.Channels);
                    wav.Samples = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        float v = BitConverter.ToInt16(data, body + i * 2 * wav.Channels) / 32768f;
                        wav.Samples[i] = v;
                        if (Math.Abs(v) > wav.Peak) { wav.Peak = Math.Abs(v); wav.PeakSeconds = i / (double)wav.SampleRate; }
                    }
                }
                at = body + size + (size & 1);
            }
            Assert.That(wav.Samples.Length, Is.GreaterThan(0), path + ": нет данных");
            return wav;
        }
    }
}
