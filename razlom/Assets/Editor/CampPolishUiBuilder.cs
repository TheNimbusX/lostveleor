using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole=Game.View.UiTheme.FontRole;
using Role=Game.View.UiTheme.Role;
namespace Game.EditorTools
{
    // Новые окна используют принятый материал UI; существующие ручные префабы не пересобираются.
    public static class CampPolishUiBuilder
    {
        const string Folder="Assets/Resources/UI/Prefabs/";
        [InitializeOnLoadMethod]
        static void ScheduleInstall(){if(!Application.isBatchMode)EditorApplication.update+=InstallWhenReady;}
        static void InstallWhenReady()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)return;
            EditorApplication.update-=InstallWhenReady;BuildAll();
        }
        [MenuItem("Разлом/Лагерь/Подготовить окна развития и похода")]
        public static void BuildAll()
        {
            UiThemeBuilder.Ensure(false);
            Save("CampPreparation",Preparation);
            Save("CampResidentProgress",Resident);
            Save("CampResidentButton",ResidentButton);
            CampForgeUiBuilder.Build();
            CampTraderProgressionUiBuilder.Build();
            ExpandAlchemy();
            AssetDatabase.SaveAssets();
        }
        static void Save(string name,System.Func<GameObject> create)
        {
            string path=Folder+name+".prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return;
            var root=create();try{Directory.CreateDirectory(Folder);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{Object.DestroyImmediate(root);}
        }
        internal static RectTransform Canvas(string name,out GameObject root,out CanvasGroup group,float width,float height)
        {
            root=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(UiScaleFollower));
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=160;
            canvas.additionalShaderChannels|=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.TexCoord2;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=1;
            group=root.AddComponent<CanvasGroup>();
            var veil=Layer((RectTransform)root.transform,"Вуаль",UiTheme.Current.Pixel,Role.Veil,.82f);veil.raycastTarget=true;
            var card=Node("Окно",root.transform);card.anchorMin=card.anchorMax=new Vector2(.5f,.5f);card.sizeDelta=new Vector2(width,height);
            UiInkKit.Plate(card);UiInkKit.HitArea(card);UiInkKit.Group(card,UiInkGroup.Sweep.FromCenter,.28f,.12f).Burn=.15f;
            return card;
        }
        internal static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,float size=22,Role role=Role.Text)
        {
            var node=TopLeft(Node(name,parent),x,y,w,h);
            var label=UiInkKit.Label(node,"Надпись",value,size>=30?FontRole.Heading:FontRole.Body,size,role,TextAlignmentOptions.TopLeft);
            label.textWrappingMode=TextWrappingModes.Normal;return label;
        }
        internal static Button Button(Transform parent,string name,float x,float y,float w,float h,out TMP_Text label,bool primary=false,float font=22)
        {
            var node=UiInkKit.Button((RectTransform)parent,name,name,primary,new Vector2(w,h),font);TopLeft(node,x,y,w,h);
            label=node.GetComponentInChildren<TMP_Text>();
            var button=node.GetComponent<Button>();var nav=button.navigation;nav.mode=Navigation.Mode.Automatic;button.navigation=nav;
            return button;
        }
        static GameObject Preparation()
        {
            var card=Canvas("CampPreparation",out var root,out var group,1480,860);
            var panel=root.AddComponent<CampPreparationPanel>();panel.Group=group;
            Text(card,"Заголовок","Перед походом",48,28,1380,58,40);
            Text(card,"Навык","Стартовый навык",48,110,310,40,28);
            Text(card,"Дары","Дар на этот поход",398,110,380,40,28);
            Text(card,"Зелья","Зелья в поход",836,110,590,40,28);
            panel.Starters=new Button[4];panel.StarterLabels=new TMP_Text[4];
            for(int i=0;i<4;i++)panel.Starters[i]=Button(card,"Навык "+i,48,172+i*96,306,80,out panel.StarterLabels[i]);
            panel.Gifts=new Button[3];panel.GiftLabels=new TMP_Text[3];
            for(int i=0;i<3;i++)panel.Gifts[i]=Button(card,"Дар "+i,398,172+i*84,392,68,out panel.GiftLabels[i]);
            panel.GiftDescription=Text(card,"Действие дара","",414,438,364,138,22,Role.Accent);
            panel.Slots=new Button[2];panel.SlotLabels=new TMP_Text[2];
            for(int i=0;i<2;i++)panel.Slots[i]=Button(card,"Позиция "+i,836,166+i*58,586,50,out panel.SlotLabels[i]);
            panel.Potions=new Button[8];panel.PotionLabels=new TMP_Text[8];
            for(int i=0;i<8;i++)panel.Potions[i]=Button(card,"Зелье "+i,836+i%2*299,302+i/2*70,287,62,out panel.PotionLabels[i],font:19);
            panel.PotionDescription=Text(card,"Действие зелья","",848,598,566,84,21,Role.Accent);
            panel.Status=Text(card,"Правила","",48,688,1374,64,20,Role.TextMuted);
            panel.Back=Button(card,"Остаться",48,776,306,56,out _);
            panel.Depart=Button(card,"Отправиться",1092,776,330,56,out _,true,26);
            root.SetActive(false);return root;
        }
        static GameObject Resident()
        {
            var card=Canvas("CampResidentProgress",out var root,out var group,960,710);
            var panel=root.AddComponent<CampResidentPanel>();panel.Group=group;
            panel.Title=Text(card,"Житель","",50,30,860,60,38);
            panel.Upgrade=Text(card,"Улучшение","",50,118,860,140,25);
            panel.Improve=Button(card,"Улучшить рабочее место",50,275,420,56,out _,true);
            panel.Status=Text(card,"Очки","",50,352,860,46,20,Role.Accent);
            panel.Chapter=Text(card,"Глава","",50,428,860,154,24);
            panel.TurnIn=Button(card,"Завершить главу",50,606,420,56,out _,true);
            panel.Back=Button(card,"Назад",626,606,284,56,out _);
            root.SetActive(false);return root;
        }
        static GameObject ResidentButton()
        {
            var root=Node("Развитие лагеря",null);
            var button=UiInkKit.Button(root,"Развитие","Развитие лагеря",false,new Vector2(280,48),22);
            TopLeft(root,40,24,280,48);Stretch(button);
            // Ссылка GetComponent<Button> нужна вызывающему окну.
            var actual=button.gameObject;actual.transform.SetParent(null,false);TopLeft((RectTransform)actual.transform,40,24,280,48);
            Object.DestroyImmediate(root.gameObject);return actual;
        }
        static void ExpandAlchemy()
        {
            string path=CampShopsWcBuilder.PrefabPath;if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)return;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<CampShopView>();var screen=view.Alchemist;
                if(screen.Potions.Length>=8)return;
                var old=screen.Potions;var cards=new CampPotionCard[8];System.Array.Copy(old,cards,old.Length);
                for(int i=6;i<8;i++)
                {
                    var clone=Object.Instantiate(old[0],old[0].transform.parent);clone.name="Зелье "+i;cards[i]=clone;
                }
                var viewport=TopLeft(Node("Прокрутка зелий",screen.PotionsPage.transform),660,206,1220,710);
                viewport.gameObject.AddComponent<RectMask2D>();
                var content=TopLeft(Node("Все виды зелий",viewport),0,0,1220,968);
                var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
                viewport.gameObject.AddComponent<CampAlchemyScrollFollow>();
                for(int i=0;i<8;i++)
                {
                    cards[i].transform.SetParent(content,false);
                    int col=i<4?i/2:i%2,row=i<4?i%2:i/2;
                    TopLeft((RectTransform)cards[i].transform,10+col*610,row*242,590,226);
                }
                screen.Potions=cards;PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
