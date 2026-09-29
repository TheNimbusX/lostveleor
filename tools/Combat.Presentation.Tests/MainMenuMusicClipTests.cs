using System;
using System.IO;
using System.Text;
using NUnit.Framework;

/// <summary>
/// Тема главного меню (Resources/Audio/Music, тема владельца 29.09): вступление один раз
/// и петля с запечённым кроссфейдом, которую крутит AudioSource.loop. Стык держится на
/// длинах: петля — целые такты 85 BPM, вступление кончается за 10 мс до сильной доли.
/// Импорт — сжатые в памяти и загруженные заранее: PlayScheduled встаёт в отсчёт только так.
/// </summary>
public sealed class MainMenuMusicClipTests
{
    private const int Rate = 48000;
    private const double Bar = 4 * 60.0 / 85.0;
    /// <summary>Первая сильная доля исходника и срез на 10 мс раньше неё.</summary>
    private const double FirstDownbeat = 0.040;
    private const double CutBefore = 0.010;
    private static readonly string[] Clips = { "MainMenuTheme_Intro", "MainMenuTheme_Loop" };

    private static string Folder => Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "Audio", "Music");

    [Test]
    public void BothClipsAreStereo48kVorbis()
    {
        foreach (string clip in Clips)
        {
            ReadOgg(Path.Combine(Folder, clip + ".ogg"), out int channels, out int rate, out long samples);
            Assert.That(channels, Is.EqualTo(2), clip);
            Assert.That(rate, Is.EqualTo(Rate), clip);
            Assert.That(samples / (double)Rate, Is.InRange(20.0, 120.0), clip);
        }
    }

    [Test]
    public void LoopIsWholeBarsAndIntroEndsJustBeforeADownbeat()
    {
        ReadOgg(Path.Combine(Folder, "MainMenuTheme_Loop.ogg"), out _, out _, out long loop);
        double bars = loop / (double)Rate / Bar;
        Assert.That(Math.Abs(bars - Math.Round(bars)) * Bar, Is.LessThan(.005), $"петля {loop} сэмплов — не целые такты");

        ReadOgg(Path.Combine(Folder, "MainMenuTheme_Intro.ogg"), out _, out _, out long intro);
        double introBars = (intro / (double)Rate - (FirstDownbeat - CutBefore)) / Bar;
        Assert.That(Math.Abs(introBars - Math.Round(introBars)) * Bar, Is.LessThan(.005),
            $"вступление {intro} сэмплов кончается не у сильной доли — петля войдёт мимо такта");
    }

    [Test]
    public void ClipsImportCompressedInMemoryPreloadedWithTheirOwnGuids()
    {
        var guids = new System.Collections.Generic.HashSet<string>();
        foreach (string clip in Clips)
        {
            string meta = File.ReadAllText(Path.Combine(Folder, clip + ".ogg.meta"));
            // loadType 1 — Compressed In Memory: поток с диска мог бы опоздать к стыку.
            StringAssert.Contains("loadType: 1", meta, clip);
            StringAssert.Contains("compressionFormat: 1", meta, clip);
            StringAssert.Contains("preloadAudioData: 1", meta, clip);
            StringAssert.Contains("loadInBackground: 0", meta, clip);
            StringAssert.Contains("forceToMono: 0", meta, clip);
            // Нормализация импортёра разнесла бы громкость вступления и петли.
            StringAssert.Contains("normalize: 0", meta, clip);
            string guid = "";
            foreach (string line in meta.Split('\n'))
                if (line.StartsWith("guid: ", StringComparison.Ordinal)) guid = line.Substring(6).Trim();
            Assert.That(guid, Has.Length.EqualTo(32), clip);
            Assert.That(guids.Add(guid), Is.True, clip + ": GUID повторяется");
        }
    }

    [Test]
    public void MenuLoadsTheseClipsAndLicenceListsThem()
    {
        string view = File.ReadAllText(Path.Combine(RepoRoot.Path, "razlom", "Assets", "Game.View", "MainMenuView.cs"), Encoding.UTF8);
        string licences = File.ReadAllText(Path.Combine(Folder, "LICENSES.md"), Encoding.UTF8);
        foreach (string clip in Clips)
        {
            StringAssert.Contains("\"Audio/Music/" + clip + "\"", view, clip);
            StringAssert.Contains(clip + ".ogg", licences, clip);
        }
    }

    /// <summary>Заголовок Vorbis (каналы, частота) и гранула последней страницы Ogg — число сэмплов канала.</summary>
    private static void ReadOgg(string path, out int channels, out int rate, out long samples)
    {
        Assert.That(File.Exists(path), Is.True, path);
        byte[] data = File.ReadAllBytes(path);
        Assert.That(data.Length, Is.GreaterThan(1024), path + ": не клип (указатель LFS?)");
        Assert.That(Encoding.ASCII.GetString(data, 0, 4), Is.EqualTo("OggS"), path);
        int id = -1;
        for (int i = 0; i + 7 <= data.Length && id < 0; i++)
            if (data[i] == 1 && Encoding.ASCII.GetString(data, i + 1, 6) == "vorbis") id = i;
        Assert.That(id, Is.GreaterThanOrEqualTo(0), path + ": нет заголовка Vorbis");
        channels = data[id + 11];
        rate = BitConverter.ToInt32(data, id + 12);
        int last = -1;
        for (int i = data.Length - 27; i >= 0 && last < 0; i--)
            if (data[i] == 'O' && data[i + 1] == 'g' && data[i + 2] == 'g' && data[i + 3] == 'S') last = i;
        Assert.That(last, Is.GreaterThanOrEqualTo(0), path);
        Assert.That(data[last + 5] & 4, Is.EqualTo(4), path + ": последняя страница без флага конца потока");
        samples = BitConverter.ToInt64(data, last + 6);
    }
}
