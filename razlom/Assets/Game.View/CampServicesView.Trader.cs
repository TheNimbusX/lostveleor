using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class CampServicesView
    {
        Camp _traderCamp;bool _traderWired;
        int _traderSlot=-1;bool _selling,_confirmSale,_confirmRefresh;
        static string TradeText(string key)=>CampServiceText.Get("trader."+key);
        void ShowTrader(bool show)
        {
            var s=_view.Trader;
            if(!show){s.Group.gameObject.SetActive(false);return;}
            _traderCamp=_driver.Session.Camp;
            WireTrader();
            Present(s.Group,s.Cells[0].Button);_traderSlot=-1;_traderWorn=false;_selling=false;_confirmSale=_confirmRefresh=false;
            s.Message.text=CampServiceText.Get("dialogue.trader.open");RefreshTraderPanel();
            GameSound.Play("shop_bell",.55f);
        }
        void WireTrader()
        {
            if(_traderWired)return;_traderWired=true;var s=_view.Trader;
            s.Title.text=CampServiceText.Get("npc.trader");s.Speaker.text=s.Title.text;
            s.Tabs[0].GetComponentInChildren<TMPro.TMP_Text>().text=TradeText("buy.tab");
            s.Tabs[1].GetComponentInChildren<TMPro.TMP_Text>().text=TradeText("sell.tab");
            s.Tabs[0].onClick.AddListener(()=>SetTradeMode(false));
            s.Tabs[1].onClick.AddListener(()=>SetTradeMode(true));
            for(int i=0;i<s.Cells.Length;i++)
            {int slot=i;s.Cells[i].Button.onClick.AddListener(()=>{_traderSlot=slot;_traderWorn=false;_confirmSale=_confirmRefresh=false;_view.Trader.Message.text="";RefreshTraderPanel();});}
            WireWorn(s,slot=>{_traderSlot=slot;_traderWorn=true;_confirmSale=_confirmRefresh=false;_view.Trader.Message.text="";RefreshTraderPanel();});
            s.Extra.onClick.AddListener(RefreshTradeStock);
            s.Action.onClick.AddListener(ApplyTrade);
            s.Back.onClick.AddListener(Close);
            var backLabel=s.Back.GetComponentInChildren<TMPro.TMP_Text>();if(backLabel!=null)backLabel.text=CampServiceText.Get("back");
        }
        void SetTradeMode(bool selling){_selling=selling;_traderSlot=-1;_traderWorn=false;_confirmSale=_confirmRefresh=false;_view.Trader.Message.text="";RefreshTraderPanel();}
        ItemInstance TradeItem(int slot)=>_selling?_traderCamp.Bag.At(slot):_traderCamp.TraderStock(slot);
        void RefreshTraderPanel()
        {
            var s=_view.Trader;var camp=_traderCamp;var inventory=GetComponent<CampInventoryView>();
            s.Gold.text=camp.Money(CurrencyType.Gold).ToString();s.ShardsGroup.SetActive(false);
            CampShopView.SetTab(s.Tabs[0],!_selling);CampShopView.SetTab(s.Tabs[1],_selling);
            s.GridCaption.text=_selling?TradeText("bag.caption")+"  ·  "+camp.Bag.Used+" / 48":TradeText("stock.caption");
            for(int i=0;i<s.Cells.Length;i++)
            {
                bool exists=_selling || i<camp.TraderStockCount;var item=exists?TradeItem(i):default;
                s.Cells[i].gameObject.SetActive(exists);s.Cells[i].Button.interactable=!item.IsEmpty;
                s.Cells[i].Show(item.IsEmpty?null:inventory.SpriteFor(item),item.IsEmpty?"":item.ItemLevel.ToString(),(int)item.Rarity,i==_traderSlot&&!_traderWorn);
            }
            ShowWorn(s,camp,_traderWorn?_traderSlot:-1,false);
            s.Info.text=_selling?TradeText("sell.info"):TradeText("rare.chance")+" "+(camp.TraderBossStock?Camp.TraderBossRareChance:Camp.TraderRareChance)+"%\n"+TradeText("stock.info");
            s.Extra.gameObject.SetActive(!_selling);s.Extra.interactable=camp.Money(CurrencyType.Gold)>=Camp.TraderRefreshPrice;
            s.ExtraLabel.text=TradeText(_confirmRefresh?"refresh.confirm":"refresh")+"  ·  "+Camp.TraderRefreshPrice;
            s.Action.interactable=false;s.ActionLabel.text=TradeText(_selling?(_confirmSale?"sell.confirm":"sell"):"buy");
            s.Detail.text="";s.Note.text="";s.Price.SetActive(false);
            var chosen=_traderSlot<0?default:_traderWorn?WornOrBag(camp,_traderSlot,true):TradeItem(_traderSlot);
            if(chosen.IsEmpty){s.Item.Show(null,"",false,false);s.ItemName.text=SmithText("choose");s.ItemMeta.text="";return;}
            s.Item.Show(inventory.SpriteFor(chosen),chosen.ItemLevel.ToString(),(int)chosen.Rarity,false);
            s.ItemName.text=inventory.ItemName(chosen.BaseId);
            s.ItemMeta.text=TradeText(chosen.Rarity==ItemRarity.Normal?"normal":chosen.Rarity==ItemRarity.Magic?"rare":chosen.Rarity==ItemRarity.Rare?"epic":"unique")+"  ·  "+SmithText("level")+" "+chosen.ItemLevel+"  ·  "+SmithText("attempts")+" "+chosen.ReforgeCount+"/3";
            var roll=new GeneratedItem();ItemGenerator.Generate(chosen,camp.Items,roll);
            string detail="";
            if(roll.HasImplicit)detail+=StatLabel(roll.ImplicitStat)+"  <color=#F4F7FB>"+AffixValue(roll.ImplicitValue,roll.ImplicitOp,roll.ImplicitStat)+"</color>\n";
            for(int i=0;i<roll.AffixCount;i++){var affix=roll.GetAffix(i);detail+=StatLabel(affix.Stat)+"  <color=#F4F7FB>"+AffixValue(affix.Value,affix.Op,affix.Stat)+"</color>\n";}
            // Надетое — только посмотреть и сравнить: без цены, продать можно лишь из сумки.
            if(_traderWorn){s.ItemMeta.text+=WornTag;s.Detail.text=detail;s.Note.text=TradeText("worn.note");s.Action.interactable=false;return;}
            string comparison=inventory.CompareStats(chosen,_driver.Session.CampSim.Entities.Stats[0],"#8CE07A","#FF7A66","#C9D2E0",true).Replace("\n\n","\n").Trim();
            s.Detail.text=detail+"\n<size=90%><color=#F4F7FB>"+TradeText("compare")+"</color></size>\n"+(comparison.Length==0?TradeText("unchanged"):comparison);
            int price=_selling?Camp.PriceOf(chosen):camp.BuyPriceOf(chosen);
            s.Price.SetActive(true);s.PriceShardsGroup.SetActive(false);s.PriceGold.text=price.ToString();
            string reason=_selling?(camp.Bag.IsKept(_traderSlot)?TradeText("protected"):""):camp.Bag.IsFull?TradeText("full"):camp.Money(CurrencyType.Gold)<price?TradeText("funds"):"";
            s.Note.text=reason;
            s.Action.interactable=reason.Length==0;
        }
        void ApplyTrade()
        {
            var s=_view.Trader;
            if(!s.Action.interactable || _traderSlot<0 || _traderWorn)return;
            if(_selling && !_confirmSale){_confirmSale=true;RefreshTraderPanel();return;}
            int value=_selling?_traderCamp.SellToTrader(_traderSlot):_traderCamp.BuyFromTrader(_traderSlot);
            _confirmSale=false;RefreshTraderPanel();
            // Реплика — только после удачи; отказ — системной строкой (CAMP-NPC-DIALOGUE.md).
            if(value>0)s.Message.text=CampServiceText.Get(_selling?"dialogue.trader.sell":"dialogue.trader.buy");
            else s.Note.text=TradeText("failed");
            // Продажа — монеты на стойку и вещь в ящик; покупка — кошель, пересчёт и тихий акцент.
            if(value>0)
            {
                if(_selling)GameSound.Sequence(("trader_coins_table",0f,.8f),("trader_crate",.22f,.65f));
                else GameSound.Sequence(("trader_pouch",0f,.7f),("trader_count",.18f,.65f),("trader_chime",.6f,.3f));
            }
        }
        void RefreshTradeStock()
        {
            if(!_confirmRefresh){_confirmRefresh=true;RefreshTraderPanel();return;}
            bool ok=_traderCamp.RefreshTrader();_confirmRefresh=false;_traderSlot=-1;RefreshTraderPanel();_view.Trader.Message.text=TradeText(ok?"refreshed":"funds");
            if(ok)GameSound.Sequence(("trader_shuffle",0f,.65f),("trader_flip",.3f,.6f),("trader_flip",.46f,.55f),("trader_chime",.75f,.3f));
        }
        internal bool ProbeTraderTransactions()
        {
            var original=_traderCamp;var action=_view.Trader.Action;var refresh=_view.Trader.Extra;
            try
            {
                _traderCamp=new Camp(PrototypeContent.Items());_traderCamp.Earn(CurrencyType.Gold,1000);_selling=false;_traderSlot=0;RefreshTraderPanel();
                int price=_traderCamp.BuyPriceOf(_traderCamp.TraderStock(0));action.onClick.Invoke();
                if(_traderCamp.Bag.Used!=1 || !_traderCamp.TraderStock(0).IsEmpty || _traderCamp.Money(CurrencyType.Gold)!=1000-price)return false;
                SetTradeMode(true);_traderSlot=0;RefreshTraderPanel();int sell=Camp.PriceOf(_traderCamp.Bag.At(0));
                action.onClick.Invoke();if(_traderCamp.Bag.Used!=1)return false;action.onClick.Invoke();
                if(_traderCamp.Bag.Used!=0 || _traderCamp.Money(CurrencyType.Gold)!=1000-price+sell)return false;
                SetTradeMode(false);int gold=_traderCamp.Money(CurrencyType.Gold);refresh.onClick.Invoke();if(_traderCamp.Money(CurrencyType.Gold)!=gold)return false;
                refresh.onClick.Invoke();return _traderCamp.TraderGeneration==1 && _traderCamp.Money(CurrencyType.Gold)==gold-Camp.TraderRefreshPrice;
            }
            finally{_traderCamp=original;SetTradeMode(false);}
        }
    }
}
