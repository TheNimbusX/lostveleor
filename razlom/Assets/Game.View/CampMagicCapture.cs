using System.Collections;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(2200)]
    public sealed class CampMagicCapture : MonoBehaviour
    {
        Camera _camera;
        Vector3 _focus;
        IEnumerator Start()
        {
            var magic=FindAnyObjectByType<CampMagicCircle>();
            if(magic==null){Debug.LogError("[camp-magic-qa] Circle missing.");yield break;}
            _camera=Camera.main;
            var follow=_camera.GetComponent<CameraFollow>();if(follow!=null)follow.enabled=false;
            var juice=_camera.GetComponent<CombatCameraJuice>();if(juice!=null)juice.enabled=false;
            Bounds bounds=new Bounds(magic.Centre.position,Vector3.zero);
            foreach(var t in new[]{magic.FireAltar,magic.IceAltar,magic.AlchemyAltar,magic.EarthAltar})if(t!=null)bounds.Encapsulate(t.position);
            _focus=bounds.center+Vector3.up*.9f;Frame();
            yield return new WaitForSecondsRealtime(1);
            bool nav=CampPlayerView.Instance!=null && CampPlayerView.Instance.WalkMap!=null;
            int decorationColliders=0;
            foreach(var decoration in magic.GetComponentsInChildren<CampMagicDecoration>())decorationColliders+=decoration.GetComponentsInChildren<Collider>().Length;
            Debug.Log($"[camp-magic-qa] navigation={nav} decorativeColliders={decorationColliders} heart={magic.Heart.position:F3} lights={magic.GetComponentsInChildren<Light>().Length}");
            foreach(var renderer in magic.GetComponentsInChildren<Renderer>())
                if(renderer.name=="Пламя алтаря")
                {
                    Debug.Log($"[camp-magic-probe] flame enabled={renderer.enabled} visible={renderer.isVisible} bounds={renderer.bounds} scale={renderer.transform.lossyScale} material={renderer.sharedMaterial.name} shader={renderer.sharedMaterial.shader.name} texture={renderer.sharedMaterial.GetTexture("_BaseMap")} screen={_camera.WorldToScreenPoint(renderer.bounds.center)}");
                    CaptureFlame(renderer);
                }
            foreach(float z in new[]{-.05f,.055f,.15f,.25f})
            {
                Vector3 at=magic.FireAltar.TransformPoint(new Vector3(0,2,z));
                if(Physics.Raycast(at,Vector3.down,out var hit,8,~0,QueryTriggerInteraction.Ignore))Debug.Log($"[camp-magic-probe] bowl z={z:F3} hit={hit.point:F3} local={magic.FireAltar.InverseTransformPoint(hit.point):F3} collider={hit.collider.name}");
            }
            if(!nav || decorationColliders!=0)Debug.LogError("[camp-magic-qa] FAILED");
        }
        void LateUpdate()=>Frame();
        void CaptureFlame(Renderer renderer)
        {
            var args=System.Environment.GetCommandLineArgs();int index=System.Array.IndexOf(args,"-capture-out");if(index<0 || index+1>=args.Length)return;
            var obj=new GameObject("Flame inspection camera");var camera=obj.AddComponent<Camera>();camera.CopyFrom(_camera);camera.enabled=false;
            camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographicSize=1.8f;camera.aspect=1;
            camera.transform.rotation=_camera.transform.rotation;camera.transform.position=renderer.bounds.center-camera.transform.forward*10;
            var rt=RenderTexture.GetTemporary(512,512,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;int layer=renderer.gameObject.layer;Texture2D pixels=null;
            try
            {
                renderer.gameObject.layer=30;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                pixels=new Texture2D(512,512,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(args[index+1],"flame-probe.png"),pixels.EncodeToPNG());
            }
            finally{renderer.gameObject.layer=layer;RenderTexture.active=old;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);if(pixels!=null)Destroy(pixels);Destroy(obj);}
        }
        void Frame()
        {
            if(_camera==null)return;
            _camera.transform.rotation=Quaternion.Euler(48,35,0);_camera.transform.position=_focus-_camera.transform.forward*60;
            _camera.orthographicSize=6.65f;
        }
    }
}
