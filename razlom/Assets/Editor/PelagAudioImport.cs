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
        if (profile == null || profile.PelagAudioRevision >= 1) return;
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

    static void Set(List<CombatSoundEntry> entries, CombatSound sound, AudioClip[] clips, float gain, float variation, int priority)
    {
        var entry = entries.Find(item => item != null && item.Sound == sound);
        if (entry == null) { entry = new CombatSoundEntry { Sound = sound }; entries.Add(entry); }
        entry.Clips = clips; entry.Gain = gain; entry.Pitch = 1f;
        entry.PitchVariation = variation; entry.Priority = priority; entry.MaxPerFrame = 1;
    }
}
