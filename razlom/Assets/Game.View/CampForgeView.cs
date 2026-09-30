using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
namespace Game.View
{
    public sealed class CampForgeView : MonoBehaviour
    {
        public static CampForgeView Instance { get; private set; }
        public static int ClosedFrame {get;private set;}=-1;
        public bool IsOpen=>_panel!=null && _panel.gameObject.activeSelf;
        TickDriver _driver;CampForgePanel _panel;Camp _camp;
        ForgeTarget _target;ForgeOperation _operation;ForgePreview _preview;
        int _affix,_choice,_donor=-1,_donorAffix,_page,_openedFrame;
        System.Action _changed;GameObject _previousSelection;
        readonly GeneratedItem _before=new GeneratedItem(),_after=new GeneratedItem(),_donorRoll=new GeneratedItem();
        readonly List<int> _donors=new List<int>();
        static readonly string[] OperationNames={"Усилить","Перенастроить","Дополнить","Перенести"};
        public void Initialize(TickDriver driver)
        {
            Instance=this;_driver=driver;
            var prefab=Resources.Load<GameObject>("UI/Prefabs/CampForge");if(prefab==null){Debug.LogError("[camp] Нет префаба кузницы");return;}
            _panel=Instantiate(prefab,transform).GetComponent<CampForgePanel>();
            CampChoiceFeedback.Install(_panel.gameObject);
            for(int i=0;i<4;i++){int index=i;_panel.Operations[i].onClick.AddListener(()=>{_operation=(ForgeOperation)index;_choice=0;_donor=-1;_donorAffix=0;Refresh();});}
            for(int i=0;i<_panel.Affixes.Length;i++){int index=i;_panel.Affixes[i].onClick.AddListener(()=>{_affix=index;_choice=0;Refresh();});}
            for(int i=0;i<_panel.Options.Length;i++){int index=i;_panel.Options[i].onClick.AddListener(()=>{_choice=index;Refresh();});}
            for(int i=0;i<_panel.Donors.Length;i++){int index=i;_panel.Donors[i].onClick.AddListener(()=>{int at=_page*_panel.Donors.Length+index;if(at<_donors.Count){_donor=_donors[at];_donorAffix=0;Refresh();}});}
            for(int i=0;i<_panel.DonorAffixes.Length;i++){int index=i;_panel.DonorAffixes[i].onClick.AddListener(()=>{_donorAffix=index;Refresh();});}
            _panel.PreviousDonors.onClick.AddListener(()=>{_page=System.Math.Max(0,_page-1);Refresh();});
            _panel.NextDonors.onClick.AddListener(()=>{_page++;Refresh();});
            _panel.Confirm.onClick.AddListener(Commit);_panel.Back.onClick.AddListener(Close);_panel.gameObject.SetActive(false);
        }
        public void Open(Camp camp,ForgeTarget target,int affix,System.Action changed)
        {
            if(_panel==null || _driver.Session.Mode!=GameMode.Camp)return;
            _camp=camp;_target=target;_affix=affix;_operation=ForgeOperation.Refine;_choice=0;_donor=-1;_donorAffix=0;_page=0;_changed=changed;
            _previousSelection=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            // В кузнице место портрета занимает сравнение вещи; разговоры жителей его сохраняют.
            CampServicesView.Instance?.SetShopModal(true,false);
            _openedFrame=Time.frameCount;_driver.ClearCapturedInput();Refresh();_panel.gameObject.SetActive(true);_panel.Group.interactable=false;
            if(TickDriver.GamepadLastUsed)_panel.Operations[0].Select();
        }
        void Update()
        {
            if(!IsOpen)return;
            if(_driver.GameplayPaused || _driver.Session.Mode!=GameMode.Camp || CampPlayerView.Instance?.Active!=true){Close();return;}
            if(Time.frameCount<=_openedFrame)return;_panel.Group.interactable=true;
            CampUiFocus.Ensure(_panel.Group,_panel.Operations[0]);
#if ENABLE_INPUT_SYSTEM
            bool cancel=Keyboard.current?.escapeKey.wasPressedThisFrame==true || Gamepad.current?.buttonEast.wasPressedThisFrame==true;
#else
            bool cancel=Input.GetKeyDown(KeyCode.Escape);
#endif
            if(cancel)Close();
        }
        public void Close(){if(!IsOpen)return;_panel.gameObject.SetActive(false);CampServicesView.Instance?.SetShopModal(false);ClosedFrame=Time.frameCount;_driver?.ClearCapturedInput();if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(_previousSelection);_changed?.Invoke();}
        void Commit()
        {
            if(_preview==null || !_panel.Confirm.interactable)return;
            // Повторный Submit не подтверждает следующую операцию без нового просмотра.
            _panel.Confirm.interactable=false;
            var result=_camp.CommitForge(_preview);
            if(result!=SmithResult.Success)Refresh();
            _panel.Status.text=result==SmithResult.Success?"Готово. Для следующей ковки выбери действие снова.":Reason(result);
            if(result==SmithResult.Success){_changed?.Invoke();GameSound.Sequence(("smith_hammer",0,.8f),("smith_sizzle",.25f,.5f),("smith_ring",.5f,.45f));}
        }
        internal void CaptureOperation(ForgeOperation operation)
        {
            _operation=operation;_choice=0;_donor=-1;_donorAffix=0;
            if(operation==ForgeOperation.Transfer)for(int slot=0;slot<_camp.Bag.Capacity && _donor<0;slot++)
            {
                if(_camp.Bag.IsEmpty(slot) || _camp.Bag.IsKept(slot) || !_target.IsWorn && slot==_target.Slot)continue;
                if(!ItemGenerator.Generate(_camp.Bag.At(slot),_camp.Items,_donorRoll))continue;
                for(int a=0;a<_donorRoll.AffixCount;a++)if(_camp.PreviewForge(_target,operation,_affix,0,slot,a).Status==SmithResult.Success){_donor=slot;_donorAffix=a;break;}
            }
            Refresh();
        }
        void Refresh()
        {
            var item=_target.IsWorn?_camp.Worn.Worn((EquipSlot)_target.Slot):_camp.Bag.At(_target.Slot);
            bool generated=ItemGenerator.Generate(item,_camp.Items,_before);
            _preview=_camp.PreviewForge(_target,_operation,_affix,_choice,_donor,_donorAffix);
            _panel.Before.text=_panel.BeforeProperties!=null?ItemHeader(item):ItemText(item,_before);
            if(_panel.BeforeProperties!=null)_panel.BeforeProperties.text=ItemProperties(_before);
            ShowItem(_panel.BeforeItem,item);
            bool valid=_preview.Status==SmithResult.Success;
            bool resultGenerated=valid && ItemGenerator.Generate(_preview.After,_camp.Items,_after);
            _panel.After.text=resultGenerated?(_panel.AfterProperties!=null?ItemHeader(_preview.After,item.ItemLevel):ItemText(_preview.After,_after)):"Выбери действие и свойство";
            if(_panel.AfterProperties!=null)_panel.AfterProperties.text=resultGenerated?ChangedProperties(_before,_after):"";
            ShowItem(_panel.AfterItem,resultGenerated?_preview.After:default);
            _panel.Changes.text=resultGenerated?HeroChanges(item,_preview.After):"";
            _panel.Price.text=valid?ForgePrice():"Цена появится после выбора допустимого результата";
            _panel.Status.text=valid?(_camp.CanAffordForge(_preview)?"Ресурсы списываются после подтверждения.":"Не хватает ресурсов"):
                _preview.Status==SmithResult.Locked?"Эни · нужно улучшение "+(int)_operation:Reason(_preview.Status);
            _panel.Confirm.interactable=valid && _camp.CanAffordForge(_preview);
            for(int i=0;i<4;i++){_panel.OperationLabels[i].text=OperationNames[i]+(_camp.ForgeOperationUnlocked((ForgeOperation)i)?"":"\n<size=65%>Улучшение Эни "+i+"</size>");CampChoiceFeedback.Choose(_panel.Operations[i],_operation==(ForgeOperation)i);}
            if(_panel.TargetAffixCaption!=null)_panel.TargetAffixCaption.gameObject.SetActive(generated && _before.AffixCount>0 && _operation!=ForgeOperation.Add);
            for(int i=0;i<_panel.Affixes.Length;i++)
            {
                bool shown=generated && i<_before.AffixCount && _operation!=ForgeOperation.Add;_panel.Affixes[i].gameObject.SetActive(shown);
                if(shown)_panel.AffixLabels[i].text=AffixText(_before.GetAffix(i));
                CampChoiceFeedback.Choose(_panel.Affixes[i],shown && _affix==i);
            }
            for(int i=0;i<_panel.Options.Length;i++)
            {
                bool shown=i<_preview.CandidateCount;_panel.Options[i].gameObject.SetActive(shown);
                if(shown)_panel.OptionLabels[i].text=AffixText(_preview.Candidate(i));
                CampChoiceFeedback.Choose(_panel.Options[i],shown && _choice==i);
            }
            RefreshDonors();
        }
        void RefreshDonors()
        {
            bool transfer=_operation==ForgeOperation.Transfer;_donors.Clear();
            if(transfer)for(int i=0;i<_camp.Bag.Capacity;i++)
            {
                if(_camp.Bag.IsEmpty(i) || _camp.Bag.IsKept(i) || !_target.IsWorn && i==_target.Slot)continue;
                var item=_camp.Bag.At(i);if(!ItemGenerator.Generate(item,_camp.Items,_donorRoll))continue;
                for(int a=0;a<_donorRoll.AffixCount;a++)if(_camp.PreviewForge(_target,ForgeOperation.Transfer,_affix,0,i,a).Status==SmithResult.Success){_donors.Add(i);break;}
            }
            int pages=System.Math.Max(1,(_donors.Count+_panel.Donors.Length-1)/_panel.Donors.Length);_page=System.Math.Min(_page,pages-1);
            _panel.DonorCaption.text=transfer?"Совместимые доноры · "+(_page+1)+" / "+pages:"";
            for(int i=0;i<_panel.Donors.Length;i++)
            {
                int at=_page*_panel.Donors.Length+i;bool shown=transfer && at<_donors.Count;_panel.Donors[i].gameObject.SetActive(shown);
                if(shown){int slot=_donors[at];var item=_camp.Bag.At(slot);_panel.DonorLabels[i].text=ItemName(item)+" · ур. "+item.ItemLevel;CampChoiceFeedback.Choose(_panel.Donors[i],_donor==slot);}
            }
            bool generated=transfer && _donor>=0 && ItemGenerator.Generate(_camp.Bag.At(_donor),_camp.Items,_donorRoll);
            for(int i=0;i<_panel.DonorAffixes.Length;i++)
            {
                bool shown=generated && i<_donorRoll.AffixCount;_panel.DonorAffixes[i].gameObject.SetActive(shown);
                if(shown){_panel.DonorAffixLabels[i].text=AffixText(_donorRoll.GetAffix(i));_panel.DonorAffixes[i].interactable=_camp.PreviewForge(_target,ForgeOperation.Transfer,_affix,0,_donor,i).Status==SmithResult.Success;CampChoiceFeedback.Choose(_panel.DonorAffixes[i],_donorAffix==i);}
            }
            ShowItem(_panel.DonorItem,generated?_camp.Bag.At(_donor):default);
            if(_panel.DonorInfo!=null){_panel.DonorInfo.gameObject.SetActive(transfer);_panel.DonorInfo.text=generated?"Донор будет израсходован\n"+ItemName(_camp.Bag.At(_donor)):"Выбери вещь-донор";}
            _panel.PreviousDonors.gameObject.SetActive(transfer);_panel.NextDonors.gameObject.SetActive(transfer);
            _panel.PreviousDonors.interactable=_page>0;_panel.NextDonors.interactable=_page+1<pages;
        }
        string ItemName(ItemInstance item)=>GetComponent<CampInventoryView>().ItemName(item.BaseId);
        string ItemHeader(ItemInstance item,int oldLevel=-1)=>ItemName(item)+"\nУровень "+(oldLevel>=0 && oldLevel!=item.ItemLevel?oldLevel+" → "+item.ItemLevel:item.ItemLevel.ToString());
        void ShowItem(CampShopCell cell,ItemInstance item)
        {if(cell==null)return;cell.gameObject.SetActive(!item.IsEmpty);if(!item.IsEmpty)cell.Show(GetComponent<CampInventoryView>().SpriteFor(item),"",(int)item.Rarity,false);}
        string ForgePrice()
        {
            string price="Цена: "+_preview.Gold+" золота · "+_preview.Shards+" осколков";
            if(_preview.Steel>0)price+="\n"+_preview.Steel+" стали";
            if(_preview.Cores>0)price+="\n"+_preview.Cores+" сердечник";
            if(_operation==ForgeOperation.Transfer && _panel.DonorInfo==null)price+="\nВещь-донор будет уничтожена";
            return price;
        }
        static string ItemProperties(GeneratedItem roll)
        {string text="";for(int i=0;i<roll.AffixCount;i++)text+=AffixText(roll.GetAffix(i))+"\n";return text.TrimEnd('\n');}
        static string Tint(string text,UiTheme.Role role)=>"<color=#"+ColorUtility.ToHtmlStringRGB(UiTheme.Current!=null?UiTheme.Current.Get(role):Color.white)+">"+text+"</color>";
        static string ChangedProperties(GeneratedItem before,GeneratedItem after)
        {
            string text="";
            for(int i=0;i<after.AffixCount;i++)
            {
                var next=after.GetAffix(i);
                if(i>=before.AffixCount){text+=Tint("Новое свойство\n"+AffixText(next),UiTheme.Role.Accent)+"\n";continue;}
                var old=before.GetAffix(i);if(old.AffixId==next.AffixId && old.Value==next.Value)continue;
                if(old.Stat==next.Stat && old.Op==next.Op)text+=StatText.Name(next.Stat)+"  "+StatText.Modifier(old.Stat,old.Value,old.Op)+" → "+Tint(StatText.Modifier(next.Stat,next.Value,next.Op),UiTheme.Role.Accent)+"\n";
                else text+=Tint(AffixText(old),UiTheme.Role.Bad)+"\n"+Tint(AffixText(next),UiTheme.Role.Good)+"\n";
            }
            return text.Length==0?"Свойства сохранятся":text.TrimEnd('\n')+"\n"+Tint("Остальные свойства сохранятся",UiTheme.Role.TextMuted);
        }
        string ItemText(ItemInstance item,GeneratedItem roll)
        {string text=ItemName(item)+"\nУровень "+item.ItemLevel+"\n\n";for(int i=0;i<roll.AffixCount;i++)text+=AffixText(roll.GetAffix(i))+"\n";return text;}
        static string AffixText(RolledAffix affix)=>StatText.Name(affix.Stat)+"  "+StatText.Modifier(affix.Stat,affix.Value,affix.Op);
        string HeroChanges(ItemInstance before,ItemInstance after)
        {
            var current=_driver.Session.CampSim.Entities.Stats[0];var first=CopyStats(current);var second=CopyStats(current);
            int index=_camp.Items.IndexOfBase(before.BaseId);if(index<0)return "";int slot=(int)Equipment.SlotOf(_camp.Items.GetBase(index).Category);
            first.RemoveSource(ModifierSource.Equipment,slot);second.RemoveSource(ModifierSource.Equipment,slot);_before.ApplyTo(first,slot);_after.ApplyTo(second,slot);
            string text=_target.IsWorn?"Характеристики героя\n":"Характеристики, если надеть\n";
            for(int i=0;i<(int)StatType.Count;i++){var stat=(StatType)i;var from=first.Get(stat);var to=second.Get(stat);if(from!=to)text+=StatText.Name(stat)+"  "+StatText.Value(stat,from)+" → "+Tint(StatText.Value(stat,to),to>from?UiTheme.Role.Good:UiTheme.Role.Bad)+"\n";}
            return text;
        }
        static StatSheet CopyStats(StatSheet source){var sheet=new StatSheet(System.Math.Max(32,source.ModifierCount+16));for(int s=0;s<(int)StatType.Count;s++)sheet.SetBase((StatType)s,source.GetBase((StatType)s));for(int m=0;m<source.ModifierCount;m++){var mod=source.GetModifier(m);sheet.Add(in mod);}return sheet;}
        static string Reason(SmithResult result)
        {
            switch(result){case SmithResult.Protected:return "Донор защищён";case SmithResult.Locked:return "Нужно улучшение Эни";case SmithResult.InvalidDonor:return "Выбери совместимую вещь-донор";case SmithResult.Incompatible:return "Свойство несовместимо с целью";case SmithResult.NoSpace:return "Достигнут предел свойств этой редкости";case SmithResult.InvalidItem:return "Нужна обычная, редкая или эпическая вещь с подходящими свойствами";case SmithResult.NoAffix:return "Выбери свойство";case SmithResult.AtMaximum:return "Свойство на пределе";case SmithResult.Exhausted:return "Усиления этой вещи использованы";case SmithResult.InsufficientFunds:return "Не хватает ресурсов";case SmithResult.StalePreview:return "Вещь изменилась — проверь новый результат";default:return result.ToString();}
        }
        void OnDestroy(){if(Instance==this)Instance=null;}
        void OnDisable(){Close();}
    }
}
