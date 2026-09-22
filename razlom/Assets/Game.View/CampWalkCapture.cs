using System.Collections;
using Game.Sim;
using UnityEngine;
namespace Game.View
{
    // Только отдельный capture player: пользовательские предметы не меняются.
    public sealed class CampWalkCapture : MonoBehaviour
    {
        IEnumerator Start()
        {
            // Для оценки тихого окружения камера и герой остаются на месте.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-ambience") >= 0) yield break;
            // Витрина сама ведёт героя: проверка клика и поход к палатке мешали съёмке способности.
            if (CaptureRig.IsVfxShowcase && !CaptureRig.LiveSkill) yield break;
            if (CaptureRig.LiveSkill)
            {
                var liveDriver=FindAnyObjectByType<TickDriver>();
                var sim=liveDriver.Sim;
                int enemy=sim.Entities.Spawn(sim.Entities.Position[0]+new FixVec2(Fix64.FromInt(2),Fix64.Zero),100000,Faction.Orvill);
                sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed,Fix64.Zero);
                sim.Entities.RefreshStats(enemy);sim.Entities.NextAttackTick[enemy]=int.MaxValue;
                yield return new WaitForSeconds(11);
                for(int slot=0;slot<4;slot++)
                {
                    if(CaptureRig.VfxShowcase==PelagVfxShowcase.AnchorLeap
                        && sim.GetAbility(slot)?.DefinitionId!=AbilityDefinition.AnchorLeapId) continue;
                    Debug.Log("[camp-abilities] slot="+slot+" ready="+sim.AbilityReadyTick(slot)+" mode="+liveDriver.Session.Mode);
                    if(sim.AbilityReadyTick(slot)<=0)Debug.LogError("[camp-abilities] Missing cast "+slot);
                }
                yield break;
            }
            yield return new WaitForSeconds(1);
            var camp = CampPlayerView.Instance;
            var driver = FindAnyObjectByType<TickDriver>();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-tent-rarities") >= 0)
            {
                yield return TentShowcase();
                yield break;
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-collision") >= 0)
            {
                var flame = FindAnyObjectByType<CampFlameProView>();
                if (flame == null) Debug.LogError("[camp-collision] Flame Pro missing: navigation regression was not exercised.");
                else
                {
                    var flameMesh = flame.GetComponent<MeshFilter>();
                    int colliders = flame.GetComponentsInChildren<Collider>(true).Length;
                    bool excluded = !CampPlayerView.UsedByNavigation(flameMesh);
                    Debug.Log($"[camp-collision] flameExcluded={excluded} flameColliders={colliders}");
                    if (!excluded || colliders != 0)
                        Debug.LogError("[camp-collision] Visual flame became a physical navigation obstacle.");
                }
                var mesh = camp.Body.GetComponentInChildren<SkinnedMeshRenderer>();
                var animator = camp.Body.GetComponentInChildren<Animator>();
                Vector3 origin = camp.Position;
                float until = Time.time + 8;
                int lastPhase = -1;
                float minimumHeight = float.MaxValue;
                while (Time.time < until)
                {
                    int phase = Mathf.FloorToInt((8 - (until - Time.time)) * 2);
                    Vector3 moveTarget = origin + Quaternion.Euler(0, phase * 70, 0) * Vector3.forward * 2;
                    driver.CaptureAim(Camera.main.WorldToScreenPoint(moveTarget),
                        moveHeld: true, movePressed: true, attackHeld: false);
                    minimumHeight = Mathf.Min(minimumHeight, mesh.bounds.size.y);
                    if (mesh.bounds.size.y < .8f)
                        Debug.LogError($"[camp-collision] Collapsed body height={mesh.bounds.size.y}");
                    if (phase != lastPhase)
                    {
                        Debug.Log($"[camp-collision] phase={phase} root={camp.Position} bounds={mesh.bounds} bone={mesh.rootBone.position} state={animator.GetCurrentAnimatorStateInfo(0).shortNameHash} scale={mesh.transform.lossyScale}");
                        lastPhase = phase;
                    }
                    yield return null;
                }
                Debug.Log($"[camp-collision] minimumBodyHeight={minimumHeight} active={camp.Active}");
                yield break;
            }
            Vector3 beforeSelfClick = camp.Position;
            Vector3 selfPointer = Camera.main.WorldToScreenPoint(camp.Position + Vector3.up);
            camp.HandleWorldPress(selfPointer);
            driver.CaptureAim(selfPointer, moveHeld: true, movePressed: true, attackHeld: false);
            if (CaptureRig.LiveSkill)
            {
                var liveDriver=FindAnyObjectByType<TickDriver>();
                var sim=liveDriver.Sim;
                int enemy=sim.Entities.Spawn(sim.Entities.Position[0]+new FixVec2(Fix64.FromInt(2),Fix64.Zero),100000,Faction.Orvill);
                sim.Entities.Stats[enemy].SetBase(StatType.MoveSpeed,Fix64.Zero);
                sim.Entities.RefreshStats(enemy);sim.Entities.NextAttackTick[enemy]=int.MaxValue;
                yield return new WaitForSeconds(11);
                for(int slot=0;slot<4;slot++)
                {
                    Debug.Log("[camp-abilities] slot="+slot+" ready="+sim.AbilityReadyTick(slot)+" mode="+liveDriver.Session.Mode);
                    if(sim.AbilityReadyTick(slot)<=0)Debug.LogError("[camp-abilities] Missing cast "+slot);
                }
                yield break;
            }
            yield return new WaitForSeconds(1);
            Debug.Log($"[camp-self-click] active={camp.Active} position={camp.Position} mode={driver.Session.Mode}");
            if (!camp.Active || Vector3.Distance(beforeSelfClick, camp.Position) > .01f)
                Debug.LogError("[camp-self-click] Self click changed player position or mode");
            foreach (int id in PrototypeContent.ItemBaseIds())
                driver.Session.Camp.Bag.Add(new ItemInstance(id,1,ItemRarity.Normal,(ulong)(uint)id));
            Debug.Log("[camp-check] approach="+camp.ApproachTent());
            yield return new WaitForSeconds(7);
            Debug.Log($"[camp-check] arrived={camp.Position} inventory={camp.InventoryOpen}");
            if (!camp.InventoryOpen) Debug.LogError("[camp-check] Failed to walk to tent and open inventory");
            var ui=FindAnyObjectByType<CampInventoryView>();
            var cells=Object.FindObjectsByType<CampInventoryCell>();
            var bagCells=new CampInventoryCell[48];var wornCells=new CampInventoryCell[5];
            foreach(var cell in cells){if(cell.Worn)wornCells[cell.Index]=cell;else bagCells[cell.Index]=cell;}
            for(int index=0;index<5;index++)
            {
                var item=driver.Session.Camp.Bag.At(index);
                int baseIndex=driver.Session.Camp.Items.IndexOfBase(item.BaseId);
                int slot=(int)Equipment.SlotOf(driver.Session.Camp.Items.GetBase(baseIndex).Category);
                // Палатка v3-tent показывает четыре слота: артефакта в ней нет намеренно.
                if(wornCells[slot]==null){Debug.Log("[camp-ui] slot "+slot+" is not shown in this layout");continue;}
                Drag(bagCells[index],wornCells[slot]);
                if(driver.Session.Camp.Worn.Worn((EquipSlot)slot).BaseId!=item.BaseId)Debug.LogError("[camp-ui] Equip failed "+slot);
                Drag(wornCells[slot],bagCells[47]);
                if(driver.Session.Camp.Bag.At(47).BaseId!=item.BaseId)Debug.LogError("[camp-ui] Unequip failed "+slot);
                Drag(bagCells[47],wornCells[slot]);
            }
            ui.Select(0,true,false);
            int pausedTick=driver.Sim.Tick;
            yield return new WaitForSeconds(.3f);
            if(driver.Sim.Tick!=pausedTick)Debug.LogError("[camp-ui] Modal leaked simulation input");
            ui.Close();yield return new WaitForSeconds(.3f);
            if(driver.Sim.Tick<=pausedTick)Debug.LogError("[camp-ui] Closing did not resume simulation");
            foreach(int id in PrototypeContent.ItemBaseIds())driver.Session.Camp.Bag.Add(new ItemInstance(id,3,ItemRarity.Normal,(ulong)(uint)id+7));
            ui.Open();ui.Select(0,false,false);
            int shown=0;foreach(var cell in wornCells)if(cell!=null)shown++;
            Debug.Log("[camp-ui] "+shown+" equipment slots dragged both ways; modal pause/resume checked");
            var tent=FindAnyObjectByType<CampTentView>();
            if(tent==null)yield break;
            yield return new WaitForSeconds(.6f);
            AuditClicks(tent,ui,cells);
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-capture-tent-rarities")>=0)
                yield return EquipShow(ui,driver);
        }

        /// <summary>
        /// Только палатка (-capture-tent-rarities): открыть сразу, без прогулки. Кадры
        /// capture.ps1 -Times 3,5.5,8: карточка редкой вещи, разбивка стата, атлас.
        /// </summary>
        IEnumerator TentShowcase()
        {
            var ui=FindAnyObjectByType<CampInventoryView>();
            if(ui==null){Debug.LogError("[tent-show] no inventory");yield break;}
            ui.Open();
            yield return new WaitForSeconds(1.2f);
            var tent=FindAnyObjectByType<CampTentView>();
            if(tent==null){Debug.LogError("[tent-show] tent prefab missing");yield break;}
            var cells=Object.FindObjectsByType<CampInventoryCell>();
            AuditClicks(tent,ui,cells);
            yield return new WaitForSeconds(1.2f);
            for(int i=0;i<48;i++)if(driver().Session.Camp.Bag.At(i).Rarity==ItemRarity.Magic){ui.Hover(i,false,true);break;}
            yield return new WaitForSeconds(2.2f);
            ui.ShowStatTooltip(1);
            yield return new WaitForSeconds(2.4f);
            ui.ShowAtlas(true);
            yield return null;
            ui.ShowAtlasTooltip(6);
            Debug.Log("[tent-show] bag, stat and atlas shown");
            static TickDriver driver()=>FindAnyObjectByType<TickDriver>();
        }

        /// <summary>
        /// Владелец 16 сентября: «не все кнопки клацаются». Для каждой кнопки и ячейки
        /// палатки луч в её центр: верхнее попадание обязано принадлежать ей.
        /// Вкладки и «Закрыть» ещё и нажимаются — проверяется реакция.
        /// </summary>
        static void AuditClicks(CampTentView tent,CampInventoryView ui,CampInventoryCell[] cells)
        {
            int pass=0,fail=0;
            foreach(var button in tent.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                if(TopHit(button.transform,out string blocker))pass++;
                else{fail++;Debug.LogError("[tent-click] FAIL button "+button.name+" covered by "+blocker);}
            foreach(var cell in cells)
                if(cell!=null&&cell.isActiveAndEnabled&&cell.Selectable)
                    if(TopHit(cell.transform,out string blocker))pass++;
                    else{fail++;Debug.LogError("[tent-click] FAIL cell "+(cell.Worn?"worn ":"bag ")+cell.Index+" covered by "+blocker);}
            var filterField=typeof(CampInventoryView).GetField("_filter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            for(int f=tent.Filters.Length-1;f>=0;f--)
            {
                if(tent.Filters[f]==null)continue;
                Click(tent.Filters[f].transform);
                int filter=(int)filterField.GetValue(ui);
                if(filter==f-1)pass++;else{fail++;Debug.LogError("[tent-click] FAIL tab "+f+" left filter at "+filter);}
            }
            var pageField=typeof(CampInventoryView).GetField("_atlasPage",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            foreach(var (tab,atlas) in new[]{(tent.AtlasTab,true),(tent.BagTab,false)})
            {
                if(tab==null){fail++;Debug.LogError("[tent-click] FAIL page tab missing");continue;}
                Click(tab.transform);
                if((bool)pageField.GetValue(ui)==atlas)pass++;else{fail++;Debug.LogError("[tent-click] FAIL tab "+tab.name+" did not switch the page");}
            }
            if(tent.Close!=null)
            {
                Click(tent.Close.transform);
                if(!ui.IsOpen)pass++;else{fail++;Debug.LogError("[tent-click] FAIL close did not close");}
                ui.Open();
            }
            else{fail++;Debug.LogError("[tent-click] FAIL close is not a button");}
            Debug.Log("[tent-click] "+pass+" PASS, "+fail+" FAIL");
        }

        static Vector2 Centre(Transform target)
        {
            var rect=(RectTransform)target;
            return rect.TransformPoint(rect.rect.center);
        }

        static bool TopHit(Transform target,out string blocker)
        {
            var events=UnityEngine.EventSystems.EventSystem.current;
            var pointer=new UnityEngine.EventSystems.PointerEventData(events){position=Centre(target)};
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(pointer,hits);
            blocker=hits.Count>0?hits[0].gameObject.name+" ("+(hits[0].gameObject.transform.parent!=null?hits[0].gameObject.transform.parent.name:"")+")":"nothing";
            return hits.Count>0&&hits[0].gameObject.transform.IsChildOf(target);
        }

        static void Click(Transform target)
        {
            var events=UnityEngine.EventSystems.EventSystem.current;
            var pointer=new UnityEngine.EventSystems.PointerEventData(events){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,position=Centre(target)};
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(pointer,hits);
            if(hits.Count==0)return;
            pointer.pointerCurrentRaycast=hits[0];
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
        }

        /// <summary>Для видео: надеть две редкие вещи, потом снять — перелёт, вспышка, досчёт статов.</summary>
        static IEnumerator EquipShow(CampInventoryView ui,TickDriver driver)
        {
            var camp=driver.Session.Camp;
            yield return new WaitForSeconds(1.2f);
            foreach(var key in new[]{"base.officer_sabre","base.scout_jacket"})
            {
                int index=-1;
                for(int i=0;i<48;i++)
                {
                    var item=camp.Bag.At(i);
                    if(!item.IsEmpty&&item.BaseId==StableId.Of(key)){index=i;break;}
                }
                if(index<0){Debug.LogError("[tent-show] no "+key+" to equip");continue;}
                ui.Hover(index,false,true);
                yield return new WaitForSeconds(.9f);
                ui.Hover(index,false,false);
                ui.Select(index,false,true);
                yield return new WaitForSeconds(1.4f);
            }
            ui.Select(1,true,true);
            yield return new WaitForSeconds(1.4f);
            ui.Hover(0,true,true);
            Debug.Log("[tent-show] equip sequence played");
        }
        static void Drag(CampInventoryCell from,CampInventoryCell to)
        {
            var events=UnityEngine.EventSystems.EventSystem.current;
            var pointer=new UnityEngine.EventSystems.PointerEventData(events){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,pointerDrag=from.gameObject,position=from.transform.position};
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(pointer,hits);
            bool hit=false;foreach(var result in hits)if(result.gameObject.GetComponentInParent<CampInventoryCell>()==from){hit=true;break;}
            if(!hit)Debug.LogError("[camp-ui] Source is not raycastable "+from.Index);
            UnityEngine.EventSystems.ExecuteEvents.Execute(from.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
            pointer.position=to.transform.position;
            UnityEngine.EventSystems.ExecuteEvents.Execute(from.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.dragHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(to.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.dropHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(from.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
        }
    }
}
