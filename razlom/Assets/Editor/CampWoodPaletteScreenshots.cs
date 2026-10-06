using System.IO;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Game.EditorTools
{
    [InitializeOnLoad]
    public static class CampWoodPaletteScreenshots
    {
        const string RuntimeRequest = "CampWoodPalette.RuntimeCapture";
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/camp-wood-palette-2026-10-05"));

        static CampWoodPaletteScreenshots() => EditorApplication.update += CaptureWhenPlaying;
        public static void RequestRuntime(string label) => SessionState.SetString(RuntimeRequest, label);
        static void CaptureWhenPlaying()
        {
            string label = SessionState.GetString(RuntimeRequest, "");
            if (label.Length == 0 || !Application.isPlaying || MainMenuView.IsOpen
                || CampPlayerView.Instance == null || !CampPlayerView.Instance.Active
                || Object.FindAnyObjectByType<CampBridgePolishProbe>() == null) return;
            SessionState.EraseString(RuntimeRequest);
            Capture(label);
        }

        public static string Capture(string label)
        {
            Directory.CreateDirectory(Output);
            Render(new Vector3(-1.13905764f,-2.26161122f,-2.21761227f),new Quaternion(-.41177246f,-.2665685f,.126049533f,-.8625332f),17.5169373f,1920,1200,Path.Combine(Output,label+"-overview.png"));
            var rotation = Quaternion.Euler(41.72f,25.65f,.06f);
            Render(new Vector3(5.4f,.9f,-4.8f),rotation,6.3f,1600,1000,Path.Combine(Output,label+"-trader.png"));
            Render(new Vector3(5.8f,1,10.2f),rotation,7.5f,1600,1000,Path.Combine(Output,label+"-arch.png"));
            return Output;
        }

        static void Render(Vector3 centre,Quaternion rotation,float size,int width,int height,string path)
        {
            var go = new GameObject("Camp wood review camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            if (Camera.main != null) camera.CopyFrom(Camera.main);
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = size; camera.aspect = width/(float)height;
            camera.nearClipPlane = .03f; camera.farClipPlane = 300;
            camera.transform.SetPositionAndRotation(centre-rotation*Vector3.forward*55f,rotation);
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var target = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels = new Texture2D(width,height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply(); File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                if (pixels != null) Object.DestroyImmediate(pixels); Object.DestroyImmediate(go);
            }
        }
    }
}
