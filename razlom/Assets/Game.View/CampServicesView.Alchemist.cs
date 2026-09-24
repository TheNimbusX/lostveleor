using Game.Sim;
using UnityEngine;
namespace Game.View
{
    public sealed partial class CampServicesView
    {
        Camp _alchemyCamp;bool _alchemyWired;
        static string PotionText(string key)=>CampServiceText.Get("potion."+key);
        void ShowAlchemist(bool show)
        {
            var s=_view.Alchemist;
            if(!show){s.Group.gameObject.SetActive(false);return;}
            _alchemyCamp=_driver.Session.Camp;_alchemyCamp.MeetAlchemist();WireAlchemist();
            _alchemyRecipes=false;_confirmOrder=-1;Present(s.Group,s.Potions[0].Buy);s.Message.text=CampServiceText.Get("dialogue.alchemist.open");RefreshAlchemy();
            // Открытие лавки: стекло на столе и тихое бурление котла.
            GameSound.Sequence(("alch_clink",0f,.5f),("alch_bubble",.12f,.3f));
        }
        void WireAlchemist()
        {
            if(_alchemyWired)return;_alchemyWired=true;var s=_view.Alchemist;
            s.Title.text=CampServiceText.Get("npc.alchemist");s.Speaker.text=s.Title.text;
            for(int i=0;i<s.Potions.Length && i<Camp.PotionKindCount;i++)
            {
                var kind=(PotionKind)i;var card=s.Potions[i];
                card.Name.text=PotionText(kind.ToString());
                card.Effect.text=i<4?PotionText("restore")+" "+Camp.PotionPercent(kind)+"% "+PotionText(Camp.PotionSlot(kind)==0?"health":"lavidium"):PotionText(kind+".effect");
                card.BuyLabel.text=PotionText("buy")+"  ·  "+Camp.PotionPrice(kind);
                card.LockedLabel.text=PotionText("recipe");
                card.Buy.onClick.AddListener(()=>BuyAlchemy(kind));
                card.Select.onClick.AddListener(()=>{if(!_alchemyCamp.SelectPotion(kind))return;RefreshAlchemy();
                    // Пробка, переливание и под ними тихое бурление — магия только вторым слоем.
                    GameSound.Sequence(("alch_cork",0f,.65f),("alch_pour",.14f,.55f),("alch_bubble",.45f,.3f));});
                // Закрытые зелья открывает заказ Лео: он живёт прямо в карточке своего зелья.
                if(i>=4 && card.OrderMain!=null)
                {
                    var order=i==4?AlchemistOrder.Resin:AlchemistOrder.Surge;
                    card.OrderMain.onClick.AddListener(()=>AdvanceOrder(order));
                    card.OrderAlt.onClick.AddListener(()=>ExchangeOrder(order));
                }
            }
            // Вкладка «Рецепты»: заказ Лео на каждое новое зелье.
            for(int i=0;i<s.Recipes.Length;i++)
            {
                var order=i==0?AlchemistOrder.Resin:AlchemistOrder.Surge;var kind=i==0?PotionKind.LivingResin:PotionKind.LavidiumSurge;var card=s.Recipes[i];
                card.Name.text=PotionText(kind.ToString());card.Effect.text=PotionText(kind+".effect");
                card.OrderMain.onClick.AddListener(()=>AdvanceOrder(order));
                card.OrderAlt.onClick.AddListener(()=>ExchangeOrder(order));
            }
            if(s.Tabs!=null && s.Tabs.Length==2 && s.Tabs[0]!=null)
            {
                s.Tabs[0].GetComponentInChildren<TMPro.TMP_Text>().text=CampServiceText.Get("alchemy.tab.potions");
                s.Tabs[1].GetComponentInChildren<TMPro.TMP_Text>().text=CampServiceText.Get("alchemy.tab.recipes");
                s.Tabs[0].onClick.AddListener(()=>{_alchemyRecipes=false;_confirmOrder=-1;RefreshAlchemy();});
                s.Tabs[1].onClick.AddListener(()=>{_alchemyRecipes=true;_confirmOrder=-1;RefreshAlchemy();});
            }
            s.Back.onClick.AddListener(Close);
            var backLabel=s.Back.GetComponentInChildren<TMPro.TMP_Text>();if(backLabel!=null)backLabel.text=CampServiceText.Get("back");
        }
        bool _alchemyRecipes;
        void BuyAlchemy(PotionKind kind)
        {
            bool success=_alchemyCamp.BuyPotion(kind);RefreshAlchemy();_view.Alchemist.Message.text=success?CampServiceText.Get("dialogue.alchemist.buy"):PotionText("failed");
            if(success)GameSound.Sequence(("alch_bottle",0f,.8f),("alch_clink",.2f,.5f),("coins",.35f,.4f));
        }
        void RefreshAlchemy()
        {
            var s=_view.Alchemist;
            s.Gold.text=_alchemyCamp.Money(CurrencyType.Gold).ToString();
            if(s.PotionsPage!=null)
            {
                s.PotionsPage.SetActive(!_alchemyRecipes);s.RecipesPage.SetActive(_alchemyRecipes);
                CampShopView.SetTab(s.Tabs[0],!_alchemyRecipes);CampShopView.SetTab(s.Tabs[1],_alchemyRecipes);
            }
            for(int i=0;i<s.Recipes.Length;i++)RefreshRecipe(s.Recipes[i],i==0?AlchemistOrder.Resin:AlchemistOrder.Surge);
            for(int i=0;i<s.Potions.Length && i<Camp.PotionKindCount;i++)
            {
                var kind=(PotionKind)i;var card=s.Potions[i];
                bool unlocked=_alchemyCamp.PotionUnlocked(kind);int count=_alchemyCamp.PotionCount(kind);
                bool selected=_alchemyCamp.SelectedPotion(Camp.PotionSlot(kind))==kind;
                card.Stock.text=PotionText("stock")+":  <color=#F4F7FB>"+count+"</color>";
                card.Locked.SetActive(!unlocked);
                card.Buy.gameObject.SetActive(unlocked);card.Select.gameObject.SetActive(unlocked);
                card.Buy.interactable=unlocked && count<Camp.PotionLimit && _alchemyCamp.Money(CurrencyType.Gold)>=Camp.PotionPrice(kind);
                card.SelectLabel.text=PotionText(selected?"selected":"select");card.Select.interactable=unlocked && !selected;
                card.Chosen.SetActive(unlocked && selected);
                card.Stock.gameObject.SetActive(unlocked);
                if(!unlocked && i>=4 && card.OrderMain!=null)RefreshOrder(card,i==4?AlchemistOrder.Resin:AlchemistOrder.Surge);
            }
        }
        static string OrderText(string key)=>CampServiceText.Get("order."+key);
        void RefreshRecipe(CampPotionCard card,AlchemistOrder order)
        {
            var state=_alchemyCamp.AlchemyStatus(order);bool open=state==AlchemistOrderStatus.Unlocked;
            card.Chosen.SetActive(open);
            card.Stock.text=OrderText(open?"state.done":state==AlchemistOrderStatus.Ready?"state.ready":state==AlchemistOrderStatus.Accepted?"state.inwork":"state.available");
            if(open){card.LockedLabel.text=OrderText("done");card.OrderMain.gameObject.SetActive(false);card.OrderAlt.gameObject.SetActive(false);return;}
            RefreshOrder(card,order);
        }
        int _confirmOrder=-1;
        void RefreshOrder(CampPotionCard card,AlchemistOrder order)
        {
            var state=_alchemyCamp.AlchemyStatus(order);bool resin=order==AlchemistOrder.Resin;
            string goal=OrderText(resin?"resin.goal":"surge.goal");
            card.LockedLabel.text=state==AlchemistOrderStatus.Ready?OrderText("ready"):state==AlchemistOrderStatus.Accepted?OrderText("inwork")+" "+goal:goal;
            card.OrderMain.gameObject.SetActive(state==AlchemistOrderStatus.Available || state==AlchemistOrderStatus.Ready);
            card.OrderMainLabel.text=OrderText(state==AlchemistOrderStatus.Ready?"turnin":"accept");
            card.OrderAlt.gameObject.SetActive(state==AlchemistOrderStatus.Accepted);
            bool confirm=_confirmOrder==(int)order;
            card.OrderAltLabel.text=confirm?OrderText("confirm"):OrderText(resin?"resin.exchange":"surge.exchange");
            card.OrderAlt.interactable=resin?ExchangeableRare()>=0:_alchemyCamp.Money(CurrencyType.Shards)>=Camp.SurgeUnlockShards;
            if(state==AlchemistOrderStatus.Accepted && resin && ExchangeableRare()<0)card.LockedLabel.text+="\n<size=85%>"+OrderText("norare")+"</size>";
        }
        int ExchangeableRare()
        {
            var bag=_alchemyCamp.Bag;
            for(int i=0;i<bag.Capacity;i++)if(!bag.IsEmpty(i) && !bag.IsKept(i) && bag.At(i).Rarity==ItemRarity.Magic)return i;
            return -1;
        }
        void AdvanceOrder(AlchemistOrder order)
        {
            bool ready=_alchemyCamp.AlchemyStatus(order)==AlchemistOrderStatus.Ready;
            var result=ready?_alchemyCamp.TurnInAlchemyOrder(order):_alchemyCamp.AcceptAlchemyOrder(order);
            _confirmOrder=-1;RefreshAlchemy();
            _view.Alchemist.Message.text=result==AlchemistActionResult.Success?CampServiceText.Get(ready?"dialogue.alchemist.order.complete":"dialogue.alchemist.order.accept"):OrderText("failed");
            if(result==AlchemistActionResult.Success)GameSound.Sequence(("alch_clink",0f,.5f),("alch_bubble",.15f,.35f));
        }
        void ExchangeOrder(AlchemistOrder order)
        {
            // Обмен забирает вещь или осколки: сначала подтверждение тем же нажатием.
            if(_confirmOrder!=(int)order){_confirmOrder=(int)order;RefreshAlchemy();return;}
            var result=order==AlchemistOrder.Resin?_alchemyCamp.ExchangeRareForResin(ExchangeableRare()):_alchemyCamp.ExchangeShardsForSurge();
            _confirmOrder=-1;RefreshAlchemy();
            _view.Alchemist.Message.text=result==AlchemistActionResult.Success?CampServiceText.Get("dialogue.alchemist.recipe.unlock"):OrderText("failed");
            if(result==AlchemistActionResult.Success)GameSound.Sequence(("alch_cork",0f,.65f),("alch_pour",.14f,.55f),("alch_bubble",.45f,.3f));
        }
        internal bool ProbeAlchemyTransactions()
        {
            var original=_alchemyCamp;var potions=_view.Alchemist.Potions;
            try
            {
                _alchemyCamp=new Camp(PrototypeContent.Items());_alchemyCamp.Earn(CurrencyType.Gold,200);RefreshAlchemy();
                for(int i=0;i<4;i++)potions[i].Buy.onClick.Invoke();
                if(_alchemyCamp.Money(CurrencyType.Gold)!=90)return false;
                for(int i=0;i<4;i++)if(_alchemyCamp.PotionCount((PotionKind)i)!=1)return false;
                potions[1].Select.onClick.Invoke();potions[3].Select.onClick.Invoke();
                if(_alchemyCamp.PotionSelection!=3)return false;
                var session=new GameSession(17,_alchemyCamp,PrototypeContent.Modules(),PrototypeContent.ItemBaseIds());
                session.ActiveSim.Entities.Health[0]=1;session.ActiveSim.Entities.Lavidium[0]=Fix64.Zero;
                session.Step(new InputFrame{PotionMask=10});
                if(_alchemyCamp.PotionCount(PotionKind.LargeHealth)!=0 || _alchemyCamp.PotionCount(PotionKind.LargeLavidium)!=0 || session.ActiveSim.Entities.Health[0]<=1 || session.ActiveSim.Entities.Lavidium[0]<=Fix64.One)return false;
                var hud=FindAnyObjectByType<CombatHudView>();
                if(hud!=null){bool valid=hud.ProbePotionDisplay(_alchemyCamp,_driver);hud.ProbePotionDisplay(_driver.Session.Camp,_driver);if(!valid)return false;}
                return true;
            }
            finally{_alchemyCamp=original;RefreshAlchemy();_view.Alchemist.Message.text="";}
        }
    }
}
