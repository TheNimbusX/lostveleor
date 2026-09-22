using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    // Проверка запускается только явным флагом съёмки или редакторным тестом.
    [DefaultExecutionOrder(2200)]
    public sealed class CampFinishCapture : MonoBehaviour
    {
        string _output;bool _frame;Camera _camera;Vector3 _focus;float _size;
        public void Initialize(string output,bool frame=false){_output=output;_frame=frame;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            var args=System.Environment.GetCommandLineArgs();if(System.Array.IndexOf(args,"-capture-camp-finish")<0)return;
            int index=System.Array.IndexOf(args,"-capture-out");var go=new GameObject("Camp finish capture");go.AddComponent<CampFinishCapture>().Initialize(index>=0?args[index+1]:Application.persistentDataPath,true);
        }
        static FixVec2 Flat(Vector3 p)=>new FixVec2(Fix64.FromRaw((long)(p.x*Fix64.One.Raw)),Fix64.FromRaw((long)(p.z*Fix64.One.Raw)));
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1);
            var river=FindAnyObjectByType<CampRiver>();var camp=CampPlayerView.Instance;var magic=FindAnyObjectByType<CampMagicCircle>();
            var text=new StringBuilder();bool passed=true;
            void Check(bool ok,string name){text.AppendLine((ok?"PASS ":"FAIL ")+name);if(!ok)passed=false;}
            Check(river!=null && camp!=null && camp.WalkMap!=null,"river and navigation ready");
            if(river==null || camp==null || camp.WalkMap==null)yield break;
            int water=0,land=0;
            var passage=river.GetComponent<CampRiverPassage>();
            for(float x=-100;x<=100;x+=.5f)
            {
                float centre=river.CentreAt(x);
                foreach(float z in passage!=null?new[]{centre}:new[]{centre,centre-river.Width*.5f-3,centre+river.Width*.5f})
                {var point=river.transform.TransformPoint(new Vector3(x,0,z));if((passage==null || !passage.IsOpen(river,point)) && camp.WalkMap.Contains(Flat(point)))water++;}
                if(x>=-12 && x<=12 && camp.WalkMap.Contains(Flat(river.transform.TransformPoint(new Vector3(x,0,river.LandEdge(x)+.6f)))))land++;
            }
            Check(water==0,"water blocked outside authored crossing, including river ends: "+water);
            Check(land>=10,"near bank reachable samples: "+land);
            int colliders=river.GetComponentsInChildren<Collider>().Length;
            Check(colliders==0,"river decoration has no physical colliders: "+colliders);
            foreach(var filter in river.GetComponentsInChildren<MeshFilter>())Check(!CampPlayerView.UsedByNavigation(filter),"decorative mesh excluded: "+filter.name);
            Check(magic!=null && magic.DecorationRoot!=null,"authored magic circle exists");
            if(_frame)
            {
                _camera=Camera.main;var follow=_camera.GetComponent<CameraFollow>();if(follow!=null)follow.enabled=false;
                var juice=_camera.GetComponent<CombatCameraJuice>();if(juice!=null)juice.enabled=false;
                _focus=magic.Centre.position+Vector3.up*.6f;_size=9.4f;
                var args=System.Environment.GetCommandLineArgs();int detail=System.Array.IndexOf(args,"-capture-camp-detail");
                if(detail>=0 && detail+1<args.Length)
                {
                    if(args[detail+1]=="flags"){_focus=magic.FireAltar.position+Vector3.up*1.5f;_size=2.1f;}
                    if(args[detail+1]=="lights"){_focus=FindAnyObjectByType<CampAmbience>().transform.Find("Campfire").position+Vector3.up*.5f;_size=3.2f;}
                    if(args[detail+1]=="river"){_focus=river.transform.TransformPoint(new Vector3(0,0,1.8f));_size=10.8f;}
                    if(args[detail+1]=="river-turn"){_focus=river.transform.TransformPoint(new Vector3(7,0,11));_size=15;}
                    if(args[detail+1]=="alchemist"){_focus=new Vector3(-3,0,-24);_size=8;}
                    if(args[detail+1]=="ice"){_focus=magic.IceAltar.position+Vector3.up*1.1f;_size=3.2f;}
                    if(args[detail+1]=="poison"){_focus=magic.AlchemyAltar.position+Vector3.up*.8f;_size=3.2f;}
                }
                Frame();
            }
            Directory.CreateDirectory(_output);File.WriteAllText(Path.Combine(_output,"river-qa.txt"),text.ToString());
            if(passed)Debug.Log("[camp-finish-qa] PASS blockedWater="+water+" accessibleBank="+land+" decorativeColliders="+colliders);else Debug.LogError("[camp-finish-qa] FAILED\n"+text);
        }
        void LateUpdate(){if(_frame)Frame();}
        void Frame(){if(_camera==null)return;_camera.transform.rotation=Quaternion.Euler(48,35,0);_camera.transform.position=_focus-_camera.transform.forward*80;_camera.orthographicSize=_size;}
    }
}
