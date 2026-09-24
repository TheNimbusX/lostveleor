using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Раскадровка серпа Вихря по возрасту эффекта.
    ///
    /// Контроллер ведёт позицию, поворот за клинком и масштаб по радиусу
    /// способности; здесь живёт только время: кадр-вспышка, дорисовка головы,
    /// собственное вращение трёх дуг вокруг героя, эрозия хвостов и
    /// расходящееся кольцо на земле. Три дуги стоят на разной высоте и радиусе
    /// и отстают друг от друга по углу — так плоское кольцо становится
    /// воронкой, у которой есть объём под наклонной камерой. Все значения
    /// уходят в MaterialPropertyBlock — материал палитры общий на пул и не
    /// копируется.
    ///
    /// Корень повёрнут контроллером на X90: местная +Z смотрит вниз, к земле,
    /// поэтому высота дуги — это её local z, а вращение вокруг героя —
    /// поворот вокруг local Z.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PelagWhirlwindSweepView : MonoBehaviour
    {
        public Renderer Sweep;
        public Renderer Echo;
        public Renderer Wisp;
        public Renderer Ring;

        [Header("Серп")]
        [Tooltip("Длина плоского белого кадра, с. Два кадра при 60 fps.")]
        public float FlashSeconds = .045f;
        [Tooltip("Какая доля серпа уже нарисована сразу после вспышки.")]
        [Range(0f, 1f)] public float HeadStart = .55f;
        public float HeadSeconds = .07f;
        public float ErodeStart = .12f;
        public float ErodeSeconds = .24f;
        [Tooltip("Хлопок масштаба: серп вылетает чуть меньше и добирает радиус.")]
        public float PopStart = .86f;
        public float PopSeconds = .05f;

        [Header("Вращение дуг вокруг героя")]
        [Tooltip("Сколько градусов дуга проходит после контакта, замедляясь.")]
        public float SpinDegrees = 240f;
        public float SpinSeconds = .32f;
        [Tooltip("+1 или −1: по часовой стрелке сверху при −1 (как идёт сабля).")]
        public float SpinSign = -1f;

        [Header("Эхо и верхний завиток")]
        public float EchoLagDegrees = 150f;
        public float EchoRadius = .80f;
        [Tooltip("Насколько ниже клинка лежит эхо, м (положительно — ниже).")]
        public float EchoDrop = .38f;
        public float EchoDelay = .03f;
        public float WispLagDegrees = 275f;
        public float WispRadius = .58f;
        public float WispDrop = -.34f;
        public float WispDelay = .06f;

        [Header("Кольцо на земле, в долях радиуса способности")]
        public float RingStartRadius = .38f;
        public float RingEndRadius = 1.12f;
        public float RingSeconds = .22f;
        public float RingErodeStart = .11f;
        public float RingErodeSeconds = .17f;
        [Tooltip("На сколько метров ниже корня лежит кольцо. Корень стоит на высоте клинка.")]
        public float RingDrop = .86f;

        private static readonly int HeadId = Shader.PropertyToID("_Head");
        private static readonly int ErodeId = Shader.PropertyToID("_Erode");
        private static readonly int ErodeAlongId = Shader.PropertyToID("_ErodeAlong");
        private static readonly int PeriodicId = Shader.PropertyToID("_Periodic");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        private MaterialPropertyBlock _block;
        private float _seed;

        public void Begin()
        {
            _seed = Random.Range(0f, 64f);
            SetAge(0f);
        }

        private float Spin(float age)
        {
            float t = Mathf.Clamp01(age / Mathf.Max(.01f, SpinSeconds));
            return SpinSign * SpinDegrees * (1f - Mathf.Pow(1f - t, 2.4f));
        }

        public void SetAge(float age)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            float flash = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlashSeconds, FlashSeconds + .02f, age));
            float scale = Mathf.Max(.01f, transform.localScale.x);
            float pop = Mathf.Lerp(PopStart, 1f, 1f - Mathf.Pow(1f - Mathf.Clamp01(age / Mathf.Max(.01f, PopSeconds)), 2f));

            Arc(Sweep, age, 0f, 1f, 0f, 0f, flash, pop, 1f, scale);
            Arc(Echo, age, EchoDelay, EchoRadius, EchoLagDegrees, EchoDrop, flash, pop, .85f, scale);
            Arc(Wisp, age, WispDelay, WispRadius, WispLagDegrees, WispDrop, flash * .5f, pop, .7f, scale);

            if (Ring != null)
            {
                float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / RingSeconds), 2.6f);
                float radius = Mathf.Lerp(RingStartRadius, RingEndRadius, grow);
                float erode = 1.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RingErodeStart, RingErodeStart + RingErodeSeconds, age));
                Transform ring = Ring.transform;
                ring.localPosition = new Vector3(0f, 0f, RingDrop / scale);
                ring.localScale = Vector3.one * radius;
                _block.Clear();
                _block.SetFloat(HeadId, 1f);
                _block.SetFloat(ErodeId, erode);
                _block.SetFloat(ErodeAlongId, 0f);
                _block.SetFloat(PeriodicId, 1f);
                _block.SetFloat(FlashId, flash);
                _block.SetFloat(OpacityId, 1f);
                _block.SetFloat(SeedId, _seed + 7f);
                Ring.SetPropertyBlock(_block);
            }
        }

        private void Arc(Renderer arc, float age, float delay, float radius, float lagDegrees, float drop,
            float flash, float pop, float opacity, float scale)
        {
            if (arc == null) return;
            float local = age - delay;
            bool born = local >= 0f;
            float head = Mathf.Lerp(HeadStart, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, HeadSeconds, local)));
            float erode = 1.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ErodeStart, ErodeStart + ErodeSeconds, local));
            Transform t = arc.transform;
            t.localPosition = new Vector3(0f, 0f, drop / scale);
            t.localRotation = Quaternion.Euler(0f, 0f, Spin(Mathf.Max(0f, local)) + SpinSign * lagDegrees);
            t.localScale = Vector3.one * (radius * pop);
            _block.Clear();
            _block.SetFloat(HeadId, born ? head : 0f);
            _block.SetFloat(ErodeId, erode);
            _block.SetFloat(FlashId, born ? flash : 0f);
            _block.SetFloat(OpacityId, born ? opacity : 0f);
            _block.SetFloat(SeedId, _seed + lagDegrees * .01f);
            arc.SetPropertyBlock(_block);
        }
    }
}
