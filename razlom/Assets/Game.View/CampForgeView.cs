using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
namespace Game.View
{
    /// <summary>
    /// Окно Эни (06.10, минимальный проход): закалка «Ещё удар?», переплавка, добавление и
    /// сердце на прежнем префабе CampForge. Цена, риск и рост — из Camp.Quote, исход броска
    /// окно не знает до удара. Настоящее окно по temper-a.png — в UI-проходе; блок доноров
    /// (перенос удалён) всегда скрыт.
    /// </summary>
    public sealed class CampForgeView : MonoBehaviour
    {
        public static CampForgeView Instance { get; private set; }
        public static int ClosedFrame {get;private set;}=-1;
        public bool IsOpen=>_panel!=null && _panel.gameObject.activeSelf;
        TickDriver _driver;CampForgePanel _panel;Camp _camp;
        ForgeTarget _target;EniAction _action;EniQuote _quote;
        int _affix,_choice,_openedFrame;
        System.Action _changed;GameObject _previousSelection;
        readonly GeneratedItem _before=new GeneratedItem(),_after=new GeneratedItem();
        static readonly string[] OperationNames={"Закалить","Переплавить","Добавить","Сердце"};
        static readonly string[] OperationRanks={"","Ранг лагеря 1","Ранг лагеря 2","Ранг лагеря 1"};
        static readonly string[] FacetNames={"","Корни","Пыльца","Цветение"};
        public void Initialize(TickDriver driver)
        {
            Instance=this;_driver=driver;
            var prefab=Resources.Load<GameObject>("UI/Prefabs/CampForge");if(prefab==null){Debug.LogError("[camp] Нет префаба кузницы");return;}
            _panel=Instantiate(prefab,transform).GetComponent<CampForgePanel>();
            CampChoiceFeedback.Install(_panel.gameObject);
            for(int i=0;i<_panel.Operations.Length && i<OperationNames.Length;i++){int index=i;_panel.Operations[i].onClick.AddListener(()=>{_action=(EniAction)index;_choice=0;Refresh();});}
            for(int i=0;i<_panel.Affixes.Length;i++){int index=i;_panel.Affixes[i].onClick.AddListener(()=>{_affix=IsNormal()?-1:index;_choice=0;Refresh();});}
            for(int i=0;i<_panel.Options.Length;i++){int index=i;_panel.Options[i].onClick.AddListener(()=>{_choice=index;Refresh();});}
            _panel.Confirm.onClick.AddListener(Commit);_panel.Back.onClick.AddListener(Close);_panel.gameObject.SetActive(false);
        }
        public void Open(Camp camp,ForgeTarget target,int affix,System.Action changed)
        {
            if(_panel==null || _driver.Session.Mode!=GameMode.Camp)return;
            _camp=camp;_target=target;_affix=affix;_action=EniAction.Temper;_choice=0;_changed=changed;
            // Ждёт оплаченный выбор этой вещи — окно открывается сразу на нём.
            var session=camp.Session;
            if(session.IsOpen && session.Target.Same(target) && session.Kind!=ForgeSessionKind.Temper)
            {_action=session.Kind==ForgeSessionKind.Add?EniAction.Add:EniAction.Remelt;_affix=session.Property;}
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
        /// <summary>Закрытие — это «забрать»: закалка закрывается с набранным, оплаченный выбор ждёт.</summary>
        public void Close()
        {
            if(!IsOpen)return;
            _camp?.SettleForgeSession();
            _panel.gameObject.SetActive(false);CampServicesView.Instance?.SetShopModal(false);ClosedFrame=Time.frameCount;_driver?.ClearCapturedInput();
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(_previousSelection);_changed?.Invoke();
        }
        ItemInstance Current=>_target.IsWorn?_camp.Worn.Worn((EquipSlot)_target.Slot):_camp.Bag.At(_target.Slot);
        bool IsNormal()=>_camp!=null && !Current.IsEmpty && Current.Rarity==ItemRarity.Normal;
        bool ChoicePending=>_camp.Session.IsOpen && _camp.Session.Target.Same(_target) && _camp.Session.Kind!=ForgeSessionKind.Temper;
        /// <summary>Сердце первого босса, которое есть в лагере; нет ни одного — Хозяин Чащи (покажет «нет сердца»).</summary>
        int HeartBoss()
        {
            for(int i=0;i<RunBossKeys.Count;i++)if(_camp.HeartCount(RunBossKeys.At(i))>0 && Camp.HeartFacetCount(RunBossKeys.At(i))>0)return RunBossKeys.At(i);
            return RunBossKeys.ThicketMaster;
        }
        HeartFacet ChosenFacet=>Camp.HeartFacetAt(HeartBoss(),_choice);
        void Commit()
        {
            if(!_panel.Confirm.interactable)return;
            SmithResult result;string done;
            switch(_action)
            {
                case EniAction.Temper:
                    if(_quote.Risky)
                    {
                        result=_camp.RiskyStrike(_target,out bool masterpiece,out int shards);
                        done=masterpiece?"Шедевр! Клеймо Эни на вещи.":"Вещь рассыпалась: +"+shards+" осколков.";
                    }
                    else
                    {
                        result=_camp.Strike(_target,_affix,out var outcome);
                        done=outcome==StrikeOutcome.Grew?"Удар лёг. Ещё удар — или «Назад», чтобы забрать."
                            :outcome==StrikeOutcome.Shattered?"Трещина. Третья — вещь расколота.":"Трещина: рост этой закалки сгорел.";
                    }
                    break;
                case EniAction.Remelt:
                case EniAction.Add:
                    if(ChoicePending){result=_camp.ChooseSessionCandidate(_choice);done="Готово. Свойство влито.";}
                    else if(_action==EniAction.Remelt){result=_camp.BeginRemelt(_target,_affix);done="Оплачено. Выбери одно из свойств.";}
                    else{result=_camp.BeginAdd(_target,out bool cracked);done=cracked?"Перелив: трещина, свойство не легло.":"Оплачено. Выбери новое свойство.";}
                    break;
                default:
                    result=_camp.InlayHeart(_target,HeartBoss(),ChosenFacet);done="Сердце вплавлено.";
                    break;
            }
            _choice=0;Refresh();
            _panel.Status.text=result==SmithResult.Success?done:Reason(result);
            if(result==SmithResult.Success){_changed?.Invoke();GameSound.Sequence(("smith_hammer",0,.8f),("smith_sizzle",.25f,.5f),("smith_ring",.5f,.45f));}
        }
        /// <summary>Съёмка: окно сразу на нужном действии.</summary>
        internal void CaptureAction(EniAction action){_action=action;_choice=0;if(action==EniAction.Temper && IsNormal())_affix=-1;Refresh();}
        void Refresh()
        {
            var item=Current;
            bool generated=!item.IsEmpty && ItemGenerator.Generate(item,_camp.Items,_before);
            if(IsNormal())_affix=-1;
            int boss=HeartBoss();
            _quote=_camp.Quote(_action,_target,_affix,boss,_action==EniAction.Heart?ChosenFacet:HeartFacet.None);
            _panel.Before.text=item.IsEmpty?"Вещи больше нет":_panel.BeforeProperties!=null?ItemHeader(item):ItemText(item,_before);
            if(_panel.BeforeProperties!=null)_panel.BeforeProperties.text=generated?ItemProperties(_before):"";
            ShowItem(_panel.BeforeItem,item);
            ItemInstance after=default;
            if(_action==EniAction.Temper && !_quote.Risky && _quote.Status==SmithResult.Success)after=_camp.PreviewTemperStrike(_target,_affix);
            else if((_action==EniAction.Remelt || _action==EniAction.Add) && ChoicePending)after=_camp.SessionCandidateItem(_choice);
            bool resultGenerated=!after.IsEmpty && ItemGenerator.Generate(after,_camp.Items,_after);
            _panel.After.text=resultGenerated?(_panel.AfterProperties!=null?ItemHeader(after):ItemText(after,_after)):AfterHint(boss);
            if(_panel.AfterProperties!=null)_panel.AfterProperties.text=resultGenerated?ChangedProperties(_before,_after):"";
            ShowItem(_panel.AfterItem,resultGenerated?after:default);
            _panel.Changes.text=resultGenerated && generated?HeroChanges(item,after):"";
            bool pending=ChoicePending;
            _panel.Price.text=PriceText(pending);
            _panel.Status.text=StatusText(pending);
            bool canPick=_action!=EniAction.Heart || ChosenFacet!=HeartFacet.None;
            _panel.Confirm.interactable=!item.IsEmpty && (pending?(_action==EniAction.Remelt || _action==EniAction.Add) && _choice<_camp.SessionCandidateCount
                :_quote.Status==SmithResult.Success && _quote.Affordable && canPick && (_action!=EniAction.Heart || _camp.HeartCount(boss)>0));
            var confirmLabel=_panel.Confirm.GetComponentInChildren<TMP_Text>();if(confirmLabel!=null)confirmLabel.text=ConfirmText(pending);
            for(int i=0;i<_panel.Operations.Length;i++)
            {
                bool shown=i<OperationNames.Length;_panel.Operations[i].gameObject.SetActive(shown);if(!shown)continue;
                _panel.OperationLabels[i].text=OperationNames[i]+(_camp.EniActionUnlocked((EniAction)i)?"":"\n<size=65%>"+OperationRanks[i]+"</size>");
                CampChoiceFeedback.Choose(_panel.Operations[i],_action==(EniAction)i);
            }
            bool pickProperty=_action==EniAction.Temper || _action==EniAction.Remelt && !pending;
            bool baseOnly=generated && item.Rarity==ItemRarity.Normal && _before.HasImplicit;
            int rows=!pickProperty || !generated?0:baseOnly?(_action==EniAction.Temper?1:0):_before.AffixCount;
            if(_panel.TargetAffixCaption!=null)_panel.TargetAffixCaption.gameObject.SetActive(rows>0);
            for(int i=0;i<_panel.Affixes.Length;i++)
            {
                bool shown=i<rows;_panel.Affixes[i].gameObject.SetActive(shown);
                if(shown)_panel.AffixLabels[i].text=baseOnly?StatText.Name(_before.ImplicitStat)+"  "+StatText.Modifier(_before.ImplicitStat,_before.ImplicitValue,_before.ImplicitOp)+"\n<size=65%>базовое свойство</size>"
                    :AffixText(_before.GetAffix(i));
                CampChoiceFeedback.Choose(_panel.Affixes[i],shown && (baseOnly || _affix==i));
            }
            int options=pending?_camp.SessionCandidateCount:_action==EniAction.Heart?Camp.HeartFacetCount(boss):0;
            for(int i=0;i<_panel.Options.Length;i++)
            {
                bool shown=i<options;_panel.Options[i].gameObject.SetActive(shown);
                if(shown)_panel.OptionLabels[i].text=pending?AffixText(_camp.SessionCandidate(i)):FacetText(Camp.HeartFacetAt(boss,i));
                CampChoiceFeedback.Choose(_panel.Options[i],shown && _choice==i);
            }
            HideDonors();
        }
        void HideDonors()
        {
            _panel.DonorCaption.text="";
            foreach(var donor in _panel.Donors)donor.gameObject.SetActive(false);
            foreach(var affix in _panel.DonorAffixes)affix.gameObject.SetActive(false);
            ShowItem(_panel.DonorItem,default);
            if(_panel.DonorInfo!=null)_panel.DonorInfo.gameObject.SetActive(false);
            _panel.PreviousDonors.gameObject.SetActive(false);_panel.NextDonors.gameObject.SetActive(false);
        }
        static string FacetText(HeartFacet facet)=>(int)facet<FacetNames.Length?"Грань «"+FacetNames[(int)facet]+"»":facet.ToString();
        string AfterHint(int boss)
        {
            switch(_action)
            {
                case EniAction.Temper:return _quote.Risky?"Рискованный удар: шедевр (+25% ко всем свойствам) или осколки":"Выбери свойство";
                case EniAction.Heart:return "Сердце не меняет числа вещи: грань действует в забеге, пока вещь надета\nСердец в лагере: "+_camp.HeartCount(boss);
                default:return "Варианты появятся после оплаты";
            }
        }
        string PriceText(bool pending)
        {
            if(pending)return "Оплачено · выбор обязателен";
            if(_quote.Status!=SmithResult.Success)return "";
            if(_action==EniAction.Temper && _quote.Strikes>0)return "Следующий удар бесплатный";
            string price="Цена: "+_quote.Gold+" золота";
            if(_quote.Shards>0)price+=" · "+_quote.Shards+" осколков";
            if(_quote.Steel>0)price+="\n"+_quote.Steel+" стали";
            if(_quote.Hearts>0)price+="\n"+_quote.Hearts+" сердце босса";
            return price;
        }
        string StatusText(bool pending)
        {
            if(pending)return "Выбери одно из оплаченных свойств — выбор ждёт и после закрытия окна";
            if(_quote.Status==SmithResult.Locked)return "Эни · нужен "+OperationRanks[(int)_action].ToLowerInvariant();
            if(_quote.Status!=SmithResult.Success)return Reason(_quote.Status);
            if(!_quote.Affordable)return "Не хватает ресурсов";
            string attempts="Попыток: "+_quote.AttemptsLeft+(_quote.Cracks>0?" · трещин "+_quote.Cracks:"");
            switch(_action)
            {
                case EniAction.Temper:
                    return _quote.Risky?"Рискованный удар · рассыпется с шансом "+_quote.RiskPercent+"%"
                        :"Удар +"+Percent(_quote.NextGrowth)+"% · трещина "+_quote.RiskPercent+"% · "+attempts;
                case EniAction.Add:return (_quote.Overflow?"Перелив сверх лимита · трещина "+_quote.RiskPercent+"% · ":"")+attempts;
                case EniAction.Remelt:return "Три варианта без старого свойства · "+attempts;
                default:return "Без риска · попытку не тратит";
            }
        }
        string ConfirmText(bool pending)
        {
            if(pending)return "Выбрать";
            switch(_action)
            {
                case EniAction.Temper:return _quote.Risky?"Рискованный удар":_quote.Strikes>0?"Ещё удар? трещина "+_quote.RiskPercent+"%":"Удар";
                case EniAction.Heart:return "Вплавить";
                default:return "Оплатить";
            }
        }
        static string Percent(Fix64 fraction)=>((int)System.Math.Round(fraction.ToFloat()*100f)).ToString();
        string ItemName(ItemInstance item)=>GetComponent<CampInventoryView>().ItemName(item.BaseId);
        string ItemHeader(ItemInstance item)=>ItemName(item)+"\nУровень "+item.ItemLevel+(_camp.IsMasterpiece(item)?" · шедевр":_camp.IsShattered(item)?" · расколота":"");
        void ShowItem(CampShopCell cell,ItemInstance item)
        {if(cell==null)return;cell.gameObject.SetActive(!item.IsEmpty);if(!item.IsEmpty)cell.Show(GetComponent<CampInventoryView>().SpriteFor(item),"",(int)item.Rarity,false);}
        static string ItemProperties(GeneratedItem roll)
        {
            string text=roll.HasImplicit?StatText.Name(roll.ImplicitStat)+"  "+StatText.Modifier(roll.ImplicitStat,roll.ImplicitValue,roll.ImplicitOp)+"\n":"";
            for(int i=0;i<roll.AffixCount;i++)text+=AffixText(roll.GetAffix(i))+"\n";return text.TrimEnd('\n');
        }
        static string Tint(string text,UiTheme.Role role)=>"<color=#"+ColorUtility.ToHtmlStringRGB(UiTheme.Current!=null?UiTheme.Current.Get(role):Color.white)+">"+text+"</color>";
        static string ChangedProperties(GeneratedItem before,GeneratedItem after)
        {
            string text="";
            if(before.HasImplicit && after.HasImplicit && before.ImplicitValue!=after.ImplicitValue)
                text+=StatText.Name(after.ImplicitStat)+"  "+StatText.Modifier(before.ImplicitStat,before.ImplicitValue,before.ImplicitOp)+" → "+Tint(StatText.Modifier(after.ImplicitStat,after.ImplicitValue,after.ImplicitOp),UiTheme.Role.Accent)+"\n";
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
        string ItemText(ItemInstance item,GeneratedItem roll)=>ItemHeader(item)+"\n\n"+ItemProperties(roll);
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
            switch(result)
            {
                case SmithResult.Locked:return "Нужен ранг лагеря";
                case SmithResult.Incompatible:return "Такую грань или свойство сюда не вплавить";
                case SmithResult.NoSpace:return "Предел свойств этой редкости и перелива";
                case SmithResult.InvalidItem:return "Это действие этой вещи недоступно";
                case SmithResult.NoAffix:return "Выбери свойство";
                case SmithResult.AtMaximum:return "Свойство на пределе — «Назад», чтобы забрать";
                case SmithResult.Exhausted:return "Попытки закалки этой вещи использованы";
                case SmithResult.InsufficientFunds:return "Не хватает ресурсов";
                case SmithResult.Shattered:return "Вещь расколота: три трещины";
                case SmithResult.NoHeart:return "Нет сердца босса";
                case SmithResult.HeartsFull:return "Сердец в вещи уже столько, сколько позволяет ранг";
                case SmithResult.SessionOpen:return "Эни ждёт выбора оплаченной переплавки";
                case SmithResult.NoSession:return "Сессия закрыта";
                case SmithResult.Masterpiece:return "Шедевр: дальше только сердце";
                case SmithResult.NotTempered:return "Рискованный удар — когда попытки кончились";
                default:return result.ToString();
            }
        }
        void OnDestroy(){if(Instance==this)Instance=null;}
        void OnDisable(){Close();}
    }
}
