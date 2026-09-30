using System.Collections;
using System.IO;
using System.Text;
using Game.Sim;
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
        bool _smithHeard;
        bool _passed=true;
        float _master,_effects,_music;bool _settingsCaptured;
        public void Initialize(string output){_output=Path.GetFullPath(output);Directory.CreateDirectory(_output);}
        void Check(bool value,string message){_report.AppendLine((value?"PASS ":"FAIL ")+message);if(!value)_passed=false;}
        IEnumerator Start()
        {
            yield return new WaitForSeconds(1);
            _driver=FindAnyObjectByType<TickDriver>();_sound=FindAnyObjectByType<CampSoundscape>();
            if(_sound==null){Check(false,"soundscape installed");Finish();yield break;}
            _master=GameUserSettings.MasterVolume;_effects=GameUserSettings.EffectsVolume;_music=GameUserSettings.MusicVolume;_settingsCaptured=true;
            GameUserSettings.SetAudio(1,1,.75f);
            // Только профиль съёмки: для аудита доступны все рабочие места.
            while(_driver.Session.Camp.AttemptCount<3)_driver.Session.Camp.RecordRealAttemptEnded(1,0);
            yield return null;
            foreach(var layer in _sound.Layers)Check(layer.Source!=null && layer.Source.clip!=null,"clip assigned: "+layer.Place);
            var root=_sound.CampRoot.transform;
            CampServiceNpc alchemist=null;
            foreach(var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
                if(npc.Kind==CampServiceKind.Alchemist){alchemist=npc;break;}
            Check(alchemist!=null,"alchemist sound stop exists");
            yield return Listen(root.Find("Campfire").position+new Vector3(-1.8f,0,-1),"01-fire",6);
            float fireNear=Layer(CampSoundscape.Place.Fire).Source.volume;
            yield return Listen(root.Find("Anchor - Smith").position,"02-smith",18);
            Check(_smithHeard,"explicit hammer contact is audible beside forge");
            Check(Layer(CampSoundscape.Place.Fire).Source.volume<fireNear*.8f,"fire becomes quieter at forge");
            if(alchemist!=null)
            {
                yield return Listen(alchemist.Approach,"03-cauldron",7);
                Check(Layer(CampSoundscape.Place.Cauldron).Source.volume>.15f,"cauldron audible by alchemist");
            }
            var passage=FindAnyObjectByType<CampRiverPassage>();
            Check(passage!=null,"bridge river sound stop exists");
            if(passage!=null)
            {
                var bridge=passage.BridgeBounds;
                yield return Listen(new Vector3(bridge.center.x,0,bridge.max.z+1f),"04-river-side",6);
            }
            Check(Layer(CampSoundscape.Place.River).Source.volume>.08f,"river audible at camp edge");
            var ambient=FindAnyObjectByType<CampAmbientSounds>();
            var drone=ambient!=null?ambient.transform.Find("Гул Разлома")?.GetComponent<AudioSource>():null;
            Check(drone!=null && drone.clip!=null,"arch hum assigned");
            if(drone!=null && ambient.DroneAnchor!=null)
            {
                yield return Listen(ambient.DroneAnchor.position+Vector3.back*2.6f,"05-arch",4);
                float near=drone.volume;Check(near>.01f,"arch hum audible near arch");
                CampPreparationView.Instance?.Open();yield return new WaitForSeconds(1);
                Check(drone.volume<near*.8f,"preparation window softens arch hum");CampPreparationView.Instance?.Close();
                yield return Listen(root.Find("Campfire").position+new Vector3(-1.8f,0,-1),"06-home",4);
                Check(drone.volume<near*.3f,"arch hum remains local away from arch");
            }
            float beforePause=Layer(CampSoundscape.Place.Fire).Source.volume;
            _driver.SetGameplayPaused(true);yield return new WaitForSeconds(1.5f);
            Check(Layer(CampSoundscape.Place.Fire).Source.volume<beforePause*.8f,"pause softens camp mix");
            _driver.SetGameplayPaused(false);yield return new WaitForSeconds(1);
            float master=GameUserSettings.MasterVolume,effects=GameUserSettings.EffectsVolume,music=GameUserSettings.MusicVolume;
            GameUserSettings.SetAudio(master,0,music);yield return new WaitForSeconds(1.5f);
            foreach(var layer in _sound.Layers)if(layer.Place!=CampSoundscape.Place.Music)Check(layer.Source.volume<.001f,"effects slider mutes: "+layer.Place);
            Check(Layer(CampSoundscape.Place.Music).Source.volume>.02f,"music independent of effects slider");
            GameUserSettings.SetAudio(master,effects,0);yield return new WaitForSeconds(1.5f);
            Check(Layer(CampSoundscape.Place.Music).Source.volume<.001f,"music slider mutes camp theme");
            GameUserSettings.SetAudio(master,effects,music);
            _driver.Session.EnterRift();yield return new WaitForSeconds(3);
            foreach(var layer in _sound.Layers)Check(!layer.Source.isPlaying && layer.Source.volume<.001f,"no camp sound in rift: "+layer.Place);
            if(drone!=null)Check(!drone.isPlaying && drone.volume<.001f,"no arch hum in rift");
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
            float until=Time.time+seconds-2;
            bool contact=false;
            while(Time.time<until)
            {
                if(name=="02-smith" && !contact)
                {
                    // Проверяем реальное событие контакта; рабочий клип добавляет владелец.
                    foreach(var life in FindObjectsByType<CampNpcLife>())if(life.Npc?.Kind==CampServiceKind.Smith)
                    {life.CampWorkContact("hammer");foreach(var source in life.GetComponentsInChildren<AudioSource>())_smithHeard|=source.isPlaying && source.volume>.001f;contact=true;break;}
                    Check(contact,"smith animation-contact receiver installed");
                }
                yield return null;
            }
        }
        void Finish(){_report.AppendLine(_passed?"ALL CAMP SOUND CHECKS PASSED":"CAMP SOUND HAS FAILURES");File.WriteAllText(Path.Combine(_output,"sound-qa.txt"),_report.ToString());Debug.Log("[camp-sound] passed="+_passed);}
        void OnDestroy(){if(_settingsCaptured)GameUserSettings.SetAudio(_master,_effects,_music);if(_driver!=null)_driver.SetGameplayPaused(false);}
    }
}
