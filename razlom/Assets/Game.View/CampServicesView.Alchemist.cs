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
            _alchemyCamp=_driver.Session.Camp;WireAlchemist();
            Present(s.Group,s.Potions[0].Buy);s.Message.text=CampServiceText.Get("dialogue.alchemist.open");if(s.Status!=null)s.Status.text="";RefreshAlchemy();
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
                card.Name.text=CampFeatureText.PotionName(kind);
                card.Effect.text=CampFeatureText.PotionEffect(kind);
                card.BuyLabel.text=PotionText("buy")+"  ·  "+Camp.PotionPrice(kind);
                card.LockedLabel.text=PotionText("recipe");
                card.Buy.onClick.AddListener(()=>BuyAlchemy(kind));
                card.Select.onClick.AddListener(()=>{if(!_alchemyCamp.SelectPotion(kind))return;RefreshAlchemy();
                    // Пробка, переливание и под ними тихое бурление — магия только вторым слоем.
                    GameSound.Sequence(("alch_cork",0f,.65f),("alch_pour",.14f,.55f),("alch_bubble",.45f,.3f));});
            }
            // Заказы Лео убраны 06.10: рецепты открывает только ранг лагеря. Кнопки заказа и вкладка
            // «Рецепты» остаются в префабе, но не подключаются и всегда скрыты (RefreshAlchemy).
            s.Back.onClick.AddListener(Close);
            var backLabel=s.Back.GetComponentInChildren<TMPro.TMP_Text>();if(backLabel!=null)backLabel.text=CampServiceText.Get("back");
        }

        /// <summary>Удача — реплика Лео в облачке; отказ — системная строка под карточками, облачко не меняется.</summary>
        void AlchemyResult(bool success,string dialogueKey,string failure)
        {
            if(success)_view.Alchemist.Message.text=CampServiceText.Get(dialogueKey);
            if(_view.Alchemist.Status!=null)_view.Alchemist.Status.text=success?"":failure;
        }
        void BuyAlchemy(PotionKind kind)
        {
            bool success=_alchemyCamp.BuyPotion(kind);RefreshAlchemy();AlchemyResult(success,"dialogue.alchemist.buy",PotionText("failed"));
            if(success)GameSound.Sequence(("alch_bottle",0f,.8f),("alch_clink",.2f,.5f),("coins",.35f,.4f));
        }
        /// <summary>
        /// Ранг лагеря, с которого Лео варит это зелье (решение 05.10): малые — сразу, большие — 1,
        /// Живица и Порыв — 2, Смешанный отвар и Ясный настой — 3. Только подпись закрытой карточки,
        /// само правило — Camp.PotionUnlocked.
        /// </summary>
        static int PotionRank(PotionKind kind)=>kind==PotionKind.LargeHealth || kind==PotionKind.LargeLavidium?1
            :kind==PotionKind.LivingResin || kind==PotionKind.LavidiumSurge?2:kind==PotionKind.Mixed || kind==PotionKind.Clear?3:0;
        void RefreshAlchemy()
        {
            var s=_view.Alchemist;
            s.Gold.text=_alchemyCamp.Money(CurrencyType.Gold).ToString();
            if(s.PotionsPage!=null)
            {
                s.PotionsPage.SetActive(true);if(s.RecipesPage!=null)s.RecipesPage.SetActive(false);
                if(s.Tabs!=null)foreach(var tab in s.Tabs)if(tab!=null)tab.gameObject.SetActive(false);
            }
            for(int i=0;i<s.Potions.Length && i<Camp.PotionKindCount;i++)
            {
                var kind=(PotionKind)i;var card=s.Potions[i];
                bool unlocked=_alchemyCamp.PotionUnlocked(kind);int count=_alchemyCamp.PotionCount(kind);
                bool selected=_alchemyCamp.SelectedPotion(0)==kind || _alchemyCamp.SelectedPotion(1)==kind;
                card.Stock.text=PotionText("stock")+":  <color=#F4F7FB>"+count+"</color>";
                card.Locked.SetActive(!unlocked);
                card.Buy.gameObject.SetActive(unlocked);card.Select.gameObject.SetActive(false);
                card.Buy.interactable=unlocked && count<Camp.PotionLimit && _alchemyCamp.Money(CurrencyType.Gold)>=Camp.PotionPrice(kind);
                card.SelectLabel.text=PotionText(selected?"selected":"select");card.Select.interactable=unlocked && !selected;
                card.Chosen.SetActive(unlocked && selected);
                card.Stock.gameObject.SetActive(unlocked);
                if(card.OrderMain!=null)card.OrderMain.gameObject.SetActive(false);if(card.OrderAlt!=null)card.OrderAlt.gameObject.SetActive(false);
                if(!unlocked)card.LockedLabel.text="Лео · ранг "+Mathf.Max(1,PotionRank(kind));
            }
        }
        internal bool ProbeAlchemyTransactions()
        {
            var original=_alchemyCamp;var potions=_view.Alchemist.Potions;
            try
            {
                // Isolated legacy camp opens all basic bottles; this never writes the owner's camp.
                _alchemyCamp=new Camp(PrototypeContent.Items(),act:3);_alchemyCamp.Earn(CurrencyType.Gold,200);RefreshAlchemy();
                for(int i=0;i<4;i++)potions[i].Buy.onClick.Invoke();
                if(_alchemyCamp.Money(CurrencyType.Gold)!=90)return false;
                for(int i=0;i<4;i++)if(_alchemyCamp.PotionCount((PotionKind)i)!=1)return false;
                var session=new GameSession(17,_alchemyCamp,PrototypeContent.Modules(),PrototypeContent.ItemBaseIds());
                if(!session.SetPreparedPotion(0,PotionKind.LargeHealth) || !session.SetPreparedPotion(1,PotionKind.LargeLavidium)
                    || _alchemyCamp.SelectedPotion(0)!=PotionKind.LargeHealth || _alchemyCamp.SelectedPotion(1)!=PotionKind.LargeLavidium)return false;
                session.EnterRift();
                var entities=session.ActiveSim.Entities;
                entities.Health[0]=1;entities.Lavidium[0]=Fix64.Zero;
                entities.Stats[0].SetBase(StatType.LavidiumRegen,Fix64.Zero);
                // Combat is irrelevant to this UI transaction probe, but consumption runs in a real Rift.
                void DisableFoes(){for(int id=1;id<entities.Count;id++)entities.Alive[id]=false;}
                DisableFoes();session.Step(new InputFrame{PotionSlotMask=3});
                int interval=8*Simulation.TicksPerSecond;
                if(_alchemyCamp.PotionCount(PotionKind.LargeHealth)!=0 || _alchemyCamp.PotionCount(PotionKind.LargeLavidium)!=1
                    || entities.Health[0]<=1 || entities.Lavidium[0]!=Fix64.Zero || session.PotionCooldownTicksLeft!=interval-1)return false;
                for(int tick=1;tick<interval;tick++)
                {
                    DisableFoes();session.Step(new InputFrame{PotionSlotMask=2});
                    if(_alchemyCamp.PotionCount(PotionKind.LargeLavidium)!=1 || entities.Lavidium[0]!=Fix64.Zero)return false;
                }
                if(session.PotionCooldownTicksLeft!=0)return false;
                DisableFoes();session.Step(new InputFrame{PotionSlotMask=2});
                if(_alchemyCamp.PotionCount(PotionKind.LargeLavidium)!=0 || entities.Lavidium[0]<=Fix64.One
                    || session.PotionCooldownTicksLeft!=interval-1)return false;
                var hud=FindAnyObjectByType<CombatHudView>();
                if(hud!=null && !hud.ProbePotionDisplay(_driver.Session.Camp,_driver))return false;
                return true;
            }
            finally{_alchemyCamp=original;RefreshAlchemy();_view.Alchemist.Message.text="";}
        }
    }
}
