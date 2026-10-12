using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Невод (кадр form-net-v1) — сеть из цепей на полосе Sim поверх стека базы в краске Невода (см. PelagVfxController.AnchorThrowIron
    /// и AnchorThrowChainNet): ложится с HoldTick (поза «держит») к рывку, узлы
    /// вспыхивают волной от руки к хвосту; пойманные сетью (Hit полосы 3 в тик рывка) — горб сети над телом и вспышка
    /// узлов рядом; в возврате хвостовой край гребёт по кривой тяги Sim к полукольцу ловли; в ловлю (или срыв) сеть
    /// стягивается к руке, узлы гаснут искрами, звенья сыплются. Бросок в стену (HasNet = false) — сети нет.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private const int AiNetCaughtMax = 8;

        private sealed class AiNet
        {
            public bool Active, Burst;
            public int Serial = -1, Fx = -1, CaughtCount;
            public GameObject Object;
            public MeshFilter Strands, Knots;
            public ParticleSystem Sparks, Links;
            public Vector3 Origin, Dir;
            public readonly int[] Caught = new int[AiNetCaughtMax];
            public readonly float[] CaughtAt = new float[AiNetCaughtMax];
            public readonly AnchorThrowChainNet Net = new AnchorThrowChainNet();
            public readonly AnchorThrowLaneGround Ground = new AnchorThrowLaneGround();
        }

        private readonly AiNet _aiNet = new AiNet();

        private void BeginAiNet(in AnchorThrowState s)
        {
            AiNet net = _aiNet;
            net.Serial = _aic.Serial;
            net.CaughtCount = 0;
            net.Burst = false;
            net.Dir = AtDir(s, 0);
            var origin = new Vector3(s.Center.X.ToFloat(), PlayerPosition().y, s.Center.Y.ToFloat());
            origin.y = AtGround(origin);
            net.Origin = origin;
            float restFar = s.Reach0.ToFloat() + PelagThrowFormLook.NetFarBeyond + PelagThrowFormLook.NetRag;
            net.Ground.Sample(origin, net.Dir, restFar + .6f, s.NetHalfWidth.ToFloat() + .5f, AnchorThrowGround());
            net.Fx = AiSpawn(PelagVfxId.AnchorThrowFormNet, Vector3.zero, Quaternion.identity, 0f, 8f, out net.Object);
            if (net.Fx < 0) return;
            Transform t = net.Object.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            t.localScale = Vector3.one;
            net.Strands = t.Find("Strands")?.GetComponent<MeshFilter>();
            net.Knots = t.Find("Knots")?.GetComponent<MeshFilter>();
            net.Sparks = t.Find("Sparks")?.GetComponent<ParticleSystem>();
            net.Links = t.Find("Links")?.GetComponent<ParticleSystem>();
            net.Active = true;
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[throw-iron] net serial={_aic.Serial} hold={s.HoldTick} taut={s.TautTick} reach={s.Reach0.ToFloat():F2} half={s.NetHalfWidth.ToFloat():F2}");
        }

        private bool UpdateAiNet(Simulation sim, float shown, float dt)
        {
            AiNet net = _aiNet;
            AnchorThrowState s = _aic.Snap;
            if (!net.Active)
            {
                if (_aic.Form != PelagForm.AnchorThrowNet || _aic.Serial < 0 || net.Serial == _aic.Serial || !s.HasNet || shown < s.HoldTick) return false;
                if (_aic.BreakFrom >= 0f && _aic.BreakFrom < s.HoldTick) return false;
                BeginAiNet(s);
                if (!net.Active) return false;
            }
            if (!AtStill(net.Fx, net.Object)) { net.Active = false; return false; }
            float collapseFrom = _aic.BreakFrom >= 0f ? _aic.BreakFrom : float.MaxValue;
            PelagThrowFormLook.NetSpan(s, shown, collapseFrom, out float near, out float far, out float restFar);
            float collapse = PelagThrowFormLook.NetCollapse(shown, collapseFrom);
            float pulse = shown < s.TautTick ? 0f : 1f - PelagThrowFormLook.Smooth01((shown - s.TautTick) / 3f);
            net.Net.ClearBumps();
            for (int i = 0; i < net.CaughtCount; i++)
            {
                int id = net.Caught[i];
                if ((uint)id >= (uint)sim.Entities.Count || !sim.Entities.Alive[id]) continue;
                float flare = 1f - PelagThrowFormLook.Smooth01((shown - net.CaughtAt[i]) / 6f);
                net.Net.AddBump(EntityPosition(id, net.Origin), flare);
            }
            Camera camera = Camera.main;
            Vector3 forward = camera != null ? camera.transform.forward : new Vector3(0f, -.74f, .67f);
            Vector3 up = camera != null ? camera.transform.up : new Vector3(0f, .67f, .74f);
            var frame = new AnchorThrowChainNet.Frame
            {
                Origin = net.Origin, Dir = net.Dir, ViewForward = forward, ViewUp = up,
                Near = near, Far = far, RestFar = restFar,
                FullHalf = s.NetHalfWidth.ToFloat() + .05f,
                Open = PelagThrowFormLook.NetOpen(shown, s.HoldTick),
                Collapse = collapse,
                // V7: остаток сети у руки гаснет с ловли (не держится до конца стяжки).
                Opacity = 1f - PelagThrowFormLook.Smooth01((collapse - .15f) / .6f),
                Clock = (shown - s.ReleaseTick) / PelagThrowFormLook.TicksPerSecond,
                Pulse = pulse,
                Serial = _aic.Serial,
            };
            if (net.Strands != null && net.Knots != null)
                net.Net.Build(FormWaterMesh.MeshFor(net.Strands, AnchorThrowChainNet.StrandMeshName),
                    FormWaterMesh.MeshFor(net.Knots, AnchorThrowChainNet.KnotMeshName), frame, net.Ground);
            if (collapse > 0f && !net.Burst)
            {
                net.Burst = true;
                AiNetBurst(net, near, far);
            }
            if (collapse >= 1f && shown >= collapseFrom + PelagThrowFormLook.NetCollapseTicks + 6f) { EndAiNet(); return false; }
            return true;
        }

        /// <summary>Стяжка: узлы гаснут искрами по всей сети, звенья сыплются с хвоста.</summary>
        private void AiNetBurst(AiNet net, float near, float far)
        {
            Vector3 right = new Vector3(net.Dir.z, 0f, -net.Dir.x);
            float half = _aic.Snap.NetHalfWidth.ToFloat();
            for (int i = 0; i < 46 && net.Sparks != null; i++)
            {
                float a = Random.Range(near, far), y = Random.Range(-1f, 1f) * half * .8f;
                Vector3 p = net.Origin + net.Dir * a + right * y;
                p.y = net.Ground.At(a, y) + .2f;
                AiSpark(net.Sparks, p, -net.Dir * Random.Range(.5f, 2.5f) + Random.insideUnitSphere * 1.6f + Vector3.up * Random.Range(.3f, 1.4f));
            }
            for (int i = 0; i < 18 && net.Links != null; i++)
            {
                float a = Random.Range(Mathf.Lerp(near, far, .4f), far), y = Random.Range(-1f, 1f) * half * .7f;
                Vector3 p = net.Origin + net.Dir * a + right * y;
                p.y = net.Ground.At(a, y) + .25f;
                net.Links.Emit(new ParticleSystem.EmitParams
                {
                    position = p, velocity = Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(1f, 2.2f) - net.Dir * .8f,
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>Hit полосы 3 — тело в сети: горб и вспышка узлов вокруг него.</summary>
        private void AiNetCaught(int target)
        {
            AiNet net = _aiNet;
            Simulation sim = _driver.Sim;
            float shown = sim != null ? PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha) : 0f;
            for (int i = 0; i < net.CaughtCount; i++) if (net.Caught[i] == target) return;
            if (net.CaughtCount >= AiNetCaughtMax) return;
            net.Caught[net.CaughtCount] = target;
            net.CaughtAt[net.CaughtCount] = shown;
            net.CaughtCount++;
        }

        private void EndAiNet()
        {
            AiNet net = _aiNet;
            if (AtStill(net.Fx, net.Object))
            {
                if (net.Strands != null) FormWaterMesh.MeshFor(net.Strands, AnchorThrowChainNet.StrandMeshName).Clear();
                if (net.Knots != null) FormWaterMesh.MeshFor(net.Knots, AnchorThrowChainNet.KnotMeshName).Clear();
                Release(net.Fx);
            }
            net.Active = false;
            net.Fx = -1;
            net.Object = null;
        }
    }
}
