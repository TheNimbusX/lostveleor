using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Разовая диагностика: что Unity реально видит в купленных клипах прыжка.
///
/// Появилась после того, как клипы приезжали длиной в один кадр, а перебор
/// гипотез по .meta и по бинарнику FBX не сходился. Печатает факты вместо
/// предположений: какие подассеты есть, какой длины, какие такты разобраны
/// и что говорит импортёр.
/// </summary>
public static class RazlomLeapClipDiagnostics
{
    private static readonly string[] Files =
    {
        "Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_LeapThrow.fbx",
        "Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_LeapAir.fbx",
        "Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_LeapLand.fbx",
        // Контроль: этот клип собирается правильно, с ним и сравниваем.
        "Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_AnchorLeap.fbx"
    };

    /// <summary>
    /// Сколько движения реально есть в каждой фазе собранного клипа.
    ///
    /// Отвечает на вопрос «замах и посадка потерялись в сборке или уже при
    /// проигрывании»: если движение в окнах есть, виноват аниматор, если нет —
    /// сборщик берёт не те куски источников.
    /// </summary>
    private static void AppendBakedPhases(StringBuilder report)
    {
        const string bakedPath = "Assets/Resources/Characters/Pelag_v5/Pelag_AN_AnchorLeap.anim";
        var baked = AssetDatabase.LoadAssetAtPath<AnimationClip>(bakedPath);
        var rig = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
        report.AppendLine("==== собранный Pelag_AN_AnchorLeap ====");
        if (baked == null || rig == null) { report.AppendLine("  не найден"); return; }
        report.AppendLine($"  length={baked.length:F3}с fps={baked.frameRate} кривых={AnimationUtility.GetCurveBindings(baked).Length}");

        var sample = Object.Instantiate(rig);
        sample.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var animator in sample.GetComponentsInChildren<Animator>()) animator.enabled = false;
            string[] watched = { "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:LeftUpLeg", "mixamorig:Spine" };
            var bones = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in sample.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("mixamorig:")) bones[t.name] = t;

            (string name, float from, float to)[] phases =
            {
                ("замах  0.00-0.30", 0f, 0.30f),
                ("полёт  0.30-0.80", 0.30f, 0.80f),
                ("посадка 0.80-конец", 0.80f, baked.length)
            };

            var hips = bones["mixamorig:Hips"];
            foreach (var phase in phases)
            {
                var moved = new StringBuilder();
                foreach (string boneName in watched)
                {
                    if (!bones.TryGetValue(boneName, out var bone)) continue;
                    float total = 0f;
                    Quaternion previous = Quaternion.identity;
                    bool first = true;
                    for (float t = phase.from; t <= phase.to; t += 1f / 30f)
                    {
                        baked.SampleAnimation(sample, t);
                        if (!first) total += Quaternion.Angle(previous, bone.localRotation);
                        previous = bone.localRotation;
                        first = false;
                    }
                    moved.Append($"{boneName.Replace("mixamorig:", "")}={total:F1}° ");
                }
                // Высота таза за фазу: показывает, есть ли дуга полёта.
                float minY = float.MaxValue, maxY = float.MinValue;
                for (float t = phase.from; t <= phase.to; t += 1f / 30f)
                {
                    baked.SampleAnimation(sample, t);
                    minY = Mathf.Min(minY, hips.localPosition.y);
                    maxY = Mathf.Max(maxY, hips.localPosition.y);
                }
                report.AppendLine($"  {phase.name}: {moved}тазY размах={(maxY - minY):F4}");
            }
        }
        finally { Object.DestroyImmediate(sample); }
    }

    [MenuItem("Разлом/Пелаг/Диагностика клипов прыжка")]
    public static void Dump()
    {
        var report = new StringBuilder();
        foreach (string path in Files)
        {
            report.AppendLine("==== " + System.IO.Path.GetFileName(path) + " ====");

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                report.AppendLine("  импортёра нет — файл не найден");
                continue;
            }

            report.AppendLine($"  animationType={importer.animationType} importAnimation={importer.importAnimation} " +
                              $"resample={importer.resampleCurves} useFileScale={importer.useFileScale}");

            var takes = importer.importedTakeInfos;
            report.AppendLine($"  importedTakeInfos: {(takes == null ? "null" : takes.Length.ToString())}");
            if (takes != null)
                foreach (var take in takes)
                    report.AppendLine($"    такт '{take.name}' defaultClip='{take.defaultClipName}' " +
                                      $"start={take.startTime:F3} stop={take.stopTime:F3} rate={take.sampleRate:F2}");

            var defaults = importer.defaultClipAnimations;
            report.AppendLine($"  defaultClipAnimations: {(defaults == null ? "null" : defaults.Length.ToString())}");
            if (defaults != null)
                foreach (var clip in defaults)
                    report.AppendLine($"    '{clip.name}' take='{clip.takeName}' {clip.firstFrame}..{clip.lastFrame}");

            var configured = importer.clipAnimations;
            report.AppendLine($"  clipAnimations (наши): {(configured == null ? "null" : configured.Length.ToString())}");
            if (configured != null)
                foreach (var clip in configured)
                    report.AppendLine($"    '{clip.name}' take='{clip.takeName}' {clip.firstFrame}..{clip.lastFrame}");

            report.AppendLine("  подассеты:");
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip)
                    report.AppendLine($"    AnimationClip '{clip.name}' length={clip.length:F3}с fps={clip.frameRate} " +
                                      $"кривых={UnityEditor.AnimationUtility.GetCurveBindings(clip).Length}");
                else if (asset != null)
                    report.AppendLine($"    {asset.GetType().Name} '{asset.name}'");
            }
            report.AppendLine();
        }

        AppendBakedPhases(report);

        // Самое важное печатаем отдельным сообщением: общий отчёт длинный и
        // консоль его обрезает ровно на этом месте.
        string windows = RazlomPelagLeapClips.DescribeWindows();
        report.AppendLine();
        report.Append(windows);
        Debug.Log(windows);

        Debug.Log(report.ToString());
        string dump = "Assets/../../artifacts/leap-clip-diagnostics.txt";
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath(dump), report.ToString());
        Debug.Log("[Pelag leap] отчёт сохранён: " + System.IO.Path.GetFullPath(dump));
    }
}
