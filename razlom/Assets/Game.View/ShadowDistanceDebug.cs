using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(-10000)]
public class ShadowDistanceDebug : MonoBehaviour
{
    private float lastValue = -999f;

    private void Awake()
    {
        Print("AWAKE");
    }

    private void OnEnable()
    {
        Print("ON ENABLE");
    }

    private void Start()
    {
        Print("START");
    }

    private void Update()
    {
        var asset =
            GraphicsSettings.currentRenderPipeline
            as UniversalRenderPipelineAsset;

        if (asset == null)
            return;

        if (!Mathf.Approximately(lastValue, asset.shadowDistance))
        {
            Debug.LogWarning(
                $"[SHADOW DEBUG] UPDATE: {lastValue} -> {asset.shadowDistance} | " +
                $"Asset: {asset.name} | Frame: {Time.frameCount}"
            );

            lastValue = asset.shadowDistance;
        }
    }

    private void Print(string stage)
    {
        var asset =
            GraphicsSettings.currentRenderPipeline
            as UniversalRenderPipelineAsset;

        if (asset == null)
        {
            Debug.LogWarning($"[SHADOW DEBUG] {stage}: no URP asset");
            return;
        }

        Debug.LogWarning(
            $"[SHADOW DEBUG] {stage}: Distance={asset.shadowDistance} | " +
            $"Asset={asset.name} | Frame={Time.frameCount}"
        );

        lastValue = asset.shadowDistance;
    }
}