using System.Collections;
using System.Globalization;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed class CombatPresentationCapture : MonoBehaviour
    {
        private StreamWriter _writer;
        public void Initialize(string folder) => StartCoroutine(Record(folder));

        private IEnumerator Record(string folder)
        {
            var driver = FindAnyObjectByType<TickDriver>();
            var arena = FindAnyObjectByType<ArenaView>();
            _writer = new StreamWriter(Path.Combine(folder, "enemy-presentation.csv"));
            _writer.WriteLine("time,tick,entity,kind,alive,state,phase,transition,x,y,z,minY,height,clipSeconds");
            var end = new WaitForEndOfFrame();
            while (driver != null && arena != null)
            {
                yield return end;
                var sim = driver.Sim;
                if (sim == null) continue;
                for (int i = 1; i < sim.Entities.Count; i++)
                {
                    if (!arena.TryGetEntityView(i, out var body) || !body.gameObject.activeInHierarchy) continue;
                    var animator = body.GetComponent<Animator>();
                    if (animator == null) continue;
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>();
                    Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(body.position, Vector3.zero);
                    for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
                    _writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F4},{1},{2},{3},{4},{5},{6:F4},{7},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4}",
                        Time.time, sim.Tick, i, sim.Entities.Kind[i], sim.Entities.Alive[i],
                        state.shortNameHash, state.normalizedTime, animator.IsInTransition(0),
                        body.position.x, body.position.y, body.position.z, bounds.min.y, bounds.size.y, state.length));
                }
            }
        }

        private void OnDestroy() { _writer?.Dispose(); _writer = null; }
        private void OnApplicationQuit() { _writer?.Dispose(); _writer = null; }
    }
}
