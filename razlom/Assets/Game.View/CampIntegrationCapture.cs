using System.Collections;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Включается только явным флагом отдельного capture player; живую сцену автора не затрагивает.
    public sealed class CampIntegrationCapture : MonoBehaviour
    {
        static CampIntegrationCapture _instance;
        public static bool IsRunning => _instance != null;
        public static Vector2 HoverPointer => _instance != null ? _instance._hoverPoint : new Vector2(-10,-10);
        string _output;
        TickDriver _driver;
        int _attack = -1;
        int _skillQueued = -1;
        readonly int[] _skillHits = new int[4];
        bool _move;
        Vector3 _movePoint;
        Vector2 _hoverPoint = new Vector2(-10,-10);
        public void Initialize(string output) { _output = Path.GetFullPath(output); Directory.CreateDirectory(_output); _instance = this; }
        public static void CaptureInput()
        {
            var instance = _instance;
            if (instance == null) return;
            if (instance._move)
            {
                instance._driver.CaptureAim(Camera.main.WorldToScreenPoint(instance._movePoint), true, false, false, false);
                return;
            }
            if (instance._attack < 0 && instance._skillQueued < 0) return;
            var dummy = CampTrainingView.Find(instance._attack >= 0 ? instance._attack : 1);
            instance._driver.CaptureAim(Camera.main.WorldToScreenPoint(dummy.VisualBounds.center),
                moveHeld: false, movePressed: false, attackHeld: instance._attack >= 0, attackPressed: false);
        }
        public static void PrepareInput(ref InputFrame input)
        {
            var instance = _instance;
            if (instance == null || instance._skillQueued < 0) return;
            // Кнопка теста проходит обычный тик способности; цель берётся из настоящего наведения.
            input.AbilityMask = (byte)(1 << instance._skillQueued);
            input.AbilityTarget = instance._driver.HoveredEntity;
            input.AttackTarget = -1;
            input.Flags = 0;
            instance._skillQueued = -1;
        }
        void LateUpdate()
        {
            if (_driver == null) return;
            foreach (var e in _driver.FrameEvents)
                if (e.Type == SimEventType.Damage && e.DamageOrigin == DamageOrigin.Ability
                    && e.Target > 0 && e.ActionVariant >= 0 && e.ActionVariant < 4) _skillHits[e.ActionVariant]++;
        }
        IEnumerator Start()
        {
            yield return new WaitForSeconds(1);
            _driver = FindAnyObjectByType<TickDriver>();
            var camp = CampPlayerView.Instance;
            var entrance = FindAnyObjectByType<CampRiftEntrance>();
            Check(camp.WalkMap != null, "camp navigation initialized");
            var tentBounds = camp.Tent.GetComponentInChildren<Renderer>().bounds;
            Check(!camp.WalkMap.Contains(CampTrainingView.Flat(tentBounds.center)), "authored tent blocks movement");
            Check(_driver.Session.Training?.Count == 2, "two authored targets");
            var dummy = CampTrainingView.Find(1);
            Check(camp.WalkMap.Contains(CampTrainingView.Flat(dummy.TargetPosition)), "target mesh does not occlude ability contact");
            Check(!CampTrainingView.IsNear(dummy, dummy.TargetPosition + Vector3.right * 5), "training hidden five metres away");
            Check(CampTrainingView.IsNear(dummy, dummy.TargetPosition + Vector3.right * 2), "training visible beside dummy");
            Vector3 authoredPosition = dummy.transform.position;
            Place(dummy.TargetPosition + Vector3.right * 2.8f);
            yield return new WaitForSeconds(.6f);
            _driver.CaptureAim(Camera.main.WorldToScreenPoint(dummy.VisualBounds.center), false, false, false);
            Check(_driver.HoveredEntity == 1, "authored dummy hover");
            _attack = 1;
            yield return new WaitForSeconds(3.5f);
            _attack = -1;
            Debug.Log($"[camp-integration-input] player={camp.Position} dummy={dummy.TargetPosition} hover={_driver.HoveredEntity} target={_driver.Sim.AttackTarget} damage={_driver.Session.Training.DamageTotal}");
            Check(_driver.Session.Training.DamageTotal > 0, "mouse aiming, approach, damage and DPS");
            Check(dummy.transform.position == authoredPosition, "authored target position stays fixed");
            Check(FindAnyObjectByType<ArenaView>().TryGetEntityView(1,out var view) && view == dummy.transform, "authored mesh binding");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output,"training.png"));
            Place(dummy.TargetPosition + Vector3.right * 1.6f);
            _driver.Sim.Entities.Facing[0] = new FixVec2(-Fix64.One, Fix64.Zero);
            yield return new WaitForSeconds(.2f);
            _skillQueued = 1;
            yield return new WaitForSeconds(1.3f);
            Check(_skillHits[1] > 0, "skill 2 cleave damages authored dummy");
            Place(dummy.TargetPosition + Vector3.right * 2.3f);
            yield return new WaitForSeconds(.2f);
            _skillQueued = 3;
            yield return new WaitForSeconds(2);
            Check(_skillHits[3] > 0, "skill 4 squall damages authored dummy");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output,"skills.png"));
            Place(entrance.transform.TransformPoint(new Vector3(0,0,-.36f)));
            yield return new WaitForSeconds(.7f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output,"entrance.png"));
            Check(!entrance.UpdateOutlineHover(_hoverPoint), "arch outline hidden without hover");
            _hoverPoint = FindArchHover(entrance);
            Check(entrance.UpdateOutlineHover(_hoverPoint), "arch outline appears on mesh hover");
            yield return new WaitForSeconds(.15f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output,"hover.png"));
            _hoverPoint = new Vector2(-10,-10);
            Check(!entrance.UpdateOutlineHover(_hoverPoint), "arch outline clears when pointer leaves");
            Vector3 center = entrance.transform.TransformPoint(new Vector3(entrance.TriggerCenter.x,0,entrance.TriggerCenter.z));
            Check(camp.RouteTo(center), "route into arch exists");
            float deadline = Time.time + 6;
            while (!entrance.IsOpen && Time.time < deadline) yield return null;
            Check(entrance.IsOpen, "walking into arch opens confirmation");
            Check(_driver.Session.Mode == GameMode.Camp, "confirmation does not start run");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output,"confirmation.png"));
            entrance.Respond(false);
            yield return new WaitForSeconds(.4f);
            Check(!entrance.IsOpen && _driver.Session.Mode == GameMode.Camp, "cancel stays in camp without repeating");
            _movePoint = entrance.transform.TransformPoint(new Vector3(0,0,.7f));
            Check(!camp.WalkMap.CanTravel(CampTrainingView.Flat(camp.Position),CampTrainingView.Flat(_movePoint)), "gate blocks route beyond arch");
            _move = true;
            yield return new WaitForSeconds(3);
            _move = false;
            Check(entrance.transform.InverseTransformPoint(camp.Position).z < .17f, "held movement cannot pass gate after refusal");
            Check(!entrance.IsOpen && _driver.Session.Mode == GameMode.Camp,"refusal stays closed at the barrier");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output,"blocked.png"));
            Place(entrance.transform.TransformPoint(new Vector3(0,0,-.36f)));
            yield return new WaitForSeconds(.2f);
            camp.RouteTo(center);
            deadline = Time.time + 6;
            while (!entrance.IsOpen && Time.time < deadline) yield return null;
            Check(entrance.IsOpen,"reentry asks again");
            entrance.Respond(true);
            Check(_driver.Session.Mode == GameMode.Rift, "confirm starts run");
            yield return new WaitForSeconds(.4f);
            _driver.Session.ReturnToCamp();
            yield return new WaitForSeconds(.5f);
            Check(!entrance.IsOpen && _driver.Session.Training.Count == 2 && _driver.Sim.Entities.Count == 3, "return preserves targets and does not reopen confirmation");
            Debug.Log("[camp-integration] ALL CHECKS PASSED");
            File.WriteAllText(Path.Combine(_output,"result.txt"),$"PASS: navigation, obstacle, mouse attack, proximity, cleave={_skillHits[1]}, squall={_skillHits[3]}, hover outline, refusal barrier, entrance and return");
        }
        static Vector2 FindArchHover(CampRiftEntrance entrance)
        {
            var bounds = entrance.GetComponentInChildren<MeshRenderer>().bounds;
            Vector2 min = new Vector2(float.MaxValue,float.MaxValue), max = new Vector2(float.MinValue,float.MinValue);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector2 screen = Camera.main.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                min = Vector2.Min(min,screen); max = Vector2.Max(max,screen);
            }
            for (int y = 11; y > 0; y--) for (int x = 1; x < 12; x++)
            {
                var pointer = new Vector2(Mathf.Lerp(min.x,max.x,x/12f),Mathf.Lerp(min.y,max.y,y/12f));
                if (entrance.UpdateOutlineHover(pointer)) return pointer;
            }
            throw new System.InvalidOperationException("Не найдена видимая часть арки для проверки наведения.");
        }
        void Place(Vector3 at)
        {
            _driver.ClearCapturedInput();
            _driver.Session.CampSim.StopPlayerMovement();
            _driver.Session.CampSim.Entities.Position[0] = CampTrainingView.Flat(at);
            // Орбитальная съёмка отключает follow; при переносе между площадкой
            // и аркой переносим и её центр, сохраняя выбранный ракурс.
            var follow = FindAnyObjectByType<CameraFollow>();
            if (Camera.main != null && follow != null && !follow.enabled)
                Camera.main.transform.position = at + Vector3.up * .8f - Camera.main.transform.forward * 20f;
        }
        static void Check(bool success, string message)
        {
            if (!success) throw new System.InvalidOperationException("[camp-integration] FAIL: " + message);
            Debug.Log("[camp-integration] PASS: " + message);
        }
        void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
