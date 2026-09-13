using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.View
{
    // Отдельный флаг проверяет микс в собранной игре и записывает настоящие выходные сэмплы AudioRenderer.
    public sealed class CampSoundCapture : MonoBehaviour
    {
        string _output;
        readonly StringBuilder _report=new StringBuilder();
        TickDriver _driver;
        CampSoundscape _sound;
        bool _passed=true;
        public void Initialize(string output){_output=Path.GetFullPath(output);Directory.CreateDirectory(_output);}
        void Check(bool value,string message){_report.AppendLine((value?"PASS ":"FAIL ")+message);if(!value)_passed=false;}
        IEnumerator Start()
        {
            yield return new WaitForSeconds(1);
            _driver=FindAnyObjectByType<TickDriver>();_sound=FindAnyObjectByType<CampSoundscape>();
            if(_sound==null){Check(false,"soundscape installed");Finish();yield break;}
            foreach(var layer in _sound.Layers)Check(layer.Source!=null && layer.Source.clip!=null,"clip assigned: "+layer.Place);
            var root=_sound.CampRoot.transform;var magic=root.GetComponentInChildren<CampMagicCircle>();
            yield return Listen(root.Find("Campfire").position+new Vector3(-1.8f,0,-1),"01-fire",6);
            float fireNear=Layer(CampSoundscape.Place.Fire).Source.volume;
            yield return Listen(root.Find("Anchor - Smith").position,"02-smith",8);
            Check(Layer(CampSoundscape.Place.Smith).Source.isPlaying,"anvil sounds beside forge");
            Check(Layer(CampSoundscape.Place.Fire).Source.volume<fireNear*.8f,"fire becomes quieter at forge");
            yield return Listen(magic.AlchemyAltar.TransformPoint(magic.AlchemyPathSocket)+new Vector3(.6f,0,-.5f),"03-cauldron",7);
            Check(Layer(CampSoundscape.Place.Cauldron).Source.volume>.15f,"cauldron audible nearby");
            yield return Listen(new Vector3(6.5625f,0,-7.8125f),"04-river-side",6);
            Check(Layer(CampSoundscape.Place.River).Source.volume>.08f,"river audible at camp edge");
            float master=GameUserSettings.MasterVolume,effects=GameUserSettings.EffectsVolume,music=GameUserSettings.MusicVolume;
            GameUserSettings.SetAudio(master,0,music);yield return new WaitForSeconds(1.5f);
            foreach(var layer in _sound.Layers)if(layer.Place!=CampSoundscape.Place.Music)Check(layer.Source.volume<.001f,"effects slider mutes: "+layer.Place);
            Check(Layer(CampSoundscape.Place.Music).Source.volume>.02f,"music independent of effects slider");
            GameUserSettings.SetAudio(master,effects,0);yield return new WaitForSeconds(1.5f);
            Check(Layer(CampSoundscape.Place.Music).Source.volume<.001f,"music slider mutes camp theme");
            GameUserSettings.SetAudio(master,effects,music);
            _driver.Session.EnterRift();yield return new WaitForSeconds(3);
            foreach(var layer in _sound.Layers)Check(!layer.Source.isPlaying && layer.Source.volume<.001f,"no camp sound in rift: "+layer.Place);
            _driver.Session.ReturnToCamp();yield return new WaitForSeconds(3);
            Check(Layer(CampSoundscape.Place.Music).Source.isPlaying && _sound.CurrentBlend>.9f,"sound returns after rift");
            var smoke=FindAnyObjectByType<CampChimneySmoke>();
            Check(smoke!=null && smoke.Smoke.particleCount>0 && smoke.Smoke.particleCount<=32,"light chimney smoke active within particle budget");
            Check(smoke!=null && smoke.GetComponentsInChildren<Collider>().Length==0,"smoke does not block gameplay");
            Finish();
        }
        CampSoundscape.Layer Layer(CampSoundscape.Place place)
        {foreach(var layer in _sound.Layers)if(layer.Place==place)return layer;throw new System.InvalidOperationException("Missing sound "+place);}
        IEnumerator Listen(Vector3 at,string name,float seconds)
        {
            _driver.ClearCapturedInput();_driver.Session.CampSim.StopPlayerMovement();_driver.Session.CampSim.Entities.Position[0]=CampTrainingView.Flat(at);
            yield return new WaitForSeconds(2);
            _report.AppendLine("MIX "+name+" time="+Time.time.ToString("F2"));
            foreach(var layer in _sound.Layers)_report.AppendLine(layer.Place+" distance="+layer.Distance.ToString("F2")+" gain="+layer.Source.volume.ToString("F3")+" playing="+layer.Source.isPlaying);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(_output,name+".png"));
            File.WriteAllText(Path.Combine(_output,"sound-progress.txt"),_report.ToString());
            yield return new WaitForSeconds(seconds-2);
        }
        void Finish(){_report.AppendLine(_passed?"ALL CAMP SOUND CHECKS PASSED":"CAMP SOUND HAS FAILURES");File.WriteAllText(Path.Combine(_output,"sound-qa.txt"),_report.ToString());Debug.Log("[camp-sound] passed="+_passed);}
    }
}
