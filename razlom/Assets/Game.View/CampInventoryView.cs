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
        public void Open() { if (_root == null) Build(); _driver.ClearCapturedInput(); _driver.Sim?.StopPlayerMovement(); _root.SetActive(true); if(_portraitStage!=null)_portraitStage.SetActive(true); _openedFrame = Time.frameCount; Refresh(); }
        public static int ClosedFrame { get; private set; } = -1;
        public void Close() { if (IsOpen) { _root.SetActive(false); if(_portraitStage!=null)_portraitStage.SetActive(false); _driver.ClearCapturedInput(); ClosedFrame = Time.frameCount; } }
        void Update()
        {
            if (!IsOpen || _openedFrame == Time.frameCount) return;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.iKey.wasPressedThisFrame)) Close();
#else
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.I)) Close();
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
        void Build()
        {
            _font=Resources.Load<Font>("UI/Fonts/CormorantSC-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _heading=Resources.Load<Font>("UI/Fonts/CormorantSC-Bold") ?? _font;
            _root=new GameObject("Camp Inventory",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=_root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=_root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(Width,Height);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            if(EventSystem.current==null)
            {
                var events=new GameObject("Camp EventSystem",typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
                events.transform.SetParent(transform);
            }
            var shade=Box(_root.transform,"Modal shade",0,0,Width,Height,new Color(.02f,.03f,.02f,.97f));
            shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;
            _board=Box(_root.transform,"Inventory board",0,0,Width,Height,Color.white);
            _board.anchorMin=_board.anchorMax=_board.pivot=new Vector2(.5f,.5f);_board.anchoredPosition=Vector2.zero;
            _board.GetComponent<Image>().sprite=FullSprite(Resources.Load<Texture2D>("UI/Inventory/InventoryBackdrop"));
            var atlas=Resources.Load<Texture2D>("UI/Inventory/ItemIcons");
            if(atlas!=null)for(int i=0;i<6;i++)
                _icons[i]=Sprite.Create(atlas,new Rect((i%3)*atlas.width/3f,(1-i/3)*atlas.height/2f,atlas.width/3f,atlas.height/2f),new Vector2(.5f,.5f),100);
            Title(_board,"СНАРЯЖЕНИЕ",570,50,470,45,34,TextAnchor.MiddleCenter);
            Label(_board,"ЛЕСНОЙ ЛАГЕРЬ",65,58,290,35,18);
            _wallet=Label(_board,"",1210,54,335,45,23);
            Button(_board,"×",1570,38,64,64,Close);
            Title(_board,"ПЕЛАГ",198,170,380,48,40);
            Label(_board,"Снаряжение странника",200,216,370,30,20);
            CreatePortrait();
            float[] xs={98,98,526,98,526};float[] ys={302,465,310,622,523};
            for(int i=0;i<5;i++)
            {
                var frame=Slot(_board,"Equipment "+Names[i],xs[i],ys[i],102,102,true);
                _wornIcons[i]=Icon(frame,_icons[i],10,10,82,82);
                _worn[i]=Title(_board,Names[i],xs[i]-12,ys[i]+104,130,30,23,TextAnchor.MiddleCenter);
                _wornCells[i]=AddCell(frame,i,true);
            }
            var statNames=new[]{"Атака","Броня","Здоровье","Скорость"};
            for(int i=0;i<4;i++){Label(_board,statNames[i],96+i*135,807,128,27,18);_stats[i]=Title(_board,"",96+i*135,833,128,31,25);}
            _itemArt=Icon(_board,null,712,195,254,213);
            _itemTitle=Title(_board,"Выберите предмет",713,413,255,76,32);
            _itemKind=Label(_board,"",715,493,250,32,18);
            var viewport=Box(_board,"Item comparison",713,535,254,124,Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            _details=Label(viewport,"",0,0,247,280,19);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=_details.rectTransform;scroll.horizontal=false;scroll.scrollSensitivity=22;
            _equip=Button(_board,"НАДЕТЬ",710,680,257,49,EquipSelected);
            _unequip=Button(_board,"СНЯТЬ",710,739,257,44,UnequipSelected);
            Title(_board,"СУМКА",1068,172,280,47,38);
            _count=Label(_board,"",1465,182,115,30,22);_count.alignment=TextAnchor.MiddleRight;
            for(int f=-1;f<5;f++)
            {
                int filter=f;
                var tab=Button(_board,f<0?"ВСЕ":"",1069+(f+1)*88,225,84,43,()=>{_filter=filter;Refresh();});
                if(f>=0)Icon(tab.transform,_icons[f],23,3,38,38);
            }
            for(int i=0;i<48;i++)
            {
                var frame=Slot(_board,"Bag "+i,1067+(i%8)*67,282+(i/8)*75,64,72,false);
                _bagIcons[i]=Icon(frame,null,3,5,58,58);
                _bag[i]=Label(frame,"",3,52,57,17,12);_bag[i].alignment=TextAnchor.LowerRight;
                _bagCells[i]=AddCell(frame,i,false);
            }
            Label(_board,"Двойной клик — надеть  ·  Перетащите предмет",1080,751,510,30,18);
            _feedback=Label(_board,"",714,846,882,40,19);
            Label(_board,"I / Esc — закрыть",70,890,400,30,17);
        }
        void EquipSelected(){_feedback.text="";if(!_selectedWorn&&!_driver.Session.Camp.EquipFromBag(_selection))_feedback.text="Не удалось надеть предмет.";Refresh();}
        void UnequipSelected()
        {
            _feedback.text="";
            if(_driver.Session.Camp.Bag.IsFull){_feedback.text="Сумка заполнена. Освободите ячейку или обменяйте вещь перетаскиванием.";return;}
            if(_selectedWorn)_driver.Session.Camp.UnequipToBag((EquipSlot)_selection);
            else {var item=_driver.Session.Camp.Bag.At(_selection);int b=_driver.Session.Camp.Items.IndexOfBase(item.BaseId);
                if(!item.IsEmpty&&b>=0)_driver.Session.Camp.UnequipToBag(Equipment.SlotOf(_driver.Session.Camp.Items.GetBase(b).Category));}
            Refresh();
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
        void CreatePortrait()
        {
            var arena=FindAnyObjectByType<ArenaView>();if(arena==null)return;
            _portraitStage=new GameObject("Inventory portrait studio");
            _portraitStage.transform.position=new Vector3(1000,1000,1000);
            var model=arena.CreateCampPlayer();model.name="Inventory Pelag";model.transform.SetParent(_portraitStage.transform,false);
            model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.Euler(0,-12,0);
            foreach(var behavior in model.GetComponentsInChildren<MonoBehaviour>())behavior.enabled=false;
            foreach(var child in model.GetComponentsInChildren<Transform>(true))if(child.name=="Pelag_AnchorGrip_Equipped")child.gameObject.SetActive(false);
            foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var animator=model.GetComponentInChildren<Animator>();if(animator!=null){animator.updateMode=AnimatorUpdateMode.UnscaledTime;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
            var shader=Resources.Load<Shader>("Shaders/InventoryPortrait");
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i];if(source==null||shader==null)continue;
                    Texture texture=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.mainTexture;
                    var material=new Material(shader);material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",new Color(1.35f,1.35f,1.35f,1));
                    materials[i]=material;_portraitMaterials.Add(material);
                }
                renderer.sharedMaterials=materials;
            }
            var cameraObject=new GameObject("Inventory portrait camera",typeof(Camera));cameraObject.transform.SetParent(_portraitStage.transform,false);
            _portraitCamera=cameraObject.GetComponent<Camera>();_portraitCamera.orthographic=true;_portraitCamera.orthographicSize=1.02f;
            cameraObject.transform.localPosition=new Vector3(0,.91f,4);cameraObject.transform.localRotation=Quaternion.Euler(0,180,0);
            _portraitCamera.clearFlags=CameraClearFlags.SolidColor;_portraitCamera.backgroundColor=Color.clear;_portraitCamera.cullingMask=1<<31;
            _portraitCamera.nearClipPlane=.1f;_portraitCamera.farClipPlane=10;_portraitCamera.allowHDR=false;
            _portraitTexture=new RenderTexture(640,900,24,RenderTextureFormat.ARGB32);_portraitTexture.Create();_portraitCamera.targetTexture=_portraitTexture;
            var portrait=Box(_board,"Pelag portrait",202,237,326,528,Color.clear);
            var portraitImage=new GameObject("Portrait image",typeof(RectTransform),typeof(RawImage)); var portraitRect=(RectTransform)portraitImage.transform; portraitRect.SetParent(portrait,false); portraitRect.anchorMin=Vector2.zero; portraitRect.anchorMax=Vector2.one; portraitRect.offsetMin=portraitRect.offsetMax=Vector2.zero; var raw=portraitImage.GetComponent<RawImage>();raw.texture=_portraitTexture;raw.raycastTarget=false;
            // Position far outside all gameplay cameras; no scene lighting or authored camera is changed.
        }
        CampInventoryCell AddCell(RectTransform rect,int index,bool worn)
        { var cell=rect.gameObject.AddComponent<CampInventoryCell>(); cell.Owner=this;cell.Index=index;cell.Worn=worn; return cell; }
        public void Select(int index,bool worn,bool twice)
        { _selection=index;_selectedWorn=worn;if(twice){if(worn)_driver.Session.Camp.UnequipToBag((EquipSlot)index);else _driver.Session.Camp.EquipFromBag(index);}Refresh(); }
        public void Drop(CampInventoryCell from,CampInventoryCell to)
        {
            if(from.Owner!=this||to.Owner!=this||!from.Selectable||!to.Selectable||!HasItem(from.Index,from.Worn))return;
            var camp=_driver.Session.Camp;
            _feedback.text="";
            if(!from.Worn&&!to.Worn)camp.Bag.Swap(from.Index,to.Index);
            else if(from.Worn&&!to.Worn){if(!camp.UnequipToSlot((EquipSlot)from.Index,to.Index))_feedback.text="Для обмена выберите вещь того же типа или пустую ячейку.";}
            else if(!from.Worn&&to.Worn)
            {
                var item=camp.Bag.At(from.Index);int b=camp.Items.IndexOfBase(item.BaseId);
                if(!item.IsEmpty&&b>=0&&Equipment.SlotOf(camp.Items.GetBase(b).Category)==(EquipSlot)to.Index)camp.EquipFromBag(from.Index);
                else _feedback.text="Этот предмет не подходит для ячейки «"+Names[to.Index]+"».";
            }
            _selection=to.Index;_selectedWorn=to.Worn;
            Refresh();
        }
        string Describe(ItemInstance item)
        { if(item.IsEmpty)return "—";int index=_driver.Session.Camp.Items.IndexOfBase(item.BaseId);return index>=0?ItemName(item.BaseId)+"\nур. "+item.ItemLevel:"Неизвестный предмет"; }
        string ItemName(int id)
        {if(id==StableId.Of("base.rusty_sword"))return "Старая сабля";if(id==StableId.Of("base.leather_jacket"))return "Кожаная куртка";if(id==StableId.Of("base.copper_ring"))return "Медное кольцо";if(id==StableId.Of("base.woodland_talisman"))return "Лесной талисман";if(id==StableId.Of("base.memory_shard"))return "Осколок памяти";return "Предмет";}
        int Category(ItemInstance item)
        {int b=_driver.Session.Camp.Items.IndexOfBase(item.BaseId);return item.IsEmpty||b<0?-1:(int)Equipment.SlotOf(_driver.Session.Camp.Items.GetBase(b).Category);}
        public Sprite ItemSprite(int index,bool worn)
        {var item=worn?_driver.Session.Camp.Worn.Worn((EquipSlot)index):_driver.Session.Camp.Bag.At(index);int category=Category(item);return category>=0?_icons[category]:null;}
        public void Refresh()
        {
            if(_root==null)return;
            var camp=_driver.Session.Camp;int count=0;
            for(int i=0;i<48;i++)
            {
                var item=camp.Bag.At(i);if(!item.IsEmpty)count++;
                int category=Category(item);bool visible=_filter<0||category==_filter;
                _bagIcons[i].sprite=ItemSprite(i,false);_bagIcons[i].enabled=!item.IsEmpty&&visible;
                _bag[i].text=!item.IsEmpty&&visible?item.ItemLevel.ToString():"";
                _bagCells[i].Selectable=visible||item.IsEmpty;
                _bagCells[i].GetComponentInChildren<InventoryFrame>().SetSelected(!_selectedWorn&&i==_selection);
            }
            for(int i=0;i<5;i++)
            {
                _wornIcons[i].sprite=ItemSprite(i,true)??_icons[i];
                _wornIcons[i].color=camp.Worn.IsWorn((EquipSlot)i)?Color.white:new Color(.6f,.56f,.44f,.18f);
                _wornCells[i].GetComponentInChildren<InventoryFrame>().SetSelected(_selectedWorn&&i==_selection);
            }
            _count.text=count+" / 48";
            _wallet.text="● "+camp.Money(CurrencyType.Gold)+"    ◆ "+camp.Money(CurrencyType.Shards)+"    ◈ "+camp.Money(CurrencyType.Lavidium);
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
            if(!chosen.IsEmpty&&ItemGenerator.Generate(in chosen,camp.Items,_roll))
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
                    var delta=v-previous;string color=delta>Fix64.Zero?"#99D86B":delta<Fix64.Zero?"#EA8D76":"#D8C5A0";
                    _details.text+=StatLabel(stat)+"   "+FormatStat(stat,previous)+(delta!=Fix64.Zero?" → "+FormatStat(stat,v)+"  <color="+color+">"+(delta>Fix64.Zero?"+":"")+FormatStat(stat,delta)+"</color>":"")+"\n\n";
                }
            }
            _details.rectTransform.sizeDelta=new Vector2(247,Mathf.Max(124,_details.preferredHeight));
            _details.rectTransform.anchoredPosition=Vector2.zero;
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
        }
        static string FormatStat(StatType stat,Fix64 value)=>stat==StatType.CritChance||stat==StatType.FireResist?(value*Fix64.FromInt(100))+"%":value.ToString();
        static string StatLabel(StatType stat)
        { switch(stat){case StatType.Damage:return "Урон";case StatType.Armor:return "Броня";case StatType.MaxHealth:return "Здоровье";case StatType.FireResist:return "Сопр. огню";case StatType.AttackSpeed:return "Скор. атаки";case StatType.MoveSpeed:return "Скорость";case StatType.CritChance:return "Шанс крита";default:return "Сила крита";} }
    }
    public sealed class CampInventoryCell : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public CampInventoryView Owner; public int Index; public bool Worn;public bool Selectable=true;
        GameObject _ghost;
        public void OnPointerClick(PointerEventData e){if(Selectable&&e.button==PointerEventData.InputButton.Left)Owner.Select(Index,Worn,e.clickCount==2);}
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
        public void OnPointerEnter(PointerEventData e){if(Selectable)GetComponentInChildren<InventoryFrame>().SetHover(true);}
        public void OnPointerExit(PointerEventData e){GetComponentInChildren<InventoryFrame>().SetHover(false);}
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
