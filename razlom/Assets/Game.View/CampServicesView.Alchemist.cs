using Game.Sim;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed partial class CampServicesView
    {
        Camp _alchemyCamp;GameObject _alchemist;CanvasGroup _alchemistInput;Text _alchemyWallet,_alchemyMessage;
        readonly Text[] _potionStock=new Text[4];readonly Button[] _potionBuy=new Button[4],_potionSelect=new Button[4];
        static string PotionText(string key)=>CampServiceText.Get("potion."+key);
        void ShowAlchemist(bool show)
        {
            if(!show){if(_alchemist!=null)_alchemist.SetActive(false);return;}
            _alchemyCamp=_driver.Session.Camp;_serviceCard.SetActive(false);if(_alchemist==null)BuildAlchemist();
            _alchemist.SetActive(true);_alchemistInput.interactable=false;_alchemyMessage.text="";RefreshAlchemy();
            // Открытие лавки: стекло на столе и тихое бурление котла.
            GameSound.Sequence(("alch_clink",0f,.5f),("alch_bubble",.12f,.3f));
        }
        void BuildAlchemist()
        {
            _alchemist=new GameObject("Alchemist shop",typeof(RectTransform),typeof(Image),typeof(CanvasGroup));_alchemist.transform.SetParent(_panel.transform,false);
            ((RectTransform)_alchemist.transform).sizeDelta=new Vector2(810,560);_alchemist.GetComponent<Image>().color=Paper;_alchemistInput=_alchemist.GetComponent<CanvasGroup>();
            Label(_alchemist.transform,CampServiceText.Get("npc.alchemist"),new Vector2(-220,234),new Vector2(280,42),28);
            _alchemyWallet=Label(_alchemist.transform,"",new Vector2(185,234),new Vector2(300,35),20);
            SmithButton(_alchemist.transform,"×",new Vector2(370,236),new Vector2(40,40),Close);
            for(int i=0;i<4;i++)
            {
                var kind=(PotionKind)i;float x=-194+i%2*388,y=97-i/2*206;
                var card=new GameObject(kind.ToString(),typeof(RectTransform),typeof(Image));card.transform.SetParent(_alchemist.transform,false);((RectTransform)card.transform).anchoredPosition=new Vector2(x,y);((RectTransform)card.transform).sizeDelta=new Vector2(365,192);card.GetComponent<Image>().color=new Color(.85f,.84f,.74f);
                var art=new GameObject("Potion",typeof(RectTransform),typeof(RawImage));art.transform.SetParent(card.transform,false);((RectTransform)art.transform).anchoredPosition=new Vector2(-135,21);((RectTransform)art.transform).sizeDelta=Vector2.one*(i%2==0?62:80);art.GetComponent<RawImage>().texture=Resources.Load<Texture2D>(i<2?"UI/HUD/PotionHealth":"UI/HUD/PotionLavidium");art.GetComponent<RawImage>().raycastTarget=false;
                Label(card.transform,PotionText(kind.ToString()),new Vector2(32,58),new Vector2(275,44),19);
                Label(card.transform,PotionText("restore")+" "+Camp.PotionPercent(kind)+"%",new Vector2(30,19),new Vector2(230,30),18);
                _potionStock[i]=Label(card.transform,"",new Vector2(30,-13),new Vector2(250,28),17);
                _potionBuy[i]=SmithButton(card.transform,PotionText("buy")+" · "+Camp.PotionPrice(kind),new Vector2(-88,-65),new Vector2(165,41),()=>BuyAlchemy(kind));
                _potionSelect[i]=SmithButton(card.transform,"",new Vector2(88,-65),new Vector2(165,41),()=>{_alchemyCamp.SelectPotion(kind);RefreshAlchemy();
                    // Пробка, переливание и под ними тихое бурление — магия только вторым слоем.
                    GameSound.Sequence(("alch_cork",0f,.65f),("alch_pour",.14f,.55f),("alch_bubble",.45f,.3f));});
            }
            _alchemyMessage=Label(_alchemist.transform,"",new Vector2(0,-238),new Vector2(740,38),17);
        }
        void BuyAlchemy(PotionKind kind)
        {
            bool success=_alchemyCamp.BuyPotion(kind);RefreshAlchemy();_alchemyMessage.text=PotionText(success?"bought":"failed");
            if(success)GameSound.Sequence(("alch_bottle",0f,.8f),("alch_clink",.2f,.5f),("coins",.35f,.4f));
        }
        void RefreshAlchemy()
        {
            _alchemyWallet.text=SmithText("gold")+": "+_alchemyCamp.Money(CurrencyType.Gold);
            for(int i=0;i<4;i++)
            {
                var kind=(PotionKind)i;int count=_alchemyCamp.PotionCount(kind);_potionStock[i].text=PotionText("stock")+": "+count;
                _potionBuy[i].interactable=count<Camp.PotionLimit && _alchemyCamp.Money(CurrencyType.Gold)>=Camp.PotionPrice(kind);
                bool selected=_alchemyCamp.SelectedPotion(i/2)==kind;_potionSelect[i].GetComponentInChildren<Text>().text=PotionText(selected?"selected":"select");_potionSelect[i].interactable=!selected;
            }
        }
        internal bool ProbeAlchemyTransactions()
        {
            var original=_alchemyCamp;
            try
            {
                _alchemyCamp=new Camp(PrototypeContent.Items());_alchemyCamp.Earn(CurrencyType.Gold,200);RefreshAlchemy();
                for(int i=0;i<4;i++)_potionBuy[i].onClick.Invoke();
                if(_alchemyCamp.Money(CurrencyType.Gold)!=90)return false;
                for(int i=0;i<4;i++)if(_alchemyCamp.PotionCount((PotionKind)i)!=1)return false;
                _potionSelect[1].onClick.Invoke();_potionSelect[3].onClick.Invoke();
                if(_alchemyCamp.PotionSelection!=3)return false;
                var session=new GameSession(17,_alchemyCamp,PrototypeContent.Modules(),PrototypeContent.ItemBaseIds());
                session.ActiveSim.Entities.Health[0]=1;session.ActiveSim.Entities.Lavidium[0]=Fix64.Zero;
                session.Step(new InputFrame{PotionMask=10});
                if(_alchemyCamp.PotionCount(PotionKind.LargeHealth)!=0 || _alchemyCamp.PotionCount(PotionKind.LargeLavidium)!=0 || session.ActiveSim.Entities.Health[0]<=1 || session.ActiveSim.Entities.Lavidium[0]<=Fix64.One)return false;
                var hud=FindAnyObjectByType<CombatHudView>();
                if(hud!=null){bool valid=hud.ProbePotionDisplay(_alchemyCamp,_driver);hud.ProbePotionDisplay(_driver.Session.Camp,_driver);if(!valid)return false;}
                return true;
            }
            finally{_alchemyCamp=original;RefreshAlchemy();_alchemyMessage.text="";}
        }
    }
}
