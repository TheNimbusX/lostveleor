using Game.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public sealed partial class CampServicesView
    {
        Camp _traderCamp;GameObject _trader;CanvasGroup _traderInput;
        Text _traderWallet,_traderTitle,_traderDetail,_traderPrice,_traderMessage,_traderInfo;
        Button _traderAction,_traderRefresh;readonly Button[] _traderSlots=new Button[48];
        readonly Image[] _traderIcons=new Image[48];readonly Text[] _traderLabels=new Text[48];
        int _traderSlot=-1;bool _selling,_confirmSale,_confirmRefresh;
        static string TradeText(string key)=>CampServiceText.Get("trader."+key);
        void ShowTrader(bool show)
        {
            if(!show){if(_trader!=null)_trader.SetActive(false);return;}
            _serviceCard.SetActive(false);_traderCamp=_driver.Session.Camp;
            if(_trader==null)BuildTrader();
            _trader.SetActive(true);_traderInput.interactable=false;_traderSlot=-1;_selling=false;_confirmSale=_confirmRefresh=false;
            _traderMessage.text="";RefreshTraderPanel();
            GameSound.Play("shop_bell",.55f);
        }
        void BuildTrader()
        {
            _trader=new GameObject("Trader shop",typeof(RectTransform),typeof(Image),typeof(CanvasGroup));_trader.transform.SetParent(_panel.transform,false);
            ((RectTransform)_trader.transform).sizeDelta=new Vector2(940,620);_trader.GetComponent<Image>().color=Paper;_traderInput=_trader.GetComponent<CanvasGroup>();
            Label(_trader.transform,CampServiceText.Get("npc.trader"),new Vector2(-320,267),new Vector2(230,40),28);
            _traderWallet=Label(_trader.transform,"",new Vector2(105,267),new Vector2(530,38),19);
            SmithButton(_trader.transform,"×",new Vector2(431,268),new Vector2(42,42),Close);
            SmithButton(_trader.transform,TradeText("buy.tab"),new Vector2(-319,210),new Vector2(200,42),()=>SetTradeMode(false));
            SmithButton(_trader.transform,TradeText("sell.tab"),new Vector2(-103,210),new Vector2(200,42),()=>SetTradeMode(true));
            for(int i=0;i<48;i++)
            {
                int slot=i;var button=SmithButton(_trader.transform,"",new Vector2(-400+i%8*54,142-i/8*56),new Vector2(49,51),()=>{_traderSlot=slot;_confirmSale=_confirmRefresh=false;_traderMessage.text="";RefreshTraderPanel();});_traderSlots[i]=button;
                var icon=new GameObject("Item",typeof(RectTransform),typeof(Image));icon.transform.SetParent(button.transform,false);((RectTransform)icon.transform).sizeDelta=new Vector2(41,41);_traderIcons[i]=icon.GetComponent<Image>();_traderIcons[i].preserveAspect=true;_traderIcons[i].raycastTarget=false;
                _traderLabels[i]=Label(button.transform,"",new Vector2(7,-17),new Vector2(35,17),12);_traderLabels[i].color=Color.white;
            }
            _traderInfo=Label(_trader.transform,"",new Vector2(-207,-198),new Vector2(430,48),16);
            _traderRefresh=SmithButton(_trader.transform,"",new Vector2(-207,-249),new Vector2(425,43),RefreshTradeStock);
            _traderTitle=Label(_trader.transform,"",new Vector2(225,206),new Vector2(390,44),24);
            _traderDetail=Label(_trader.transform,"",new Vector2(225,17),new Vector2(390,330),16);_traderDetail.alignment=TextAnchor.UpperLeft;_traderDetail.resizeTextForBestFit=true;_traderDetail.resizeTextMinSize=12;_traderDetail.resizeTextMaxSize=16;
            _traderPrice=Label(_trader.transform,"",new Vector2(225,-180),new Vector2(390,57),18);
            _traderAction=SmithButton(_trader.transform,"",new Vector2(225,-243),new Vector2(390,47),ApplyTrade);
            _traderMessage=Label(_trader.transform,"",new Vector2(0,-287),new Vector2(870,30),16);
        }
        void SetTradeMode(bool selling){_selling=selling;_traderSlot=-1;_confirmSale=_confirmRefresh=false;_traderMessage.text="";RefreshTraderPanel();}
        ItemInstance TradeItem(int slot)=>_selling?_traderCamp.Bag.At(slot):_traderCamp.TraderStock(slot);
        void RefreshTraderPanel()
        {
            var camp=_traderCamp;var inventory=GetComponent<CampInventoryView>();
            _traderWallet.text=SmithText("gold")+": "+camp.Money(CurrencyType.Gold)+"     "+TradeText("bag")+": "+camp.Bag.Used+"/48";
            for(int i=0;i<48;i++)
            {
                bool exists=_selling || i<camp.TraderStockCount;var item=exists?TradeItem(i):default;
                _traderSlots[i].gameObject.SetActive(exists);_traderSlots[i].interactable=!item.IsEmpty;
                _traderSlots[i].image.color=i==_traderSlot?new Color(.69f,.48f,.25f):item.Rarity==ItemRarity.Magic?new Color(.33f,.47f,.62f):Accent;
                _traderIcons[i].sprite=item.IsEmpty?null:inventory.SpriteFor(item);_traderIcons[i].enabled=_traderIcons[i].sprite!=null;
                _traderLabels[i].text=item.IsEmpty?"—":item.ItemLevel.ToString();
            }
            _traderInfo.text=_selling?TradeText("sell.info"):TradeText("rare.chance")+" "+(camp.TraderBossStock?Camp.TraderBossRareChance:Camp.TraderRareChance)+"%\n"+TradeText("stock.info");
            _traderRefresh.gameObject.SetActive(!_selling);_traderRefresh.interactable=camp.Money(CurrencyType.Gold)>=Camp.TraderRefreshPrice;
            _traderRefresh.GetComponentInChildren<Text>().text=TradeText(_confirmRefresh?"refresh.confirm":"refresh")+" · "+Camp.TraderRefreshPrice+" "+SmithText("gold");
            _traderAction.interactable=false;_traderAction.GetComponentInChildren<Text>().text=TradeText(_selling?(_confirmSale?"sell.confirm":"sell"):"buy");
            _traderTitle.text=SmithText("choose");_traderDetail.text="";_traderPrice.text="";
            if(_traderSlot<0)return;var chosen=TradeItem(_traderSlot);if(chosen.IsEmpty)return;
            _traderTitle.text=inventory.ItemName(chosen.BaseId);
            var roll=new GeneratedItem();ItemGenerator.Generate(chosen,camp.Items,roll);
            string detail=TradeText(chosen.Rarity==ItemRarity.Normal?"normal":chosen.Rarity==ItemRarity.Magic?"rare":chosen.Rarity==ItemRarity.Rare?"epic":"unique")+" · "+SmithText("level")+" "+chosen.ItemLevel+" · "+SmithText("attempts")+" "+chosen.ReforgeCount+"/3\n\n";
            if(roll.HasImplicit)detail+=StatLabel(roll.ImplicitStat)+"  "+AffixValue(roll.ImplicitValue,roll.ImplicitOp,roll.ImplicitStat)+"\n";
            for(int i=0;i<roll.AffixCount;i++){var affix=roll.GetAffix(i);detail+=StatLabel(affix.Stat)+"  "+AffixValue(affix.Value,affix.Op,affix.Stat)+"\n";}
            string comparison=inventory.CompareStats(chosen,_driver.Session.CampSim.Entities.Stats[0],"#2E703E","#AA4035","#334944",true).Replace("\n\n","\n").Trim();
            _traderDetail.text=detail+"\n"+TradeText("compare")+"\n"+(comparison.Length==0?TradeText("unchanged"):comparison);
            int price=_selling?Camp.PriceOf(chosen):camp.BuyPriceOf(chosen);
            _traderPrice.text=price+" "+SmithText("gold");
            string reason=_selling?(camp.Bag.IsKept(_traderSlot)?TradeText("protected"):""):camp.Bag.IsFull?TradeText("full"):camp.Money(CurrencyType.Gold)<price?TradeText("funds"):"";
            if(reason.Length>0)_traderPrice.text+="\n"+reason;
            _traderAction.interactable=reason.Length==0;
        }
        void ApplyTrade()
        {
            if(!_traderAction.interactable || _traderSlot<0)return;
            if(_selling && !_confirmSale){_confirmSale=true;RefreshTraderPanel();return;}
            int value=_selling?_traderCamp.SellToTrader(_traderSlot):_traderCamp.BuyFromTrader(_traderSlot);
            _confirmSale=false;RefreshTraderPanel();_traderMessage.text=value>0?TradeText(_selling?"sold":"bought")+" · "+value+" "+SmithText("gold"):TradeText("failed");
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
            bool ok=_traderCamp.RefreshTrader();_confirmRefresh=false;_traderSlot=-1;RefreshTraderPanel();_traderMessage.text=TradeText(ok?"refreshed":"funds");
            if(ok)GameSound.Sequence(("trader_shuffle",0f,.65f),("trader_flip",.3f,.6f),("trader_flip",.46f,.55f),("trader_chime",.75f,.3f));
        }
        internal bool ProbeTraderTransactions()
        {
            var original=_traderCamp;
            try
            {
                _traderCamp=new Camp(PrototypeContent.Items());_traderCamp.Earn(CurrencyType.Gold,1000);_selling=false;_traderSlot=0;RefreshTraderPanel();
                int price=_traderCamp.BuyPriceOf(_traderCamp.TraderStock(0));_traderAction.onClick.Invoke();
                if(_traderCamp.Bag.Used!=1 || !_traderCamp.TraderStock(0).IsEmpty || _traderCamp.Money(CurrencyType.Gold)!=1000-price)return false;
                SetTradeMode(true);_traderSlot=0;RefreshTraderPanel();int sell=Camp.PriceOf(_traderCamp.Bag.At(0));
                _traderAction.onClick.Invoke();if(_traderCamp.Bag.Used!=1)return false;_traderAction.onClick.Invoke();
                if(_traderCamp.Bag.Used!=0 || _traderCamp.Money(CurrencyType.Gold)!=1000-price+sell)return false;
                SetTradeMode(false);int gold=_traderCamp.Money(CurrencyType.Gold);_traderRefresh.onClick.Invoke();if(_traderCamp.Money(CurrencyType.Gold)!=gold)return false;
                _traderRefresh.onClick.Invoke();return _traderCamp.TraderGeneration==1 && _traderCamp.Money(CurrencyType.Gold)==gold-Camp.TraderRefreshPrice;
            }
            finally{_traderCamp=original;SetTradeMode(false);}
        }
    }
}
