using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Game.View
{
    // AudioRenderer шагает вместе с покадровой съёмкой, поэтому медленная
    // запись PNG не растягивает аудио относительно игрового видео.
    public sealed class CombatAudioCapture : IDisposable
    {
        public static bool Recording { get; private set; }
        private readonly BinaryWriter _writer;
        private readonly int _rate, _channels, _fps;
        private long _renderedFrames;
        private NativeArray<float> _samples;
        private long _sampleCount, _clipped;
        private double _energy;
        private float _peak;
        private bool _disposed;

        public CombatAudioCapture(string folder)
        {
            if (Time.captureFramerate <= 0)
                throw new InvalidOperationException("Audio capture requires fixed captureFramerate.");
            _rate = AudioSettings.outputSampleRate;
            _fps = Time.captureFramerate;
            Debug.Log($"[capture-audio] listener={AudioListener.volume} paused={AudioListener.pause} effects={GameUserSettings.EffectsVolume} sources={UnityEngine.Object.FindObjectsByType<AudioSource>().Length}");
            // Capture fixtures use a repeatable mix without writing user preferences.
            GameUserSettings.SetAudio(1f, 1f, GameUserSettings.MusicVolume);
            AudioListener.pause = false;
            switch (AudioSettings.speakerMode)
            {
                case AudioSpeakerMode.Mono: _channels = 1; break;
                case AudioSpeakerMode.Quad: _channels = 4; break;
                case AudioSpeakerMode.Surround: _channels = 5; break;
                case AudioSpeakerMode.Mode5point1: _channels = 6; break;
                case AudioSpeakerMode.Mode7point1: _channels = 8; break;
                default: _channels = 2; break;
            }
            if (!AudioRenderer.Start()) throw new InvalidOperationException("AudioRenderer is already recording.");
            Recording = true;
            try
            {
                _writer = new BinaryWriter(File.Create(Path.Combine(folder, "game-audio.wav")));
                WriteHeader(0);
            }
            catch { AudioRenderer.Stop(); throw; }
        }

        public void Frame(bool save)
        {
            // В standalone GetSampleCountForCaptureFrame на первом кадре даёт
            // ноль. Render принимает размер запроса: считаем его по шкале видео,
            // сохраняя остаток деления для частот, не кратных sample rate.
            long end = (_renderedFrames + 1) * _rate / _fps;
            long start = _renderedFrames * _rate / _fps;
            int length = checked((int)(end - start) * _channels);
            int available = AudioRenderer.GetSampleCountForCaptureFrame();
            if (_renderedFrames < 3) Debug.Log($"[capture-audio-frame] available={available} requested={length} dsp={AudioSettings.dspTime}");
            if (!_samples.IsCreated || _samples.Length != length)
            {
                if (_samples.IsCreated) _samples.Dispose();
                _samples = new NativeArray<float>(length, Allocator.Persistent);
            }
            if (!AudioRenderer.Render(_samples)) throw new InvalidOperationException("AudioRenderer.Render failed.");
            _renderedFrames++;
            if (!save) return;
            for (int i = 0; i < length; i++)
            {
                float value = _samples[i];
                _peak = Mathf.Max(_peak, Mathf.Abs(value));
                _energy += value * value;
                if (Mathf.Abs(value) >= 1f) _clipped++;
                _writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * short.MaxValue));
            }
            _sampleCount += length;
        }

        private void WriteHeader(int bytes)
        {
            _writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); _writer.Write(bytes + 36);
            _writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); _writer.Write(16);
            _writer.Write((short)1); _writer.Write((short)_channels); _writer.Write(_rate);
            _writer.Write(_rate * _channels * 2); _writer.Write((short)(_channels * 2)); _writer.Write((short)16);
            _writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); _writer.Write(bytes);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AudioRenderer.Stop();
            Recording = false;
            if (_samples.IsCreated) _samples.Dispose();
            _writer.Seek(0, SeekOrigin.Begin);
            WriteHeader(checked((int)(_sampleCount * 2)));
            _writer.Dispose();
            Debug.Log(FormattableString.Invariant($"[capture-audio] samples={_sampleCount} channels={_channels} rate={_rate} peak={_peak:F4} rms={Math.Sqrt(_energy / Math.Max(1, _sampleCount)):F4} clipped={_clipped}"));
        }
    }
}
