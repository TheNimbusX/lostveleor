using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Opt-in capture of real A/B states, damage events and blade trajectories.</summary>
    public sealed class PelagAttackCapture : MonoBehaviour
    {
        private StreamWriter _writer;
        private static string F(float v) => v.ToString("F6", CultureInfo.InvariantCulture);
        public void Initialize(string directory) => StartCoroutine(Record(directory));

        private IEnumerator Record(string directory)
        {
            ArenaView arena = null;
            TickDriver driver = null;
            Transform body = null, bladeRoot = null, tip = null;
            while (tip == null || body == null)
            {
                yield return null;
                arena = FindAnyObjectByType<ArenaView>();
                driver = FindAnyObjectByType<TickDriver>();
                if (arena != null && driver != null && driver.Sim != null)
                {
                    arena.TryGetEntityView(Simulation.PlayerId, out body);
                    arena.TryGetPlayerBlade(out bladeRoot, out tip);
                }
            }
            Sample(body, tip.name, directory);
            Animator animator = body.GetComponent<Animator>();
            var equipment = body.GetComponent<PelagEquipmentView>();
            var vfx = FindAnyObjectByType<PelagVfxController>();
            int upper = animator.GetLayerIndex("UpperBody Combat");
            int lower = animator.GetLayerIndex("LowerBody Combat");
            _writer = new StreamWriter(Path.Combine(directory, "attacks.csv")) { AutoFlush = true };
            _writer.WriteLine("time,tick,attack,damage,variant,state,phase,upperWeight,lowerWeight,bladeX,bladeY,bladeZ,cast,abilityDamage,baseState,basePhase,deaths,timeScale,heldAnchor,flyingAnchors,handX,handY,handZ");
            var end = new WaitForEndOfFrame();
            while (body != null && driver.Sim != null)
            {
                yield return end;
                int attacks = 0, damage = 0, variant = -1, cast = 0, abilityDamage = 0, deaths = 0;
                foreach (SimEvent e in driver.FrameEvents)
                {
                    if (e.Type == SimEventType.Death && e.Target != Simulation.PlayerId) deaths++;
                    if (e.Source != Simulation.PlayerId) continue;
                    if (e.Type == SimEventType.AbilityCast) cast++;
                    if (e.Type == SimEventType.Damage && e.DamageOrigin == DamageOrigin.Ability) abilityDamage++;
                    if (e.Type == SimEventType.Attack) { attacks++; variant = e.Amount; }
                    if (e.Type == SimEventType.Damage && e.DamageOrigin == DamageOrigin.BasicAttack)
                    { damage++; variant = e.ActionVariant; }
                }
                var state = animator.GetCurrentAnimatorStateInfo(upper);
                string name = state.IsName("UpperBody Combat.Whirlwind_v5") ? "Whirlwind" : state.IsName("UpperBody Combat.Saber_A_v5") ? "A"
                    : state.IsName("UpperBody Combat.Saber_B_v5") ? "B" : "other";
                Vector3 point = body.InverseTransformPoint(tip.position) * body.lossyScale.y;
                Vector3 hand = equipment != null && equipment.ChainHand != null
                    ? body.InverseTransformPoint(equipment.ChainHand.position) * body.lossyScale.y : Vector3.zero;
                _writer.WriteLine(string.Join(",", F(Time.time), driver.Sim.Tick, attacks, damage, variant,
                    name, F(state.normalizedTime), F(animator.GetLayerWeight(upper)),
                    F(animator.GetLayerWeight(lower)), F(point.x), F(point.y), F(point.z), cast, abilityDamage,
                    animator.GetCurrentAnimatorStateInfo(0).shortNameHash, F(animator.GetCurrentAnimatorStateInfo(0).normalizedTime), deaths, F(Time.timeScale),
                    equipment != null && equipment.AnchorHeadVisible ? 1 : 0, vfx != null ? vfx.VisibleFlyingAnchors : 0,
                    F(hand.x), F(hand.y), F(hand.z)));
            }
        }

        private static void Sample(Transform body, string tipName, string directory)
        {
            GameObject copy = Instantiate(body.gameObject);
            foreach (Behaviour behaviour in copy.GetComponentsInChildren<Behaviour>()) behaviour.enabled = false;
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform tip = copy.GetComponentsInChildren<Transform>(true).First(t => t.name == tipName);
            Transform hips = copy.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:Hips");
            Transform left = copy.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:LeftToeBase");
            Transform right = copy.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:RightToeBase");
            try
            {
                using (var writer = new StreamWriter(Path.Combine(directory, "attack-clips.csv")))
                {
                    writer.WriteLine("clip,time,length,bladeX,bladeY,bladeZ,hipsYaw,leftX,leftY,leftZ,rightX,rightY,rightZ");
                    var clips = Resources.LoadAll<AnimationClip>("Characters/Pelag_v5/Mixamo/Pelag_MX_SaberCombo")
                        .Where(c => !c.name.StartsWith("__preview__")).ToList();
                    clips.AddRange(Resources.LoadAll<AnimationClip>("Characters/Pelag_v5/Mixamo/Pelag_MX_Whirlwind")
                        .Where(c => !c.name.StartsWith("__preview__")));
                    foreach (string name in new[] { "Pelag_Saber_A_Timed", "Pelag_Saber_B_Timed", "Pelag_Whirlwind_Timed" })
                    {
                        var clip = Resources.Load<AnimationClip>("Characters/Pelag_v5/" + name);
                        if (clip != null) clips.Add(clip);
                    }
                    foreach (AnimationClip clip in clips)
                    {
                        int frames = Mathf.CeilToInt(clip.length * 300f);
                        for (int i = 0; i <= frames; i++)
                        {
                            float t = clip.length * i / frames;
                            clip.SampleAnimation(copy, t);
                            Vector3 p = tip.position;
                            writer.WriteLine(string.Join(",", clip.name, F(t), F(clip.length), F(p.x), F(p.y), F(p.z), F(hips.eulerAngles.y),
                                F(left.position.x), F(left.position.y), F(left.position.z),
                                F(right.position.x), F(right.position.y), F(right.position.z)));
                        }
                    }
                }
            }
            finally { Destroy(copy); }
        }

        private void OnDestroy() => _writer?.Dispose();
        private void OnApplicationQuit() => _writer?.Dispose();
    }
}
