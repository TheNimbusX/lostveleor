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
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-collision") >= 0)
            {
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
                    driver.CaptureAim(Camera.main.WorldToScreenPoint(moveTarget), true, true);
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
            driver.CaptureAim(selfPointer, true, true);
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
            Debug.Log("[camp-ui] Five equipment slots dragged both ways; modal pause/resume checked");
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
