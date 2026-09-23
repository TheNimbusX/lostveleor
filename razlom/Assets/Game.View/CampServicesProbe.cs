using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Game.View
{
    // Явный редакторный прогон маршрутов без съёмки и без изменения прогресса.
    public sealed class CampServicesProbe : MonoBehaviour
    {
        readonly StringBuilder _report=new StringBuilder();bool _passed=true;
        bool _originalWasd,_settingsCaptured;
        public static bool IsRunning { get; private set; }
        public static bool AllowInteractionInput { get; private set; }
#if ENABLE_INPUT_SYSTEM
        Keyboard _testKeyboard;
#endif
        void Awake()=>IsRunning=true;
        void OnDestroy()
        {
            IsRunning=false;AllowInteractionInput=false;
#if ENABLE_INPUT_SYSTEM
            if(_testKeyboard!=null)InputSystem.RemoveDevice(_testKeyboard);
#endif
            if(_settingsCaptured)GameUserSettings.SetWasdMovement(_originalWasd);
        }
        void Check(bool condition,string label){_report.AppendLine((condition?"PASS ":"FAIL ")+label);_passed&=condition;}
        IEnumerator Start()
        {
            var player=CampPlayerView.Instance;var services=CampServicesView.Instance;
            while(MainMenuView.IsOpen || !player.Active || GetComponent<TickDriver>().GameplayPaused)yield return null;
            yield return new WaitForSecondsRealtime(.5f);var origin=player.Position;
            _originalWasd=GameUserSettings.WasdMovement;_settingsCaptured=true;
            Check(player.WalkMap!=null,"walk map ready");
            Check(ProbeTraderBoss(),"boss kill refreshes shop once; developer fight leaves shop unchanged");
            var river=FindAnyObjectByType<CampRiver>();var crossing=river.GetComponent<CampRiverPassage>();int leaks=0;
            for(float x=-40;x<40;x+=.25f){var p=river.transform.TransformPoint(new Vector3(x,0,river.CentreAt(x)));if(!crossing.IsOpen(river,p) && player.WalkMap.Contains(CampTrainingView.Flat(p)))leaks++;}
            Check(leaks==0,"water blocked outside bridge; leaks="+leaks);
            var bridge=crossing.BridgeBounds;int sideLeaks=0;int sideChecks=0;
            for(float z=bridge.min.z+.2f;z<bridge.max.z-.2f;z+=.2f)
            for(int side=-1;side<=1;side+=2)
            {
                var inside=new Vector3(bridge.center.x,0,z);
                var outside=new Vector3(bridge.center.x+side*(bridge.extents.x+.6f),0,z);
                var rail=new Vector3(bridge.center.x+side*(crossing.CrossingSize.x*.5f+.1f),0,z);
                if(player.WalkMap.Contains(CampTrainingView.Flat(rail)) || player.WalkMap.CanTravel(CampTrainingView.Flat(inside),CampTrainingView.Flat(outside)))sideLeaks++;
                sideChecks++;
            }
            Check(sideLeaks==0,"bridge railings block both sides; samples="+sideChecks+" leaks="+sideLeaks);
            foreach(bool wasd in new[]{false,true})
            {
            GameUserSettings.SetWasdMovement(wasd);_report.AppendLine("MODE "+(wasd?"WASD":"mouse"));
            foreach(var kind in new[]{CampServiceKind.Tent,CampServiceKind.Smith,CampServiceKind.Alchemist,CampServiceKind.Trader})
            {
                CampServiceNpc target=null;foreach(var npc in FindObjectsByType<CampServiceNpc>())if(npc.Kind==kind)target=npc;
                Check(target!=null,"registered "+kind);if(target==null)continue;
                Check(services.Begin(target),"route to "+kind);
                float end=Time.realtimeSinceStartup+28;
                while(!services.IsOpen && !player.InventoryOpen && Time.realtimeSinceStartup<end)yield return null;
                Check(kind==CampServiceKind.Tent?player.InventoryOpen:services.IsOpen && services.Current==target,"arrival opens "+kind+" at "+player.Position);
                Check((!services.IsOpen && !player.InventoryOpen) || player.InputBlocked,"world blocked during conversation");
                if(kind==CampServiceKind.Trader && services.IsOpen)Check(services.ProbeTraderTransactions(),"trader UI: purchase, sale confirmation, refresh confirmation and charges; isolated inventory");
                if(kind==CampServiceKind.Smith && services.IsOpen)Check(services.ProbeSmithTransactions(),"smith UI: three reforges, costs, limit, dismantle confirmation; isolated inventory");
                if(kind==CampServiceKind.Alchemist && services.IsOpen)Check(services.ProbeAlchemyTransactions(),"alchemist: four purchases, prices, selection, consumption, HUD counts and empty icons; isolated inventory");
                services.Close();GetComponent<CampInventoryView>().Close();yield return null;Check(!player.InputBlocked,"input restored after closing "+kind);
#if ENABLE_INPUT_SYSTEM
                // Проходим через настоящую очередь ввода, чтобы обнаружить неверную клавишу в UI.
                _testKeyboard=InputSystem.AddDevice<Keyboard>();AllowInteractionInput=true;
                InputSystem.QueueStateEvent(_testKeyboard,new KeyboardState(Key.E));
                yield return null;yield return null;
                Check(kind==CampServiceKind.Tent?player.InventoryOpen:services.IsOpen && services.Current==target,"E opens "+kind);
                yield return null;
                Check(kind==CampServiceKind.Tent?player.InventoryOpen:services.IsOpen,"holding E keeps window open "+kind);
                InputSystem.QueueStateEvent(_testKeyboard,new KeyboardState());yield return null;
                InputSystem.RemoveDevice(_testKeyboard);_testKeyboard=null;AllowInteractionInput=false;
                services.Close();GetComponent<CampInventoryView>().Close();yield return null;
#endif
            }
            }
            CampServiceNpc alchemist=null;foreach(var npc in FindObjectsByType<CampServiceNpc>())if(npc.Kind==CampServiceKind.Alchemist)alchemist=npc;
            services.Begin(alchemist);services.CancelApproach();yield return null;
            Check(services.Pending==null && !services.IsOpen,"cancel approach clears interaction");
            var stopped=GetComponent<TickDriver>().Session.CampSim.Entities.Position[0];yield return new WaitForSecondsRealtime(.4f);Check(stopped.Equals(GetComponent<TickDriver>().Session.CampSim.Entities.Position[0]),"cancel approach stops movement");
            GameUserSettings.SetWasdMovement(false);
            Check(player.RouteTo(origin),"return route");float deadline=Time.realtimeSinceStartup+28;
            while(Vector3.Distance(player.Position,origin)>.5f && Time.realtimeSinceStartup<deadline)yield return null;
            Check(Vector3.Distance(player.Position,origin)<.5f,"return to start "+player.Position);
            // Шаги звучат в кадр касания ступни; маршрут к алхимику проходит дорожки, траву и мост.
            Check(CampFootsteps.GrassSteps>0 && CampFootsteps.PathSteps>0 && CampFootsteps.WoodSteps>0,
                "footsteps by surface grass="+CampFootsteps.GrassSteps+" path="+CampFootsteps.PathSteps+" bridge="+CampFootsteps.WoodSteps);
            GameUserSettings.SetWasdMovement(_originalWasd);
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/camp-services-result.txt"));File.WriteAllText(path,(_passed?"PASS\n":"FAIL\n")+_report);
        }
        public static bool ProbeTraderBoss()
        {
            var profile=Resources.Load<Game.Data.LocationProfileAsset>("Locations/MeadowGameplay").ToDefinition();
            foreach(bool developer in new[]{false,true})
            {
                var session=new Game.Sim.GameSession(71,Game.Sim.PrototypeContent.NewCamp(),profile.Modules,Game.Sim.PrototypeContent.ItemBaseIds(),location:profile);
                if(developer)session.StartDeveloperRift(profile,10,true,71);
                                else
                {
                    session.EnterRift();
                    for(int level=1;level<10;level++)
                    {
                        for(int id=1;id<session.Run.Sim.Entities.Count;id++)session.Run.Sim.Entities.Alive[id]=false;
                        session.Step(Game.Sim.InputFrame.Empty);
                        session.Run.Sim.Entities.Position[0]=session.Run.Map.ExitPoint(0);session.Step(Game.Sim.InputFrame.Empty);
                        session.Step(new Game.Sim.InputFrame{Command=(byte)Game.Sim.RunCommand.ChooseReward1});
                        if(session.Run.Phase==Game.Sim.RunPhase.ReplacingAbility)session.Step(new Game.Sim.InputFrame{Command=(byte)Game.Sim.RunCommand.SalvageAbility});
                        if(session.Run.Phase==Game.Sim.RunPhase.ChoosingRoute)session.Step(new Game.Sim.InputFrame{Command=(byte)Game.Sim.RunCommand.ChooseRoute1});
                    }
                    if(session.Camp.TraderGeneration!=0)return false;
                }
                int boss=session.Run.BossId;if(boss<0)return false;
                session.Run.Sim.Statuses.ApplyBurn(boss,Game.Sim.Fix64.FromInt(1000000),1,0,0);
                int gold=session.Camp.Money(Game.Sim.CurrencyType.Gold);
                session.Step(Game.Sim.InputFrame.Empty);
                if(session.Run.Sim.Entities.Alive[boss] || session.Camp.TraderGeneration!=(developer?0:1) || session.Camp.TraderBossStock==developer || session.Camp.Money(Game.Sim.CurrencyType.Gold)!=gold)return false;
                session.Step(Game.Sim.InputFrame.Empty);
                if(session.Camp.TraderGeneration!=(developer?0:1))return false;
            }
            return true;
        }
    }
}
