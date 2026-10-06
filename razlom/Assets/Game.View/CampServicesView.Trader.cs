using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Торговец на одной странице (владелец 29.09, кадр «Торговец А»): «Товары» и «Сумка» рядом, под
    /// ними выбранная вещь со сравнением с надетым и одна кнопка по ней — «Купить · N» у товара,
    /// «Продать · +N» у вещи из сумки (продажа — со вторым нажатием: выкупа нет). Надетое — только
    /// посмотреть. «Обновить товары» — тоже со вторым нажатием.
    /// </summary>
    public sealed partial class CampServicesView
    {
        /// <summary>Откуда выбранная вещь: прилавок, сумка или надетое.</summary>
        enum TradePick { None, Stock, Bag, Worn }

        Camp _traderCamp;bool _traderWired;
        int _traderSlot=-1;TradePick _tradePick;bool _confirmSale,_confirmRefresh;
        readonly GeneratedItem _traderRoll=new GeneratedItem();
        static string TradeText(string key)=>CampServiceText.Get("trader."+key);
        void ShowTrader(bool show)
        {
            var s=_view.Trader;
            if(!show){s.Group.gameObject.SetActive(false);return;}
            _traderCamp=_driver.Session.Camp;
            WireTrader();
            // Без выбранной ячейки: Enter — кнопка сделки, а не Submit товару (он выбрал бы вещь и тут же её купил).
            Present(s.Group,null);_traderSlot=-1;_tradePick=TradePick.None;_confirmSale=_confirmRefresh=false;
            s.Message.text=CampServiceText.Get("dialogue.trader.open");RefreshTraderPanel();
            GameSound.Play("shop_bell",.55f);
        }
        void WireTrader()
        {
            if(_traderWired)return;_traderWired=true;var s=_view.Trader;
            s.Title.text=CampServiceText.Get("npc.trader");s.Speaker.text=s.Title.text;
            HideTabs(s);
            for(int i=0;i<s.Goods.Length;i++){int slot=i;if(s.Goods[i]?.Button!=null)s.Goods[i].Button.onClick.AddListener(()=>PickTrade(TradePick.Stock,slot));}
            for(int i=0;i<s.Cells.Length;i++){int slot=i;s.Cells[i].Button.onClick.AddListener(()=>PickTrade(TradePick.Bag,slot));}
            WireWorn(s,slot=>PickTrade(TradePick.Worn,slot));
            s.Extra.onClick.AddListener(RefreshTradeStock);
            s.Action.onClick.AddListener(ApplyTrade);
            s.Back.onClick.AddListener(Close);
            var backLabel=s.Back.GetComponentInChildren<TMPro.TMP_Text>();if(backLabel!=null)backLabel.text=CampServiceText.Get("back");
        }
        /// <summary>Выбор вещи; повторный выбор той же ничего не сбрасывает (Submit по Enter не снимает подтверждение).</summary>
        void PickTrade(TradePick pick,int slot)
        {
            if(pick==_tradePick && slot==_traderSlot)return;
            _tradePick=pick;_traderSlot=slot;_confirmSale=_confirmRefresh=false;_view.Trader.Message.text="";RefreshTraderPanel();
        }
        ItemInstance TradeItem()=>_traderSlot<0?default:_tradePick==TradePick.Stock?_traderCamp.TraderStock(_traderSlot)
            :_tradePick==TradePick.Bag?(_traderSlot<_traderCamp.Bag.Capacity?_traderCamp.Bag.At(_traderSlot):default)
            :_tradePick==TradePick.Worn?WornOrBag(_traderCamp,_traderSlot,true):default;
        void RefreshTraderPanel()
        {
            var s=_view.Trader;var camp=_traderCamp;var inventory=GetComponent<CampInventoryView>();int gold=camp.Money(CurrencyType.Gold);
            RefreshTraderProgression();
            s.Gold.text=gold.ToString();s.ShardsGroup.SetActive(false);
            HideTabs(s);
            Say(s.Subtitle,TradeText("subtitle"));Say(s.GoodsCaption,TradeText("stock.caption"));

            // Прилавок: вещь, название и цена; цена, которую не потянуть, — красным ещё до выбора.
            for(int i=0;i<s.Goods.Length;i++)
            {
                var good=s.Goods[i];if(good==null || good.Button==null)continue;
                bool exists=i<camp.TraderStockCount;Show(good.Button.gameObject,exists);if(!exists)continue;
                var item=camp.TraderStock(i);bool chosen=_tradePick==TradePick.Stock && _traderSlot==i;
                good.Cell.Show(item.IsEmpty?null:inventory.SpriteFor(item),item.IsEmpty?"":item.ItemLevel.ToString(),(int)item.Rarity,chosen);
                Show(good.Chosen,chosen);
                good.Name.text=item.IsEmpty?TradeText("sold.out"):inventory.ItemName(item.BaseId);Tone(good.Name,item.IsEmpty?UiTheme.Role.TextMuted:UiTheme.Role.Text);
                Show(good.PriceGroup,!item.IsEmpty);
                int price=item.IsEmpty?0:camp.BuyPriceOf(item);good.Price.text=price.ToString();Tone(good.Price,gold>=price?UiTheme.Role.Coins:UiTheme.Role.Bad);
                good.Button.interactable=!item.IsEmpty;
            }

            // Сумка: под каждой вещью — сколько за неё дадут; помеченное «беречь» — подписью.
            s.GridCaption.text=Format("trader.bag.count",TradeText("bag.caption"),camp.Bag.Used,camp.Bag.Capacity);
            for(int i=0;i<s.Cells.Length;i++)
            {
                var item=i<camp.Bag.Capacity?camp.Bag.At(i):default;
                s.Cells[i].gameObject.SetActive(i<camp.Bag.Capacity);s.Cells[i].Button.interactable=!item.IsEmpty;
                s.Cells[i].Show(item.IsEmpty?null:inventory.SpriteFor(item),item.IsEmpty?"":item.ItemLevel.ToString(),(int)item.Rarity,_tradePick==TradePick.Bag && i==_traderSlot);
                if(i>=s.CellPrices.Length || s.CellPrices[i]==null)continue;
                bool kept=!item.IsEmpty && camp.Bag.IsKept(i);
                s.CellPrices[i].text=item.IsEmpty?"":kept?TradeText("kept.tag"):"+"+Camp.PriceOf(item);
                Tone(s.CellPrices[i],kept?UiTheme.Role.TextMuted:UiTheme.Role.Coins);
            }
            ShowWorn(s,camp,_tradePick==TradePick.Worn?_traderSlot:-1,false);

            // Обновление товаров: цена на кнопке, второе нажатие подтверждает; ниже — шансы редкого.
            s.Extra.gameObject.SetActive(true);s.Extra.interactable=gold>=Camp.TraderRefreshPrice;
            s.ExtraLabel.text=Format(_confirmRefresh?"trader.refresh.confirm.label":"trader.refresh.label",Camp.TraderRefreshPrice);
            // Шанс — у платного обновления: оно всегда катает обычный шанс (Camp.RefreshTrader → RollTrader(false)),
            // даже когда на прилавке товар после босса. Раньше рядом с кнопкой стоял шанс текущего прилавка — 25%.
            s.Info.text=_confirmRefresh?TradeText("refresh.confirm.note"):Format("trader.info",Camp.TraderRareChance,Camp.TraderBossRareChance);
            Tone(s.Info,_confirmRefresh?UiTheme.Role.Bad:UiTheme.Role.TextMuted);

            s.Price.SetActive(false);s.Preview.text="";s.Note.text="";Tone(s.Note,UiTheme.Role.TextMuted);s.Detail.text="";Say(s.Compare,"");
            var chosenItem=TradeItem();
            if(chosenItem.IsEmpty)
            {
                s.Item.Show(null,"",0,false);s.ItemName.text=TradeText("choose");s.ItemMeta.text=TradeText("choose.hint");
                s.Action.interactable=false;s.ActionLabel.text=TradeText("buy");
                KeyHints(s,"","",_confirmRefresh);return;
            }
            s.Item.Show(inventory.SpriteFor(chosenItem),chosenItem.ItemLevel.ToString(),(int)chosenItem.Rarity,false);
            s.ItemName.text=inventory.ItemName(chosenItem.BaseId);
            int cracks=camp.CrackCount(chosenItem);
            s.ItemMeta.text=RarityAndLevel(chosenItem)+"  ·  "+SmithText("attempts")+" "+camp.AttemptsUsed(chosenItem)+"/"+Camp.TemperAttempts(chosenItem.Rarity)
                +(cracks>0?" · трещин "+cracks:"")+(_tradePick==TradePick.Worn?WornTag:"");
            ItemGenerator.Generate(chosenItem,camp.Items,_traderRoll);
            s.Detail.text=Properties(_traderRoll);
            Say(s.Compare,_tradePick==TradePick.Worn?Paint(TradeText("worn.now"),UiTheme.Role.TextMuted):Comparison(camp,inventory,chosenItem));

            bool buying=_tradePick==TradePick.Stock;
            var deal=buying?CampShopDeals.PlanBuy(camp,_traderSlot):CampShopDeals.PlanSell(camp,_traderSlot,_tradePick==TradePick.Worn);
            if(!deal.Allowed)_confirmSale=false;
            s.ActionLabel.text=buying?Format("trader.buy.action",deal.Price):_tradePick==TradePick.Worn?TradeText("sell")
                :Format(_confirmSale?"trader.sell.confirm.action":"trader.sell.action",deal.Price);
            s.Note.text=TradeReason(deal,buying);
            Tone(s.Note,_confirmSale || deal.Block==CampShopDeals.Block.Funds?UiTheme.Role.Bad:UiTheme.Role.TextMuted);
            s.Action.interactable=deal.Allowed;
            // Пока ждёт подтверждение обновления товаров, Enter не подписан: он не купит вместо ответа (ShopKeys).
            KeyHints(s,deal.Allowed && !_confirmRefresh?(_confirmSale?CampServiceText.Get("key.confirm"):TradeText(buying?"buy":"sell")):"","",_confirmSale || _confirmRefresh);
        }
        /// <summary>
        /// Что изменится, если надеть: заголовок называет, с чем сравнение («Вместо «Ржавая сабля»:»
        /// или «Если надеть:» при пустом слоте), ниже — только меняющиеся характеристики.
        /// </summary>
        string Comparison(Camp camp,CampInventoryView inventory,ItemInstance item)
        {
            int index=camp.Items.IndexOfBase(item.BaseId);
            var worn=index<0?default:camp.Worn.Worn(Equipment.SlotOf(camp.Items.GetBase(index).Category));
            string header=worn.IsEmpty?TradeText("compare.empty"):Format("trader.compare.instead",inventory.ItemName(worn.BaseId));
            string lines=inventory.CompareStats(item,_driver.Session.CampSim.Entities.Stats[0],Hex(UiTheme.Role.Good),Hex(UiTheme.Role.Bad),Hex(UiTheme.Role.TextMuted),true).Replace("\n\n","\n").Trim();
            return Paint(header,UiTheme.Role.TextMuted)+"\n"+(lines.Length==0?TradeText("unchanged"):lines);
        }
        string TradeReason(in CampShopDeals.Deal deal,bool buying)
        {
            switch(deal.Block)
            {
                case CampShopDeals.Block.None:
                    if(_confirmSale)return TradeText("sell.confirm.note");
                    return Format(buying?"trader.after.buy":"trader.after.sell",deal.GoldAfter);
                case CampShopDeals.Block.BagFull:return TradeText("full.note");
                case CampShopDeals.Block.Funds:return Format("trader.funds.short",deal.GoldShort);
                case CampShopDeals.Block.Protected:return TradeText("protected.note");
                case CampShopDeals.Block.Worn:return TradeText("worn.note");
                case CampShopDeals.Block.SoldOut:return TradeText("sold.out.note");
                default:return "";
            }
        }
        void ApplyTrade()
        {
            var s=_view.Trader;var camp=_traderCamp;
            if(!s.Action.interactable || _traderSlot<0 || _tradePick==TradePick.Worn || _tradePick==TradePick.None)return;
            _confirmRefresh=false;
            bool selling=_tradePick==TradePick.Bag;
            if(selling && !_confirmSale){_confirmSale=true;RefreshTraderPanel();return;}
            // Куда ляжет покупка: первая пустая ячейка (Inventory.Add) — её толкнём.
            int landing=-1;
            if(!selling)for(int i=0;i<camp.Bag.Capacity;i++)if(camp.Bag.IsEmpty(i)){landing=i;break;}
            int value=selling?camp.SellToTrader(_traderSlot):camp.BuyFromTrader(_traderSlot);
            _confirmSale=false;
            if(value>0){_traderSlot=-1;_tradePick=TradePick.None;}
            RefreshTraderPanel();
            // Реплика — только после удачи; отказ — системной строкой (CAMP-NPC-DIALOGUE.md).
            if(value>0)
            {
                s.Message.text=CampServiceText.Get(selling?"dialogue.trader.sell":"dialogue.trader.buy");
                s.Note.text=Paint(selling?Format("trader.sold.note",value):TradeText("bought.note"),UiTheme.Role.Accent);
                Punch(s.Gold);
                if(landing>=0 && landing<s.Cells.Length)Punch(s.Cells[landing].Icon,.2f);
            }
            else{s.Note.text=TradeText("failed");Tone(s.Note,UiTheme.Role.Bad);}
            // Продажа — монеты на стойку и вещь в ящик; покупка — кошель, пересчёт и тихий акцент.
            if(value>0)
            {
                if(selling)GameSound.Sequence(("trader_coins_table",0f,.8f),("trader_crate",.22f,.65f));
                else GameSound.Sequence(("trader_pouch",0f,.7f),("trader_count",.18f,.65f),("trader_chime",.6f,.3f));
            }
        }
        void RefreshTradeStock()
        {
            if(!_confirmRefresh){_confirmRefresh=true;_confirmSale=false;RefreshTraderPanel();return;}
            bool ok=_traderCamp.RefreshTrader();_confirmRefresh=false;
            if(ok && _tradePick==TradePick.Stock){_traderSlot=-1;_tradePick=TradePick.None;}
            RefreshTraderPanel();_view.Trader.Message.text=TradeText(ok?"refreshed":"funds");
            if(ok)
            {
                Punch(_view.Trader.Gold);
                foreach(var good in _view.Trader.Goods)if(good?.Cell!=null)Punch(good.Cell.Icon,.12f);
                GameSound.Sequence(("trader_shuffle",0f,.65f),("trader_flip",.3f,.6f),("trader_flip",.46f,.55f),("trader_chime",.75f,.3f));
            }
        }
        /// <summary>
        /// Самопроверка без прогресса (CampServicesProbe): покупка с прилавка одной кнопкой, место
        /// становится «Продано»; продажа из сумки на той же странице — второе нажатие; «беречь» не
        /// продаётся; обновление товаров — второе нажатие и списание.
        /// </summary>
        internal bool ProbeTraderTransactions()
        {
            var original=_traderCamp;var s=_view.Trader;var action=s.Action;var refresh=s.Extra;
            try
            {
                if(s.Goods.Length==0 || s.Goods[0]==null)return false;
                _traderCamp=new Camp(PrototypeContent.Items());_traderCamp.Earn(CurrencyType.Gold,1000);
                _tradePick=TradePick.None;_traderSlot=-1;_confirmSale=_confirmRefresh=false;RefreshTraderPanel();
                if(action.interactable)return false;
                s.Goods[0].Button.onClick.Invoke();
                if(_tradePick!=TradePick.Stock || _traderSlot!=0 || !action.interactable)return false;
                int price=_traderCamp.BuyPriceOf(_traderCamp.TraderStock(0));action.onClick.Invoke();
                if(_traderCamp.Bag.Used!=1 || !_traderCamp.TraderStock(0).IsEmpty || _traderCamp.Money(CurrencyType.Gold)!=1000-price || s.Goods[0].Button.interactable)return false;
                // «Беречь» не продаётся.
                _traderCamp.Bag.SetKeep(0,true);s.Cells[0].Button.onClick.Invoke();
                if(_tradePick!=TradePick.Bag || action.interactable)return false;
                _traderCamp.Bag.SetKeep(0,false);RefreshTraderPanel();int sell=Camp.PriceOf(_traderCamp.Bag.At(0));
                action.onClick.Invoke();if(_traderCamp.Bag.Used!=1 || !_confirmSale)return false;
                action.onClick.Invoke();
                if(_traderCamp.Bag.Used!=0 || _traderCamp.Money(CurrencyType.Gold)!=1000-price+sell)return false;
                int gold=_traderCamp.Money(CurrencyType.Gold);refresh.onClick.Invoke();
                if(_traderCamp.Money(CurrencyType.Gold)!=gold || !_confirmRefresh)return false;
                refresh.onClick.Invoke();
                return _traderCamp.TraderGeneration==1 && _traderCamp.Money(CurrencyType.Gold)==gold-Camp.TraderRefreshPrice && !_confirmRefresh;
            }
            finally{_traderCamp=original;_tradePick=TradePick.None;_traderSlot=-1;_confirmSale=_confirmRefresh=false;if(_traderCamp!=null)RefreshTraderPanel();s.Message.text="";}
        }
    }
}
