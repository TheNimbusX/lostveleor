using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class CampServicesView
    {
        Camp _smithCamp;
        bool _smithWired;
        int _smithSlot=-1,_smithAffix;bool _dismantling,_confirmDismantle;
        readonly GeneratedItem _smithRoll=new GeneratedItem();
        void ShowSmith(bool show)
        {
            var s=_view.Smith;
            if(!show){s.Group.gameObject.SetActive(false);return;}
            _smithCamp=_driver.Session.Camp;
            WireSmith();
            Present(s.Group,s.Cells[0].Button);_smithSlot=-1;_smithWorn=false;_smithAffix=0;_dismantling=false;_confirmDismantle=false;
            // Открытие кузни: один удар по наковальне.
            GameSound.Play("smith_hammer",.55f);
            s.Message.text=CampServiceText.Get("dialogue.smith.open");RefreshSmith();
        }
        static string SmithText(string key)=>CampServiceText.Get("smith."+key);
        void WireSmith()
        {
            if(_smithWired)return;_smithWired=true;var s=_view.Smith;
            s.Title.text=CampServiceText.Get("npc.smith");s.Speaker.text=s.Title.text;
            s.Tabs[0].GetComponentInChildren<TMPro.TMP_Text>().text=SmithText("reforge");
            s.Tabs[1].GetComponentInChildren<TMPro.TMP_Text>().text=SmithText("dismantle");
            s.Tabs[0].onClick.AddListener(()=>{_dismantling=false;_confirmDismantle=false;_view.Smith.Message.text="";RefreshSmith();});
            s.Tabs[1].onClick.AddListener(()=>{_dismantling=true;_confirmDismantle=false;if(_smithWorn){_smithSlot=-1;_smithWorn=false;}_view.Smith.Message.text="";RefreshSmith();});
            for(int i=0;i<s.Cells.Length;i++)
            {int slot=i;s.Cells[i].Button.onClick.AddListener(()=>{_smithSlot=slot;_smithWorn=false;_smithAffix=0;_confirmDismantle=false;_view.Smith.Message.text="";RefreshSmith();});}
            for(int i=0;i<s.Rows.Length;i++)
            {int affix=i;s.Rows[i].onClick.AddListener(()=>{_smithAffix=affix;_confirmDismantle=false;RefreshSmith();});}
            WireWorn(s,slot=>{_smithSlot=slot;_smithWorn=true;_smithAffix=0;_confirmDismantle=false;_view.Smith.Message.text="";RefreshSmith();});
            s.Action.onClick.AddListener(ApplySmith);
            s.Back.onClick.AddListener(Close);
            var backLabel=s.Back.GetComponentInChildren<TMPro.TMP_Text>();if(backLabel!=null)backLabel.text=CampServiceText.Get("back");
        }
        void RefreshSmith()
        {
            var s=_view.Smith;var camp=_smithCamp;var inventory=GetComponent<CampInventoryView>();
            s.Gold.text=camp.Money(CurrencyType.Gold).ToString();s.Shards.text=camp.Money(CurrencyType.Shards).ToString();s.ShardsGroup.SetActive(true);
            CampShopView.SetTab(s.Tabs[0],!_dismantling);CampShopView.SetTab(s.Tabs[1],_dismantling);
            s.GridCaption.text=SmithText("bag.caption");s.Info.text=SmithText("bag.hint");
            for(int i=0;i<s.Cells.Length;i++)
            {
                var value=i<camp.Bag.Capacity?camp.Bag.At(i):default;
                s.Cells[i].Button.interactable=!value.IsEmpty;
                s.Cells[i].Show(value.IsEmpty?null:inventory.SpriteFor(value),value.IsEmpty?"":value.ItemLevel.ToString(),(int)value.Rarity,i==_smithSlot&&!_smithWorn);
            }
            ShowWorn(s,camp,_smithWorn?_smithSlot:-1,_dismantling);
            foreach(var row in s.Rows)row.gameObject.SetActive(false);
            s.Action.interactable=false;s.Preview.text="";s.Note.text="";s.Price.SetActive(false);
            s.ActionLabel.text=SmithText(_dismantling?"dismantle":"reforge");
            var item=WornOrBag(camp,_smithSlot,_smithWorn);
            if(item.IsEmpty)
            {s.Item.Show(null,"",false,false);s.ItemName.text=SmithText("choose");s.ItemMeta.text="";return;}
            ItemGenerator.Generate(item,camp.Items,_smithRoll);
            s.Item.Show(inventory.SpriteFor(item),item.ItemLevel.ToString(),(int)item.Rarity,false);
            s.ItemName.text=inventory.ItemName(item.BaseId);
            s.ItemMeta.text=SmithText("level")+" "+item.ItemLevel+"  ·  "+SmithText("attempts")+" "+item.ReforgeCount+"/3"+(_smithWorn?WornTag:"");
            if(_dismantling)
            {
                bool kept=camp.Bag.IsKept(_smithSlot);
                if(kept)s.Note.text=SmithText("protected");
                else{s.Preview.text=SmithText("yield")+"  <color=#FA883C>"+Inventory.ShardsFor(item)+" "+SmithText("shards")+"</color>";s.Note.text=SmithText("destroy.warning");}
                s.Action.interactable=!kept;s.ActionLabel.text=SmithText(_confirmDismantle?"confirm":"dismantle");return;
            }
            for(int i=0;i<_smithRoll.AffixCount && i<s.Rows.Length;i++)
            {var a=_smithRoll.GetAffix(i);s.Rows[i].gameObject.SetActive(true);CampShopView.SetRow(s.Rows[i],i==_smithAffix);s.RowLabels[i].text=StatLabel(a.Stat)+"  <color=#C9D2E0>"+AffixValue(a.Value,a.Op,a.Stat)+"</color>";}
            Fix64 lo,hi;var result=_smithWorn?camp.ReforgeRange((EquipSlot)_smithSlot,_smithAffix,out lo,out hi):camp.ReforgeRange(_smithSlot,_smithAffix,out lo,out hi);
            if(result!=SmithResult.Success){s.Note.text=SmithFailure(result);return;}
            var selected=_smithRoll.GetAffix(_smithAffix);
            s.Preview.text=StatLabel(selected.Stat)+"  "+AffixValue(selected.Value,selected.Op,selected.Stat)+"  →  <color=#FA883C>"+AffixValue(lo,selected.Op,selected.Stat)+"…"+AffixValue(hi,selected.Op,selected.Stat)+"</color>";
            s.Price.SetActive(true);s.PriceShardsGroup.SetActive(true);
            s.PriceGold.text=Camp.ReforgeGold(item).ToString();s.PriceShards.text=Camp.ReforgeShards(item).ToString();
            bool enough=camp.Money(CurrencyType.Gold)>=Camp.ReforgeGold(item) && camp.Money(CurrencyType.Shards)>=Camp.ReforgeShards(item);
            s.Note.text=enough?SmithText("level")+" +"+(1+(int)item.Rarity):SmithFailure(SmithResult.InsufficientFunds);
            s.Action.interactable=enough;
        }
        void ApplySmith()
        {
            var camp=_smithCamp;
            if(_dismantling && !_confirmDismantle){_confirmDismantle=true;RefreshSmith();return;}
            SmithResult result;string message;
            string note="";
            if(_dismantling){result=camp.Dismantle(_smithSlot,out int shards);message=CampServiceText.Get("dialogue.smith.dismantle");note="+"+shards+" "+SmithText("shards");}
            else{result=_smithWorn?camp.Reforge((EquipSlot)_smithSlot,_smithAffix):camp.Reforge(_smithSlot,_smithAffix);message=CampServiceText.Get("dialogue.smith.reforge");}
            _confirmDismantle=false;RefreshSmith();
            // Реплика — только после удачи; числа и отказы — системной строкой (CAMP-NPC-DIALOGUE.md).
            if(result==SmithResult.Success){_view.Smith.Message.text=message;if(note.Length>0)_view.Smith.Note.text=note;}
            else _view.Smith.Note.text=SmithFailure(result);
            // Перековка: два удара молота, пар, звон готовой вещи. Разбор: лом, осыпающиеся детали, осколки.
            if(result==SmithResult.Success)
            {
                // Разбор короткий (владелец: «слишком долгий»): удар и осыпающиеся детали.
                if(_dismantling)GameSound.Sequence(("smith_break",0f,.8f),("smith_debris",.08f,.6f));
                else GameSound.Sequence(("smith_hammer",0f,.85f),("smith_hammer",.32f,.8f),("smith_sizzle",.62f,.55f),("smith_ring",1f,.5f));
            }
        }
        static string SmithFailure(SmithResult value)=>SmithText("error."+value);
        static string AffixValue(Fix64 value,ModifierOp op,StatType stat)=>StatText.Modifier(stat,value,op);
        static string StatLabel(StatType stat)=>StatText.Name(stat);
        internal bool ProbeSmithTransactions()
        {
            // Изолированный кошелёк и сумка: проверка не записывает тестовые вещи в прогресс.
            var original=_smithCamp;var action=_view.Smith.Action;
            try
            {
                _smithCamp=new Camp(PrototypeContent.Items());_smithCamp.Earn(CurrencyType.Gold,200);_smithCamp.Earn(CurrencyType.Shards,20);
                _smithCamp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"),10,ItemRarity.Magic,123));
                _smithSlot=0;_smithAffix=0;_dismantling=false;RefreshSmith();
                for(int i=0;i<3;i++){if(!action.interactable)return false;action.onClick.Invoke();}
                if(action.interactable || _smithCamp.Bag.At(0).ReforgeCount!=3 || _smithCamp.Money(CurrencyType.Gold)!=20 || _smithCamp.Money(CurrencyType.Shards)!=2)return false;
                _dismantling=true;RefreshSmith();action.onClick.Invoke();
                if(_smithCamp.Bag.IsEmpty(0) || !_confirmDismantle)return false;
                int reward=Inventory.ShardsFor(_smithCamp.Bag.At(0));action.onClick.Invoke();
                return _smithCamp.Bag.IsEmpty(0) && _smithCamp.Money(CurrencyType.Shards)==2+reward && !action.interactable;
            }
            finally{_smithCamp=original;_smithSlot=-1;_dismantling=false;_confirmDismantle=false;RefreshSmith();_view.Smith.Message.text="";}
        }
    }
}
