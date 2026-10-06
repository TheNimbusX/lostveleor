using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение «холодное железо» — малые знаки серии (махи 1–2 — небольшие, в той же железной
    /// семье): короткий стально-голубой росчерк у головы якоря в удар маха и пара обломков с
    /// искрами; искра железа на задетом теле (вместо всплеска пены), камешки и бурая пыль у ног
    /// сбитого (вместо короны пены); «Четвёртый удар» — круг «железа» вокруг героя без полосы.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private static readonly Color WiSteel = new Color(.45f, .68f, 1f);

        /// <summary>Удар маха: росчерк по касательной головы якоря, искры, 3 обломка.</summary>
        private void PlayWreckIronStreak(WreckArcRun run)
        {
            if (CaptureRig.NoVfx) return;
            Vector3 head;
            Vector3 tangent;
            if (run != null && run.HasHead)
            {
                head = run.Head;
                tangent = run.Velocity;
            }
            else
            {
                Vector3 hero = PlayerPosition();
                head = hero + PlayerFacing() * 2.2f + Vector3.up * .9f;
                tangent = Vector3.Cross(Vector3.up, PlayerFacing()) * (_wkSwingSide > 0 ? -12f : 12f);
            }
            float speed = tangent.magnitude;
            Vector3 along = speed > .5f ? tangent / speed : PlayerFacing();
            if (WiSpawn(PelagVfxId.WreckIronStreak, head, Quaternion.LookRotation(along, Vector3.up), 0f, 0f, out GameObject go) < 0) return;
            go.transform.localScale = Vector3.one;
            ParticleSystem streak = go.transform.Find("Streak")?.GetComponent<ParticleSystem>();
            ParticleSystem glints = go.transform.Find("Glints")?.GetComponent<ParticleSystem>();
            ParticleSystem chips = go.transform.Find("Chips")?.GetComponent<ParticleSystem>();
            float length = PelagWreckIronRules.StreakLength(speed);
            // Росчерк: ширина = размер, длина = 4 × размер (lengthScale префаба); медленно — только направление.
            WiEmit(streak, head - along * (length * .3f), along * .6f, WiSteel, length / 4f, .14f);
            WiEmit(streak, head - along * (length * .75f), along * .4f, WiSteel * .8f, length / 5.5f, .11f);
            for (int i = 0; i < 6; i++)
                WiEmit(glints, head, (along + Random.insideUnitSphere * .55f + Vector3.up * .2f).normalized * Random.Range(3f, 6.5f),
                    i % 2 == 0 ? WiHot : WiSky, Random.Range(.04f, .07f), Random.Range(.12f, .22f));
            for (int i = 0; i < 3; i++)
                WiEmit(chips, head, (along * 1.4f + Random.insideUnitSphere * .6f + Vector3.up * .6f) * Random.Range(1.6f, 2.8f),
                    i == 0 ? WiChipIron : WiChipStone, Random.Range(.05f, .09f), Random.Range(.35f, .5f));
        }

        /// <summary>Задетый «железом»: искра на теле по удару; сбитый — ещё камешки и пыль у ног.</summary>
        private void PlayWreckIronHit(in WkPending p, Vector3 body, Vector3 dir)
        {
            if (CaptureRig.NoVfx) return;
            Camera camera = Camera.main;
            Vector3 at = body + Vector3.up * .9f;
            if (camera != null) at += (camera.transform.position - at).normalized * .4f;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = FlatDirection(PlayerPosition(), body);
            float scale = p.Hit == WreckVfxHit.Circle ? 1.25f : p.Hit == WreckVfxHit.Wave ? .85f : 1f;
            WiSpawn(PelagVfxId.WreckIronHit, at, Quaternion.LookRotation((dir.normalized + Vector3.up * .35f).normalized, Vector3.up), scale, 0f, out _);
            if (!PelagWreckVfxRules.Knocks(p.Hit)) return;
            Simulation sim = _driver.Sim;
            float radius = sim != null && (uint)p.Target < (uint)sim.Entities.Count
                ? sim.Entities.BodyRadius[p.Target].ToFloat() : EntityStore.DefaultBodyRadius.ToFloat();
            var foot = new Vector3(body.x, WreckGroundAt(body) + .03f, body.z);
            WiSpawn(PelagVfxId.WreckIronKnock, foot, Quaternion.LookRotation(dir.normalized, Vector3.up),
                Mathf.Clamp(.7f + .7f * radius, .8f, 1.5f), 0f, out _);
        }

        /// <summary>«Четвёртый удар» (талант): круг «железа» вокруг героя — камни кромкой, трещины, без полосы и звеньев.</summary>
        private void PlayWreckIronFourth(Vector3 hero)
        {
            Simulation sim = _driver.Sim;
            WreckState w = sim != null ? sim.Wreck : default;
            AbilityBuild build = sim != null && IsWreckSlot(sim, w.Slot) ? sim.GetAbility(w.Slot) : null;
            float radius = build != null ? Mathf.Clamp(build.Get(AbilityStatType.Radius).ToFloat(), 1f, 2.6f) : 1.8f;
            Vector3 facing = PlayerFacing();
            var wave = new WkWave { Origin = hero, Dir = facing, Impact = hero, ImpactRadius = radius, Tick = _wkSwingTick };
            BeginWreckIron(_wkSwingTick, w.Serial * 8 + 3, hero, radius, wave, false);
            _juice?.PunchCamera(.26f, .05f);
        }
    }
}
