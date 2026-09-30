#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Text;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    // Только изолированный capture-профиль; обычная игра этот компонент не создаёт.
    public sealed class CampPolishCapture : MonoBehaviour
    {
        string _output;readonly StringBuilder _report=new StringBuilder();TickDriver _driver;
        public void Initialize(string output){_output=output;}
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1);
            _driver=FindAnyObjectByType<TickDriver>();var camp=_driver.Session.Camp;
            Check(camp.Level==1 && camp.AttemptCount==0 && !camp.HasResident(CampResident.Trader) && !camp.HasTravelTable && !camp.HasResident(CampResident.Alchemist),"clean onboarding services");
            Check(camp.PotionCount(PotionKind.SmallHealth)>0 && camp.PotionCount(PotionKind.SmallLavidium)>0,"initial small potion stock");
            camp.RecordRealAttemptEnded(1,0);Check(camp.HasResident(CampResident.Trader) && camp.TraderStockCount==4,"first return: four goods");
            camp.RecordRealAttemptEnded(2,0);Check(camp.HasTravelTable && !camp.HasResident(CampResident.Alchemist),"second return: preparation before Leo");
            camp.RecordRealAttemptEnded(3,1);Check(camp.HasResident(CampResident.Alchemist),"third return: Leo");
            camp.GainExperience(2700);for(int rank=0;rank<3;rank++)for(int resident=0;resident<3;resident++)Check(camp.TryUpgradeResident((CampResident)resident)==CampUpgradeResult.Success,"rank "+(rank+1)+" resident "+resident);
            Check(camp.AvailableCampPoints==0 && camp.TraderStockCount==6,"all nine upgrades at level ten");
            camp.Earn(CurrencyType.Gold,2000);camp.Earn(CurrencyType.Shards,500);camp.EarnForgeMaterial(ForgeMaterial.Steel,20);camp.EarnForgeMaterial(ForgeMaterial.Core,20);
            for(int i=0;i<Camp.PotionKindCount;i++)camp.GrantPotions((PotionKind)i,8);
            camp.SelectGift(camp.GiftOfferAt(0));
            var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"-capture-camp-polish");string chosen=flag>=0 && flag+1<args.Length?args[flag+1]:"prep";
            if(chosen=="prep")
            {
                _driver.Session.SetPreparedStarter(1);CampPreparationView.Instance.Open();Check(CampPreparationView.Instance.IsOpen,"preparation panel open");
            }
            else if(chosen.StartsWith("forge"))
            {
                int target=-1;var rolled=new GeneratedItem();
                for(ulong seed=1;seed<1000;seed++)
                {
                    var item=new ItemInstance(StableId.Of("base.officer_sabre"),12,ItemRarity.Rare,seed);
                    if(ItemGenerator.Generate(item,camp.Items,rolled) && rolled.AffixCount<4){target=camp.Bag.Add(item);break;}
                }
                if(target<0)target=camp.Bag.Add(new ItemInstance(StableId.Of("base.officer_sabre"),12,ItemRarity.Magic,123));
                for(ulong seed=201;seed<220;seed++)camp.Bag.Add(new ItemInstance(StableId.Of("base.officer_sabre"),10,ItemRarity.Magic,seed));
                var operation=chosen.EndsWith("replace")?ForgeOperation.Replace:chosen.EndsWith("add")?ForgeOperation.Add:chosen.EndsWith("transfer")?ForgeOperation.Transfer:ForgeOperation.Refine;
                // Тот же путь, что у игрока: окно Эни, ячейка вещи, действие кузницы.
                // Прямой Open не проверял бы скрытие старого прилавка и его обновление после ковки.
                foreach(var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))if(npc.Kind==CampServiceKind.Smith)
                {
                    if(CampPlayerView.Instance.TryServiceApproach(npc,CampPlayerView.Instance.InteractionPosition,out var at))
                        _driver.Session.CampSim.Entities.Position[0]=CampTrainingView.Flat(at);
                    CampServicesView.Instance.Open(npc);break;
                }
                Check(CampServicesView.Instance.IsOpen,"real Eni shop opens before forge");
                yield return null;
                var shop=FindAnyObjectByType<CampShopView>();
                shop.Smith.Cells[target].Button.onClick.Invoke();shop.Smith.Action.onClick.Invoke();
                CampForgeView.Instance.CaptureOperation(operation);
                Check(CampForgeView.Instance.IsOpen,"forge panel open "+operation);
                Check(ShopGraphicsHidden(shop),"forge hides every old shop graphic including Eni portrait");
            }
            else if(chosen=="alchemist" || chosen=="alchemist-recipes" || chosen=="trader" || chosen=="smith" || chosen=="resident" || chosen=="trader-config")
            {
                var kind=chosen.StartsWith("alchemist")?CampServiceKind.Alchemist:chosen.StartsWith("trader")?CampServiceKind.Trader:CampServiceKind.Smith;
                foreach(var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))if(npc.Kind==kind)
                {
                    npc.gameObject.SetActive(true);var at=CampTrainingView.Flat(npc.Approach);_driver.Session.CampSim.Entities.Position[0]=at;
                    CampServicesView.Instance.Open(npc);Check(CampServicesView.Instance.IsOpen,"shop panel open "+kind);break;
                }
                if(chosen=="resident" || chosen=="trader-config")
                {
                    yield return null;
                    string buttonName=chosen=="resident"?"Развитие лагеря":"Резерв и заказ";
                    foreach(var button in CampServicesView.Instance.GetComponentsInChildren<Button>())
                        if(button.name==buttonName && button.isActiveAndEnabled){button.onClick.Invoke();break;}
                }
                if(chosen=="alchemist-recipes")
                {
                    yield return null;Canvas.ForceUpdateCanvases();
                    var shop=FindAnyObjectByType<CampShopView>();
                    Check(shop.Alchemist.Potions.Length==Camp.PotionKindCount,"all eight recipes in real alchemist window");
                    var scroll=shop.Alchemist.Potions[6].Buy.GetComponentInParent<ScrollRect>();
                    Check(scroll!=null,"lower potion recipes scrollable");if(scroll!=null)scroll.verticalNormalizedPosition=0;
                }
            }
            yield return new WaitForSecondsRealtime(.8f);
            Canvas.ForceUpdateCanvases();
            Transform panelRoot=chosen=="prep"?FindAnyObjectByType<CampPreparationPanel>()?.transform:chosen.StartsWith("forge")?FindAnyObjectByType<CampForgePanel>()?.transform:chosen=="resident"?FindAnyObjectByType<CampResidentPanel>()?.transform:chosen=="trader-config"?FindAnyObjectByType<CampTraderProgressionPanel>()?.transform:null;
            foreach(var label in panelRoot!=null?panelRoot.GetComponentsInChildren<TMP_Text>():Array.Empty<TMP_Text>())
            {
                if(!label.isActiveAndEnabled || string.IsNullOrEmpty(label.text))continue;
                label.ForceMeshUpdate();if(label.isTextOverflowing)Check(false,"text overflow: "+label.transform.parent.name+" / "+label.name+" / "+label.text.Replace('\n',' '));
            }
            ulong before=0;camp.HashInto(ref before);var decoded=CampSaveCodec.Decode(CampSaveCodec.Encode(camp),camp.Items);ulong after=0;decoded.HashInto(ref after);Check(before==after,"save v9 roundtrip exact camp state");
            File.WriteAllText(Path.Combine(_output,"camp-polish-qa.txt"),_report.ToString());
            File.WriteAllText(Path.Combine(_output,"camp-routes.txt"),CampRouteAudit.Report());
            if(chosen.StartsWith("forge"))
            {
                // Кадр показывает результат до операции; затем проверяем два настоящих UI-submit.
                yield return new WaitForSecondsRealtime(9);
                var panel=FindAnyObjectByType<CampForgePanel>();
                if(panel!=null && panel.Confirm.interactable)
                {
                    int gold=camp.Money(CurrencyType.Gold);panel.Confirm.onClick.Invoke();
                    int once=camp.Money(CurrencyType.Gold);panel.Confirm.onClick.Invoke();
                    Check(once<gold && camp.Money(CurrencyType.Gold)==once && !panel.Confirm.interactable,"double forge submit charges only the reviewed operation");
                    Check(ShopGraphicsHidden(FindAnyObjectByType<CampShopView>()),"forge commit refresh keeps old shop hidden");
                    File.WriteAllText(Path.Combine(_output,"camp-polish-qa.txt"),_report.ToString());
                    // После второго кадра возвращаемся: пустой донор и новые значки должны восстановиться.
                    yield return new WaitForSecondsRealtime(3);
                    panel.Back.onClick.Invoke();yield return null;
                    var shop=FindAnyObjectByType<CampShopView>();bool icons=true;
                    foreach(var cell in shop.Smith.Cells)if(cell.Icon!=null && cell.Icon.enabled!=(cell.Icon.sprite!=null))icons=false;
                    Check(CampServicesView.Instance.IsOpen && !CampForgeView.Instance.IsOpen && icons
                        && (shop.Smith.Portrait==null || shop.Smith.Portrait.enabled),"back restores updated item icons and Eni portrait");
                    File.WriteAllText(Path.Combine(_output,"camp-polish-qa.txt"),_report.ToString());
                }
                else {Check(false,"forge confirmation unavailable");File.WriteAllText(Path.Combine(_output,"camp-polish-qa.txt"),_report.ToString());}
            }
        }
        static bool ShopGraphicsHidden(CampShopView shop)
        {
            if(shop==null)return false;
            foreach(var graphic in shop.Smith.Group.GetComponentsInChildren<Graphic>(true))if(graphic.enabled)return false;
            return true;
        }
        void Check(bool passed,string reason){_report.AppendLine((passed?"PASS ":"FAIL ")+reason);if(!passed)Debug.LogError("[camp-polish-qa] "+reason);}
    }
}
#endif
