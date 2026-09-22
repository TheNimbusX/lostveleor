using Game.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public sealed partial class CampServicesView
    {
        Camp _smithCamp;
        CanvasGroup _smithInput;
        GameObject _smith;Text _smithWallet,_smithTitle,_smithDetail,_smithCost,_smithMessage;
        Button _smithAction;Text _smithActionText;
        readonly Button[] _smithSlots=new Button[48],_smithAffixes=new Button[6];
        readonly Image[] _smithIcons=new Image[48];
        readonly Text[] _smithLevels=new Text[48];
        int _smithSlot=-1,_smithAffix;bool _dismantling,_confirmDismantle;
        readonly GeneratedItem _smithRoll=new GeneratedItem();
        static readonly Color Paper=new Color(.94f,.89f,.78f),Ink=new Color(.18f,.23f,.22f),Accent=new Color(.3f,.43f,.37f);
        void ShowSmith(bool show)
        {
            _serviceCard.SetActive(!show);
            if(!show){if(_smith!=null)_smith.SetActive(false);return;}
            _smithCamp=_driver.Session.Camp;
            if(_smith==null)BuildSmith();
            _smith.SetActive(true);_smithInput.interactable=false;_smithSlot=-1;_smithAffix=0;_confirmDismantle=false;
            // Открытие кузни: один удар по наковальне.
            GameSound.Play("smith_hammer",.55f);
            _smithMessage.text="";RefreshSmith();
        }
        Button SmithButton(Transform parent,string text,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction click)
        {
            var go=new GameObject(text,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;rect.anchoredPosition=pos;rect.sizeDelta=size;
            go.GetComponent<Image>().color=Accent;var button=go.GetComponent<Button>();button.onClick.AddListener(click);
            Label(go.transform,text,Vector2.zero,size-Vector2.one*8,18).color=Paper;return button;
        }
        static string SmithText(string key)=>CampServiceText.Get("smith."+key);
        void BuildSmith()
        {
            _smith=new GameObject("Smith workshop",typeof(RectTransform),typeof(Image));_smith.transform.SetParent(_panel.transform,false);
            ((RectTransform)_smith.transform).sizeDelta=new Vector2(940,620);_smith.GetComponent<Image>().color=Paper;_smithInput=_smith.AddComponent<CanvasGroup>();
            Label(_smith.transform,CampServiceText.Get("npc.smith"),new Vector2(-330,267),new Vector2(200,40),28);
            _smithWallet=Label(_smith.transform,"",new Vector2(95,267),new Vector2(540,38),19);
            SmithButton(_smith.transform,"×",new Vector2(431,268),new Vector2(42,42),Close);
            SmithButton(_smith.transform,SmithText("reforge"),new Vector2(-319,210),new Vector2(200,42),()=>{_dismantling=false;_confirmDismantle=false;_smithMessage.text="";RefreshSmith();});
            SmithButton(_smith.transform,SmithText("dismantle"),new Vector2(-103,210),new Vector2(200,42),()=>{_dismantling=true;_confirmDismantle=false;_smithMessage.text="";RefreshSmith();});
            for(int i=0;i<48;i++)
            {
                int slot=i;var button=SmithButton(_smith.transform,"",new Vector2(-400+i%8*54,142-i/8*56),new Vector2(49,51),()=>{_smithSlot=slot;_smithAffix=0;_confirmDismantle=false;_smithMessage.text="";RefreshSmith();});_smithSlots[i]=button;
                var icon=new GameObject("Item",typeof(RectTransform),typeof(Image));icon.transform.SetParent(button.transform,false);((RectTransform)icon.transform).sizeDelta=new Vector2(41,41);_smithIcons[i]=icon.GetComponent<Image>();_smithIcons[i].preserveAspect=true;_smithIcons[i].raycastTarget=false;
                _smithLevels[i]=Label(button.transform,"",new Vector2(10,-17),new Vector2(27,17),12);_smithLevels[i].color=Color.white;
            }
            Label(_smith.transform,SmithText("bag.hint"),new Vector2(-207,-222),new Vector2(435,62),16);
            _smithTitle=Label(_smith.transform,"",new Vector2(225,207),new Vector2(390,43),25);
            _smithDetail=Label(_smith.transform,"",new Vector2(225,164),new Vector2(390,45),17);
            for(int i=0;i<6;i++)
            {int affix=i;_smithAffixes[i]=SmithButton(_smith.transform,"",new Vector2(225,113-i*40),new Vector2(390,35),()=>{_smithAffix=affix;_confirmDismantle=false;RefreshSmith();});}
            _smithCost=Label(_smith.transform,"",new Vector2(225,-160),new Vector2(390,90),18);
            _smithAction=SmithButton(_smith.transform,"",new Vector2(225,-230),new Vector2(390,47),ApplySmith);_smithActionText=_smithAction.GetComponentInChildren<Text>();
            _smithMessage=Label(_smith.transform,"",new Vector2(0,-282),new Vector2(870,36),16);
        }
        void RefreshSmith()
        {
            var camp=_smithCamp;var inventory=GetComponent<CampInventoryView>();
            _smithWallet.text=SmithText("gold")+": "+camp.Money(CurrencyType.Gold)+"     "+SmithText("shards")+": "+camp.Money(CurrencyType.Shards);
            for(int i=0;i<48;i++)
            {var value=camp.Bag.At(i);_smithSlots[i].interactable=!value.IsEmpty;_smithSlots[i].image.color=i==_smithSlot?new Color(.69f,.48f,.25f):Accent;_smithIcons[i].sprite=value.IsEmpty?null:inventory.ItemSprite(i,false);_smithIcons[i].enabled=_smithIcons[i].sprite!=null;_smithLevels[i].text=value.IsEmpty?"":value.ItemLevel.ToString();}
            foreach(var button in _smithAffixes)button.gameObject.SetActive(false);
            _smithAction.interactable=false;_smithCost.text="";_smithDetail.text="";
            _smithActionText.text=SmithText(_dismantling?"dismantle":"reforge");
            if(_smithSlot<0 || camp.Bag.IsEmpty(_smithSlot)){_smithTitle.text=SmithText("choose");return;}
            var item=camp.Bag.At(_smithSlot);ItemGenerator.Generate(item,camp.Items,_smithRoll);
            _smithTitle.text=inventory.ItemName(item.BaseId);
            _smithDetail.text=SmithText("level")+" "+item.ItemLevel+"  ·  "+SmithText("attempts")+" "+item.ReforgeCount+"/3";
            if(_dismantling)
            {
                bool kept=camp.Bag.IsKept(_smithSlot);_smithCost.text=kept?SmithText("protected"):SmithText("yield")+" "+Inventory.ShardsFor(item)+" "+SmithText("shards")+"\n"+SmithText("destroy.warning");
                _smithAction.interactable=!kept;_smithActionText.text=SmithText(_confirmDismantle?"confirm":"dismantle");return;
            }
            for(int i=0;i<_smithRoll.AffixCount;i++)
            {var a=_smithRoll.GetAffix(i);var button=_smithAffixes[i];button.gameObject.SetActive(true);button.image.color=i==_smithAffix?new Color(.63f,.43f,.24f):Accent;button.GetComponentInChildren<Text>().text=StatLabel(a.Stat)+"  "+AffixValue(a.Value,a.Op,a.Stat);}
            var result=camp.ReforgeRange(_smithSlot,_smithAffix,out var lo,out var hi);
            if(result!=SmithResult.Success){_smithCost.text=SmithFailure(result);return;}
            var selected=_smithRoll.GetAffix(_smithAffix);
            _smithCost.text=AffixValue(selected.Value,selected.Op,selected.Stat)+" → "+AffixValue(lo,selected.Op,selected.Stat)+"…"+AffixValue(hi,selected.Op,selected.Stat)+"\n"+Camp.ReforgeGold(item)+" "+SmithText("gold")+" + "+Camp.ReforgeShards(item)+" "+SmithText("shards")+"  ·  "+SmithText("level")+" +"+(1+(int)item.Rarity);
            bool enough=camp.Money(CurrencyType.Gold)>=Camp.ReforgeGold(item) && camp.Money(CurrencyType.Shards)>=Camp.ReforgeShards(item);
            _smithAction.interactable=enough;if(!enough)_smithCost.text+="\n"+SmithFailure(SmithResult.InsufficientFunds);
        }
        void ApplySmith()
        {
            var camp=_smithCamp;
            if(_dismantling && !_confirmDismantle){_confirmDismantle=true;RefreshSmith();return;}
            SmithResult result;string message;
            if(_dismantling){result=camp.Dismantle(_smithSlot,out int shards);message=SmithText("received")+" "+shards+" "+SmithText("shards");}
            else{result=camp.Reforge(_smithSlot,_smithAffix);message=SmithText("done");}
            _confirmDismantle=false;RefreshSmith();_smithMessage.text=result==SmithResult.Success?message:SmithFailure(result);
            // Перековка: два удара молота, пар, звон готовой вещи. Разбор: лом, осыпающиеся детали, осколки.
            if(result==SmithResult.Success)
            {
                // Разбор короткий (владелец: «слишком долгий»): удар и осыпающиеся детали.
                if(_dismantling)GameSound.Sequence(("smith_break",0f,.8f),("smith_debris",.08f,.6f));
                else GameSound.Sequence(("smith_hammer",0f,.85f),("smith_hammer",.32f,.8f),("smith_sizzle",.62f,.55f),("smith_ring",1f,.5f));
            }
        }
        static string SmithFailure(SmithResult value)=>SmithText("error."+value);
        static string AffixValue(Fix64 value,ModifierOp op,StatType stat)=>op==ModifierOp.Flat && stat!=StatType.FireResist && stat!=StatType.CritChance && stat!=StatType.CritMultiplier?value.ToFloat().ToString("0.##"):(value.ToFloat()*100).ToString("0.##")+"%";
        static string StatLabel(StatType stat)=>CampServiceText.Get("stat."+stat);
        internal bool ProbeSmithTransactions()
        {
            // Изолированный кошелёк и сумка: проверка не записывает тестовые вещи в прогресс.
            var original=_smithCamp;
            try
            {
                _smithCamp=new Camp(PrototypeContent.Items());_smithCamp.Earn(CurrencyType.Gold,200);_smithCamp.Earn(CurrencyType.Shards,20);
                _smithCamp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"),10,ItemRarity.Magic,123));
                _smithSlot=0;_smithAffix=0;_dismantling=false;RefreshSmith();
                for(int i=0;i<3;i++){if(!_smithAction.interactable)return false;_smithAction.onClick.Invoke();}
                if(_smithAction.interactable || _smithCamp.Bag.At(0).ReforgeCount!=3 || _smithCamp.Money(CurrencyType.Gold)!=20 || _smithCamp.Money(CurrencyType.Shards)!=2)return false;
                _dismantling=true;RefreshSmith();_smithAction.onClick.Invoke();
                if(_smithCamp.Bag.IsEmpty(0) || !_confirmDismantle)return false;
                int reward=Inventory.ShardsFor(_smithCamp.Bag.At(0));_smithAction.onClick.Invoke();
                return _smithCamp.Bag.IsEmpty(0) && _smithCamp.Money(CurrencyType.Shards)==2+reward && !_smithAction.interactable;
            }
            finally{_smithCamp=original;_smithSlot=-1;_dismantling=false;_confirmDismantle=false;RefreshSmith();_smithMessage.text="";}
        }
    }
}
