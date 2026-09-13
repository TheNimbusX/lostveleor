using UnityEngine;
namespace Game.View
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Лёгкий дым трубы")]
    public sealed class CampChimneySmoke : MonoBehaviour
    {
        public ParticleSystem Smoke;
        public void Preview(float step)
        {
            if(Application.isPlaying || Smoke==null)return;
            Smoke.Simulate(step,false,false,true);
        }
        public void StopPreview()
        {
            if(!Application.isPlaying && Smoke!=null)Smoke.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
