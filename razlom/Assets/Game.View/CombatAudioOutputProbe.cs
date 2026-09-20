using System;
using UnityEngine;

namespace Game.View
{
    // Checks the actual playback mixer without enabling offline AudioRenderer.
    public sealed class CombatAudioOutputProbe : MonoBehaviour
    {
        readonly float[] _samples = new float[2048];
        float _peak;
        double _energy, _startDsp;
        long _count;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args,"-razlom-capture") >= 0 && Array.IndexOf(args,"-capture-audio-probe") >= 0)
                new GameObject("Capture audio output probe").AddComponent<CombatAudioOutputProbe>();
        }
        void Start() => _startDsp = AudioSettings.dspTime;
        void LateUpdate()
        {
            AudioListener.GetOutputData(_samples,0);
            foreach (float sample in _samples) { _peak = Mathf.Max(_peak,Mathf.Abs(sample)); _energy += sample*sample; }
            _count += _samples.Length;
        }
        void OnDestroy()
        {
            bool pass = _peak > .0001f && AudioSettings.dspTime > _startDsp;
            string report = FormattableString.Invariant($"[live-audio] pass={pass} peak={_peak:F6} rms={Math.Sqrt(_energy/Math.Max(1,_count)):F6} dspElapsed={AudioSettings.dspTime-_startDsp:F3}");
            if (pass) Debug.Log(report); else Debug.LogError(report);
        }
    }
}
