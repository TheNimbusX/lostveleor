#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    // Диагностика без изменений мира, по настоящей карте CampPlayerView в игре.
    public static class CampRouteAudit
    {
        public static string Report(bool includeMap = false)
        {
            var camp = CampPlayerView.Instance;
            if (camp == null || camp.WalkMap == null) return "Camp walk map is not ready";
            var map = camp.WalkMap;
            var passage = Object.FindAnyObjectByType<CampRiverPassage>();
            var text = new StringBuilder();
            var start = CampTrainingView.Flat(camp.InteractionPosition);
            text.AppendLine("start=" + camp.InteractionPosition.ToString("F2") + " map=" + map.Contains(start));
            text.AppendLine("walkableCells=" + map.WalkableCellCount + " connectedAreas=" + map.ComponentCount
                + " footprintCount=" + (camp.NavigationFootprints?.Count ?? 0));
            if (camp.NavigationFootprints != null)
                foreach (var footprint in camp.NavigationFootprints)
                {
                    var source = CampNavigationGeometry.Source(footprint, camp.GroundHeight);
                    text.AppendLine("obstacle " + footprint.Role + " " + footprint.Root.name
                        + " at=" + source.transform.GetColumn(3).ToString("F2") + " size=" + source.size.ToString("F2"));
                }
            if (passage != null)
            {
                Bounds b = passage.BridgeBounds;
                text.AppendLine("bridge=" + b + " crossing=" + passage.CrossingSize);
                for (float z = b.max.z + 1f; z >= b.min.z - 1f; z -= .25f)
                {
                    text.Append(z.ToString("0.00") + " ");
                    foreach (float dx in new[] { -.7f, -.45f, 0f, .45f, .7f })
                        text.Append(map.Contains(CampTrainingView.Flat(new Vector3(b.center.x + dx, 0, z))) ? '#' : '.');
                    text.Append(" open=");
                    var river = passage.GetComponent<CampRiver>();
                    foreach (float dx in new[] { -.7f, -.45f, 0f, .45f, .7f })
                        text.Append(passage.IsOpen(river, new Vector3(b.center.x + dx, 0, z)) ? '#' : '.');
                    text.AppendLine(" height=" + camp.SurfaceHeight(b.center.x, z).ToString("0.000"));
                }
                Route(text, map, "bridge-across", new Vector3(b.center.x, 0, b.max.z + 1.2f),
                    new Vector3(b.center.x, 0, b.min.z - 1.2f));
                Walk(text, map, "bridge-walk", new Vector3(b.center.x, 0, b.max.z + 1.2f),
                    new Vector3(b.center.x, 0, b.min.z - 1.2f));
                foreach (float lane in new[] { -.6f, 0f, .6f })
                {
                    var near = new Vector3(b.center.x + lane, 0, b.max.z + 1f);
                    var far = new Vector3(b.center.x + lane, 0, b.min.z - 1f);
                    var a = CampTrainingView.Flat(near);
                    var c = CampTrainingView.Flat(far);
                    text.AppendLine("bridge-lane " + lane.ToString("0.0") + " forward=" + map.CanTravel(a, c)
                        + " backward=" + map.CanTravel(c, a));
                    Walk(text, map, "bridge-lane-forward-" + lane.ToString("0.0"), near, far);
                    Walk(text, map, "bridge-lane-backward-" + lane.ToString("0.0"), far, near);
                }
            }
            var world = Object.FindAnyObjectByType<SceneWorldView>();
            Transform fire = world != null && world.CampRoot != null
                ? world.CampRoot.transform.Find("Campfire") : null;
            Vector3 fireSeed = Vector3.zero;
            bool fireSeedValid = false;
            if (fire != null)
            {
                text.AppendLine("fire=" + fire.position.ToString("F2"));
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8;
                    var at = fire.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2f;
                    text.Append(map.Contains(CampTrainingView.Flat(at)) ? '#' : '.');
                }
                text.AppendLine(" fire-ring 16 samples at 2m");
                for (float z = 3f; z >= -3f; z -= .5f)
                {
                    text.Append("fire-map z=" + z.ToString("0.0") + " ");
                    for (float x = -3f; x <= 3f; x += .5f)
                        text.Append(map.Contains(CampTrainingView.Flat(fire.position + new Vector3(x, 0, z))) ? '#' : '.');
                    text.AppendLine();
                }
                foreach (var collider in Physics.OverlapSphere(fire.position, 3f))
                    text.AppendLine("fire-collider " + collider.name + " " + collider.bounds);
                Vector3 preferred = fire.position + new Vector3(-1.8f, 0, -1f);
                if (map.TryNearestReachable(start, CampTrainingView.Flat(preferred), out var resolved))
                {
                    fireSeed = new Vector3(resolved.X.ToFloat(), camp.GroundHeight, resolved.Y.ToFloat());
                    float snap = Vector2.Distance(new Vector2(preferred.x, preferred.z), new Vector2(fireSeed.x, fireSeed.z));
                    fireSeedValid = snap <= .75f && map.Contains(resolved);
                    text.AppendLine("fire-seed preferred=" + preferred.ToString("F2") + " valid=" + fireSeedValid
                        + " resolved=" + fireSeed.ToString("F2") + " snap=" + snap.ToString("0.00"));
                }
                if (fireSeedValid) Route(text, map, "spawn-fire", camp.InteractionPosition, fireSeed);
                Walk(text, map, "around-fire", camp.InteractionPosition,
                    fire.position + new Vector3(0, 0, 2.8f));
            }
            foreach (var npc in Object.FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
            {
                bool foundApproach = camp.TryServiceApproach(npc, camp.InteractionPosition, out var target);
                text.AppendLine("service " + npc.Kind + " active=" + npc.isActiveAndEnabled
                    + " reachableApproach=" + foundApproach + " target=" + target.ToString("F2"));
                if (!foundApproach) continue;
                var path = map.FindPath(start, CampTrainingView.Flat(target));
                var end = path.Length == 0 ? FixVec2.Zero : path[path.Length - 1];
                var endWorld = new Vector3(end.X.ToFloat(), camp.GroundHeight, end.Y.ToFloat());
                text.AppendLine(npc.Kind + " at=" + npc.transform.position.ToString("F2")
                    + " approach=" + target.ToString("F2") + " approachOnMap="
                    + map.Contains(CampTrainingView.Flat(target)) + " pathCorners=" + path.Length
                    + " routeEndsNear=" + (path.Length > 0 && npc.Near(endWorld))
                    + " routeEnd=" + endWorld.ToString("F2"));
                Walk(text, map, "spawn-" + npc.Kind, camp.InteractionPosition, target, npc);
                if (fireSeedValid)
                {
                    if (camp.TryServiceApproach(npc, fireSeed, out var fireTarget))
                        Walk(text, map, "fire-" + npc.Kind + "-walk", fireSeed, fireTarget, npc);
                    else text.AppendLine("fire-" + npc.Kind + "-walk no interaction approach");
                }
                if (npc.Kind == CampServiceKind.Trader && fireSeedValid)
                {
                    Route(text, map, "fire-trader", fireSeed, target);
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * Mathf.PI / 6;
                        var candidate = npc.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.4f;
                        var point = CampTrainingView.Flat(candidate);
                        if (!map.Contains(point)) continue;
                        var candidatePath = map.FindPath(start, point);
                        var firePath = map.FindPath(CampTrainingView.Flat(fireSeed), point);
                        text.AppendLine("trader-candidate " + i + " at=" + candidate.ToString("F2")
                            + " spawn=" + Length(candidatePath).ToString("0.00")
                            + " fire=" + Length(firePath).ToString("0.00"));
                    }
                }
            }
            // Карта проходимости целиком, шаг 0,5 м: по ней расставляется стена леса по краю лагеря.
            if (includeMap) text.AppendLine("walkmap x=-50..40 z=-50..40 step=0.5 (строки сверху вниз, # — проходимо)");
            for (float z = 40f; includeMap && z >= -50f; z -= .5f)
            {
                var row = new StringBuilder(181);
                for (float x = -50f; x <= 40f; x += .5f)
                    row.Append(map.Contains(new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(z))) ? '#' : '.');
                text.AppendLine(row.ToString());
            }
            return text.ToString();
        }

        static void Route(StringBuilder text, CampWalkMap map, string name, Vector3 from, Vector3 to)
        {
            var a = CampTrainingView.Flat(from);
            var b = CampTrainingView.Flat(to);
            var path = map.FindPath(a, b);
            text.AppendLine(name + " start=" + map.Contains(a) + " end=" + map.Contains(b)
                + " direct=" + map.CanTravel(a, b) + " corners=" + path.Length);
            for (int i = 0; i < path.Length; i++) text.AppendLine("  " + i + " " + path[i]);
        }

        static float Length(FixVec2[] path)
        {
            if (path.Length == 0) return -1;
            float distance = 0;
            for (int i = 1; i < path.Length; i++) distance += (path[i] - path[i - 1]).Length.ToFloat();
            return distance;
        }

        static void Walk(StringBuilder text, CampWalkMap map, string name, Vector3 from, Vector3 to, CampServiceNpc npc = null)
        {
            var start = CampTrainingView.Flat(from);
            var goal = CampTrainingView.Flat(to);
            if (!map.Contains(start))
            { text.AppendLine(name + " invalid start=" + from.ToString("F2")); return; }
            if (!map.TryNearestReachable(start, goal, out var reachable))
            { text.AppendLine(name + " no reachable cell"); return; }
            float snap = FixVec2.Distance(goal, reachable).ToFloat();
            if (snap > .75f)
            { text.AppendLine(name + " target too far from reachable cell targetSnap=" + snap.ToString("0.00")); return; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var route = new CampRoute(map);
            bool found = route.To(start, goal);
            clock.Stop();
            if (!found) { text.AppendLine(name + " no route"); return; }
            var session = PrototypeContent.NewSession(963UL);
            session.ConfigureCampWorld(start, map);
            var sim = session.ActiveSim;
            int still = 0, maxStill = 0, ticks = 0;
            for (; ticks < 600; ticks++)
            {
                var before = sim.Entities.Position[Simulation.PlayerId];
                if (FixVec2.DistanceSq(before, reachable) < Fix64.Ratio(7, 20) * Fix64.Ratio(7, 20)) break;
                var input = InputFrame.Empty;
                if (route.Advance(before, out var aim, out bool final))
                {
                    input.Aim = aim;
                    input.Flags = CampRoute.FlagsFor(final);
                    input.AttackTarget = -1;
                }
                session.Step(input);
                bool stopped = sim.Entities.Position[Simulation.PlayerId].Equals(before);
                still = stopped ? still + 1 : 0;
                maxStill = System.Math.Max(maxStill, still);
            }
            FixVec2 finalPosition = sim.Entities.Position[Simulation.PlayerId];
            Vector3 finalWorld = new Vector3(finalPosition.X.ToFloat(), from.y, finalPosition.Y.ToFloat());
            bool near = npc == null || npc.Near(finalWorld);
            text.AppendLine(name + " arrived=" + (ticks < 600 && near) + " ticks=" + ticks
                + " longestStop=" + maxStill + " targetSnap=" + snap.ToString("0.00")
                + " interactionNear=" + near + " actualEnd=" + finalWorld.ToString("F2")
                + " routeMs=" + clock.Elapsed.TotalMilliseconds.ToString("0.00"));
        }
    }
}
#endif
