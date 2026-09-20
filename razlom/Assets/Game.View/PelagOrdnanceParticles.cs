using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    public sealed partial class PelagOrdnanceView
    {
        private ParticleSystem _embers, _glass;
        private Mesh _glassMesh;
        private Material _glassMaterial;

        private void PrepareFireParticles()
        {
            _embers = MakeParticles("Bottle embers", "M_ArcadiaEmber", 96, false);
            _glass = MakeParticles("Bottle fragments", "M_ArcadiaEmber", 64, true);
        }

        private ParticleSystem MakeParticles(string name, string material, int count, bool glass)
        {
            var obj = new GameObject(name); obj.transform.SetParent(transform, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false;
            main.maxParticles = count; main.startSpeed = 0; main.startLifetime = .5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.gravityModifier = glass ? 1 : 0;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                new Keyframe(0, .5f), new Keyframe(.12f, 1), new Keyframe(.5f, .8f), new Keyframe(1, 0)));
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1.6f,1.2f,.65f),0),
                new GradientColorKey(new Color(1,.38f,.045f),.35f), new GradientColorKey(new Color(.4f,.08f,.015f),1) },
                new[] { new GradientAlphaKey(.9f,0), new GradientAlphaKey(.85f,.45f), new GradientAlphaKey(0,1) });
            color.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("VFX/Pelag/BlazeFire/" + material);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (glass)
            {
                _glassMesh = new Mesh { name = "Bottle shard tetrahedron" };
                _glassMesh.vertices = new[] { new Vector3(-.7f,0,0),new Vector3(.8f,0,0),new Vector3(0,1,0),new Vector3(0,.2f,.25f) };
                _glassMesh.triangles = new[] {0,2,1,0,1,3,1,2,3,2,0,3}; _glassMesh.RecalculateNormals();
                renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = _glassMesh;
                _glassMaterial = new Material(Shader.Find("Razlom/Forest Fruit Debris"));
                renderer.sharedMaterial = _glassMaterial;
            }
            ps.useAutoRandomSeed = false; ps.randomSeed = 379;
            return ps;
        }

        private void EmitExplosion(Vector3 position, float radius, int tick)
        {
            _embers.Play(); _glass.Play();
            for (int i=0;i<12;i++)
            {
                float a=i*2.39996f+tick*.13f;
                Vector3 velocity=new Vector3(Mathf.Cos(a)*2.4f,1.1f+(i%4)*.3f,Mathf.Sin(a)*2.4f);
                _embers.Emit(new ParticleSystem.EmitParams {position=position+Vector3.up*.14f,
                    velocity=velocity, startSize=.028f, startLifetime=.4f+(i%3)*.08f},1);
                if(i<6) _glass.Emit(new ParticleSystem.EmitParams {position=position+Vector3.up*.13f,
                    velocity=velocity*.7f, startSize=.06f+(i%3)*.02f, startLifetime=.5f,
                    rotation3D=new Vector3(a*57.3f,i*35,47),startColor=new Color(.16f,.32f,.25f,1)},1);
            }
        }

        private void ClearFireParticles()
        {
            if(_embers!=null)_embers.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(_glass!=null)_glass.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
