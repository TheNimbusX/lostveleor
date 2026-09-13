using UnityEngine;

namespace Game.View
{
    // Геометрия и частицы сохранены в сцене; компонент нужен только для предпросмотра.
    public sealed class CampMicrobiome : MonoBehaviour
    {
        public bool PreviewParticles = true;
        public ParticleSystem[] Particles;
        public void Preview(float delta)
        {
            if(!PreviewParticles || Particles==null)return;
            foreach(var particles in Particles)if(particles!=null)particles.Simulate(delta,false,false,false);
        }
    }
}
