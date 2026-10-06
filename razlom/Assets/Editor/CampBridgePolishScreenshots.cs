using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Game.EditorTools
{
    public static class CampBridgePolishScreenshots
    {
        public static string Capture(string label)
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/camp-bridge-polish-2026-10-05"));
            Directory.CreateDirectory(output);
            Render(new Vector3(2.82f,0,-21.53f),Quaternion.Euler(41.72f,25.65f,.06f),7f,1600,1000,Path.Combine(output,label+"-bridge.png"));
            Render(new Vector3(-1.13905764f,-2.26161122f,-2.21761227f),new Quaternion(-.41177246f,-.2665685f,.126049533f,-.8625332f),17.5169373f,1920,1200,Path.Combine(output,label+"-overview.png"));
            return output;
        }
        static void Render(Vector3 centre,Quaternion rotation,float size,int width,int height,string path)
        {
            var go=new GameObject("Bridge review camera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();
            if(Camera.main!=null)camera.CopyFrom(Camera.main);
            camera.enabled=false;camera.orthographic=true;camera.orthographicSize=size;camera.aspect=width/(float)height;
            camera.nearClipPlane=.03f;camera.farClipPlane=300;
            camera.transform.SetPositionAndRotation(centre-rotation*Vector3.forward*55f,rotation);
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;Texture2D pixels=null;
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                if(pixels!=null)Object.DestroyImmediate(pixels);Object.DestroyImmediate(go);
            }
        }
    }
}
