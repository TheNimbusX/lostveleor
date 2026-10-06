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
            // Прибытия 06.10: Вен и Лео — уровень 3, стол — первый завершённый забег, ранги — босс и уровень вместе.
            camp.DeveloperSetLevel(3);Check(camp.HasResident(CampResident.Trader) && camp.HasResident(CampResident.Alchemist) && camp.TraderStockCount==4 && !camp.HasTravelTable,"level 3: Ven and Leo, four goods, table still closed");
            camp.RecordRealAttemptEnded(1,0);Check(camp.HasTravelTable,"first ended run: travel table");
            var arrivals=CampUnlock.Trader|CampUnlock.Alchemist|CampUnlock.TravelTable;
            Check((camp.PendingUnlocks&arrivals)==arrivals,"arrivals wait for presentation");
            camp.DeveloperSetLevel(18);Check(camp.CampRank==0,"level alone opens no rank");
            for(int boss=0;boss<3;boss++){camp.DeveloperCreditBoss(boss);Check(camp.CampRank==boss+1,"boss "+(boss+1)+" with level 18: rank "+(boss+1));}
            Check(camp.TraderStockCount==6,"rank 3: six goods");
            camp.Earn(CurrencyType.Gold,2000);camp.Earn(CurrencyType.Shards,500);camp.Earn(CurrencyType.Steel,20);camp.DeveloperAddHeart(0);camp.DeveloperAddHeart(0);
            for(int i=0;i<Camp.PotionKindCount;i++)camp.GrantPotions((PotionKind)i,8);
            camp.SelectCarry(camp.CarryOfferAt(0));
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
                var operation=chosen.EndsWith("remelt") || chosen.EndsWith("replace")?EniAction.Remelt:chosen.EndsWith("add")?EniAction.Add:chosen.EndsWith("heart")?EniAction.Heart:EniAction.Temper;
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
                if(Temper(shop))
                {
                    // CampShopsWc v4: кузница — вкладки внутри окна Эни; портрет и облачко остаются, второй модалки нет.
                    var tab=chosen.EndsWith("dismantle")?EniTab.Dismantle:(EniTab)operation;
                    shop.Smith.Cells[target].Button.onClick.Invoke();shop.Temper.Tabs[(int)tab].onClick.Invoke();
                    if(tab==EniTab.Heart)shop.Temper.Cards[0].Button.onClick.Invoke();
                    Check(shop.Temper.Root.activeInHierarchy && CampServicesView.Instance.IsOpen,"temper tab open "+tab);
                    Check(shop.Smith.Portrait==null || shop.Smith.Portrait.enabled,"temper tabs keep Eni portrait");
                }
                else
                {
                    shop.Smith.Cells[target].Button.onClick.Invoke();shop.Smith.Action.onClick.Invoke();
                    CampForgeView.Instance.CaptureAction(operation);
                    Check(CampForgeView.Instance.IsOpen,"forge panel open "+operation);
                    Check(ShopGraphicsHidden(shop),"forge hides every old shop graphic including Eni portrait");
                }
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
            var temperShop=FindAnyObjectByType<CampShopView>();
            // Стол «Перед походом» (CampTravelWc, 06.10) заменил CampPreparation: без него проверка переполнения на prep молча пропускалась.
            Transform panelRoot=chosen=="prep"?((Component)FindAnyObjectByType<CampTravelPanel>()??FindAnyObjectByType<CampPreparationPanel>())?.transform:chosen.StartsWith("forge")?(Temper(temperShop)?temperShop.Temper.Root.transform:FindAnyObjectByType<CampForgePanel>()?.transform):chosen=="resident"?FindAnyObjectByType<CampResidentPanel>()?.transform:chosen=="trader-config"?FindAnyObjectByType<CampTraderProgressionPanel>()?.transform:null;
            foreach(var label in panelRoot!=null?panelRoot.GetComponentsInChildren<TMP_Text>():Array.Empty<TMP_Text>())
            {
                if(!label.isActiveAndEnabled || string.IsNullOrEmpty(label.text))continue;
                label.ForceMeshUpdate();if(label.isTextOverflowing)Check(false,"text overflow: "+label.transform.parent.name+" / "+label.name+" / "+label.text.Replace('\n',' '));
            }
            ulong before=0;camp.HashInto(ref before);var decoded=CampSaveCodec.Decode(CampSaveCodec.Encode(camp),camp.Items);ulong after=0;decoded.HashInto(ref after);Check(before==after,"save v10 roundtrip exact camp state");
            File.WriteAllText(Path.Combine(_output,"camp-polish-qa.txt"),_report.ToString());
            File.WriteAllText(Path.Combine(_output,"camp-routes.txt"),CampRouteAudit.Report());
            if(chosen.StartsWith("forge"))
            {
                // Кадр показывает результат до операции; затем проверяем два настоящих UI-submit.
                yield return new WaitForSecondsRealtime(9);
                if(Temper(temperShop))
                {
                    var t=temperShop.Temper;
                    if(t.Primary!=null && t.Primary.interactable)
                    {
                        // Два настоящих нажатия: второй удар ждёт молот, выбор и сердце ждут нового выбора, разбор — вопрос.
                        int gold=camp.Money(CurrencyType.Gold);t.Primary.onClick.Invoke();
                        int once=camp.Money(CurrencyType.Gold);t.Primary.onClick.Invoke();
                        if(!chosen.EndsWith("dismantle"))Check(once<gold && camp.Money(CurrencyType.Gold)==once,"double temper submit charges only the reviewed operation");
                        File.WriteAllText(Path.Combine(_output,"camp-polish-qa.txt"),_report.ToString());
                        yield return new WaitForSecondsRealtime(3);
                        // Esc: вопрос снят или закалка взята — окно Эни остаётся, значки сумки на месте.
                        CampServicesView.Instance.CancelShopConfirm();yield return null;
                        bool icons=true;
                        foreach(var cell in temperShop.Smith.Cells)if(cell.Icon!=null && cell.Icon.enabled!=(cell.Icon.sprite!=null))icons=false;
                        Check(CampServicesView.Instance.IsOpen && t.Root.activeInHierarchy && icons && (!camp.Session.IsOpen || camp.Session.Kind!=ForgeSessionKind.Temper),
                            "Esc takes the temper session and keeps Eni window with item icons");
                    }
                    else Check(false,"temper primary unavailable");
                    File.WriteAllText(Path.Combine(_output,"camp-polish-qa.txt"),_report.ToString());
                    yield break;
                }
                var panel=FindAnyObjectByType<CampForgePanel>();
                if(panel!=null && panel.Confirm.interactable)
                {
                    int gold=camp.Money(CurrencyType.Gold);panel.Confirm.onClick.Invoke();
                    int once=camp.Money(CurrencyType.Gold);panel.Confirm.onClick.Invoke();
                    // Второе нажатие — следующий удар той же оплаченной закалки или выбор: золото не берётся дважды.
                    Check(once<gold && camp.Money(CurrencyType.Gold)==once,"double forge submit charges only the reviewed operation");
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
        /// <summary>Префаб окон лагеря v4: кузница Эни вкладками (CampShopView.Temper собран).</summary>
        static bool Temper(CampShopView shop)=>shop!=null && shop.Temper!=null && shop.Temper.Root!=null;
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
