using Game.Sim;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
namespace Game.View
{
    public sealed partial class CampServicesView
    {
        CampResidentPanel _residentPanel;
        CampResident _resident;
        int _residentOpenedFrame;
        GameObject _residentPreviousSelection;
        CanvasGroup _modalHiddenShopGroup;
        bool _modalKeepShopPortrait=true;
        readonly System.Collections.Generic.List<Graphic> _modalHiddenShopGraphics=new();
        readonly System.Collections.Generic.List<GameObject> _modalHiddenShopControls=new();
        bool ResidentProgressOpen => _residentPanel!=null && _residentPanel.gameObject.activeSelf;
        bool ServiceAvailable(CampServiceKind kind)
        {
            var camp=_driver.Session.Camp;
            return kind==CampServiceKind.TravelTable?camp.HasTravelTable:kind==CampServiceKind.Trader?camp.HasResident(CampResident.Trader)
                :kind==CampServiceKind.Alchemist?camp.HasResident(CampResident.Alchemist):true;
        }
        void EnsureTravelTable()
        {
            var world=FindAnyObjectByType<SceneWorldView>();if(world?.CampRoot==null)return;
            var table=world.CampRoot.transform.Find("Travel Table");
            if(table==null)
            {
                // Функциональный контейнер ждёт модель владельца; геометрия из кода не создаётся.
                table=new GameObject("Travel Table").transform;table.SetParent(world.CampRoot.transform,false);
                var preferred=new Vector3(2,0,12.5f);
                if(_player.WalkMap.TryNearestReachable(CampTrainingView.Flat(_player.InteractionPosition),CampTrainingView.Flat(preferred),out var at))
                {preferred.x=at.X.ToFloat();preferred.z=at.Y.ToFloat();}
                preferred.y=_player.SurfaceHeight(preferred.x,preferred.z);
                table.position=preferred;table.rotation=Quaternion.identity;
                var scale=world.CampRoot.transform.lossyScale;table.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
            }
            var npc=table.GetComponent<CampServiceNpc>()??table.gameObject.AddComponent<CampServiceNpc>();
            npc.Kind=CampServiceKind.TravelTable;npc.Reach=2;npc.ApproachOffset=new Vector3(0,0,-1);
        }
        void RefreshResidentPresence()
        {
            if(_npcs==null || _driver.Session==null)return;
            foreach(var npc in _npcs)
            {
                if(npc==null || npc.Kind==CampServiceKind.Tent || npc.Kind==CampServiceKind.OathBoard)continue;
                bool visible=_player.Active && ServiceAvailable(npc.Kind);
                if(npc.gameObject.activeSelf!=visible)npc.gameObject.SetActive(visible);
            }
        }
        void BuildResidentProgress()
        {
            var prefab=Resources.Load<GameObject>("UI/Prefabs/CampResidentProgress");
            if(prefab==null || _view==null)return;
            _residentPanel=Instantiate(prefab,transform).GetComponent<CampResidentPanel>();
            // Очков лагеря больше нет (06.10): ранг открывают босс и уровень сами, кнопка «Улучшить» не нужна.
            _residentPanel.Improve.interactable=false;_residentPanel.Improve.gameObject.SetActive(false);
            _residentPanel.TurnIn.onClick.AddListener(()=>{_driver.Session.Camp.TurnInChapter(_resident);RefreshResidentProgress();RefreshResidentShop();});
            _residentPanel.Back.onClick.AddListener(CloseResidentProgress);_residentPanel.gameObject.SetActive(false);
            AddResidentButton(_view.Smith.Group,CampResident.Smith);
            AddResidentButton(_view.Trader.Group,CampResident.Trader);
            AddResidentButton(_view.Alchemist.Group,CampResident.Alchemist);
        }
        void AddResidentButton(CanvasGroup group,CampResident resident)
        {
            var prefab=Resources.Load<GameObject>("UI/Prefabs/CampResidentButton");if(prefab==null)return;
            var button=Instantiate(prefab,group.transform).GetComponent<Button>();
            button.name="Развитие лагеря";
            var rect=(RectTransform)button.transform;
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(40,-124);
            rect.sizeDelta=new Vector2(280,48);
            button.onClick.AddListener(()=>
            {
                _residentPreviousSelection=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;_resident=resident;RefreshResidentProgress();
                // Новый ранг увиден в окне: значок «Новый ранг · загляни» над жителями гаснет (CampGuideView).
                _driver.Session.Camp.AcknowledgeUnlocks(RankUnlocks);
                _residentPanel.gameObject.SetActive(true);SetShopModal(true);_residentPanel.Group.interactable=false;_residentOpenedFrame=Time.frameCount;_driver.ClearCapturedInput();
                if(TickDriver.GamepadLastUsed)ResidentFirstControl().Select();
            });
        }
        internal const CampUnlock RankUnlocks=CampUnlock.Rank1|CampUnlock.Rank2|CampUnlock.Rank3;
        Selectable ResidentFirstControl()=>_residentPanel.TurnIn.interactable && _residentPanel.TurnIn.gameObject.activeSelf?_residentPanel.TurnIn:_residentPanel.Back;
        void SetShopModalContent(bool open,bool keepPortrait=true)
        {
            if(open)
            {
                // Только временный вид: прилавок не просвечивает сквозь отдельный разговор.
                // Включённые компоненты и наши кнопки восстанавливаются без изменения старого префаба.
                if(_openGroup==null || _modalHiddenShopGroup==_openGroup)return;
                if(_modalHiddenShopGroup!=null)SetShopModalContent(false);
                _modalHiddenShopGroup=_openGroup;
                _modalKeepShopPortrait=keepPortrait;
                var keep=new System.Collections.Generic.HashSet<Graphic>();
                var portrait=_modalHiddenShopGroup==_view.Smith.Group?_view.Smith.Portrait
                    :_modalHiddenShopGroup==_view.Trader.Group?_view.Trader.Portrait:_view.Alchemist.Portrait;
                if(keepPortrait && portrait!=null)
                {
                    keep.Add(portrait);
                    // Не выключаем stencil image, если ручная версия портрета добавит родительскую маску.
                    for(var parent=portrait.transform.parent;parent!=null && parent.IsChildOf(_modalHiddenShopGroup.transform);parent=parent.parent)
                        if(parent.GetComponent<Mask>()!=null)
                        {var stencil=parent.GetComponent<Graphic>();if(stencil!=null)keep.Add(stencil);}
                }
                foreach(var graphic in _modalHiddenShopGroup.GetComponentsInChildren<Graphic>(true))
                    if(graphic.enabled && !keep.Contains(graphic)){_modalHiddenShopGraphics.Add(graphic);graphic.enabled=false;}
                foreach(var button in _modalHiddenShopGroup.GetComponentsInChildren<Button>(true))
                    if(button.gameObject.activeSelf && (button.name=="Развитие лагеря" || button.name=="Резерв и заказ"))
                    {_modalHiddenShopControls.Add(button.gameObject);button.gameObject.SetActive(false);}
                return;
            }
            foreach(var graphic in _modalHiddenShopGraphics)if(graphic!=null)graphic.enabled=true;
            foreach(var control in _modalHiddenShopControls)if(control!=null)control.SetActive(true);
            _modalHiddenShopGraphics.Clear();_modalHiddenShopControls.Clear();_modalHiddenShopGroup=null;_modalKeepShopPortrait=true;
        }
        void RefreshBehindShopModal(System.Action refresh)
        {
            if(_modalHiddenShopGroup==null){refresh();return;}
            bool keepPortrait=_modalKeepShopPortrait;
            // Refresh может включить новые icon Graphics или скрыть пустые: сохраняем уже новое состояние.
            // Между restore и повторным скрытием кадр не рисуется, ввод старого окна остаётся заблокирован.
            SetShopModalContent(false);
            try{refresh();}
            finally
            {
                if(ResidentProgressOpen || TraderProgressionOpen || CampForgeView.Instance?.IsOpen==true)
                    SetShopModalContent(true,keepPortrait);
            }
        }
        void RefreshResidentShop()
        {
            RefreshBehindShopModal(()=>{if(_resident==CampResident.Smith)RefreshSmith();else if(_resident==CampResident.Trader)RefreshTraderPanel();else RefreshAlchemy();});
        }
        void RefreshResidentProgress()
        {
            Camp camp=_driver.Session.Camp;int rank=camp.Rank(_resident),next=rank+1;
            _residentPanel.Title.text=CampServiceText.Get("npc."+(_resident==CampResident.Smith?"smith":_resident==CampResident.Trader?"trader":"alchemist"));
            // Ранг один на весь лагерь: показываем, что даст следующий у этого жителя и чем он открывается.
            _residentPanel.Upgrade.text="Ранг "+rank+" / 3\n"+(rank>=3?"Все улучшения открыты":UpgradeDescription(_resident,next)+"\n"+RankRequirementText(next));
            _residentPanel.Status.text=rank>=3?"":RankProgressText(camp,next);
            var status=camp.ChapterStatus(_resident);_residentPanel.TurnIn.interactable=status==CampChapterStatus.Ready;
            _residentPanel.TurnIn.gameObject.SetActive(status==CampChapterStatus.Active || status==CampChapterStatus.Ready);
            _residentPanel.Chapter.text=status==CampChapterStatus.Hidden?"Следующая часть главы откроется после разговора с предыдущим жителем.":status==CampChapterStatus.Completed?"Эта часть главы «Наладить жизнь» завершена.":
                "Наладить жизнь\n"+(_resident==CampResident.Smith?"Вернуться с найденной вещью и обсудить её в кузнице.\nНаграда: 1 сталь":_resident==CampResident.Trader?"Добраться до третьей арены. Поражение после этого учитывается.\nНаграда: 1 сталь и 50 золота":"Применить два разных вида зелий в настоящих походах.\nНаграда: по две бутылки выбранных открытых видов")+(status==CampChapterStatus.Ready?"\nМожно завершить разговором":"");
        }
        static string RankRequirementText(int rank)
        {
            var need=Camp.RankRequirement(rank);
            return "Нужно: босс "+(need.BossIndex+1)+" и уровень "+need.Level;
        }
        /// <summary>Где игрок сейчас относительно следующего ранга: уровень и победа над нужным боссом.</summary>
        static string RankProgressText(Camp camp,int rank)
        {
            var need=Camp.RankRequirement(rank);
            bool defeated=need.BossIndex>=0 && need.BossIndex<RunBossKeys.Count && camp.BossDefeated(RunBossKeys.At(need.BossIndex));
            return "Сейчас: уровень "+camp.Level+" · босс "+(need.BossIndex+1)+(defeated?" побеждён":" ещё не побеждён");
        }
        static string UpgradeDescription(CampResident resident,int rank)
        {
            // Ранги Эни больше не дают стартовых навыков, перенос свойства убран (06.10); сердце босса — с закалкой T1.
            if(resident==CampResident.Smith)return rank==1?"Переплавка свойства · сердце босса в вещь · дар Морской узел":rank==2?"Добавление свойства":"Второе сердце в ту же вещь";
            if(resident==CampResident.Trader)return rank==1?"Шесть товаров · дар Резервный план":rank==2?"Сохранение одного товара при обновлении":"Выбор категории одного следующего товара";
            // Ресурс способностей игроку — «концентрация», зелье «Порыв» (владелец 01.10).
            return rank==1?"Большие зелья здоровья и концентрации · Запасная фляга":rank==2?"Живица и Порыв":"Смешанный отвар и Ясный настой";
        }
        void CloseResidentProgress()
        {
            if(!ResidentProgressOpen)return;_residentPanel.gameObject.SetActive(false);SetShopModal(false);_driver.ClearCapturedInput();ConsumedFrame=Time.frameCount;
            var system=UnityEngine.EventSystems.EventSystem.current;
            if(system!=null)system.SetSelectedGameObject(_residentPreviousSelection!=null && _residentPreviousSelection.activeInHierarchy?_residentPreviousSelection:_firstSelect?.gameObject);
        }
        void TickResidentProgress()
        {
            if(!ResidentProgressOpen)return;
            if(_driver.GameplayPaused || !_player.Active){CloseResidentProgress();return;}
            if(Time.frameCount<=_residentOpenedFrame)return;_residentPanel.Group.interactable=true;
            CampUiFocus.Ensure(_residentPanel.Group,ResidentFirstControl());
#if ENABLE_INPUT_SYSTEM
            bool cancel=Keyboard.current?.escapeKey.wasPressedThisFrame==true || Gamepad.current?.buttonEast.wasPressedThisFrame==true;
#else
            bool cancel=Input.GetKeyDown(KeyCode.Escape);
#endif
            if(cancel){CloseResidentProgress();_driver.ClearCapturedInput();ConsumedFrame=Time.frameCount;}
        }
    }
}
