using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Game.View
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class CampServicesView : MonoBehaviour
    {
        public static CampServicesView Instance { get; private set; }
        public static int ConsumedFrame { get; private set; }=-1;
        public static bool PointerGesture { get; private set; }
        public bool IsOpen => _panel!=null && _panel.activeSelf;
        public CampServiceNpc Current { get; private set; }
        public CampServiceNpc Pending { get; private set; }
        CampServiceNpc[] _npcs;
        CampPlayerView _player;TickDriver _driver;
        GameObject _canvas,_panel,_serviceCard;Text _hint,_title,_description;
        Button _close;GameObject _previousSelection;float _errorUntil;
        int _openedFrame;
        public void Initialize(CampPlayerView player,TickDriver driver)
        {
            Instance=this;_player=player;_driver=driver;_npcs=FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include);Build();
        }
        void Update()
        {
            if(_player==null)return;
            if(!_player.Active || _driver.GameplayPaused || _player.InventoryOpen || _player.EntranceOpen){Close();Pending=null;HideHints();return;}
            bool use=false,cancel=false,left=false,right=false,moving=false;Vector2 pointer=Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;var mouse=Mouse.current;var pad=Gamepad.current;
            use=(keyboard!=null && keyboard.eKey.wasPressedThisFrame) || GameKeyBindings.Pressed(GameAction.Interact);
            cancel=keyboard!=null && keyboard.escapeKey.wasPressedThisFrame;
            if(pad!=null){use|=pad.buttonSouth.wasPressedThisFrame;cancel|=pad.buttonEast.wasPressedThisFrame;moving=pad.leftStick.ReadValue().sqrMagnitude>.12f;}
            if(mouse!=null){pointer=mouse.position.ReadValue();left=mouse.leftButton.wasPressedThisFrame;right=mouse.rightButton.wasPressedThisFrame;if(!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)PointerGesture=false;}
            if(GameUserSettings.WasdMovement && keyboard!=null)moving|=keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed;
#else
            use=Input.GetKeyDown(KeyCode.E) || GameKeyBindings.Pressed(GameAction.Interact);cancel=Input.GetKeyDown(KeyCode.Escape);pointer=Input.mousePosition;left=Input.GetMouseButtonDown(0);right=Input.GetMouseButtonDown(1);
            moving=GameUserSettings.WasdMovement && (Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.D));
            if(!Input.GetMouseButton(0) && !Input.GetMouseButton(1))PointerGesture=false;
#endif
            if(CampServicesProbe.IsRunning && !CampServicesProbe.AllowInteractionInput){use=cancel=left=right=moving=false;}
            if(IsOpen){HideHints();if(Time.frameCount>_openedFrame && !_close.interactable){_close.interactable=true;if(_smith!=null && _smith.activeSelf){_smithInput.interactable=true;_smith.GetComponentInChildren<Button>().Select();}else if(_trader!=null && _trader.activeSelf){_traderInput.interactable=true;_trader.GetComponentInChildren<Button>().Select();}else if(_alchemist!=null && _alchemist.activeSelf){_alchemistInput.interactable=true;_alchemist.GetComponentInChildren<Button>().Select();}else _close.Select();}if(cancel)Close();return;}
            if(ConsumedFrame==Time.frameCount)return;
            bool overUi=CampInventoryView.PointerOverUI() || _driver.PointerOverHud(pointer);
            CampServiceNpc nearest=null,hover=null;float distance=3f,hitDistance=float.MaxValue;
            var camera=Camera.main;Ray ray=camera!=null?camera.ScreenPointToRay(pointer):default;
            foreach(var npc in _npcs)
            {
                if(npc==null || !npc.isActiveAndEnabled)continue;
                float d=npc.Distance(_player.InteractionPosition);
                if(npc.Near(_player.InteractionPosition) && d<distance){nearest=npc;distance=d;}
                if(!overUi && camera!=null && npc.Shape.IntersectRay(ray,out float t) && t<hitDistance){hover=npc;hitDistance=t;}
            }
            var focus=hover!=null?hover:nearest;
            foreach(var npc in _npcs)if(npc!=null)npc.Highlight(npc==hover);
            ShowHint(focus,camera);
            if(right && hover!=null){PointerGesture=true;Begin(hover);return;}
            var interaction=hover!=null && hover.Near(_player.InteractionPosition)?hover:nearest;
            if(use && interaction!=null){Begin(interaction);return;}
            if(Pending!=null && (moving || left || right || cancel)){CancelApproach();if(cancel)Consume();}
            if(Pending!=null && Pending.Near(_player.InteractionPosition))Open(Pending);
        }
        void ShowHint(CampServiceNpc focus,Camera camera)
        {
            if(focus==null || camera==null){_hint.text="";return;}
            var bounds=focus.Shape;
            var screen=camera.WorldToScreenPoint(new Vector3(bounds.center.x,bounds.max.y+.3f,bounds.center.z));
            if(screen.z<=0){_hint.text="";return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform,screen,null,out var point);
            _hint.rectTransform.anchoredPosition=point;
            if(Time.unscaledTime<_errorUntil)return;
            string key=focus.Near(_player.InteractionPosition)?(focus.Kind==CampServiceKind.Tent?"open.hint":"talk.hint"):"approach.hint";
            _hint.text=focus.Title+"\n<size=15>"+CampServiceText.Get(key)+"</size>";
        }
        void HideHints(){if(_hint!=null)_hint.text="";if(_npcs!=null)foreach(var npc in _npcs)if(npc!=null)npc.Highlight(false);}
        public bool Begin(CampServiceNpc npc)
        {
            if(npc==null || _player==null || !_player.Active || _driver.GameplayPaused || IsOpen)return false;
            Consume();_player.StopForService();Pending=npc;
            if(npc.Near(_player.InteractionPosition)){Open(npc);return true;}
            var approach=npc.Kind==CampServiceKind.Tent?npc.Shape.ClosestPoint(_player.InteractionPosition):npc.Approach;
            // Достижимую клетку выбираем у подхода, не внутри геометрии персонажа или верстака.
            for(float radius=0;radius<=1.5f;radius+=.25f)
                for(int i=0;i<(radius==0?1:16);i++)
                {
                    var p=approach+new Vector3(Mathf.Cos(i*Mathf.PI/8),0,Mathf.Sin(i*Mathf.PI/8))*radius;
                    if(npc.Near(p) && _player.WalkMap.Contains(CampTrainingView.Flat(p)) && _player.RouteTo(p))return true;
                }
            Pending=null;_errorUntil=Time.unscaledTime+2;_hint.text=CampServiceText.Get("unreachable");return false;
        }
        public void CancelPending(){Pending=null;}
        public void CancelApproach(){Pending=null;_driver.ClearCapturedInput();_player.StopForService();}
        public void Open(CampServiceNpc npc)
        {
            if(npc==null || !npc.Near(_player.InteractionPosition))return;
            _player.StopForService();Pending=null;Current=npc;Consume();
            if(npc.Kind==CampServiceKind.Tent){Current=null;HideHints();_player.OpenTent();return;}
            ShowSmith(npc.Kind==CampServiceKind.Smith);ShowTrader(npc.Kind==CampServiceKind.Trader);ShowAlchemist(npc.Kind==CampServiceKind.Alchemist);
            _title.text=npc.Title;_description.text=CampServiceText.Get("service."+npc.Kind.ToString().ToLowerInvariant());
            _previousSelection=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            // Подтверждение открытия с геймпада не должно тем же нажатием отправить Submit кнопке закрытия.
            _openedFrame=Time.frameCount;_close.interactable=false;_panel.SetActive(true);HideHints();
        }
        void Consume(){ConsumedFrame=Time.frameCount;_driver.ClearCapturedInput();}
        public void Close()
        {
            if(!IsOpen)return;_panel.SetActive(false);Current=null;Pending=null;Consume();
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(_previousSelection);
        }
        void Build()
        {
            if(EventSystem.current==null)
            {
                var events=new GameObject("Camp service events",typeof(EventSystem));events.transform.SetParent(transform);
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
            _canvas=new GameObject("Camp conversations",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));_canvas.transform.SetParent(transform,false);
            _canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;_canvas.GetComponent<Canvas>().sortingOrder=105;
            var scaler=_canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            _hint=Label(_canvas.transform,"",Vector2.zero,new Vector2(350,62),22);_hint.color=new Color(1,.95f,.83f);
            var shadow=_hint.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(.1f,.13f,.1f,.7f);shadow.effectDistance=new Vector2(0,-1);
            _panel=new GameObject("Conversation",typeof(RectTransform),typeof(Image));_panel.transform.SetParent(_canvas.transform,false);
            var shade=_panel.GetComponent<RectTransform>();shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;_panel.GetComponent<Image>().color=new Color(.04f,.055f,.045f,.25f);
            var board=new GameObject("Card",typeof(RectTransform),typeof(Image));_serviceCard=board;board.transform.SetParent(_panel.transform,false);var rect=board.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(570,300);board.GetComponent<Image>().color=new Color(.91f,.87f,.76f,.98f);
            _title=Label(board.transform,"",new Vector2(0,88),new Vector2(500,50),30);
            _description=Label(board.transform,"",new Vector2(0,25),new Vector2(510,60),21);
            var button=new GameObject("Close",typeof(RectTransform),typeof(Image),typeof(Button));button.transform.SetParent(board.transform,false);var br=button.GetComponent<RectTransform>();br.anchoredPosition=new Vector2(0,-63);br.sizeDelta=new Vector2(390,48);button.GetComponent<Image>().color=new Color(.31f,.38f,.29f);
            _close=button.GetComponent<Button>();_close.onClick.AddListener(Close);var label=Label(button.transform,CampServiceText.Get("close"),Vector2.zero,new Vector2(380,44),21);label.color=new Color(.99f,.96f,.86f);
            Label(board.transform,CampServiceText.Get("close.hint"),new Vector2(0,-118),new Vector2(500,30),16);_panel.SetActive(false);
        }
        static Text Label(Transform parent,string value,Vector2 position,Vector2 size,int font)
        {
            var go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);var rt=go.GetComponent<RectTransform>();rt.anchoredPosition=position;rt.sizeDelta=size;
            var text=go.GetComponent<Text>();text.font=GameTypography.Regular;text.fontSize=font;text.text=value;text.alignment=TextAnchor.MiddleCenter;text.color=new Color(.16f,.20f,.15f);text.raycastTarget=false;return text;
        }
        void OnDisable(){Close();CancelPending();HideHints();PointerGesture=false;}
        void OnDestroy(){if(Instance==this)Instance=null;}
    }
}
