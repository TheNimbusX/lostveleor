using System;
using System.Collections;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Изолированный сценарий запускается только явным флагом capture-плеера.
    internal sealed class HudReviewCapture : MonoBehaviour
    {
        public static bool Enabled { get; private set; }
        public static bool HoverXp => Enabled && _phase==2;
        public static int HoverSlot => Enabled && (_phase==1 || _phase==4) ? 0 : -1;
        static HudReviewCapture _instance;
        static int _phase=-1;
        static float _began=-1f;
        int _readyTickBefore,_cooldownTick;
        bool _checkedCast,_pressedCooldown;
        string _directory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args=Environment.GetCommandLineArgs();
            Enabled=Array.IndexOf(args,"-razlom-capture")>=0 && Array.IndexOf(args,"-capture-hud-review")>=0;
            if(!Enabled)return;
            _phase=-1; _began=-1f;
            _instance=new GameObject("HUD UX capture").AddComponent<HudReviewCapture>();
            int output=Array.IndexOf(args,"-capture-out");
            _instance._directory=output>=0 && output+1<args.Length?args[output+1]:Application.temporaryCachePath;
        }
        public static void FrameInput(TickDriver driver)
        {
            if(!Enabled || _instance==null || driver.Sim==null || CampPlayerView.Instance?.InputBlocked==true)return;
            if(_began<0f)_began=Time.unscaledTime;
            _instance.Advance(driver,Time.unscaledTime-_began);
        }
        void Advance(TickDriver driver,float elapsed)
        {
            var sim=driver.Sim;
            var hud=driver.GetComponent<PlayerHud>();
            int next=Mathf.Min(6,Mathf.FloorToInt(elapsed/2f));
            if(next!=_phase)
            {
                _phase=next;
                if(next==0)
                {
                    Debug.Log("[hud-qa] fonts="+GameTypography.Regular.name+"/"+GameTypography.Display.name+"; cyrillic="+GameTypography.Regular.HasCharacter('П'));
                    Debug.Log("[hud-qa] "+hud.ReviewAssets+"; direct-cutout="+(Resources.Load<Texture2D>("UI/HUD/PelagPortraitCutout")!=null));
                    Report(Resources.Load<Texture2D>("UI/HUD/PelagPortraitCutout")!=null,"portrait imports and loads as a 2D texture");
                }
                if(next==3)
                {
                    sim.Entities.Lavidium[Simulation.PlayerId]=Fix64.Zero;
                    _readyTickBefore=sim.AbilityReadyTick(0);
                    Click(driver,hud);
                    Report(hud.LastFeedbackBlock==HudAbilityBlock.Resource,"mouse resource denial explains missing resource");
                }
                if(next==4)
                {
                    Report(sim.AbilityReadyTick(0)==_readyTickBefore,"denied click did not cast or spend cooldown");
                    sim.Entities.Lavidium[Simulation.PlayerId]=Fix64.FromInt(sim.Entities.MaxLavidium[Simulation.PlayerId]);
                    Click(driver,hud);
                    Report(hud.LastFeedbackBlock==HudAbilityBlock.None,"ready mouse click accepted");
                }
                if(next<6)StartCoroutine(Snapshot(next));
                if(next==6)Debug.Log("[hud-qa] sequence completed");
            }
            if(_phase==3)sim.Entities.Lavidium[Simulation.PlayerId]=Fix64.Zero;
            if(_phase==4 && elapsed>8.2f && !_checkedCast)
            {
                _checkedCast=true;
                _cooldownTick=sim.AbilityReadyTick(0);
                Report(_cooldownTick>sim.Tick,"accepted click reached simulation and started cooldown");
            }
            if(_phase==4 && elapsed>8.3f && !_pressedCooldown)
            {
                _pressedCooldown=true;
                Click(driver,hud);
                Report(hud.LastFeedbackBlock==HudAbilityBlock.Cooldown,"repeated click explains cooldown");
                Report(sim.AbilityReadyTick(0)==_cooldownTick,"denied repeated click preserves cooldown");
            }
        }
        static void Click(TickDriver driver,PlayerHud hud)
        {
            Vector2 point=hud.ReviewAbilityPoint(0);
            Report(hud.HitTest(point,out int slot)&&slot==0,"mouse coordinates resolve to the intended skill");
            driver.CaptureAim(point,moveHeld:false,movePressed:false,attackHeld:true,attackPressed:true);
        }
        IEnumerator Snapshot(int phase)
        {
            yield return new WaitForSecondsRealtime(.6f);
            yield return new WaitForEndOfFrame();
            string[] names={"ready","tooltip","xp-hover","no-resource","cooldown","recovery"};
            var texture=ScreenCapture.CaptureScreenshotAsTexture();
            string path=Path.Combine(_directory,"ux-"+phase+"-"+names[phase]+".png");
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(path,texture.EncodeToPNG());
            Destroy(texture);
            Debug.Log("[hud-qa] captured "+path);
        }
        static void Report(bool passed,string check)
        {
            if(passed)Debug.Log("[hud-qa] PASS "+check);
            else Debug.LogError("[hud-qa] FAIL "+check);
        }
    }
}
