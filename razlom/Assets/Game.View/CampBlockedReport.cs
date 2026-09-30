using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Диагностика «невидимых препятствий»: проходит по карте ходьбы лагеря,
    /// находит запертые клетки и называет простые навигационные препятствия.
    /// Использует те же footprint и запас тела, что построитель карты.
    /// Пишет отчёт и карту PNG.
    /// Только под флагом съёмки, в обычной игре не создаётся.
    /// </summary>
    public sealed class CampBlockedReport : MonoBehaviour
    {
        string _output;
        public void Initialize(string output) { _output = output; }

        IEnumerator Start()
        {
            CampPlayerView camp = CampPlayerView.Instance;
            while (camp == null || camp.WalkMap == null) { yield return null; camp = CampPlayerView.Instance; }
            yield return new WaitForSeconds(1f);

            Bounds map = camp.MapBounds;
            const float step = .25f;
            int width = Mathf.CeilToInt(map.size.x / step);
            int height = Mathf.CeilToInt(map.size.z / step);
            float y = camp.Position.y;

            var blockedBy = new Dictionary<string, int>();
            var samples = new Dictionary<string, Vector3>();
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            int walkable = 0, blocked = 0;
            var npcs = FindObjectsByType<CampServiceNpc>();

            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                var point = new Vector3(map.min.x + (x + .5f) * step, y, map.min.z + (z + .5f) * step);
                bool open = camp.WalkMap.Contains(CampTrainingView.Flat(point));
                if (open) { walkable++; texture.SetPixel(x, z, new Color(.85f, .85f, .85f)); continue; }
                blocked++;

                var reasons = new List<string>();
                if (camp.NavigationFootprints != null)
                    foreach (var footprint in camp.NavigationFootprints)
                        if (CampNavigationGeometry.Covers(footprint, point, .3f))
                            reasons.Add(footprint.Role + ": " + footprint.Root.name);
                foreach (var npc in npcs)
                    if (Mathf.Abs(point.x - npc.transform.position.x) <= .625f
                        && Mathf.Abs(point.z - npc.transform.position.z) <= .625f)
                        reasons.Add("персонаж: " + npc.Kind);
                reasons.Sort();
                string name = reasons.Count == 0 ? "(граница воды/арки/площадки или край bake)" : string.Join(" + ", reasons);
                blockedBy.TryGetValue(name, out int count);
                blockedBy[name] = count + 1;
                if (!samples.ContainsKey(name)) samples[name] = point;
                texture.SetPixel(x, z, reasons.Count == 0 ? new Color(.2f, .2f, .35f) : new Color(.9f, .25f, .2f));
            }

            texture.Apply();
            Directory.CreateDirectory(_output);
            File.WriteAllBytes(Path.Combine(_output, "camp-blocked-map.png"), texture.EncodeToPNG());

            var lines = new List<string>
            {
                $"карта {width}x{height} клеток по {step} м, площадь {map.min.x:0.0}..{map.max.x:0.0} x {map.min.z:0.0}..{map.max.z:0.0}",
                $"проходимых клеток {walkable}, закрытых {blocked}",
                "",
                "что закрывает (клеток, пример точки):"
            };
            foreach (var pair in blockedBy.OrderByDescending(p => p.Value).Take(40))
                lines.Add($"{pair.Value,6}  {pair.Key}  пример {samples[pair.Key].x:0.0} {samples[pair.Key].z:0.0}");
            File.WriteAllLines(Path.Combine(_output, "camp-blocked.txt"), lines);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            File.WriteAllText(Path.Combine(_output, "camp-routes.txt"), CampRouteAudit.Report());
#endif
            Debug.Log($"[camp-blocked] клеток закрыто {blocked} из {walkable + blocked}; отчёт записан");
        }

    }
}
