using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Изолированный замер хвата на игровом риге, только для capture.</summary>
    public sealed class PelagEquipmentCapture : MonoBehaviour
    {
        private StreamWriter _writer;
        private static string F(float value) => value.ToString("F6", CultureInfo.InvariantCulture);
        public void Initialize(string directory) => StartCoroutine(Record(directory));

        private IEnumerator Record(string directory)
        {
            ArenaView arena = FindAnyObjectByType<ArenaView>();
            if (arena == null || !arena.TryGetEntityView(Simulation.PlayerId, out Transform body)) yield break;
            var equipment = body.GetComponent<PelagEquipmentView>();
            var presentation = body.GetComponent<CharacterAnimatorView>();
            Transform hand = body.GetComponentsInChildren<Transform>().First(t => t.name == "mixamorig:RightHand");
            Transform saber = body.GetComponentsInChildren<Transform>().First(t => t.name == "Pelag_FantasySaber_Equipped");
            _writer = new StreamWriter(Path.Combine(directory, "equipment.csv")) { AutoFlush = true };
            _writer.WriteLine("time,ready,phase,inHand,gripError,transferDistance,handX,handY,handZ,saberX,saberY,saberZ,action");
            var end = new WaitForEndOfFrame();
            while (body != null)
            {
                yield return end;
                Vector3 h = body.InverseTransformPoint(hand.position) * body.lossyScale.y;
                Vector3 s = body.InverseTransformPoint(saber.position) * body.lossyScale.y;
                _writer.WriteLine(string.Join(",", F(Time.time - CaptureRig.EquipmentStartedAt),
                    equipment.CombatReady, F(equipment.DrawPhase), equipment.SaberInHand,
                    F(equipment.GripError), F(equipment.TransferDistance), F(h.x), F(h.y), F(h.z), F(s.x), F(s.y), F(s.z),
                    presentation.HasCommittedAction));
            }
        }

        private void OnDestroy() { _writer?.Dispose(); _writer = null; }
    }
}
