using Game.Sim;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Game.View
{
    public sealed class CampInventoryView : MonoBehaviour
    {
        public bool IsOpen => _root != null && _root.activeSelf;
        public Transform CanvasRoot => _root.transform;
        public bool HasItem(int index,bool worn)=>worn?_driver.Session.Camp.Worn.IsWorn((EquipSlot)index):!_driver.Session.Camp.Bag.IsEmpty(index);
        TickDriver _driver; GameObject _root; Font _font; Text _details;
        readonly Text[] _bag = new Text[48]; readonly Text[] _worn = new Text[5];
        int _selection; bool _selectedWorn;
        int _openedFrame;
        readonly GeneratedItem _roll = new GeneratedItem();
        static readonly string[] Names = { "Оружие", "Броня", "Кольцо", "Талисман", "Артефакт" };
        public void Initialize(TickDriver driver) { _driver = driver; }
        public static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        public void Open() { if (_root == null) Build(); _driver.ClearCapturedInput(); _driver.Sim?.StopPlayerMovement(); _root.SetActive(true); SyncPortraitStage(); _openedFrame = Time.frameCount; GameSound.Play("bag_open", .8f); Refresh(); if (_tent != null) StartCoroutine(RevealTent()); }
        public static int ClosedFrame { get; private set; } = -1;
        public void Close() { if (IsOpen) { _root.SetActive(false); SyncPortraitStage(); _driver.ClearCapturedInput(); ClosedFrame = Time.frameCount; } }
        void Update()
        {
            if (!IsOpen || _openedFrame == Time.frameCount) return;
#if ENABLE_INPUT_SYSTEM
            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) || GameKeyBindings.Pressed(GameAction.Interact)) Close();
#else
            if (Input.GetKeyDown(KeyCode.Escape) || GameKeyBindings.Pressed(GameAction.Interact)) Close();
#endif
        }
        const float Width=1672, Height=941;
        RectTransform _board;
        Text _itemTitle, _itemKind, _wallet, _count, _feedback;
        readonly Text[] _stats=new Text[4];
        Image _itemArt;
        readonly Image[] _bagIcons=new Image[48], _wornIcons=new Image[5];
        readonly Sprite[] _icons=new Sprite[6];
        readonly CampInventoryCell[] _bagCells=new CampInventoryCell[48], _wornCells=new CampInventoryCell[5];
        UnityEngine.UI.Button _equip, _unequip;
        Font _heading;
        int _filter=-1;
        GameObject _portraitStage;
        RenderTexture _portraitTexture;
        Camera _portraitCamera;
        readonly System.Collections.Generic.List<Material> _portraitMaterials=new System.Collections.Generic.List<Material>();
        static readonly Color Ink=new Color(.055f,.063f,.055f,.68f), Bronze=new Color(.52f,.40f,.23f), Ivory=new Color(.95f,.86f,.67f);

        // ---- страница ----
        //
        // Вкладка «Таланты» убрана 15 сентября: с разворота в роглайк таланты
        // берутся в забеге, а для отладки визуала включаются в F8. Её место на
        // доске займёт атлас артефактов. Снаряжение остаётся отдельным
        // контейнером, чтобы атлас встал рядом без пересборки доски.
        RectTransform _equipmentPage;

        void Build()
        {
            // Палатка в новом стиле (16 сентября): префаб CampTentBuilder. Без него — прежняя доска.
            var tentPrefab=Resources.Load<GameObject>("UI/Prefabs/CampTent");
            if(tentPrefab!=null&&BuildTent(tentPrefab))return;
            _font=GameTypography.Regular;
            _heading=GameTypography.Semibold;
            _root=new GameObject("Camp Inventory",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=_root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=_root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(Width,Height);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            EnsureEventSystem();
            var shade=Box(_root.transform,"Modal shade",0,0,Width,Height,new Color(.02f,.03f,.02f,.97f));
            shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;
            _board=Box(_root.transform,"Inventory board",0,0,Width,Height,Color.white);
            _board.anchorMin=_board.anchorMax=_board.pivot=new Vector2(.5f,.5f);_board.anchoredPosition=Vector2.zero;
            _board.GetComponent<Image>().sprite=FullSprite(Resources.Load<Texture2D>("UI/Inventory/InventoryBackdrop"));
            LoadIcons();
            Label(_board,"ЛЕСНОЙ ЛАГЕРЬ",65,58,290,35,18);
            Title(_board,"СНАРЯЖЕНИЕ",505,46,660,50,34,TextAnchor.MiddleCenter);
            _wallet=Label(_board,"",1210,54,335,45,23);
            Button(_board,"×",1570,38,64,64,Close);
            _equipmentPage=Page("Equipment page");

            var page=_equipmentPage;
            Title(page,"ПЕЛАГ",198,170,380,48,40);
            Label(page,"Снаряжение странника",200,216,370,30,20);
            CreatePortrait();
            float[] xs={98,98,526,98,526};float[] ys={302,465,310,622,523};
            for(int i=0;i<5;i++)
            {
                var frame=Slot(page,"Equipment "+Names[i],xs[i],ys[i],102,102,true);
                _wornIcons[i]=Icon(frame,_icons[i],10,10,82,82);
                _worn[i]=Title(page,Names[i],xs[i]-12,ys[i]+104,130,30,23,TextAnchor.MiddleCenter);
                _wornCells[i]=AddCell(frame,i,true);
            }
            var statNames=new[]{"Атака","Броня","Здоровье","Скорость"};
            for(int i=0;i<4;i++){Label(page,statNames[i],96+i*135,807,128,27,18);_stats[i]=Title(page,"",96+i*135,833,128,31,25);}
            _itemArt=Icon(page,null,712,195,254,213);
            _itemTitle=Title(page,"Выберите предмет",713,413,255,76,32);
            _itemKind=Label(page,"",715,493,250,32,18);
            var viewport=Box(page,"Item comparison",713,535,254,124,Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            _details=Label(viewport,"",0,0,247,280,19);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=_details.rectTransform;scroll.horizontal=false;scroll.scrollSensitivity=22;
            _equip=Button(page,"НАДЕТЬ",710,680,257,49,EquipSelected);
            _unequip=Button(page,"СНЯТЬ",710,739,257,44,UnequipSelected);
            Title(page,"СУМКА",1068,172,280,47,38);
            _count=Label(page,"",1465,182,115,30,22);_count.alignment=TextAnchor.MiddleRight;
            for(int f=-1;f<5;f++)
            {
                int filter=f;
                var tab=Button(page,f<0?"ВСЕ":"",1069+(f+1)*88,225,84,43,()=>{_filter=filter;Refresh();});
                if(f>=0)Icon(tab.transform,_icons[f],23,3,38,38);
            }
            for(int i=0;i<48;i++)
            {
                var frame=Slot(page,"Bag "+i,1067+(i%8)*67,282+(i/8)*75,64,72,false);
                _bagIcons[i]=Icon(frame,null,3,5,58,58);
                _bag[i]=Label(frame,"",3,52,57,17,12);_bag[i].alignment=TextAnchor.LowerRight;
                _bagCells[i]=AddCell(frame,i,false);
            }
            Label(page,"Двойной клик — надеть  ·  Перетащите предмет",1080,751,510,30,18);
            _feedback=Label(page,"",714,846,882,40,19);

            Label(_board,"I / Esc — закрыть",70,890,400,30,17);
            SyncPortraitStage();
            Refresh();
        }

        RectTransform Page(string name)
        {
            var page=Box(_board,name,0,0,Width,Height,Color.clear);
            page.GetComponent<Image>().raycastTarget=false;
            return page;
        }

        void SyncPortraitStage(){if(_portraitStage!=null)_portraitStage.SetActive(IsOpen);}

        void EquipSelected(){SetFeedback("");Animated(()=>{if(!_selectedWorn&&!_driver.Session.Camp.EquipFromBag(_selection))SetFeedback("Не удалось надеть предмет.");});}
        void UnequipSelected()
        {
            SetFeedback("");
            if(_driver.Session.Camp.Bag.IsFull){SetFeedback("Сумка заполнена. Освободите ячейку или обменяйте вещь перетаскиванием.");return;}
            Animated(()=>{
            if(_selectedWorn)_driver.Session.Camp.UnequipToBag((EquipSlot)_selection);
            else {var item=_driver.Session.Camp.Bag.At(_selection);int b=_driver.Session.Camp.Items.IndexOfBase(item.BaseId);
                if(!item.IsEmpty&&b>=0)_driver.Session.Camp.UnequipToBag(Equipment.SlotOf(_driver.Session.Camp.Items.GetBase(b).Category));}
            });
        }
        static Sprite FullSprite(Texture2D tex)=>tex!=null?Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100):null;
        Image Icon(Transform parent,Sprite sprite,float x,float y,float w,float h)
        {var rect=Box(parent,"Icon",x,y,w,h,Color.white);var image=rect.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;image.enabled=sprite!=null;return image;}
        Text Title(Transform p,string t,float x,float y,float w,float h,int size,TextAnchor alignment=TextAnchor.UpperLeft)
        {var label=Label(p,t,x,y,w,h,size);label.font=_heading;label.alignment=alignment;return label;}
        RectTransform Slot(Transform p,string name,float x,float y,float w,float h,bool ornate)
        {
            var r=Box(p,name,x,y,w,h,Ink);
            var edge=new GameObject("Border",typeof(RectTransform),typeof(InventoryFrame)); var edgeRect=(RectTransform)edge.transform; edgeRect.SetParent(r,false); edgeRect.anchorMin=Vector2.zero; edgeRect.anchorMax=Vector2.one; edgeRect.offsetMin=edgeRect.offsetMax=Vector2.zero; var frame=edge.GetComponentInChildren<InventoryFrame>();frame.Border=Bronze;frame.Cut=ornate?18:3;frame.Thickness=ornate?4:1;frame.raycastTarget=false;
            return r;
        }
        bool CreatePortraitStage()
        {
            var arena=FindAnyObjectByType<ArenaView>();if(arena==null)return false;
            _portraitTexture=new RenderTexture(640,900,24,RenderTextureFormat.ARGB32);_portraitTexture.Create();
            // Position far outside all gameplay cameras; no scene lighting or authored camera is changed.
            _portraitStage=PortraitStudio.Create(arena,"Inventory portrait studio",new Vector3(1000,1000,1000),new Vector3(0,.91f,4),1.02f,_portraitTexture,_portraitMaterials,out _portraitCamera);
            return true;
        }
        void CreatePortrait()
        {
            if(!CreatePortraitStage())return;
            var portrait=Box(_equipmentPage,"Pelag portrait",202,237,326,528,Color.clear);
            var portraitImage=new GameObject("Portrait image",typeof(RectTransform),typeof(RawImage)); var portraitRect=(RectTransform)portraitImage.transform; portraitRect.SetParent(portrait,false); portraitRect.anchorMin=Vector2.zero; portraitRect.anchorMax=Vector2.one; portraitRect.offsetMin=portraitRect.offsetMax=Vector2.zero; var raw=portraitImage.GetComponent<RawImage>();raw.texture=_portraitTexture;raw.raycastTarget=false;
        }
        CampInventoryCell AddCell(RectTransform rect,int index,bool worn)
        { var cell=rect.gameObject.AddComponent<CampInventoryCell>(); cell.Owner=this;cell.Index=index;cell.Worn=worn; return cell; }
        public void Select(int index,bool worn,bool twice)
        { _selection=index;_selectedWorn=worn;if(twice)Animated(()=>{if(worn)_driver.Session.Camp.UnequipToBag((EquipSlot)index);else _driver.Session.Camp.EquipFromBag(index);});else Refresh(); }
        public void Drop(CampInventoryCell from,CampInventoryCell to)
        {
            if(from.Owner!=this||to.Owner!=this||!from.Selectable||!to.Selectable||!HasItem(from.Index,from.Worn))return;
            var camp=_driver.Session.Camp;
            SetFeedback("");
            Animated(()=>{
            if(!from.Worn&&!to.Worn)camp.Bag.Swap(from.Index,to.Index);
            else if(from.Worn&&!to.Worn){if(!camp.UnequipToSlot((EquipSlot)from.Index,to.Index))SetFeedback("Для обмена выберите вещь того же типа или пустую ячейку.");}
            else if(!from.Worn&&to.Worn)
            {
                var item=camp.Bag.At(from.Index);int b=camp.Items.IndexOfBase(item.BaseId);
                if(!item.IsEmpty&&b>=0&&Equipment.SlotOf(camp.Items.GetBase(b).Category)==(EquipSlot)to.Index)camp.EquipFromBag(from.Index);
                else SetFeedback("Этот предмет не подходит для ячейки «"+Names[to.Index]+"».");
            }
            _selection=to.Index;_selectedWorn=to.Worn;
            });
        }
        string Describe(ItemInstance item)
        { if(item.IsEmpty)return "—";int index=_driver.Session.Camp.Items.IndexOfBase(item.BaseId);return index>=0?ItemName(item.BaseId)+"\nур. "+item.ItemLevel:"Неизвестный предмет"; }
        string ItemName(int id)
        {if(id==StableId.Of("base.rusty_sword"))return "Старая сабля";if(id==StableId.Of("base.leather_jacket"))return "Кожаная куртка";if(id==StableId.Of("base.copper_ring"))return "Медное кольцо";if(id==StableId.Of("base.woodland_talisman"))return "Лесной талисман";if(id==StableId.Of("base.memory_shard"))return "Осколок памяти";return "Предмет";}
        int Category(ItemInstance item)
        {int b=_driver.Session.Camp.Items.IndexOfBase(item.BaseId);return item.IsEmpty||b<0?-1:(int)Equipment.SlotOf(_driver.Session.Camp.Items.GetBase(b).Category);}
        public Sprite ItemSprite(int index,bool worn)
        {var item=worn?_driver.Session.Camp.Worn.Worn((EquipSlot)index):_driver.Session.Camp.Bag.At(index);int category=Category(item);return category<0?null:RaritySprite(category,(int)item.Rarity)??_icons[category];}

        // Значки вещей по слоту и редкости (16 сентября): Resources/UI/Items/{слот}_{редкость}.png.
        // Нет файла — остаётся общий значок категории из атласа.
        static readonly string[] SlotFiles={"weapon","armor","ring","talisman"}, RarityFiles={"common","rare","epic","unique"};
        readonly Sprite[] _raritySprites=new Sprite[16];
        readonly bool[] _rarityLoaded=new bool[16];
        Sprite RaritySprite(int category,int rarity)
        {
            if(category<0||category>=SlotFiles.Length||rarity<0||rarity>=RarityFiles.Length)return null;
            int key=category*4+rarity;
            if(!_rarityLoaded[key])
            {
                _rarityLoaded[key]=true;
                var tex=Resources.Load<Texture2D>("UI/Items/"+SlotFiles[category]+"_"+RarityFiles[rarity]);
                if(tex!=null)_raritySprites[key]=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100);
            }
            return _raritySprites[key];
        }

        /// <summary>
        /// Меняет снаряжение и показывает это: вещь, переехавшая между сумкой и
        /// слотом, перелетает копией, слот вспыхивает цветом редкости, статы досчитывают.
        /// </summary>
        void Animated(System.Action change)
        {
            var camp=_driver.Session.Camp;
            if(_tent==null){change();Refresh();return;}
            int worn=Mathf.Min(4,_tent.Worn.Length);
            var wornBefore=new ItemInstance[worn];var bagBefore=new ItemInstance[48];
            for(int i=0;i<worn;i++)wornBefore[i]=camp.Worn.Worn((EquipSlot)i);
            for(int i=0;i<48;i++)bagBefore[i]=camp.Bag.At(i);
            change();
            bool moved=false;
            for(int s=0;s<worn;s++)
            {
                var now=camp.Worn.Worn((EquipSlot)s);
                if(now.IsEmpty||Same(now,wornBefore[s])||_tent.Worn[s]==null)continue;
                int from=System.Array.FindIndex(bagBefore,b=>Same(b,now));
                if(from<0)continue;
                moved=true;
                _tent.Fly(ItemSprite(s,true),(RectTransform)_tentBag[from].transform,_tent.Worn[s],_tent.ColourFor((int)now.Rarity),Refresh);
            }
            for(int j=0;j<48;j++)
            {
                var now=camp.Bag.At(j);
                if(now.IsEmpty||Same(now,bagBefore[j]))continue;
                int from=System.Array.FindIndex(wornBefore,w=>Same(w,now));
                if(from<0||_tent.Worn[from]==null)continue;
                moved=true;
                _tent.Fly(ItemSprite(j,false),(RectTransform)_tent.Worn[from].transform,_tentBag[j],_tent.ColourFor((int)now.Rarity),Refresh);
            }
            if(moved)GameSound.Play("bag_open",.55f);
            Refresh();
        }
        static bool Same(ItemInstance a,ItemInstance b)=>a.BaseId==b.BaseId&&a.Seed==b.Seed&&a.ItemLevel==b.ItemLevel&&a.Rarity==b.Rarity;
        public void Refresh()
        {
            if(_root==null)return;
            if(_tent!=null){RefreshTent();return;}
            var camp=_driver.Session.Camp;
            _wallet.text="● "+camp.Money(CurrencyType.Gold)+"    ◆ "+camp.Money(CurrencyType.Shards)+"    ◈ "+camp.Money(CurrencyType.Lavidium);
            int count=0;
            for(int i=0;i<48;i++)
            {
                var item=camp.Bag.At(i);if(!item.IsEmpty)count++;
                int category=Category(item);bool visible=_filter<0||category==_filter;
                _bagIcons[i].sprite=ItemSprite(i,false);_bagIcons[i].enabled=!item.IsEmpty&&visible;
                _bag[i].text=!item.IsEmpty&&visible?item.ItemLevel.ToString():"";
                _bagCells[i].Selectable=visible||item.IsEmpty;
                _bagCells[i].GetComponentInChildren<InventoryFrame>(true).SetSelected(!_selectedWorn&&i==_selection);
            }
            for(int i=0;i<5;i++)
            {
                _wornIcons[i].sprite=ItemSprite(i,true)??_icons[i];
                _wornIcons[i].color=camp.Worn.IsWorn((EquipSlot)i)?Color.white:new Color(.6f,.56f,.44f,.18f);
                _wornCells[i].GetComponentInChildren<InventoryFrame>(true).SetSelected(_selectedWorn&&i==_selection);
            }
            _count.text=count+" / 48";
            var stats=_driver.Session.CampSim.Entities.Stats[0];
            _stats[0].text=stats.Get(StatType.Damage).ToString();_stats[1].text=stats.Get(StatType.Armor).ToString();_stats[2].text=stats.Get(StatType.MaxHealth).ToString();_stats[3].text=stats.Get(StatType.MoveSpeed).ToString();
            var chosen=_selectedWorn?camp.Worn.Worn((EquipSlot)_selection):camp.Bag.At(_selection);
            _itemTitle.text=chosen.IsEmpty?"Выберите предмет":ItemName(chosen.BaseId);
            int kind=Category(chosen);
            _itemKind.text=chosen.IsEmpty?"Сумка и снаряжение":Names[kind]+"   ·   Ур. "+chosen.ItemLevel;
            _itemArt.sprite=ItemSprite(_selection,_selectedWorn);_itemArt.enabled=!chosen.IsEmpty;
            _details.text=chosen.IsEmpty?"Выберите вещь в сумке или в ячейке снаряжения.":"";
            _equip.interactable=!chosen.IsEmpty&&!_selectedWorn;
            _unequip.interactable=kind>=0&&camp.Worn.IsWorn((EquipSlot)kind);
            if(!chosen.IsEmpty)_details.text+=CompareStats(chosen,stats,"#99D86B","#EA8D76","#D8C5A0");
            _details.rectTransform.sizeDelta=new Vector2(247,Mathf.Max(124,_details.preferredHeight));
            _details.rectTransform.anchoredPosition=Vector2.zero;
        }

        // ---- палатка на Canvas ----
        CampTentView _tent;
        readonly CampTentCell[] _tentBag=new CampTentCell[48];

        bool BuildTent(GameObject prefab)
        {
            _root=Instantiate(prefab);
            _root.name="Camp Tent";
            _tent=_root.GetComponentInChildren<CampTentView>(true);
            if(_tent==null||_tent.BagTemplate==null||_tent.BagGrid==null){Destroy(_root);_root=null;_tent=null;return false;}
            EnsureEventSystem();
            LoadIcons();
            if(_tent.Close!=null)_tent.Close.onClick.AddListener(Close);
            if(_tent.CloseHint!=null)_tent.CloseHint.onClick.AddListener(Close);
            if(_tent.Equip!=null)_tent.Equip.onClick.AddListener(EquipSelected);
            if(_tent.Unequip!=null)_tent.Unequip.onClick.AddListener(UnequipSelected);
            for(int f=0;f<_tent.Filters.Length;f++)
            {
                int tab=f,filter=f-1;
                if(_tent.Filters[f]!=null)_tent.Filters[f].onClick.AddListener(()=>{_filter=filter;UiSound.Play(UiSoundEvent.Tab);_tent.ShowFilter(tab,false);Refresh();});
                if(f>0&&f<_tent.FilterIcons.Length&&_tent.FilterIcons[f]!=null)_tent.FilterIcons[f].sprite=_icons[f-1];
            }
            for(int i=0;i<_tent.Worn.Length&&i<4;i++)
            {
                var cell=_tent.Worn[i];if(cell==null)continue;
                if(cell.Placeholder!=null)cell.Placeholder.sprite=_icons[i];
                _wornCells[i]=AddCell((RectTransform)cell.transform,i,true);
            }
            for(int i=0;i<48;i++)
            {
                var cell=Instantiate(_tent.BagTemplate,_tent.BagGrid);
                cell.name="Bag "+i;
                cell.gameObject.SetActive(true);
                _tentBag[i]=cell;
                _bagCells[i]=AddCell((RectTransform)cell.transform,i,false);
            }
            // Рисованный Пелаг владельца: лежит файлом в Resources и подхватывается без пересборки префаба.
            if(_tent.HeroArt!=null&&_tent.HeroArt.sprite==null)
            {
                var art=Resources.Load<Texture2D>("UI/Tent/PelagArt");
                if(art!=null)
                {
                    _heroSprite=Sprite.Create(art,new Rect(0,0,art.width,art.height),new Vector2(.5f,0f),100);
                    _tent.HeroArt.sprite=_heroSprite;
                    _tent.HeroArt.enabled=true;
                }
            }
            bool painted=_tent.HeroArt!=null&&_tent.HeroArt.sprite!=null;
            if(_tent.Portrait!=null)_tent.Portrait.enabled=!painted;
            if(!painted&&_tent.Portrait!=null&&CreatePortraitStage())
            {
                _tent.Portrait.texture=_portraitTexture;
                // Студия снимает 640×900, а карточка уже: срезаем бока, а не сплющиваем героя.
                var rect=_tent.Portrait.rectTransform.rect;
                float want=rect.width/Mathf.Max(1f,rect.height),have=640f/900f;
                _tent.Portrait.uvRect=want<have?new Rect((1f-want/have)*.5f,0f,want/have,1f):new Rect(0f,(1f-have/want)*.5f,1f,have/want);
            }
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-capture-tent-rarities")>=0)AddRaritySample();
            _tent.ShowFilter(0,true);
            SyncPortraitStage();
            Refresh();
            return true;
        }

        System.Collections.IEnumerator RevealTent()
        {
            // Снимок лагеря берётся, пока палатка прозрачна, иначе размытие захватит её саму.
            _tent.HideForSnapshot();
            if(_tent.BackdropBlur!=null)yield return _tent.BackdropBlur.Capture();
            _tent.PlayOpen();
        }

        int _hoverIndex=-1; bool _hoverWorn;
        Sprite _heroSprite;

        /// <summary>
        /// Только для съёмки (-capture-tent-rarities): по вещи каждого слота каждой
        /// редкости, уникальные отмечены «беречь» и одна выбрана — на кадре видны
        /// шкала рамок, замок и карточка предмета.
        /// </summary>
        void AddRaritySample()
        {
            var bag=_driver.Session.Camp.Bag;
            string[] bases={"base.rusty_sword","base.leather_jacket","base.copper_ring","base.woodland_talisman"};
            int unique=-1;
            for(int r=3;r>=0;r--)
                for(int b=0;b<bases.Length;b++)
                {
                    int slot=bag.Add(new ItemInstance(StableId.Of(bases[b]),(short)(2+r*4),(ItemRarity)r,(ulong)(9000+r*10+b)));
                    if(slot<0)continue;
                    if(r==3){bag.SetKeep(slot,true);if(unique<0)unique=slot;}
                }
            if(unique>=0){_selection=unique;_selectedWorn=false;_hoverIndex=unique;_hoverWorn=false;}
        }

        /// <summary>Мышь над ячейкой: карточка предмета показывает её, а не выбранную.</summary>
        public void Hover(int index,bool worn,bool on)
        {
            if(_tent==null)return;
            if(on){_hoverIndex=index;_hoverWorn=worn;}
            else if(_hoverIndex==index&&_hoverWorn==worn)_hoverIndex=-1;
            RefreshTooltip();
        }

        /// <summary>ПКМ по вещи в сумке: «беречь» — разбор её не заберёт.</summary>
        public void ToggleKeep(int index)
        {
            var bag=_driver.Session.Camp.Bag;
            if(bag.IsEmpty(index))return;
            bag.SetKeep(index,!bag.IsKept(index));
            UiSound.Play(UiSoundEvent.Toggle);
            SetFeedback(bag.IsKept(index)?"Вещь отмечена — в разбор не уйдёт.":"");
            Refresh();
        }

        void RefreshTent()
        {
            var camp=_driver.Session.Camp;
            if(_tent.Gold!=null)_tent.Gold.text=camp.Money(CurrencyType.Gold).ToString();
            if(_tent.Shards!=null)_tent.Shards.text=camp.Money(CurrencyType.Shards).ToString();
            if(_tent.Lavidium!=null)_tent.Lavidium.text=camp.Money(CurrencyType.Lavidium).ToString();
            int count=0;
            for(int i=0;i<48;i++)
            {
                var item=camp.Bag.At(i);if(!item.IsEmpty)count++;
                int category=Category(item);bool visible=_filter<0||category==_filter;
                _bagCells[i].Selectable=visible||item.IsEmpty;
                _tentBag[i].Show(item.IsEmpty?_tent.EmptyFrame:_tent.FrameFor((int)item.Rarity),Color.white,
                    item.IsEmpty?null:ItemSprite(i,false),item.IsEmpty?"":item.ItemLevel.ToString(),
                    !_selectedWorn&&i==_selection&&!item.IsEmpty,!item.IsEmpty&&!visible,!item.IsEmpty&&camp.Bag.IsKept(i),
                    item.IsEmpty?-1:(int)item.Rarity,_tent.ColourFor((int)item.Rarity));
            }
            for(int i=0;i<_tent.Worn.Length&&i<4;i++)
            {
                var cell=_tent.Worn[i];if(cell==null)continue;
                var item=camp.Worn.Worn((EquipSlot)i);
                cell.Show(null,item.IsEmpty?_tent.EmptyRing:_tent.ColourFor((int)item.Rarity),
                    item.IsEmpty?null:ItemSprite(i,true),item.IsEmpty?"":item.ItemLevel.ToString(),
                    _selectedWorn&&i==_selection&&!item.IsEmpty,false,false,
                    item.IsEmpty?-1:(int)item.Rarity,_tent.ColourFor((int)item.Rarity));
            }
            if(_tent.BagCount!=null)_tent.BagCount.text=count+" / 48";
            for(int f=0;f<_tent.Filters.Length;f++)
                if(_tent.Filters[f]!=null&&_tent.Filters[f].image!=null)
                    // Выбранная вкладка прозрачна — под ней видна коралловая подложка.
                    _tent.Filters[f].image.color=f==_filter+1?new Color(1f,1f,1f,0f):Color.white;

            var stats=_driver.Session.CampSim.Entities.Stats[0];
            StatType[] shown={StatType.Damage,StatType.Armor,StatType.MaxHealth,StatType.MaxLavidium};
            for(int i=0;i<_tent.StatValues.Length&&i<shown.Length;i++)
                _tent.ShowStat(i,Mathf.Round(stats.Get(shown[i]).ToFloat()));
            RefreshTooltip();
        }

        /// <summary>Карточка у ячейки под мышью, а без мыши — у выбранной; пустая ячейка карточку прячет.</summary>
        void RefreshTooltip()
        {
            var camp=_driver.Session.Camp;
            if(_hoverIndex<0){_tent.ShowTooltip(false);return;}
            int index=_hoverIndex;
            bool worn=_hoverWorn;
            if(worn&&index>=_tent.Worn.Length){_tent.ShowTooltip(false);return;}
            var item=worn?camp.Worn.Worn((EquipSlot)index):camp.Bag.At(index);
            if(item.IsEmpty){_tent.ShowTooltip(false);return;}
            int kind=Category(item);
            int rarity=(int)item.Rarity;
            if(_tent.ItemTitle!=null)_tent.ItemTitle.text=ItemName(item.BaseId);
            if(_tent.ItemRarity!=null){_tent.ItemRarity.text=_tent.NameFor(rarity);_tent.ItemRarity.color=_tent.ColourFor(rarity);}
            if(_tent.ItemKind!=null)_tent.ItemKind.text=(kind>=0?Names[kind]:"Предмет")+"  ·  ур. "+item.ItemLevel;
            if(_tent.ItemFrame!=null)_tent.ItemFrame.sprite=_tent.FrameFor(rarity);
            if(_tent.TooltipBand!=null)_tent.TooltipBand.color=_tent.ColourFor(rarity);
            if(_tent.ItemArt!=null){_tent.ItemArt.sprite=ItemSprite(index,worn);_tent.ItemArt.enabled=_tent.ItemArt.sprite!=null;}
            if(_tent.ItemStats!=null)
            {
                var stats=_driver.Session.CampSim.Entities.Stats[0];
                string compare=worn?"":CompareStats(item,stats,"#2E8B3E","#C2452F","#1C3A5E",true).Replace("\n\n","\n").Trim();
                _tent.ItemStats.text=worn?"<color=#5A7390>Надето на Пелаге</color>"
                    :compare.Length==0?"<color=#5A7390>Без изменений</color>":"<color=#5A7390>Сравнение:</color>\n"+compare;
            }
            if(_tent.ItemStats!=null&&_tent.Tooltip!=null)
            {
                _tent.ItemStats.ForceMeshUpdate();
                var statsRect=_tent.ItemStats.rectTransform;
                float statsHeight=_tent.ItemStats.preferredHeight;
                statsRect.sizeDelta=new Vector2(statsRect.sizeDelta.x,statsHeight);
                _tent.Tooltip.sizeDelta=new Vector2(_tent.Tooltip.sizeDelta.x,Mathf.Max(200f,-statsRect.anchoredPosition.y+statsHeight+20f));
            }
            var cell=worn?_tent.Worn[index]:_tentBag[index];
            if(cell!=null)_tent.PlaceTooltip((RectTransform)cell.transform);
            _tent.ShowTooltip(true);
        }

        /// <summary>У кнопок нет смены спрайта: недоступная просто притухает.</summary>
        static void SetInteractable(UnityEngine.UI.Button button,bool on)
        {
            if(button==null)return;
            button.interactable=on;
            var group=button.GetComponent<CanvasGroup>();
            if(group==null)group=button.gameObject.AddComponent<CanvasGroup>();
            group.alpha=on?1f:.45f;
        }

        void SetFeedback(string text)
        {
            if(_tent!=null){if(_tent.Feedback!=null)_tent.Feedback.text=text;}
            else if(_feedback!=null)_feedback.text=text;
        }

        void EnsureEventSystem()
        {
            if(EventSystem.current!=null)return;
            var events=new GameObject("Camp EventSystem",typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            events.AddComponent<InputSystemUIInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
            events.transform.SetParent(transform);
        }

        void LoadIcons()
        {
            if(_icons[0]!=null)return;
            var atlas=Resources.Load<Texture2D>("UI/Inventory/ItemIcons");
            if(atlas!=null)for(int i=0;i<6;i++)
                _icons[i]=Sprite.Create(atlas,new Rect((i%3)*atlas.width/3f,(1-i/3)*atlas.height/2f,atlas.width/3f,atlas.height/2f),new Vector2(.5f,.5f),100);
        }

        /// <summary>Строки «стат: было → станет» для выбранной вещи; цвета — под фон доски.</summary>
        string CompareStats(ItemInstance chosen,StatSheet stats,string better,string worse,string same,bool onlyChanges=false)
        {
            var camp=_driver.Session.Camp;
            if(!ItemGenerator.Generate(in chosen,camp.Items,_roll))return "";
            string text="";
            {
                var sheet=new StatSheet();
                for(int s=0;s<(int)StatType.Count;s++)sheet.SetBase((StatType)s,stats.GetBase((StatType)s));
                var previewEquipment=new Equipment(camp.Items);previewEquipment.Bind(sheet);
                for(int i=0;i<(int)EquipSlot.Count;i++)
                {var item=camp.Worn.Worn((EquipSlot)i);if(!item.IsEmpty)previewEquipment.Equip(item,out _);}
                previewEquipment.Equip(chosen,out _);
                var displayStats=new[]{StatType.Damage,StatType.AttackSpeed,StatType.CritChance,StatType.Armor,StatType.MaxHealth,StatType.MoveSpeed,StatType.FireResist};
                for(int s=0;s<displayStats.Length;s++)
                {
                    var stat=displayStats[s];var v=sheet.Get(stat);var previous=stats.Get(stat);
                    if(v==Fix64.Zero&&previous==Fix64.Zero)continue;
                    var delta=v-previous;if(onlyChanges&&delta==Fix64.Zero)continue;string color=delta>Fix64.Zero?better:delta<Fix64.Zero?worse:same;
                    text+=StatLabel(stat)+"   "+FormatStat(stat,previous)+(delta!=Fix64.Zero?" → "+FormatStat(stat,v)+"  <color="+color+">"+(delta>Fix64.Zero?"+":"")+FormatStat(stat,delta)+"</color>":"")+"\n\n";
                }
            }
            return text;
        }

        RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);go.GetComponent<Image>().color=color;return r;
        }
        Text Label(Transform parent,string text,float x,float y,float w,float h,int size)
        { var go=new GameObject("Text",typeof(RectTransform),typeof(Text));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);var t=go.GetComponent<Text>();t.font=_font;t.fontSize=size;t.color=new Color(.96f,.89f,.73f);t.text=text;t.raycastTarget=false;return t; }
        UnityEngine.UI.Button Button(Transform parent,string text,float x,float y,float w,float h,UnityEngine.Events.UnityAction action)
        {
            var r=Slot(parent,text,x,y,w,h,false);var image=r.GetComponent<Image>();image.color=new Color(.30f,.10f,.06f,.94f);
            var button=r.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
            var colors=button.colors;colors.highlightedColor=new Color(1.5f,1.35f,1.1f);colors.pressedColor=new Color(.75f,.65f,.5f);colors.disabledColor=new Color(.33f,.33f,.3f,.65f);button.colors=colors;
            button.onClick.AddListener(action);Title(r,text,7,1,w-14,h-2,27,TextAnchor.MiddleCenter);return button;
        }
        void OnDestroy()
        {
            if(_root!=null)Destroy(_root);if(_portraitStage!=null)Destroy(_portraitStage);
            if(_portraitTexture!=null){_portraitTexture.Release();Destroy(_portraitTexture);}
            foreach(var material in _portraitMaterials)Destroy(material);
            foreach(var icon in _icons)if(icon!=null)Destroy(icon);
            if(_heroSprite!=null)Destroy(_heroSprite);
            foreach(var icon in _raritySprites)if(icon!=null)Destroy(icon);
        }
        static string FormatStat(StatType stat,Fix64 value)=>stat==StatType.CritChance||stat==StatType.FireResist||stat==StatType.AbilitySpeed||stat==StatType.CooldownRecovery
            ?(value*Fix64.FromInt(100)).ToFloat().ToString("0.#")+"%":value.ToFloat().ToString("0.#");
        static string StatLabel(StatType stat)
        { switch(stat){case StatType.AbilitySpeed:return "Исполнение";case StatType.CooldownRecovery:return "Восстановление";case StatType.LavidiumRegen:return "Лавидий/с";case StatType.MaxLavidium:return "Лавидий";case StatType.Damage:return "Урон";case StatType.Armor:return "Броня";case StatType.MaxHealth:return "Здоровье";case StatType.FireResist:return "Сопр. огню";case StatType.AttackSpeed:return "Скор. атаки";case StatType.MoveSpeed:return "Скорость";case StatType.CritChance:return "Шанс крита";default:return "Сила крита";} }
    }
    public sealed class CampInventoryCell : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public CampInventoryView Owner; public int Index; public bool Worn;public bool Selectable=true;
        GameObject _ghost;
        public void OnPointerClick(PointerEventData e)
        {
            if(e.button==PointerEventData.InputButton.Right){if(!Worn&&Selectable&&Owner.HasItem(Index,false))Owner.ToggleKeep(Index);return;}
            if(Selectable&&e.button==PointerEventData.InputButton.Left)Owner.Select(Index,Worn,e.clickCount==2);
        }
        public void OnBeginDrag(PointerEventData e)
        {
            if(!Selectable||!Owner.HasItem(Index,Worn)||e.button!=PointerEventData.InputButton.Left)return;
            Owner.Select(Index,Worn,false);
            _ghost=new GameObject("Dragged item",typeof(RectTransform),typeof(Image),typeof(CanvasGroup));
            _ghost.transform.SetParent(Owner.CanvasRoot,false);_ghost.GetComponent<RectTransform>().sizeDelta=new Vector2(78,78);
            var image=_ghost.GetComponent<Image>();image.sprite=Owner.ItemSprite(Index,Worn);image.preserveAspect=true;image.raycastTarget=false;
            _ghost.GetComponent<CanvasGroup>().blocksRaycasts=false;_ghost.transform.position=e.position;
        }
        public void OnDrag(PointerEventData e){if(_ghost!=null)_ghost.transform.position=e.position;}
        public void OnEndDrag(PointerEventData e){if(_ghost!=null)Destroy(_ghost);Owner.Refresh();}
        void OnDisable(){if(_ghost!=null)Destroy(_ghost);}
        public void OnDrop(PointerEventData e){var from=e.pointerDrag!=null?e.pointerDrag.GetComponent<CampInventoryCell>():null;if(from!=null&&from.Selectable&&from.Owner==Owner)Owner.Drop(from,this);}
        // У ячеек новой палатки старой рамки нет: без проверки наведение роняло игру.
        public void OnPointerEnter(PointerEventData e){if(!Selectable)return;var frame=GetComponentInChildren<InventoryFrame>();if(frame!=null)frame.SetHover(true);Owner.Hover(Index,Worn,true);}
        public void OnPointerExit(PointerEventData e){var frame=GetComponentInChildren<InventoryFrame>();if(frame!=null)frame.SetHover(false);Owner.Hover(Index,Worn,false);}
    }
    public sealed class InventoryFrame : MaskableGraphic
    {
        public Color Border=new Color(.52f,.40f,.23f);public float Cut=3,Thickness=1;bool _selected,_hover;
        public void SetSelected(bool value){_selected=value;SetVerticesDirty();}
        public void SetHover(bool value){_hover=value;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Rect r=rectTransform.rect;float c=Cut;
            var points=new Vector2[]{new Vector2(r.xMin+c,r.yMin),new Vector2(r.xMax-c,r.yMin),new Vector2(r.xMax,r.yMin+c),new Vector2(r.xMax,r.yMax-c),new Vector2(r.xMax-c,r.yMax),new Vector2(r.xMin+c,r.yMax),new Vector2(r.xMin,r.yMax-c),new Vector2(r.xMin,r.yMin+c)};
            Color edge=_selected?new Color(1,.76f,.3f):_hover?new Color(.85f,.7f,.4f):Border;
            float thickness=_selected?Thickness+1:Thickness;
            for(int i=0;i<8;i++)
            {
                Vector2 point=points[i],inner=point;inner.x=Mathf.Clamp(inner.x,r.xMin+thickness,r.xMax-thickness);inner.y=Mathf.Clamp(inner.y,r.yMin+thickness,r.yMax-thickness);
                vh.AddVert(point,edge,Vector2.zero);vh.AddVert(inner,edge*.65f,Vector2.zero);
            }
            for(int i=0;i<8;i++){int n=(i+1)%8;vh.AddTriangle(i*2,n*2,i*2+1);vh.AddTriangle(n*2,n*2+1,i*2+1);}
        }
    }
}
