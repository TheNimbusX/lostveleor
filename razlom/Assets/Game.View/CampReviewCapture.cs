using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Sim;
using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    // Отдельный player проходит настоящие маршруты и открывает сервисы обычным Begin.
    // Профиль автора не читается и не сохраняется при обязательном флаге -capture-camp.
    public sealed class CampReviewCapture : MonoBehaviour
    {
        string _output;
        CampPlayerView _camp;
        TickDriver _driver;
        Transform _root;
        readonly StringBuilder _report = new StringBuilder();
        bool _passed = true;
        public bool Finished { get; private set; }
        public void Initialize(string output) { _output = Path.GetFullPath(output); Directory.CreateDirectory(_output); }

        void Check(bool value, string message)
        {
            _report.AppendLine((value ? "PASS " : "FAIL ") + message);
            if (!value) _passed = false;
            SaveProgress();
        }

        IEnumerator Start()
        {
            // Этот флаг в CampSaveStore отключает загрузку и запись настоящего сохранения.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-camp") < 0)
            { Check(false, "isolated capture profile required"); Finish(); yield break; }
            yield return new WaitForSecondsRealtime(1);
            _camp = CampPlayerView.Instance;
            _driver = FindAnyObjectByType<TickDriver>();
            var world = FindAnyObjectByType<SceneWorldView>();
            if (_camp == null || _camp.WalkMap == null || _driver == null || world?.CampRoot == null)
            { Check(false, "navigation initialized"); Finish(); yield break; }
            _root = world.CampRoot.transform;
            Check(_camp.Active && _camp.WalkMap.Contains(Flat(_camp.InteractionPosition)), "initial spawn lies on walk map");
            while (_driver.Session.Camp.AttemptCount < 3) _driver.Session.Camp.RecordRealAttemptEnded(1, 0);
            // Скрытые жители появляются через обычный lifecycle SceneWorldView.
            yield return null;
            yield return WaitForControl();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            File.WriteAllText(Path.Combine(_output, "camp-routes.txt"), CampRouteAudit.Report());
#endif

            var origin = _camp.InteractionPosition;
            yield return Shot("01-arrival");
            yield return SwapVisualAudit();
            var smith = Npc(CampServiceKind.Smith);
            var trader = Npc(CampServiceKind.Trader);
            var leo = Npc(CampServiceKind.Alchemist);
            var table = Npc(CampServiceKind.TravelTable);

            yield return CancelApproach(trader);
            yield return Service(smith, "02-smith");
            var fire = _root.Find("Campfire");
            if (fire != null) yield return Walk(fire.position + new Vector3(-1.8f, 0, -1), "03-fire");
            else Check(false, "campfire exists");
            yield return Service(trader, "04-trader");
            // Обратный путь от лавки проверяет проход, который был перекрыт тенью изгороди.
            if (fire != null) yield return Walk(fire.position + new Vector3(-1.8f, 0, -1), "05-trader-fire-return");

            var passage = FindAnyObjectByType<CampRiverPassage>();
            Check(passage != null && passage.Bridge != null, "bridge installed");
            if (passage != null && passage.Bridge != null)
            {
                Bounds bridge = passage.BridgeBounds;
                yield return Walk(new Vector3(bridge.center.x, 0, bridge.max.z + 1f), "06-near-bank");
                yield return Walk(new Vector3(bridge.center.x, 0, bridge.min.z - 1f), "07-across-bridge");
                yield return Service(leo, "08-leo");
                yield return Walk(new Vector3(bridge.center.x, 0, bridge.max.z + 1f), "09-bridge-return");
                AuditRiver(passage);
            }
            else yield return Service(leo, "08-leo");
            yield return Service(table, "10-table");
            yield return Walk(origin, "11-return");
            yield return Service(Npc(CampServiceKind.Tent), "12-inventory");
            AuditGeometry(_root);
            Finish();
        }

        static FixVec2 Flat(Vector3 p) => CampTrainingView.Flat(p);
        static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        CampServiceNpc Npc(CampServiceKind kind)
        {
            foreach (var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
                if (npc.Kind == kind) return npc;
            return null;
        }

        IEnumerator WaitForControl()
        {
            float deadline = Time.unscaledTime + 8;
            while ((_camp.InputBlocked || _driver.GameplayPaused || MainMenuView.IsOpen) && Time.unscaledTime < deadline)
                yield return null;
            Check(!_camp.InputBlocked && !_driver.GameplayPaused && !MainMenuView.IsOpen, "camp control available");
        }

        IEnumerator CancelApproach(CampServiceNpc npc)
        {
            Check(npc != null && npc.isActiveAndEnabled, "cancellation resident available");
            if (npc == null || !npc.isActiveAndEnabled) yield break;
            var services = CampServicesView.Instance;
            bool began = services.Begin(npc);
            Check(began && services.Pending == npc, "ordinary Begin starts distant approach");
            yield return null;
            services.CancelApproach();
            _driver.ClearCapturedInput();
            Vector3 stopped = _camp.InteractionPosition;
            yield return new WaitForSecondsRealtime(.3f);
            Check(services.Pending == null && !WindowOpen(npc.Kind), "cancelled approach does not open a window");
            Check(Distance(stopped, _camp.InteractionPosition) < .02f, "cancelled route stops simulation movement");
        }

        IEnumerator Service(CampServiceNpc npc, string name)
        {
            Check(npc != null && npc.isActiveAndEnabled, name + " service is active");
            if (npc == null || !npc.isActiveAndEnabled) yield break;
            yield return WaitForControl();
            var services = CampServicesView.Instance;
            bool began = services.Begin(npc);
            Check(began, name + " ordinary Begin accepted");
            if (!began) yield break;
            float deadline = Time.unscaledTime + 20;
            int offMap = 0;
            while (!WindowOpen(npc.Kind) && Time.unscaledTime < deadline)
            {
                if (!_camp.WalkMap.Contains(_driver.Session.CampSim.Entities.Position[0])) offMap++;
                yield return null;
            }
            bool opened = WindowOpen(npc.Kind);
            Check(opened, name + " walking opens the matching window");
            Check(npc.Near(_camp.InteractionPosition), name + " actual simulation stop is inside NPC.Near");
            Check(offMap == 0, name + " simulation stayed on walk map: " + offMap);
            _report.AppendLine("SERVICE " + name + " position=" + _camp.InteractionPosition.ToString("F2")
                + " resident=" + npc.transform.position.ToString("F2") + " distance=" + npc.Distance(_camp.InteractionPosition).ToString("F3"));
            yield return new WaitForSecondsRealtime(.35f);
            yield return Shot(name);
            if (npc.Kind == CampServiceKind.TravelTable) CampPreparationView.Instance?.Close();
            else if (npc.Kind == CampServiceKind.Tent) FindAnyObjectByType<CampInventoryView>()?.Close();
            else services.Close();
            if (!opened) services.CancelApproach();
            yield return new WaitForSecondsRealtime(.25f);
            yield return WaitForControl();
        }

        bool WindowOpen(CampServiceKind kind) => kind == CampServiceKind.TravelTable
            ? CampPreparationView.Instance?.IsOpen == true : kind == CampServiceKind.Tent
            ? _camp.InventoryOpen : CampServicesView.Instance?.IsOpen == true && CampServicesView.Instance.Current?.Kind == kind;

        IEnumerator Walk(Vector3 wanted, string name)
        {
            yield return WaitForControl();
            var start = Flat(_camp.InteractionPosition);
            if (!_camp.WalkMap.TryNearestReachable(start, Flat(wanted), out var resolved))
            { Check(false, name + " has no reachable target"); yield break; }
            var target = new Vector3(resolved.X.ToFloat(), _camp.GroundHeight, resolved.Y.ToFloat());
            float snap = Distance(target, wanted);
            Check(snap <= .75f, name + " target remains local: snap=" + snap.ToString("F3"));
            if (snap > .75f) yield break;
            bool alreadyThere = Distance(_camp.InteractionPosition, target) <= .42f;
            bool routed = alreadyThere || _camp.RouteTo(target);
            Check(routed, name + (alreadyThere ? " already at destination" : " real player route exists"));
            if (!routed) yield break;
            float deadline = Time.unscaledTime + 20;
            int offMap = 0, visualOffMap = 0;
            while (Distance(_camp.InteractionPosition, target) > .42f && Time.unscaledTime < deadline)
            {
                if (!_camp.WalkMap.Contains(_driver.Session.CampSim.Entities.Position[0])) offMap++;
                if (!_camp.WalkMap.Contains(Flat(_camp.Position))) visualOffMap++;
                yield return null;
            }
            float distance = Distance(_camp.InteractionPosition, target);
            Check(distance <= .42f, name + " arrived distance=" + distance.ToString("F3")
                + " position=" + _camp.InteractionPosition.ToString("F2"));
            Check(offMap == 0, name + " simulation stayed on walk map: " + offMap);
            _report.AppendLine("INTERPOLATION " + name + " visual boundary frames=" + visualOffMap);
            yield return new WaitForSecondsRealtime(.25f);
            yield return Shot(name);
        }

        IEnumerator Shot(string name)
        {
            // Отчёт сохраняет ошибки отдельно; консоль не заслоняет горизонтальный кадр игры.
            Debug.developerConsoleVisible = false;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output, name + ".png"));
            SaveProgress();
        }

        IEnumerator SwapVisualAudit()
        {
            var passage = FindAnyObjectByType<CampRiverPassage>();
            var before = CampNavigationGeometry.Collect(_root, passage?.Bridge, _camp.GroundHeight);
            CampNavigationObstacle proxy = null;
            MeshFilter mesh = null;
            foreach (var candidate in _root.GetComponentsInChildren<CampNavigationObstacle>())
            {
                if (candidate.Role == CampObstacleRole.Passable || candidate.Role == CampObstacleRole.Trunk || candidate.FitVisualFootprint) continue;
                foreach (var filter in candidate.GetComponentsInChildren<MeshFilter>())
                    if (filter.sharedMesh != null && filter.GetComponentInParent<LODGroup>() == null)
                    { proxy = candidate; mesh = filter; break; }
                if (proxy != null) break;
            }
            Check(proxy != null, "frozen authoring proxy is available for visual swap audit");
            if (proxy == null) yield break;
            GameObject temporary = null;
            var renderers = proxy.GetComponentsInChildren<Renderer>(true);
            var enabledBefore = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) enabledBefore[i] = renderers[i].enabled;
            bool unchanged = false;
            try
            {
                // Временный меш расширяет габарит дочерней модели, не меняя стабильный корень.
                // Сам ассет, физика и уже построенная карта не редактируются.
                temporary = new GameObject("Проверка сменной модели — временно");
                temporary.transform.SetParent(proxy.transform, false);
                temporary.transform.localPosition = new Vector3(37, 0, -29);
                temporary.transform.localScale = Vector3.one * 11;
                temporary.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;
                var changed = CampNavigationGeometry.Collect(_root, passage?.Bridge, _camp.GroundHeight);
                unchanged = SameSources(before, changed);
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = enabledBefore[i];
                if (temporary != null) Destroy(temporary);
            }
            Check(unchanged, "visual replacement bounds and disabled renderers preserve every navigation source");
            yield return null;
            Check(SameSources(before, CampNavigationGeometry.Collect(_root, passage?.Bridge, _camp.GroundHeight)),
                "temporary visual swap is fully restored");
        }

        bool SameSources(List<CampNavigationGeometry.Footprint> a, List<CampNavigationGeometry.Footprint> b)
        {
            if (a.Count != b.Count) return false;
            var sources = new Dictionary<Transform, NavMeshBuildSource>();
            foreach (var footprint in a) sources[footprint.Root] = CampNavigationGeometry.Source(footprint, _camp.GroundHeight);
            foreach (var footprint in b)
            {
                if (!sources.TryGetValue(footprint.Root, out var expected)) return false;
                var actual = CampNavigationGeometry.Source(footprint, _camp.GroundHeight);
                if (actual.shape != expected.shape || actual.area != expected.area || (actual.size - expected.size).sqrMagnitude > 1e-10f) return false;
                for (int i = 0; i < 16; i++) if (Mathf.Abs(actual.transform[i] - expected.transform[i]) > 1e-5f) return false;
            }
            return true;
        }

        void AuditRiver(CampRiverPassage passage)
        {
            var river = passage.GetComponent<CampRiver>();
            if (river == null) { Check(false, "bridge has river reference"); return; }
            int leaking = 0;
            for (float x = -35; x < 28; x += .5f)
            {
                var point = river.transform.TransformPoint(new Vector3(x, 0, river.CentreAt(x)));
                if (!passage.IsOpen(river, point) && _camp.WalkMap.Contains(Flat(point))) leaking++;
            }
            Check(leaking == 0, "river centre remains blocked outside bridge: " + leaking);
        }

        void AuditGeometry(Transform root)
        {
            var ranked = new List<(long triangles, string name)>();
            long total = 0; int renderers = 0, shadows = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                long triangles = 0;
                for (int i = 0; i < mesh.subMeshCount; i++) triangles += (long)mesh.GetIndexCount(i) / 3;
                total += triangles; renderers++;
                if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadows++;
                ranked.Add((triangles, renderer.name + " / " + mesh.name));
            }
            ranked.Sort((a, b) => b.triangles.CompareTo(a.triangles));
            var budget = new StringBuilder();
            budget.AppendLine("Active camp renderers=" + renderers + " shadow casters=" + shadows + " source triangles=" + total);
            for (int i = 0; i < Mathf.Min(30, ranked.Count); i++) budget.AppendLine(ranked[i].triangles + " " + ranked[i].name);
            File.WriteAllText(Path.Combine(_output, "geometry-audit.txt"), budget.ToString());
        }

        void SaveProgress() => File.WriteAllText(Path.Combine(_output, "walk-progress.txt"), _report.ToString());
        void Finish()
        {
            Finished = true;
            _report.AppendLine(_passed ? "ALL CAMP REVIEW CHECKS PASSED" : "CAMP REVIEW HAS FAILURES");
            File.WriteAllText(Path.Combine(_output, "walk-review.txt"), _report.ToString());
            Debug.Log("[camp-review] finished passed=" + _passed);
        }
    }
}
