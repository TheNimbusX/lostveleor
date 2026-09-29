using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Game.View;
using NUnit.Framework;

/// <summary>
/// Звуки мобов леса (поток K, выбор владельца 29.09): клипы Resources/Audio/Combat/Mobs,
/// таблица слотов — MobSoundBank, нарезка — ART/SFX/epidemic-2026-09-29/process.py.
/// Проверяем то, что на слух в редакторе ломается незаметно: у каждого слота есть
/// 2-4 дубля, формат, щелчки на краях, удар в начале контактного клипа, пик взмаха
/// там, где его ждёт CombatAudio, ровная громкость и строки лицензий.
/// </summary>
public sealed class MobAudioClipTests
{
    private const int Rate = 48000;
    private const double CeilingDb = -1.0;

    [Test]
    public void EverySlotHasTwoToFourNumberedVariations()
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in MobSoundBank.Slots)
        {
            string[] files = SlotFiles(slot);
            Assert.That(files.Length, Is.InRange(2, 4), slot.Path);
            for (int i = 0; i < files.Length; i++)
            {
                string name = $"{slot.Name}_{i + 1:00}.wav";
                Assert.That(Path.GetFileName(files[i]), Is.EqualTo(name), slot.Path + ": номера подряд с 01");
                expected.Add(Path.Combine(slot.Mob, name));
            }
        }
        // Лишний клип в папке моба CombatAudio не загрузит, а в сборку он попадёт.
        foreach (string file in Directory.GetFiles(Mobs, "*.wav", SearchOption.AllDirectories))
            Assert.That(expected, Does.Contain(Path.GetRelativePath(Mobs, file)), file + ": нет в MobSoundBank");
    }

    [Test]
    public void ClipsAreMono48kPcmWithoutClippingOrEdgeClicks()
    {
        foreach (var (slot, path, clip) in AllClips())
        {
            Assert.That(clip.Channels, Is.EqualTo(1), path);
            Assert.That(clip.SampleRate, Is.EqualTo(Rate), path);
            Assert.That(clip.Peak, Is.LessThanOrEqualTo(Math.Pow(10, -0.9 / 20)).And.GreaterThan(0.05), path);
            Assert.That(Math.Abs(clip.Samples[0]), Is.LessThan(0.02), path + ": щелчок в начале");
            Assert.That(Math.Abs(clip.Samples[^1]), Is.LessThan(0.02), path + ": щелчок в конце");
            Assert.That(clip.Seconds, Is.LessThanOrEqualTo(slot.MaxSeconds + 0.01), path);
        }
    }

    [Test]
    public void ContactClipsSoundAtTheirEvent()
    {
        // Контакт играет в тик события без задержки: удар — в первые 15 мс, голос — начинается в 30 мс.
        foreach (var (slot, path, clip) in AllClips())
        {
            if (slot.Align == MobSoundBank.Align.Attack)
                Assert.That(clip.FirstAbove(0.5), Is.LessThan(0.015), path + ": удар не в начале");
            else if (slot.Align == MobSoundBank.Align.Onset)
                Assert.That(clip.FirstAbove(0.0316), Is.LessThan(0.03), path + ": тишина до звука");
        }
    }

    [Test]
    public void AlignedClipsPeakWhereCombatAudioExpectsIt()
    {
        // CombatAudio запускает взмах, взлёт и вой за PeakSeconds до контакта: пик (RMS 10 мс) — там.
        int checkedClips = 0;
        foreach (var (slot, path, clip) in AllClips())
        {
            if (slot.Align != MobSoundBank.Align.Peak) continue;
            Assert.That(clip.EnvelopePeakSeconds(0.01), Is.EqualTo(slot.PeakSeconds).Within(0.025), path);
            checkedClips++;
        }
        Assert.That(checkedClips, Is.GreaterThanOrEqualTo(15));
    }

    [Test]
    public void LoudnessIsMatchedAcrossTheSet()
    {
        // Цель слота ±2 LU. Острые записи (щелчок, сучок) упираются в пик -1 dBFS раньше
        // цели: такие могут быть тише, но только если стоят на потолке, и не тише цели на 12.
        foreach (var (slot, path, clip) in AllClips())
        {
            double loud = clip.LoudnessMax();
            Assert.That(loud, Is.LessThanOrEqualTo(slot.Loudness + 2.0), path + $": {loud:0.0} LUFS");
            Assert.That(loud, Is.GreaterThanOrEqualTo(slot.Loudness - 12.0), path + $": {loud:0.0} LUFS");
            if (loud < slot.Loudness - 2.0)
                Assert.That(20 * Math.Log10(clip.Peak), Is.GreaterThan(CeilingDb - 0.3),
                    path + $": {loud:0.0} LUFS тише цели {slot.Loudness}, а пик не на потолке");
        }
    }

    [Test]
    public void LicensesNameEveryPickedEpidemicSource()
    {
        string licenses = File.ReadAllText(Path.Combine(Audio, "LICENSES.md"));
        Assert.That(licenses, Does.Contain("Epidemic Sound — прототип, проверить лицензию на использование в игре до релиза"));
        using var picks = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Path, "ART", "SFX", "epidemic-2026-09-29", "picks.json")));
        int sources = 0;
        foreach (var slot in picks.RootElement.EnumerateObject())
        {
            if (slot.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var pick in slot.Value.GetProperty("picked").EnumerateArray())
            {
                string title = pick.GetProperty("title").GetString();
                Assert.That(licenses, Does.Contain("«" + title + "»"), title);
                sources++;
            }
        }
        Assert.That(sources, Is.GreaterThanOrEqualTo(48));
    }

    // ------------------------------------------------------------------

    private static IEnumerable<(MobSoundBank.Slot slot, string path, Wav clip)> AllClips()
    {
        foreach (var slot in MobSoundBank.Slots)
            foreach (string path in SlotFiles(slot))
                yield return (slot, path, Wav.Read(path));
    }

    private static string[] SlotFiles(MobSoundBank.Slot slot)
    {
        string folder = Path.Combine(Mobs, slot.Mob);
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.GetFiles(folder, slot.Name + "_*.wav")
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), "^" + slot.Name + @"_\d\d\.wav$"))
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    private static string Audio => Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "Audio", "Combat");
    private static string Mobs => Path.Combine(Audio, "Mobs");

    private sealed class Wav
    {
        public int Channels, SampleRate;
        public double[] Samples = Array.Empty<double>();
        public double Seconds => Samples.Length / (double)Math.Max(1, SampleRate);
        public double Peak => Samples.Length == 0 ? 0 : Samples.Max(Math.Abs);

        /// <summary>Первый сэмпл не тише fraction от пика, секунды.</summary>
        public double FirstAbove(double fraction)
        {
            double limit = Peak * fraction;
            for (int i = 0; i < Samples.Length; i++)
                if (Math.Abs(Samples[i]) >= limit) return i / (double)SampleRate;
            return Seconds;
        }

        /// <summary>Где максимум RMS по центрированному окну window, секунды.</summary>
        public double EnvelopePeakSeconds(double window)
        {
            int n = Math.Max(1, (int)(window * SampleRate));
            var sum = new double[Samples.Length + 1];
            for (int i = 0; i < Samples.Length; i++) sum[i + 1] = sum[i] + Samples[i] * Samples[i];
            int best = 0;
            double bestValue = -1;
            for (int i = 0; i < Samples.Length; i++)
            {
                int lo = Math.Clamp(i - n / 2, 0, Samples.Length), hi = Math.Clamp(lo + n, 0, Samples.Length);
                double v = (sum[hi] - sum[lo]) / Math.Max(1, hi - lo);
                if (v > bestValue) { bestValue = v; best = i; }
            }
            return best / (double)SampleRate;
        }

        /// <summary>
        /// Максимум громкости M по BS.1770 (K-взвешивание для 48 кГц, окно 400 мс, шаг
        /// 10 мс). Клип короче 400 мс — окно по его длине, не короче 100 мс: та же мера,
        /// что lufs_m_max в process.py.
        /// </summary>
        public double LoudnessMax()
        {
            double[] y = Biquad(Biquad(Samples,
                    1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585),
                1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);
            int w = (int)(Math.Min(0.4, Math.Max(0.1, Seconds)) * SampleRate), hop = SampleRate / 100;
            if (y.Length < w) Array.Resize(ref y, w);
            var sum = new double[y.Length + 1];
            for (int i = 0; i < y.Length; i++) sum[i + 1] = sum[i] + y[i] * y[i];
            double best = 0;
            for (int s = 0; s + w <= y.Length; s += hop) best = Math.Max(best, (sum[s + w] - sum[s]) / w);
            return -0.691 + 10 * Math.Log10(best + 1e-20);
        }

        private static double[] Biquad(double[] x, double b0, double b1, double b2, double a1, double a2)
        {
            var y = new double[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v;
            }
            return y;
        }

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
                    wav.Samples = new double[count];
                    for (int i = 0; i < count; i++)
                        wav.Samples[i] = BitConverter.ToInt16(data, body + i * 2 * wav.Channels) / 32768.0;
                }
                at = body + size + (size & 1);
            }
            Assert.That(wav.Samples.Length, Is.GreaterThan(0), path + ": нет данных");
            return wav;
        }
    }
}
