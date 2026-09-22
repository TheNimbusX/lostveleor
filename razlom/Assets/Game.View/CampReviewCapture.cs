using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.View
{
    // Явная проверка готового лагеря ведёт героя обычным маршрутом и сохраняет фактические точки остановки.
    public sealed class CampReviewCapture : MonoBehaviour
    {
        string _output;
        CampPlayerView _camp;
        TickDriver _driver;
        readonly StringBuilder _report = new StringBuilder();
        bool _passed=true;
        public bool Finished { get; private set; }
        public void Initialize(string output) { _output=Path.GetFullPath(output);Directory.CreateDirectory(_output); }
        void Check(bool value,string message)
        {
            _report.AppendLine((value?"PASS ":"FAIL ")+message);
            if(!value){_passed=false;Debug.LogError("[camp-review] "+message);}
        }
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1);
            _camp=CampPlayerView.Instance;
            _driver=FindAnyObjectByType<TickDriver>();
            if(_camp==null || _camp.WalkMap==null){Check(false,"navigation initialized");Finish();yield break;}
            var root=FindAnyObjectByType<SceneWorldView>().CampRoot.transform;
            var magic=root.GetComponentInChildren<CampMagicCircle>();var river=root.GetComponentInChildren<CampRiver>();
            var origin=root.Find("Anchor - Player").position;
            yield return Walk(origin,"01-arrival");
            yield return Walk(root.Find("Anchor - Smith").position,"02-smith");
            yield return Walk(root.Find("Campfire").position+new Vector3(-1.8f,0,-1),"03-campfire");
            yield return Walk(root.Find("Anchor - Trader").position,"04-trader");
            // Ограда и крупные деревья отделяют наружный берег от игрового лагеря.
            // Точки взяты из связной области запечённой карты, чтобы проверить вид от самой границы.
            foreach(float x in river.GetComponent<CampRiverPassage>()!=null?new float[0]:new[]{12f,19f})
            {
                var outside=river.transform.TransformPoint(new Vector3(x,0,river.LandEdge(x)+1.3f));
                Check(_camp.WalkMap.FindPath(CampTrainingView.Flat(_camp.Position),CampTrainingView.Flat(outside)).Length==0,
                    "authored perimeter separates outer shore at river x="+x);
            }
            yield return Walk(new Vector3(1.3125f,0,-13.3125f),"05-bank-from-camp");
            yield return Walk(new Vector3(6.5625f,0,-7.8125f),"05b-turn-from-camp");
            yield return Walk(magic.IceAltar.TransformPoint(magic.IcePathSocket)+new Vector3(-.7f,0,-.4f),"06-ice");
            yield return Walk(magic.Centre.position+new Vector3(.8f,0,-.6f),"07-magic-centre");
            yield return Walk(magic.FireAltar.TransformPoint(magic.FirePathSocket)+new Vector3(-.5f,0,-.5f),"07b-fire");
            yield return Walk(magic.EarthAltar.TransformPoint(magic.EarthPathSocket)+new Vector3(-.65f,0,-.5f),"08-earth");
            yield return Walk(magic.AlchemyAltar.TransformPoint(magic.AlchemyPathSocket)+new Vector3(.6f,0,-.5f),"09-alchemy");
            yield return Walk(origin,"10-return");
            Check(_camp.ApproachTent(),"inventory approach route exists");
            float deadline=Time.unscaledTime+18;
            while(!_camp.InventoryOpen && Time.unscaledTime<deadline)yield return null;
            Check(_camp.InventoryOpen,"walking to player tent opens inventory");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(_output,"11-inventory.png"));
            var inventory=FindAnyObjectByType<CampInventoryView>();if(inventory!=null)inventory.Close();
            var decoration=root.Find("Художественный проход лагеря");
            Check(decoration!=null && decoration.GetComponentsInChildren<Collider>(true).Length==0,"new scenery has no physical colliders");
            int leaking=0;
            for(float x=-35;x<28;x+=.5f)
            {
                var p=river.transform.TransformPoint(new Vector3(x,0,river.CentreAt(x)));
                var passage=river.GetComponent<CampRiverPassage>();
                if((passage==null || !passage.IsOpen(river,p)) && _camp.WalkMap.Contains(CampTrainingView.Flat(p)))leaking++;
            }
            Check(leaking==0,"river centre blocked at every sampled section: "+leaking);
            Finish();
        }
        IEnumerator Walk(Vector3 wanted,string name,float searchRadius=1.5f)
        {
            Vector3 target=wanted;target.y=_camp.GroundHeight;
            bool exact=_camp.WalkMap.Contains(CampTrainingView.Flat(target));
            if(!exact)
            {
                float best=float.MaxValue;
                for(float z=-searchRadius;z<=searchRadius;z+=.15f)for(float x=-searchRadius;x<=searchRadius;x+=.15f)
                {
                    var at=target+new Vector3(x,0,z);float d=x*x+z*z;
                    if(d<best && _camp.WalkMap.Contains(CampTrainingView.Flat(at))){best=d;wanted=at;}
                }
                if(best==float.MaxValue){Check(false,name+" has no reachable approach");yield break;}
                _report.AppendLine("APPROACH "+name+" requested="+target.ToString("F2")+" shifted="+Mathf.Sqrt(best).ToString("F2")+" target="+wanted.ToString("F2"));target=wanted;
            }
            bool alreadyThere=Vector2.Distance(new Vector2(_camp.Position.x,_camp.Position.z),new Vector2(target.x,target.z))<=.42f;
            bool routed=alreadyThere || _camp.RouteTo(target);Check(routed,name+(alreadyThere?" already at destination":" route exists"));
            if(!routed)yield break;
            float deadline=Time.unscaledTime+20;int offMap=0,visualOffMap=0;float minHeight=float.MaxValue;
            while(Vector2.Distance(new Vector2(_camp.Position.x,_camp.Position.z),new Vector2(target.x,target.z))>.42f && Time.unscaledTime<deadline)
            {
                // Состояние движения проверяем до интерполяции отображаемой модели между тиками.
                if(!_camp.WalkMap.Contains(_driver.Session.CampSim.Entities.Position[0]))offMap++;
                if(!_camp.WalkMap.Contains(CampTrainingView.Flat(_camp.Position)))
                {
                    visualOffMap++;
                    _report.AppendLine("VISUAL-BOUNDARY "+name+" body="+_camp.Position.ToString("F4")+" sim="+_driver.Session.CampSim.Entities.Position[0]);
                }
                var body=_camp.Body.GetComponentInChildren<SkinnedMeshRenderer>();if(body!=null)minHeight=Mathf.Min(minHeight,body.bounds.size.y);
                yield return null;
            }
            float distance=Vector2.Distance(new Vector2(_camp.Position.x,_camp.Position.z),new Vector2(target.x,target.z));
            Check(distance<=.42f,name+" arrived distance="+distance.ToString("F3")+" position="+_camp.Position.ToString("F2"));
            Check(offMap==0,name+" simulation stayed on walk map: "+offMap);
            _report.AppendLine("INTERPOLATION "+name+" visual boundary frames="+visualOffMap);
            Check(minHeight==float.MaxValue || minHeight>.8f,name+" character pose remains valid");
            yield return new WaitForSecondsRealtime(.45f);
            Vector3 view=Camera.main.WorldToViewportPoint(_camp.Position+Vector3.up);
            Check(view.z>0 && view.x>.1f && view.x<.9f && view.y>.1f && view.y<.9f,name+" hero within game camera");
            var blockers=new HashSet<string>();Vector3 focus=_camp.Position+Vector3.up;
            foreach(var hit in Physics.RaycastAll(focus-Camera.main.transform.forward*100,Camera.main.transform.forward,99.8f,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(_camp.Body))blockers.Add(hit.transform.name);
            _report.AppendLine("SIGHTLINE "+name+" potential mesh occluders="+string.Join(",",blockers));
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(_output,name+".png"));
            File.WriteAllText(Path.Combine(_output,"walk-progress.txt"),_report.ToString());
        }
        void Finish()
        {
            Finished=true;_report.AppendLine(_passed?"ALL CAMP REVIEW CHECKS PASSED":"CAMP REVIEW HAS FAILURES");
            File.WriteAllText(Path.Combine(_output,"walk-review.txt"),_report.ToString());Debug.Log("[camp-review] finished passed="+_passed);
        }
    }
}
