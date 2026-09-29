using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Director = Game.View.CombatMusicDirector;

/// <summary>
/// Клипы музыки леса (Resources/Audio/Music/Forest, выбор владельца 29.09): формат,
/// импорт Streaming и главное — период петли (длина клипа − LoopCrossfade из
/// таблицы режиссёра) в целых тактах трека. Не целый такт — второй голос петли
/// входит не в долю или не в свой аккорд; на слух это «спотыкается» раз в пару
/// минут, и в редакторе этого никто не заметит. Длина — из Ogg: гранула
/// последней страницы — число сэмплов канала.
/// </summary>
public sealed class CombatMusicClipTests
{
    private const int Rate = 48000;

    /// <summary>Такт трека, секунды, по порядку Track: лес — 120 BPM, босс — ~130 BPM (доля 0,4616 с).</summary>
    private static readonly double[] Bar = { 2.0, 2.0, 2.0, 2.0, 2.0, 4 * .4616 };

    private static string Folder => Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "Audio", "Music", "Forest");

    [Test]
    public void EveryTrackIsAStereo48kVorbisClip()
    {
        foreach (Director.TrackInfo info in Director.Tracks)
        {
            string path = Path.Combine(Folder, info.Resource + ".ogg");
            Assert.That(File.Exists(path), Is.True, path);
            ReadOgg(path, out int channels, out int rate, out long samples);
            Assert.That(channels, Is.EqualTo(2), path);
            Assert.That(rate, Is.EqualTo(Rate), path);
            Assert.That(samples / (double)Rate, Is.InRange(60.0, 240.0), path);
        }
    }

    [Test]
    public void LoopPeriodIsAWholeNumberOfBars()
    {
        for (int t = 0; t < Director.TrackCount; t++)
        {
            Director.TrackInfo info = Director.Tracks[t];
            ReadOgg(Path.Combine(Folder, info.Resource + ".ogg"), out _, out _, out long samples);
            Assert.That(info.LoopStart, Is.Zero, info.Resource + ": клип нарезан петлёй целиком");
            Assert.That(info.LoopEnd, Is.Zero, info.Resource);

            // Как в игре: второй голос с нуля, когда первый дошёл до «конец − кроссфейд».
            long crossfade = (long)Math.Round(info.LoopCrossfade * (double)Rate);
            double period = (samples - crossfade) / (double)Rate;
            double bars = period / Bar[t];
            double offBeat = Math.Abs(bars - Math.Round(bars)) * Bar[t];
            Assert.That(offBeat, Is.LessThan(.005), $"{info.Resource}: период петли {period:0.000} с — не целые такты");
            // И сам кроссфейд — целые такты: иначе новый голос входит посреди такта.
            double fadeBars = info.LoopCrossfade / Bar[t];
            Assert.That(Math.Abs(fadeBars - Math.Round(fadeBars)) * Bar[t], Is.LessThan(.005), info.Resource);
            Assert.That(Math.Round(fadeBars), Is.GreaterThanOrEqualTo(1), info.Resource);
        }
    }

    [Test]
    public void ClipsImportAsStreamingVorbisInStereoWithTheirOwnGuids()
    {
        var guids = new System.Collections.Generic.HashSet<string>();
        foreach (Director.TrackInfo info in Director.Tracks)
        {
            string meta = File.ReadAllText(Path.Combine(Folder, info.Resource + ".ogg.meta"));
            // loadType 2 — Streaming: распакованным трек занял бы ~26 МБ.
            StringAssert.Contains("loadType: 2", meta, info.Resource);
            StringAssert.Contains("compressionFormat: 1", meta, info.Resource);
            StringAssert.Contains("preloadAudioData: 0", meta, info.Resource);
            StringAssert.Contains("forceToMono: 0", meta, info.Resource);
            // Нормализация импортёра сбила бы замер громкости −22 LUFS.
            StringAssert.Contains("normalize: 0", meta, info.Resource);
            string guid = GuidOf(meta);
            Assert.That(guid, Has.Length.EqualTo(32), info.Resource);
            Assert.That(guids.Add(guid), Is.True, info.Resource + ": GUID повторяется");
        }
    }

    [Test]
    public void LicenceFileListsEveryTrack()
    {
        string licences = File.ReadAllText(Path.Combine(Folder, "LICENSES.md"), Encoding.UTF8);
        foreach (Director.TrackInfo info in Director.Tracks)
            StringAssert.Contains(info.Resource + ".ogg", licences, info.Resource);
    }

    private static string GuidOf(string meta)
    {
        foreach (string line in meta.Split('\n'))
            if (line.StartsWith("guid: ", StringComparison.Ordinal)) return line.Substring(6).Trim();
        return "";
    }

    /// <summary>Заголовок Vorbis (каналы, частота) и гранула последней страницы Ogg.</summary>
    private static void ReadOgg(string path, out int channels, out int rate, out long samples)
    {
        byte[] data = File.ReadAllBytes(path);
        Assert.That(data.Length, Is.GreaterThan(1024), path + ": не клип (указатель LFS?)");
        Assert.That(Encoding.ASCII.GetString(data, 0, 4), Is.EqualTo("OggS"), path);

        int id = IndexOf(data, new byte[] { 1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }, 0);
        Assert.That(id, Is.GreaterThanOrEqualTo(0), path + ": нет заголовка Vorbis");
        channels = data[id + 11];
        rate = BitConverter.ToInt32(data, id + 12);

        int last = -1;
        for (int i = data.Length - 27; i >= 0; i--)
            if (data[i] == 'O' && data[i + 1] == 'g' && data[i + 2] == 'g' && data[i + 3] == 'S') { last = i; break; }
        Assert.That(last, Is.GreaterThanOrEqualTo(0), path);
        Assert.That(data[last + 5] & 4, Is.EqualTo(4), path + ": последняя страница без флага конца потока");
        samples = BitConverter.ToInt64(data, last + 6);
    }

    private static int IndexOf(byte[] data, byte[] pattern, int from)
    {
        for (int i = from; i <= data.Length - pattern.Length; i++)
        {
            int k = 0;
            while (k < pattern.Length && data[i + k] == pattern[k]) k++;
            if (k == pattern.Length) return i;
        }
        return -1;
    }
}
