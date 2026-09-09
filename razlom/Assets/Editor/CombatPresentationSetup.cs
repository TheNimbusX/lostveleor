using System;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CombatPresentationSetup
{
    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += EnsureProfiles;

    [MenuItem("Разлом/Подготовить профили боя")]
    public static void EnsureProfiles()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string folder = "Assets/Resources/Combat";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources", "Combat");
        if (AssetDatabase.LoadAssetAtPath<EnemyPresentationProfile>(folder + "/EnemyPresentation.asset") == null)
        {
            var reactions = ScriptableObject.CreateInstance<EnemyPresentationProfile>();
            ReadDeathClip(reactions.Guardian, "Forest_Guardian");
            ReadDeathClip(reactions.RootSwarm, "Forest_RootSwarm");
            AssetDatabase.CreateAsset(reactions, folder + "/EnemyPresentation.asset");
            AssetDatabase.SaveAssets();
        }
        var profile = AssetDatabase.LoadAssetAtPath<CombatAudioProfile>(folder + "/CombatAudio.asset");
        bool created = profile == null;
        if (created) profile = ScriptableObject.CreateInstance<CombatAudioProfile>();
        var entries = new System.Collections.Generic.List<CombatSoundEntry>(profile.Sounds);
        for (int i = 0; i < (int)CombatSound.Count; i++)
        {
            var sound = (CombatSound)i;
            if (profile.Find(sound) != null) continue;
            entries.Add(new CombatSoundEntry
            {
                Sound = sound, Priority = CombatAudioProfile.DefaultPriority(sound),
                Clips = LoadExisting(sound)
            });
        }
        if (!created && entries.Count == profile.Sounds.Length) return;
        profile.Sounds = entries.ToArray();
        if (created) AssetDatabase.CreateAsset(profile, folder + "/CombatAudio.asset");
        else EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    private static void ReadDeathClip(EnemyDeathPresentation settings, string mob)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            $"Assets/Resources/Characters/{mob}/{mob}_Combat.controller");
        if (controller == null) return;
        foreach (var state in controller.layers[0].stateMachine.states)
        {
            if (state.state.name != "DeathBack" || !(state.state.motion is AnimationClip clip)) continue;
            settings.ClipSeconds = clip.length;
            settings.StateSpeed = state.state.speed;
        }
    }

    private static AudioClip[] LoadExisting(CombatSound sound)
    {
        const string root = "Assets/Resources/Audio/Combat/";
        if (sound == CombatSound.Whirlwind)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(root + "whirlwind_pelag_pcm.wav");
            return clip == null ? Array.Empty<AudioClip>() : new[] { clip };
        }
        string folder = root + sound;
        if (!Directory.Exists(folder)) return Array.Empty<AudioClip>();
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        var clips = new AudioClip[guids.Length];
        for (int i = 0; i < clips.Length; i++)
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[i]));
        Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
        return clips;
    }
}
