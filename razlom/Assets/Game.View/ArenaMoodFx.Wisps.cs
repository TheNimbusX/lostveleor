using UnityEngine;

namespace Game.View
{
    // Огоньки поляны Кости (голубые, LayoutView.Glade, BuildWisps) по настроению: в тумане бледнее, в сумерках
    // тёплые жёлтые и тише (план 3.6). Система его — меняем только цвет начала жизни и уже живые огоньки,
    // при откате возвращаем как было (до сборки следующей арены, которая их переиспользует).
    public sealed partial class ArenaMoodFx
    {
        private ParticleSystem _wisps;
        private ParticleSystem.MinMaxGradient _wispColourBase;
        private Color32 _wispBaseTint;
        private bool _wispsTinted;

        private bool TintWisps()
        {
            RestoreWisps();
            if (_layout == null) return false;
            ParticleSystem wisps = _layout.GladeWisps;
            if (wisps == null || !wisps.gameObject.activeInHierarchy) return false;
            var main = wisps.main;
            ParticleSystem.MinMaxGradient original = main.startColor;
            if (original.mode != ParticleSystemGradientMode.Color) return false;
            Color baseColour = original.color;
            Color target = _state.WispColor;
            target.a = Mathf.Clamp01(_state.WispAlpha) * baseColour.a;
            if (Approximately(baseColour, target)) return false;
            _wisps = wisps;
            _wispColourBase = original;
            _wispBaseTint = baseColour;
            main.startColor = target;
            Recolour(wisps, target);
            _wispsTinted = true;
            return true;
        }

        private void RestoreWisps()
        {
            if (!_wispsTinted) return;
            _wispsTinted = false;
            ParticleSystem wisps = _wisps;
            _wisps = null;
            if (wisps == null) return;
            var main = wisps.main;
            main.startColor = _wispColourBase;
            Recolour(wisps, _wispBaseTint);
        }

        /// <summary>Уже живые огоньки — тем же цветом: иначе голубые дожили бы своё в сумерках.</summary>
        private void Recolour(ParticleSystem system, Color32 colour)
        {
            if (_particleBuffer == null) _particleBuffer = new ParticleSystem.Particle[160];
            // Все живые разом или никак: SetParticles заменяет весь набор системы.
            if (system.particleCount > _particleBuffer.Length) return;
            int alive = system.GetParticles(_particleBuffer);
            for (int i = 0; i < alive; i++)
            {
                ParticleSystem.Particle particle = _particleBuffer[i];
                particle.startColor = colour;
                _particleBuffer[i] = particle;
            }
            if (alive > 0) system.SetParticles(_particleBuffer, alive);
        }

        private static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < .01f && Mathf.Abs(a.g - b.g) < .01f && Mathf.Abs(a.b - b.b) < .01f && Mathf.Abs(a.a - b.a) < .01f;
    }
}
