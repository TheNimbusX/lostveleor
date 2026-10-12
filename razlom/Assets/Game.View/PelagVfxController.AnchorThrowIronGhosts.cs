using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Веер (кадр form-fan-ghost-v1) — призрачные якоря на боковых полосах (см. PelagVfxController.AnchorThrowIron). Подход
    /// Призрачного якоря Крушения (PelagWreckComboVfxSetup.Ghost — копии его материалов, БЕЗ чёрного обвода): меш якоря Пелага
    /// на светящемся призрачном шейдере. Поза — поза НАСТОЯЩЕЙ ГОЛОВЫ рига (поворот, наклон, крутка), повёрнутая вокруг вертикали
    /// на угол своей полосы Sim и перенесённая на голову полосы (формула Sim, AnchorThrowHead): летит, держит, дёргается и
    /// возвращается как железо. Кольцо призрака — конец его полосы стека (струна, ореол, звенья призрачной цепи).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class AiGhost
        {
            public bool Active, Burst;
            public int Lane, Fx = -1;
            public GameObject Object;
            public Transform Model;
            public Renderer[] Renderers;
            public ParticleSystem Shards, Motes;
            public float Born, DissolveFrom = -1f;
        }

        private readonly AiGhost[] _aiGhosts = { new AiGhost { Lane = 1 }, new AiGhost { Lane = 2 } };
        private MaterialPropertyBlock _aiGhostBlock;

        private bool AiAnyGhost => _aiGhosts[0].Active || _aiGhosts[1].Active;

        private void BeginAiGhost(int lane)
        {
            AiGhost g = _aiGhosts[lane - 1];
            EndAiGhost(g);
            Vector3 fist = _aic.Ring;
            g.Fx = AiSpawn(PelagVfxId.AnchorThrowFormGhost, fist, Quaternion.identity, 0f, 8f, out g.Object);
            if (g.Fx < 0) return;
            Transform t = g.Object.transform;
            t.localScale = Vector3.one;
            g.Model = t.Find("Model");
            g.Renderers = g.Model != null ? g.Model.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            g.Shards = t.Find("Shards")?.GetComponent<ParticleSystem>();
            g.Motes = t.Find("Motes")?.GetComponent<ParticleSystem>();
            g.Born = Time.time;
            g.DissolveFrom = -1f;
            g.Burst = false;
            g.Active = true;
            _aiLanes[lane].Ring = fist;
            AiTintGhost(g, 0f, 0f, 0f);
        }

        /// <summary>Кольцо и поза призраков этого кадра (до струн: струна боковой полосы идёт к кольцу призрака).</summary>
        private void UpdateAiGhosts(float shown, float dt)
        {
            AnchorThrowState s = _aic.Snap;
            Vector3 dir0 = AtDir(s, 0);
            AtHeadOnLine(_aic.ReleaseHeight, s, 0, shown, out Vector3 head0);
            float jerk = PelagAnchorThrowVfxRules.Jerk(shown, s.TautTick);
            head0 -= dir0 * jerk;
            foreach (AiGhost g in _aiGhosts)
            {
                if (!g.Active) continue;
                if (!AtStill(g.Fx, g.Object)) { g.Active = false; continue; }
                Vector3 dir = AtDir(s, g.Lane);
                Quaternion yaw = Quaternion.FromToRotation(dir0, dir);
                AtHeadOnLine(_aic.ReleaseHeight, s, g.Lane, shown, out Vector3 head);
                head -= dir * jerk;
                // Отклонение настоящего кольца от линии Sim (мах рига, высота) — то же у призрака, повёрнутое на угол полосы.
                Vector3 ring = head + yaw * (_aic.Ring - head0);
                _aiLanes[g.Lane].Ring = ring;
                g.Object.transform.position = ring;
                // Подлёт к руке: призрак сжимается к размеру головы и распадается до ловли (не пятном на герое).
                float toHand = Vector3.Distance(ring, _aic.Grip);
                if (g.DissolveFrom < 0f && shown >= s.TautTick && shown >= s.CatchTick - PelagThrowFormLook.GhostDissolveBeforeCatch)
                    g.DissolveFrom = shown;
                PoseAiGhost(g, ring, yaw, PelagThrowFormLook.GhostGrow(toHand));
                int arrive = PelagAnchorThrowVfxRules.LaneArriveTick(s, g.Lane);
                float appear = PelagThrowFormLook.GhostAppear(shown, s.ReleaseTick);
                float dissolve = g.DissolveFrom >= 0f ? PelagThrowFormLook.GhostDissolve(shown, g.DissolveFrom) : 0f;
                float flash = .7f * (1f - PelagThrowFormLook.Smooth01(Mathf.Abs(shown - arrive) / 1.5f));
                AiTintGhost(g, appear, dissolve, flash);
                if (g.DissolveFrom >= 0f && !g.Burst && shown >= g.DissolveFrom)
                {
                    g.Burst = true;
                    AiGhostBurst(g, ring);
                }
                if (dissolve >= 1f && shown >= g.DissolveFrom + 14f) EndAiGhost(g);
            }
        }

        /// <summary>Поза настоящей головы рига (меш Tripo) в осях меша призрака (низ 0, высота 1), повёрнутая на угол полосы.</summary>
        private void PoseAiGhost(AiGhost g, Vector3 ring, Quaternion yaw, float grow)
        {
            if (g.Model == null) return;
            MeshFilter real = AiRealHead();
            if (real != null && real.sharedMesh != null)
            {
                Bounds b = real.sharedMesh.bounds;
                float k = 1f / Mathf.Max(.01f, b.size.y);
                Transform rt = real.transform;
                Vector3 origin = rt.TransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
                // Крупнее настоящей головы вокруг кольца цепи (кольцо остаётся на конце призрачной цепи).
                g.Model.SetPositionAndRotation(ring + yaw * (origin - _aic.Ring) * grow, yaw * rt.rotation);
                g.Model.localScale = rt.lossyScale / k * grow;
                return;
            }
            Vector3 dir = AtDir(_aic.Snap, g.Lane);
            g.Model.SetPositionAndRotation(ring + dir * (PelagThrowFormLook.GhostFallbackHeight + .1f), Quaternion.LookRotation(Vector3.up, -dir));
            g.Model.localScale = Vector3.one * PelagThrowFormLook.GhostFallbackHeight * grow;
        }

        private Transform _aiHeadOwner;
        private MeshFilter _aiHead;

        /// <summary>Меш настоящей головы якоря (SlamHead снаряжения: «Stored anchor head», меш Tripo).</summary>
        private MeshFilter AiRealHead()
        {
            AnchorThrowFist();
            Transform head = _atEquipment != null ? _atEquipment.SlamHead : null;
            if (head != _aiHeadOwner)
            {
                _aiHeadOwner = head;
                _aiHead = head != null ? head.GetComponentInChildren<MeshFilter>(true) : null;
            }
            return _aiHead != null && _aiHead.gameObject.activeInHierarchy ? _aiHead : null;
        }

        private void AiTintGhost(AiGhost g, float appear, float dissolve, float flash)
        {
            if (g.Renderers == null) return;
            if (_aiGhostBlock == null) _aiGhostBlock = new MaterialPropertyBlock();
            foreach (Renderer r in g.Renderers)
            {
                if (r == null) continue;
                _aiGhostBlock.Clear();
                _aiGhostBlock.SetFloat(WgAppearId, appear);
                _aiGhostBlock.SetFloat(WgDissolveId, dissolve);
                _aiGhostBlock.SetFloat(WgFlashId, flash);
                _aiGhostBlock.SetFloat(WgAgeId, Time.time - g.Born);
                r.SetPropertyBlock(_aiGhostBlock);
                r.enabled = appear > .005f && dissolve < .999f;
            }
        }

        /// <summary>Распад призрака: осколки-кристаллы и искорки вверх от тела.</summary>
        private void AiGhostBurst(AiGhost g, Vector3 ring)
        {
            Vector3 at = g.Model != null ? g.Model.position : ring;
            Vector3 dir = AtDir(_aic.Snap, g.Lane);
            for (int i = 0; i < 14 && g.Shards != null; i++)
                g.Shards.Emit(new ParticleSystem.EmitParams
                {
                    position = Vector3.Lerp(ring, at, Random.value) + Random.insideUnitSphere * .2f,
                    velocity = dir * Random.Range(-.5f, 1.5f) + Random.insideUnitSphere * 1.3f + Vector3.up * Random.Range(.6f, 1.8f),
                    applyShapeToPosition = false
                }, 1);
            for (int i = 0; i < 24 && g.Motes != null; i++)
                g.Motes.Emit(new ParticleSystem.EmitParams
                {
                    position = Vector3.Lerp(ring, at, Random.value) + Random.insideUnitSphere * .25f,
                    velocity = Random.insideUnitSphere * .9f + Vector3.up * Random.Range(.8f, 2f),
                    applyShapeToPosition = false
                }, 1);
        }

        /// <summary>Ловля или срыв: призраки распадаются (в ловлю — у руки, их голова уже там по формуле Sim).</summary>
        private void CatchAiGhosts(float shown)
        {
            foreach (AiGhost g in _aiGhosts)
                if (g.Active && g.DissolveFrom < 0f) g.DissolveFrom = shown;
        }

        private void EndAiGhost(AiGhost g)
        {
            if (AtStill(g.Fx, g.Object)) Release(g.Fx);
            g.Active = false;
            g.Fx = -1;
            g.Object = null;
            g.Renderers = null;
        }

        private void EndAiGhosts() { foreach (AiGhost g in _aiGhosts) EndAiGhost(g); }
    }
}
