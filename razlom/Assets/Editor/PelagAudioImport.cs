using System;
using System.Collections.Generic;
using Game.View;
using UnityEditor;
using UnityEngine;

// Исходные записи лежат в ART; здесь только настройки игровых производных.
public sealed class PelagAudioImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Resources/Audio/Combat/Pelag/")) return;
        var importer = (AudioImporter)assetImporter;
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = false;
        importer.loadInBackground = false;
    }

    [InitializeOnLoadMethod]
    static void Schedule() => EditorApplication.delayCall += Install;

    // Однократная миграция не перетирает дальнейшее сведение владельца в Inspector.
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var profile = AssetDatabase.LoadAssetAtPath<CombatAudioProfile>("Assets/Resources/Combat/CombatAudio.asset");
        if (profile == null) return;
        InstallWhirlwindSet(profile);
        if (profile.PelagAudioRevision >= 1) return;
        string[] names = { "Attack_01", "Attack_02", "Attack_03", "Attack_04", "Attack_05", "Cleave", "Dash", "Whirlwind", "BlazePrepare", "BlazeFire", "Finisher" };
        var clips = new AudioClip[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Audio/Combat/Pelag/" + names[i] + ".wav");
            if (clips[i] == null) return;
        }
        var entries = new List<CombatSoundEntry>(profile.Sounds);
        Set(entries, CombatSound.PelagAttack, new[] { clips[0], clips[1], clips[2], clips[3], clips[4] }, 1.35f, .025f, 55);
        Set(entries, CombatSound.Cleave, new[] { clips[5] }, 1.3f, .01f, 65);
        Set(entries, CombatSound.Dash, new[] { clips[6] }, 1.55f, .015f, 60);
        Set(entries, CombatSound.Whirlwind, new[] { clips[7] }, 1.3f, 0f, 65);
        Set(entries, CombatSound.BlazePrepare, new[] { clips[8] }, 1.3f, 0f, 55);
        Set(entries, CombatSound.BlazeFire, new[] { clips[9] }, .8f, 0f, 60);
        Set(entries, CombatSound.Finisher, new[] { clips[10] }, 1.2f, .02f, 85);
        profile.Sounds = entries.ToArray();
        profile.PelagAudioRevision = 1;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[PelagAudio] Installed 11 prepared clips from 7 owner recordings.");
    }

    /// <summary>
    /// Набор Вихря из записей Epidemic Sound владельца (24 сентября): каст с
    /// пиком на 0,21 с, три импульса удержания, три удара по толпе, конец.
    /// Ревизия 2 — ставится один раз, дальнейшее сведение в Inspector не трогается.
    /// </summary>
    static void InstallWhirlwindSet(CombatAudioProfile profile)
    {
        // Ревизия 4 (24.09): сведение v3 — семья сабли (Attack_01..05 + HitBody/HitMetal),
        // низ из Epidemic только для веса; конец Вихря без звука.
        if (profile.PelagAudioRevision >= 4) return;
        AudioClip Load(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Audio/Combat/Pelag/" + name + ".wav");
        var cast = new[] { Load("Whirlwind_01"), Load("Whirlwind_02") };
        var pulse = new[] { Load("WhirlwindPulse_01"), Load("WhirlwindPulse_02"), Load("WhirlwindPulse_03") };
        var hit = new[] { Load("WhirlwindHit_01"), Load("WhirlwindHit_02"), Load("WhirlwindHit_03") };
        foreach (var clip in cast) if (clip == null) return;
        foreach (var clip in pulse) if (clip == null) return;
        foreach (var clip in hit) if (clip == null) return;
        var entries = new List<CombatSoundEntry>(profile.Sounds);
        Set(entries, CombatSound.Whirlwind, cast, 1.4f, .02f, 65);
        Set(entries, CombatSound.WhirlwindPulse, pulse, 1.25f, .03f, 60);
        Set(entries, CombatSound.WhirlwindHit, hit, 1.3f, .03f, 72);
        Set(entries, CombatSound.WhirlwindEnd, System.Array.Empty<AudioClip>(), .8f, .02f, 50);
        profile.Sounds = entries.ToArray();
        profile.PelagAudioRevision = 4;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[PelagAudio] Whirlwind set installed: 2 casts, 3 pulses, 3 hits, no end cue.");
    }

    static void Set(List<CombatSoundEntry> entries, CombatSound sound, AudioClip[] clips, float gain, float variation, int priority)
    {
        var entry = entries.Find(item => item != null && item.Sound == sound);
        if (entry == null) { entry = new CombatSoundEntry { Sound = sound }; entries.Add(entry); }
        entry.Clips = clips; entry.Gain = gain; entry.Pitch = 1f;
        entry.PitchVariation = variation; entry.Priority = priority; entry.MaxPerFrame = 1;
    }
}
