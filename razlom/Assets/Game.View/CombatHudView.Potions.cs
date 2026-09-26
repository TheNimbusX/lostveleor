using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed partial class CombatHudView
    {
        [Header("Зелья: необязательные части префаба")]
        [Tooltip("Подсказка над зельем. Пусто — создаётся простая кремовая, как в первом HUD")]
        public RectTransform PotionTooltip;
        public TMP_Text PotionTooltipText;
        [Tooltip("Отступ подсказки зелья над плиткой, единицы Canvas")]
        public float PotionTooltipGap = 14f;

        readonly RectTransform[] _potionTiles=new RectTransform[2];
        readonly RawImage[] _potionArts=new RawImage[2];
        readonly TMP_Text[] _potionCounts=new TMP_Text[2],_potionKeys=new TMP_Text[2];
        readonly Texture[] _potionOriginal=new Texture[2];readonly Texture2D[] _potionGrey=new Texture2D[2];
        GameObject _potionTip;TMP_Text _potionTipText;
        void BindPotions()
        {
            if(PotionPanel==null || _potionTiles[0]!=null)return;
            for(int i=0;i<2;i++)
            {
                var tile=PotionPanel.Find(i==0?"Health Potion":"Lavidium Potion") as RectTransform;if(tile==null)continue;
                _potionTiles[i]=tile;_potionArts[i]=tile.Find("Art")?.GetComponent<RawImage>();_potionCounts[i]=tile.Find("Count")?.GetComponent<TMP_Text>();
                if(_potionArts[i]!=null){_potionOriginal[i]=_potionArts[i].texture;_potionGrey[i]=GreyPotion(_potionOriginal[i]);}
                // Подпись клавиши и размера: своя в префабе («Key»), иначе создаётся по-старому.
                _potionKeys[i]=tile.Find("Key")?.GetComponent<TMP_Text>() ?? tile.Find("Клавиша/Буква")?.GetComponent<TMP_Text>();
                if(_potionKeys[i]!=null)continue;
                var go=new GameObject("Potion key and size",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(tile,false);
                var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-2);rect.sizeDelta=new Vector2(tile.sizeDelta.x,20);
                var label=go.GetComponent<TextMeshProUGUI>();label.font=_potionCounts[i]!=null?_potionCounts[i].font:Level.font;label.fontSize=15;label.alignment=TextAlignmentOptions.Center;label.color=new Color(1,.96f,.83f);label.raycastTarget=false;_potionKeys[i]=label;
            }
            if(PotionTooltip!=null && PotionTooltipText!=null)
            {_potionTip=PotionTooltip.gameObject;_potionTipText=PotionTooltipText;_potionTip.SetActive(false);return;}
            _potionTip=new GameObject("Potion tooltip",typeof(RectTransform),typeof(Image));_potionTip.transform.SetParent(transform,false);((RectTransform)_potionTip.transform).sizeDelta=new Vector2(350,100);_potionTip.GetComponent<Image>().color=new Color(.94f,.89f,.78f,.98f);_potionTip.GetComponent<Image>().raycastTarget=false;
            var text=new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI));text.transform.SetParent(_potionTip.transform,false);((RectTransform)text.transform).sizeDelta=new Vector2(328,86);
            _potionTipText=text.GetComponent<TextMeshProUGUI>();_potionTipText.font=_potionKeys[0]?.font;_potionTipText.fontSize=18;_potionTipText.color=new Color(.16f,.22f,.19f);_potionTipText.alignment=TextAlignmentOptions.Center;_potionTipText.raycastTarget=false;_potionTip.SetActive(false);
        }
        public int PotionHit(Vector2 screen)
        {BindPotions();for(int i=0;i<2;i++)if(_potionTiles[i]!=null && Contains(_potionTiles[i],screen))return i;return -1;}
        readonly int[] _potionSeen=new int[Camp.PotionKindCount];
        Camp _potionCamp;

        /// <summary>
        /// Запас зелья уменьшился — его выпили: глоток и тихий отклик ресурса
        /// (здоровье или лавидий). Покупка запас только увеличивает, смена лагеря сбрасывает счёт.
        /// </summary>
        void SoundPotions(Camp camp)
        {
            if(camp!=_potionCamp){_potionCamp=camp;for(int k=0;k<_potionSeen.Length;k++)_potionSeen[k]=-1;}
            for(int k=0;k<_potionSeen.Length;k++)
            {
                int count=camp.PotionCount((PotionKind)k);
                if(_potionSeen[k]>=0&&count<_potionSeen[k])
                    GameSound.Sequence(("potion_drink",0f,.75f),("potion_gulp",.28f,.5f),
                        (Camp.PotionSlot((PotionKind)k)==0?"potion_heal":"potion_lavidium",.38f,.4f));
                _potionSeen[k]=count;
            }
        }
        void RefreshPotions(Camp camp,TickDriver driver)
        {
            if(camp==null)return;BindPotions();SoundPotions(camp);
            for(int i=0;i<2;i++)
            {
                var kind=camp.SelectedPotion(i);int count=camp.PotionCount(kind);
                if(_potionCounts[i]!=null)_potionCounts[i].text=count.ToString();
                if(_potionKeys[i]!=null)
                {
                    string key=TickDriver.GamepadLastUsed
                        ? (i==0?"←":"→")
                        : GameKeyBindings.Label(i==0?GameAction.HealthPotion:GameAction.LavidiumPotion);
                    // Клавиша-кружок растягивается в капсулу под длинную подпись после смены клавиши.
                    if(_potionKeys[i].text!=key){_potionKeys[i].text=key;FitKeycap(_potionKeys[i]);}
                }
                if(_potionArts[i]!=null)
                {
                    // Бутылка по выбранному зелью: малая или большая (набор предметов 21 сентября).
                    var art=PotionArt(kind);
                    if(art!=null && art!=_potionOriginal[i]){_potionOriginal[i]=art;_potionGrey[i]=GreyCached(art);}
                    _potionArts[i].texture=count>0?_potionOriginal[i]:_potionGrey[i];_potionArts[i].color=Color.white;
                }
            }
            if(_potionTip==null)return;int hover=PotionHit(Pointer);
            _potionTip.SetActive(hover>=0 && !driver.GameplayPaused && CampPlayerView.Instance?.InputBlocked!=true);
            if(!_potionTip.activeSelf)return;
            var selected=camp.SelectedPotion(hover);
            _potionTipText.text=CampServiceText.Get("potion."+selected)+" · "+Camp.PotionPercent(selected)+"%\n"+CampServiceText.Get(camp.PotionCount(selected)>0?"potion.use.hint":"potion.empty")+"\n"+CampServiceText.Get("potion.switch.hint");
            var corners=new Vector3[4];_potionTiles[hover].GetWorldCorners(corners);
            if(PotionTooltip!=null)
            {
                // Подсказка из префаба стоит низом над плиткой; высоту даёт её раскладка.
                LayoutRebuilder.ForceRebuildLayoutImmediate(PotionTooltip);
                PotionTooltip.pivot=new Vector2(.5f,0f);
                PotionTooltip.position=(corners[1]+corners[2])*.5f+Vector3.up*PotionTooltipGap*Scale;
            }
            else _potionTip.transform.position=(corners[1]+corners[2])*.5f+Vector3.up*65*Scale;
        }
        static readonly System.Collections.Generic.Dictionary<PotionKind,Texture2D> _potionArt=new System.Collections.Generic.Dictionary<PotionKind,Texture2D>();
        readonly System.Collections.Generic.Dictionary<Texture,Texture2D> _greyCache=new System.Collections.Generic.Dictionary<Texture,Texture2D>();
        /// <summary>Картинка зелья из Resources/UI/Items: здоровье или лавидий, малая или большая; у зелий заказов — большая.</summary>
        static Texture2D PotionArt(PotionKind kind)
        {
            if(_potionArt.TryGetValue(kind,out var cached))return cached;
            bool health=Camp.PotionSlot(kind)==0,large=kind==PotionKind.LargeHealth || kind==PotionKind.LargeLavidium || (int)kind>=4;
            var tex=Resources.Load<Texture2D>("UI/Items/potion_"+(health?"health":"lavidium")+(large?"_large":"_small"));
            _potionArt[kind]=tex;return tex;
        }
        Texture2D GreyCached(Texture source)
        {
            if(source==null)return null;
            if(!_greyCache.TryGetValue(source,out var grey)){grey=GreyPotion(source);_greyCache[source]=grey;}
            return grey;
        }
        static Texture2D GreyPotion(Texture source)
        {
            if(source==null)return null;
            var rt=RenderTexture.GetTemporary(128,128,0);var previous=RenderTexture.active;
            var grey=new Texture2D(128,128,TextureFormat.RGBA32,false);
            try{Graphics.Blit(source,rt);RenderTexture.active=rt;grey.ReadPixels(new Rect(0,0,128,128),0,0);var pixels=grey.GetPixels();for(int i=0;i<pixels.Length;i++){float v=pixels[i].grayscale*.7f;pixels[i]=new Color(v,v,v,pixels[i].a*.75f);}grey.SetPixels(pixels);grey.Apply(false,true);}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}return grey;
        }
        void OnDestroy(){foreach(var texture in _potionGrey)if(texture!=null)Destroy(texture);foreach(var texture in _greyCache.Values)if(texture!=null)Destroy(texture);}
        internal bool ProbePotionDisplay(Camp camp,TickDriver driver)
        {
            RefreshPotions(camp,driver);
            for(int i=0;i<2;i++)
            {
                int count=camp.PotionCount(camp.SelectedPotion(i));
                if(_potionCounts[i]==null || _potionCounts[i].text!=count.ToString() || _potionArts[i]==null || _potionArts[i].texture!=(count>0?_potionOriginal[i]:_potionGrey[i]))return false;
            }
            return true;
        }
    }
}
