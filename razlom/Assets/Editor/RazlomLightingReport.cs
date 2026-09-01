using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Печатает ФАКТИЧЕСКОЕ состояние освещения из работающей игры.
///
/// Заведено 1 сентября после того, как причина отсутствующих теней была
/// названа неверно трижды подряд — по атласу, по шейдеру пола и по режиму
/// запекания. Каждый раз вывод делался из файлов проекта, и каждый раз
/// файл говорил не то, что происходит в кадре.
///
/// Смысл отчёта именно в том, чтобы читать значения ПОСЛЕ инициализации:
/// конвейер, который реально активен, флаги камеры, созданной в рантайме,
/// и режимы рендереров, собранных кодом. Ни одно из этого не видно в
/// .unity и .asset до запуска.
///
/// Запускать в Play mode: меню «Разлом → Диагностика → Отчёт по свету».
/// </summary>
public static class RazlomLightingReport
{
    [MenuItem("Разлом/Диагностика/Отчёт по свету")]
    public static void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ОТЧЁТ ПО СВЕТУ ===");
        sb.AppendLine(Application.isPlaying
            ? "Режим: PLAY (данные боевые)"
            : "Режим: РЕДАКТОР — запусти игру и выполни снова, иначе смысла мало");

        // ---- конвейер, который реально активен ----
        RenderPipelineAsset active = GraphicsSettings.currentRenderPipeline;
        sb.AppendLine("\n-- Конвейер --");
        sb.AppendLine("  активный ассет: " + (active != null ? active.name : "НЕТ (встроенный)"));
        sb.AppendLine("  уровень качества: " + QualitySettings.names[QualitySettings.GetQualityLevel()]);

        if (active is UniversalRenderPipelineAsset urp)
        {
            sb.AppendLine("  тени главного света: " + urp.supportsMainLightShadows);
            sb.AppendLine("  разрешение карты теней: " + urp.mainLightShadowmapResolution);
            sb.AppendLine("  дистанция теней: " + urp.shadowDistance);
            sb.AppendLine("  каскадов: " + urp.shadowCascadeCount);
            sb.AppendLine("  мягкие тени: " + urp.supportsSoftShadows);
            sb.AppendLine("  режим главного света: " + urp.mainLightRenderingMode);
            sb.AppendLine("  depth bias: " + urp.shadowDepthBias
                          + "  normal bias: " + urp.shadowNormalBias);
        }
        else
        {
            sb.AppendLine("  !! активный ассет НЕ UniversalRenderPipelineAsset");
        }

        // ---- источники света ----
        sb.AppendLine("\n-- Источники света --");
        Light[] lights = Object.FindObjectsByType<Light>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (lights.Length == 0) sb.AppendLine("  НИ ОДНОГО");
        foreach (Light light in lights)
        {
            sb.AppendLine($"  «{light.name}» активен={light.isActiveAndEnabled} тип={light.type} " +
                          $"режим={light.lightmapBakeType} тени={light.shadows} " +
                          $"сила={light.shadowStrength:0.00} яркость={light.intensity:0.00} " +
                          $"culling=0x{light.cullingMask:X} слой={light.gameObject.layer}");
        }

        // ---- камеры ----
        sb.AppendLine("\n-- Камеры --");
        Camera[] cameras = Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            sb.AppendLine($"  «{camera.name}» активна={camera.isActiveAndEnabled} " +
                          $"орто={camera.orthographic} размер={camera.orthographicSize:0.00} " +
                          $"far={camera.farClipPlane:0} позиция={camera.transform.position} " +
                          $"тени={(data != null ? data.renderShadows.ToString() : "нет URP-данных")} " +
                          $"пост={(data != null ? data.renderPostProcessing.ToString() : "?")}");
        }

        // ---- кто отбрасывает и принимает ----
        sb.AppendLine("\n-- Рендереры (первые 12 видимых) --");
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int shown = 0;
        int casting = 0;
        int receiving = 0;
        foreach (Renderer renderer in renderers)
        {
            if (renderer.shadowCastingMode != ShadowCastingMode.Off) casting++;
            if (renderer.receiveShadows) receiving++;
            if (shown++ < 12)
            {
                sb.AppendLine($"  «{renderer.name}» бросает={renderer.shadowCastingMode} " +
                              $"принимает={renderer.receiveShadows} " +
                              $"шейдер={(renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : "нет")}");
            }
        }
        sb.AppendLine($"  ИТОГО: рендереров {renderers.Length}, " +
                      $"бросают тень {casting}, принимают {receiving}");

        // ---- окружение ----
        sb.AppendLine("\n-- Окружение --");
        sb.AppendLine("  ambient mode: " + RenderSettings.ambientMode);
        sb.AppendLine("  ambient intensity: " + RenderSettings.ambientIntensity);
        sb.AppendLine("  sky/equator/ground: " + RenderSettings.ambientSkyColor + " "
                      + RenderSettings.ambientEquatorColor + " " + RenderSettings.ambientGroundColor);

        Debug.Log(sb.ToString());
    }
}
