using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Мягкое внимание поверх существующей анимации NPC.
    /// Звук работы вызывается AnimationEvent настоящего контакта, а не таймером ожидания.
    /// Компонент стоит на объекте Animator; точка входа события — CampWorkContact(string).
    /// </summary>
    [DefaultExecutionOrder(80)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Внимание и контакты NPC")]
    public sealed class CampNpcLife : MonoBehaviour
    {
        public CampServiceNpc Npc;
        [Min(.1f)] public float AttentionNear = 2.5f, AttentionFar = 4.5f;
        [Range(0, 30)] public float MaximumHeadTurn = 18;
        [Range(0, 1)] public float WorkGain = .35f;
        [Min(0)] public float WorkNear = 2, WorkFar = 12;
        [Tooltip("Необязательный функциональный якорь контакта: наковальня, стол, котёл.")]
        public Transform WorkAnchor;
        Animator _animator;
        Transform _head;
        Quaternion _animatedHead;
        bool _applied, _wasNear, _greeted;
        int _greetingGeneration = -1;
        float _attention, _nod = 2, _nextContact, _soundGain;
        AudioSource _source;
        TickDriver _driver;
        Camera _camera;
        static Dictionary<string, AudioClip[]> _clips;
        readonly Dictionary<string, AudioClip> _last = new Dictionary<string, AudioClip>();

        public static void Install(Transform campRoot)
        {
            if (!Application.isPlaying || campRoot == null) return;
            // Авторские NPC сейчас лежат отдельными корнями сцены, вне CampRoot.
            foreach (var npc in UnityEngine.Object.FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
            {
                if (npc.Kind != CampServiceKind.Smith && npc.Kind != CampServiceKind.Trader
                    && npc.Kind != CampServiceKind.Alchemist) continue;
                var animator = npc.GetComponentInChildren<Animator>(true);
                if (animator == null) continue;
                var life = animator.GetComponent<CampNpcLife>() ?? animator.gameObject.AddComponent<CampNpcLife>();
                life.Npc = npc;
            }
        }

        void Awake()
        {
            _animator = GetComponent<Animator>();
            _driver = FindAnyObjectByType<TickDriver>(); _camera = Camera.main;
            if (_animator != null && _animator.isHuman) _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (_head == null)
                foreach (Transform bone in GetComponentsInChildren<Transform>())
                    if (bone.name.Equals("Head", StringComparison.OrdinalIgnoreCase)
                        || bone.name.EndsWith(":Head", StringComparison.OrdinalIgnoreCase)) { _head = bone; break; }
            var host = new GameObject("Контакты работы NPC"); host.transform.SetParent(transform, false);
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false; _source.spatialBlend = 0; _source.dopplerLevel = 0;
        }

        bool Audible => CampPlayerView.Instance != null && CampPlayerView.Instance.Active
            && !MainMenuView.IsOpen && !CampTransition.LeavingCamp;

        void Update()
        {
            // Восстановление до расчёта Animator, в том числе у скрытого отсечением рига.
            RestoreHead();
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            var player = CampPlayerView.Instance;
            bool paused = _driver != null && _driver.GameplayPaused;
            if (player != null && player.Active && _driver != null
                && _greetingGeneration != _driver.Generation)
            {
                _greetingGeneration = _driver.Generation;
                _greeted = false; _wasNear = false; _nod = 2;
            }
            float wanted = 0;
            if (Audible && !paused && player != null && Npc != null)
            {
                Vector3 delta = player.Position - Npc.transform.position; delta.y = 0;
                // Стоящее тело не разворачивается вслед за человеком за спиной.
                float facing = delta.sqrMagnitude > .01f ? Vector3.Dot(Npc.transform.forward, delta.normalized) : 1;
                if (facing > -.25f) wanted = CampAudioSpatial.Attenuation(player.Position,
                    Npc.transform.position, AttentionNear, AttentionFar);
            }
            bool near = wanted > .45f;
            if (near && !_wasNear && !_greeted) { _nod = 0; _greeted = true; }
            _wasNear = near; _nod += paused ? 0 : dt;
            _attention = Mathf.Lerp(_attention, wanted, 1 - Mathf.Exp(-dt * 3));
            if (player != null && Npc != null)
            {
                Vector3 at = WorkAnchor != null ? WorkAnchor.position : Npc.transform.position;
                float duck = CampAudioSpatial.PanelOpen || paused ? .55f : 1;
                float target = Audible ? WorkGain * GameUserSettings.EffectsVolume * duck
                    * CampAudioSpatial.Attenuation(player.Position, at, WorkNear, WorkFar) : 0;
                _soundGain = Mathf.Lerp(_soundGain, target, 1 - Mathf.Exp(-dt * 7));
                _source.volume = _soundGain;
                if (_camera == null) _camera = Camera.main;
                _source.panStereo = CampAudioSpatial.Pan(player.Position, at, _camera);
            }
        }

        void LateUpdate()
        {
            if (_head == null || _head.parent == null || Npc == null || _attention < .001f) return;
            var player = CampPlayerView.Instance; if (player == null) return;
            Vector3 direction = player.Position - Npc.transform.position; direction.y = 0;
            if (direction.sqrMagnitude < .01f) return;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(Npc.transform.forward, direction, Vector3.up),
                -MaximumHeadTurn, MaximumHeadTurn) * _attention;
            float nod = _nod < 1.1f ? Mathf.Sin(_nod / 1.1f * Mathf.PI) * 2.5f * _attention : 0;
            _animatedHead = _head.localRotation;
            Quaternion parent = _head.parent.rotation;
            Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up)
                * Quaternion.AngleAxis(nod, Npc.transform.right);
            _head.localRotation = Quaternion.Inverse(parent) * turn * parent * _animatedHead;
            _applied = true;
        }

        void RestoreHead()
        {
            if (_applied && _head != null) _head.localRotation = _animatedHead;
            _applied = false;
        }

        /// <summary>
        /// Событие ставится в настоящий видимый контакт. cue: hammer, count,
        /// coins, bottle, pour, cork, stir или cloth. Пустое значение выбирает звук NPC.
        /// В текущих клипах Idle/Talking таких событий нет.
        /// </summary>
        public void CampWorkContact(string cue)
        {
            if (!Audible || Npc == null || _source == null || Time.unscaledTime < _nextContact
                || CampAudioSpatial.PanelOpen || _driver != null && _driver.GameplayPaused) return;
            if (string.IsNullOrEmpty(cue)) cue = Npc.Kind == CampServiceKind.Smith ? "hammer"
                : Npc.Kind == CampServiceKind.Trader ? "count" : "bottle";
            string prefix;
            switch (cue)
            {
                case "hammer": prefix = "smith_hammer"; break;
                case "count": prefix = "trader_count"; break;
                case "coins": prefix = "trader_coins_table"; break;
                case "bottle": prefix = "alch_bottle"; break;
                case "pour": prefix = "alch_pour"; break;
                case "cork": prefix = "alch_cork"; break;
                case "stir": prefix = "alch_clink"; break;
                case "cloth": prefix = "tent_cloth"; break;
                default: return;
            }
            if (_clips == null)
            {
                _clips = new Dictionary<string, AudioClip[]>();
                var all = Resources.LoadAll<AudioClip>("Audio/Game/Prepared");
                foreach (string key in new[] { "smith_hammer", "trader_count", "trader_coins_table", "alch_bottle",
                    "alch_pour", "alch_cork", "alch_clink", "tent_cloth" })
                    _clips[key] = Array.FindAll(all, clip => clip != null && (clip.name == key
                        || clip.name.StartsWith(key + "_", StringComparison.Ordinal)));
            }
            if (!_clips.TryGetValue(prefix, out var clips) || clips.Length == 0) return;
            int index = UnityEngine.Random.Range(0, clips.Length);
            if (clips.Length > 1 && _last.TryGetValue(prefix, out var last) && clips[index] == last)
                index = (index + 1) % clips.Length;
            _last[prefix] = clips[index]; _nextContact = Time.unscaledTime + .12f;
            _source.pitch = UnityEngine.Random.Range(.98f, 1.02f);
            _source.PlayOneShot(clips[index]);
        }

        void OnDisable()
        {
            RestoreHead(); _attention = 0; _wasNear = false;
            if (_source != null) { _source.Stop(); _source.volume = 0; }
            _soundGain = 0;
        }
    }
}
