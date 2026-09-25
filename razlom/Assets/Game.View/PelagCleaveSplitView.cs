using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Раскол» Рассекающего удара: серп на контакте и линия раскола на земле.
    ///
    /// Целевой кадр 24.09: на контакте серп во весь экран целиком, два кадра
    /// плоско-белый, затем живёт: хлопок масштаба, дрейф вниз по ходу удара,
    /// доворот, бегущие линии скорости и ползущая граница эрозии от хвоста
    /// (сверху) к острию у земли, за ним внутренний штрих и отстающее эхо;
    /// уходит по прозрачности, а не обрывом. На земле от ног героя вперёд
    /// выстреливает линия раскола с веером ответвлений и кольцом ударной
    /// волны, держится и стирается от хвоста.
    ///
    /// Каждый слой — система частиц с одной мешевой частицей (сборка —
    /// PelagCleaveVfxSetup): хлопок, дрейф и доворот — кривые модулей, а
    /// вспышка, эрозия, поток и затухание — в шейдере серпа Вихря по возрасту
    /// частицы из вершинного потока (_Timed/_LifeSeconds). В кадре ничего не
    /// пишется; здесь остаётся только то, что зависит от каста: длина линии
    /// раскола (досягаемость и таланты) — размер и положение частиц ставятся
    /// один раз перед Play. В сборке плеера эти слои пока не рисуются
    /// (LOG 24–25.09: тридцать сборок, причина не найдена; в редакторе всё
    /// видно) — открытый пункт, устройство эффекта здесь ни при чём.
    /// Серп и линия живут в разных префабах: либо Arc/Inner/Echo, либо
    /// Crack/Forks/Ring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PelagCleaveSplitView : MonoBehaviour
    {
        [Header("Серп (билборд, полумеш пака; u=0 — хвост наверху)")]
        public ParticleSystem Arc;
        public ParticleSystem Inner;
        public ParticleSystem Echo;

        [System.Serializable]
        public struct Fork
        {
            public ParticleSystem Line;
            [Tooltip("Точка ветвления вдоль главной линии, доля её длины.")] public float At;
            [Tooltip("Угол от главной линии, град (вокруг местной +Z = вверх).")] public float Angle;
            [Tooltip("Длина в долях главной линии.")] public float Length;
            [Tooltip("Ширина в долях главной линии.")] public float Width;
        }

        [Header("Трещины (на земле, u вдоль удара, длина 1 по X): главная и ответвления от неё")]
        public ParticleSystem Crack;
        [Tooltip("Ответвления: каждое растёт из своей точки главной линии и сужается к концу — не «крест» из точки удара.")]
        public Fork[] Forks;

        [Header("Ударная волна по земле: тонкое жёсткое кольцо от точки удара")]
        public ParticleSystem Ring;
        [Tooltip("Положение кольца вдоль главной линии, доля её длины.")]
        public float RingAt = .25f;

        [Header("Разброс веера по касту: угол ±град и длина ±доля, чтобы веер не был чертежом")]
        public float ForkAngleJitter = 7f;
        public float ForkLengthJitter = .15f;

        private ParticleSystem[] _all;

        private void Awake()
        {
            _all = GetComponentsInChildren<ParticleSystem>(true);
        }

        /// <param name="length">Длина линии раскола, м; для серпа не используется.</param>
        public void Begin(float length)
        {
            length = Mathf.Max(.1f, length);
            if (Crack != null)
            {
                Crack.transform.localPosition = Vector3.zero;
                Crack.transform.localRotation = Quaternion.identity;
                Size(Crack, length, 1f);
            }
            if (Forks != null)
                for (int i = 0; i < Forks.Length; i++)
                {
                    Fork fork = Forks[i];
                    if (fork.Line == null) continue;
                    float angle = fork.Angle + Random.Range(-ForkAngleJitter, ForkAngleJitter);
                    float reach = fork.Length * (1f + Random.Range(-ForkLengthJitter, ForkLengthJitter));
                    fork.Line.transform.localPosition = new Vector3(fork.At * length, 0f, .005f);
                    fork.Line.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                    Size(fork.Line, Mathf.Max(.05f, reach * length), fork.Width);
                }
            if (Ring != null) Ring.transform.localPosition = new Vector3(RingAt * length, 0f, .003f);
            if (_all == null) _all = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particles in _all)
            {
                particles.Clear(true);
                particles.Play(true);
            }
        }

        /// <summary>Возврат в пул: всё гаснет.</summary>
        public void End()
        {
            if (_all == null) return;
            foreach (var particles in _all) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>Раскадровка идёт внутри частиц и шейдера; в кадре здесь нечего делать.</summary>
        public void SetAge(float age) { }

        private static void Size(ParticleSystem particles, float x, float y)
        {
            var main = particles.main;
            main.startSize3D = true;
            main.startSizeX = x;
            main.startSizeY = y;
            main.startSizeZ = 1f;
        }
    }
}
