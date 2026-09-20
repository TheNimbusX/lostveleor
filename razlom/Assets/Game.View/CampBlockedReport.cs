using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Диагностика «невидимых препятствий»: проходит по карте ходьбы лагеря,
    /// находит запертые клетки и называет коллайдеры, которые их перекрывают.
    ///
    /// Нужна потому, что навигация лагеря строится в игре: декоративному мешу
    /// без коллайдера он выдаётся автоматически, и лишний объект перекрывает
    /// проход там, где на картинке пусто. Пишет отчёт и карту PNG.
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

            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                var point = new Vector3(map.min.x + (x + .5f) * step, y, map.min.z + (z + .5f) * step);
                bool open = camp.WalkMap.Contains(CampTrainingView.Flat(point));
                if (open) { walkable++; texture.SetPixel(x, z, new Color(.85f, .85f, .85f)); continue; }
                blocked++;

                // Кто стоит в этой клетке: коробка высотой с героя над точкой.
                Collider[] hits = Physics.OverlapBox(point + Vector3.up * .8f, new Vector3(.12f, .8f, .12f));
                string name = hits.Length == 0 ? "(пусто — нет коллайдера)" : Name(hits);
                blockedBy.TryGetValue(name, out int count);
                blockedBy[name] = count + 1;
                if (!samples.ContainsKey(name)) samples[name] = point;
                texture.SetPixel(x, z, hits.Length == 0 ? new Color(.2f, .2f, .35f) : new Color(.9f, .25f, .2f));
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
            Debug.Log($"[camp-blocked] клеток закрыто {blocked} из {walkable + blocked}; отчёт записан");
        }

        /// <summary>Имя виновника: объект вместе с родителем, чтобы «LOD0» не выглядел одинаково у всех.</summary>
        static string Name(Collider[] hits)
        {
            var names = new List<string>();
            foreach (Collider hit in hits)
            {
                Transform t = hit.transform;
                string name = t.parent != null ? t.parent.name + " / " + t.name : t.name;
                if (!names.Contains(name)) names.Add(name);
            }
            names.Sort();
            return string.Join(" + ", names);
        }
    }
}
