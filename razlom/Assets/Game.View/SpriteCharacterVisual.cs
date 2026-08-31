using UnityEngine;
using Game.Sim;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Покадровое 2.5D-представление из утверждённых PNG-поз. Здесь нет
    /// 3D-модели: каждый кадр — исходный рисунок художника на прозрачном фоне.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpriteCharacterVisual : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private SpriteRenderer _ghostRenderer;
        private Transform _directionMark;
        private Faction _faction;

        private Sprite[] _idle;
        private Sprite[] _move;
        private Sprite[] _attackA;
        private Sprite[] _attackB;
        private Sprite[] _ability;
        private Sprite[] _hitA;
        private Sprite[] _hitB;
        private Sprite[] _death;

        private Sprite[] _action;
        private float _actionStarted;
        private float _actionFrameTime;
        private bool _actionLocks;
        private bool _moving;
        private bool _dead;
        private float _locomotionClock;
        private float _actionEndsAt;
        private int _actionPriority;
        private float _lastHitAt = -100f;
        private float _hitKick;
        private float _flash;
        private float _ghostFade;
        private float _screenHorizontal = 1f;
        private float _deathStartedAt;
        private MaterialPropertyBlock _properties;
        private Vector3 _restLocalPosition;
        private Vector3 _restLocalScale;

        private static Texture2D _softDiscTexture;
        private static Texture2D _directionTexture;
        private static Material _shadowMaterial;
        private static Material _comicMaterial;
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        public bool Ready { get; private set; }

        public void Configure(Faction faction, SpriteRenderer spriteRenderer)
        {
            _faction = faction;
            _renderer = spriteRenderer;
            _properties = new MaterialPropertyBlock();
            _restLocalPosition = transform.localPosition;
            _restLocalScale = transform.localScale;
            CreateComicLayers();
            CreateGrounding(faction);

            if (faction == Faction.Wole)
            {
                _idle = Load("Pelag", "idle_0");
                _move = Load("Pelag", "run_0", "run_1");
                _attackA = Load("Pelag", "idle_1", "attack_0", "attack_1", "attack_0");
                _attackB = Load("Pelag", "idle_1", "attack_1", "attack_0", "idle_1");
                _ability = Load("Pelag", "attack_0", "hook_0", "hook_1", "hook_0");
                // Одна выразительная pose на один hit. Частые события больше не
                // перещёлкивают тело туда-сюда как в конвульсии.
                _hitA = Load("Pelag", "hit_0");
                _hitB = _hitA;
                _death = Load("Pelag", "knockback_0", "death_0");
            }
            else
            {
                _idle = Load("Orvill", "idle_0");
                _move = Load("Orvill", "walk_0", "idle_1");
                _attackA = Load("Orvill", "idle_1", "attack_0", "attack_1", "idle_1");
                _attackB = Load("Orvill", "block_0", "attack_0", "attack_1", "idle_0");
                _ability = _attackB;
                _hitA = Load("Orvill", "hit_0");
                _hitB = _hitA;
                _death = Load("Orvill", "death_0", "death_1");
            }

            Ready = _idle.Length > 0 && _idle[0] != null;
            if (!Ready)
                Debug.LogError($"[Разлом] Не загружены рисованные кадры для {faction}.", this);
            else
                SetSprite(_idle[0], false);
        }

        public void ResetForSpawn()
        {
            _dead = false;
            _moving = false;
            _action = null;
            _actionLocks = false;
            _actionPriority = 0;
            _actionEndsAt = 0f;
            _locomotionClock = 0f;
            _lastHitAt = -100f;
            _hitKick = 0f;
            _flash = 0f;
            _ghostFade = 0f;
            _deathStartedAt = 0f;
            _renderer.color = Color.white;
            _ghostRenderer.color = Color.clear;
            ApplyShaderProperties(0f);
            transform.localPosition = _restLocalPosition;
            transform.localScale = _restLocalScale;
            if (_idle.Length > 0) SetSprite(_idle[0], false);
        }

        public void SetMoving(bool moving)
        {
            if (_dead) return;
            _moving = moving;
        }

        public void PlayAttack(int variant)
            => BeginAction((variant & 1) == 0 ? _attackA : _attackB, 0.12f, false, 1);

        public void PlayAbility()
            => BeginAction(_ability, 0.16f, false, 1);

        public void PlayHit(int variant)
        {
            if (_dead || Time.time - _lastHitAt < 0.42f) return;
            _lastHitAt = Time.time;
            BeginAction((variant & 1) == 0 ? _hitA : _hitB, 0.26f, false, 2);
            _hitKick = _renderer.flipX ? -1f : 1f;
            _flash = 1f;
        }

        public void PlayDeath()
        {
            if (_dead) return;
            _dead = true;
            _deathStartedAt = Time.unscaledTime;
            BeginAction(_death, 0.42f, true, 3);
        }

        public void FaceCamera(Vector3 facing)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            float horizontal = Vector3.Dot(facing, camera.transform.right);
            if (Mathf.Abs(horizontal) > 0.10f)
            {
                _screenHorizontal = horizontal;
                _renderer.flipX = horizontal < 0f;
                _ghostRenderer.flipX = _renderer.flipX;
            }

            float hitLean = _hitKick * 8f;
            float runLean = _moving && _actionPriority == 0 ? -Mathf.Clamp(_screenHorizontal, -1f, 1f) * 5f : 0f;
            // Cylindrical billboard: рисунок остаётся вертикальным в мире и
            // ощущается как 2.5D cutout, а не приклеенная к монитору карточка.
            Vector3 toCamera = camera.transform.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up)
                    * Quaternion.Euler(0f, 0f, hitLean + runLean);

            Vector3 planarFacing = new Vector3(facing.x, 0f, facing.z);
            if (planarFacing.sqrMagnitude > 0.001f && _directionMark != null)
            {
                planarFacing.Normalize();
                _directionMark.localPosition = planarFacing * 0.48f + Vector3.up * 0.035f;
                _directionMark.rotation = Quaternion.LookRotation(planarFacing, Vector3.up)
                    * Quaternion.Euler(90f, 0f, 0f);
            }

            Vector3 viewport = camera.WorldToViewportPoint(transform.parent.position);
            int order = 100 + Mathf.RoundToInt((1f - viewport.y) * 1000f);
            _renderer.sortingOrder = order;
            _ghostRenderer.sortingOrder = order - 1;
        }

        private void Update()
        {
            if (!Ready) return;
            if (_idle == null || _idle.Length == 0 || _renderer == null || _ghostRenderer == null)
            {
                Ready = false;
                return;
            }

            // Hot reload can restore an action reference without its frame
            // data. Drop that stale action instead of indexing frame -1.
            if (_action != null && (_action.Length == 0 || _actionFrameTime <= 0f))
            {
                _action = null;
                _actionLocks = false;
                _actionPriority = 0;
                _actionEndsAt = 0f;
            }

            _locomotionClock += Time.deltaTime;

            if (_action != null)
            {
                float elapsed = Time.time - _actionStarted;
                int frame = Mathf.Min(_action.Length - 1, Mathf.FloorToInt(elapsed / _actionFrameTime));
                SetSprite(_action[frame]);

                if (!_actionLocks && Time.time >= _actionEndsAt)
                {
                    _action = null;
                    _actionPriority = 0;
                }
            }

            if (_action == null)
            {
                Sprite[] loop = _moving && _move != null && _move.Length > 0 ? _move : _idle;
                // Две разные иллюстрации не должны мигать как flipbook. Более
                // спокойный темп плюс короткий comic-smear воспринимаются плавно.
                float fps = _moving ? 3.8f : 0.85f;
                int frame = loop.Length == 1 ? 0 : Mathf.FloorToInt(_locomotionClock * fps) % loop.Length;
                SetSprite(loop[frame]);
            }

            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 5.5f);
            float dissolve = _dead
                ? Mathf.Clamp01((Time.unscaledTime - _deathStartedAt - 0.62f) / 0.72f)
                : 0f;
            ApplyShaderProperties(dissolve);

            _ghostFade = Mathf.MoveTowards(_ghostFade, 0f, Time.deltaTime / 0.10f);
            _ghostRenderer.color = new Color(1f, 0.90f, 0.72f, _ghostFade * 0.14f);
            _hitKick = Mathf.MoveTowards(_hitKick, 0f, Time.deltaTime * 5f);

            if (_action == null && !_moving && !_dead)
            {
                float breath = Mathf.Sin(_locomotionClock * 3.2f);
                transform.localPosition = _restLocalPosition + Vector3.up * (breath * 0.009f);
                transform.localScale = new Vector3(
                    _restLocalScale.x * (1f - breath * 0.003f),
                    _restLocalScale.y * (1f + breath * 0.005f),
                    _restLocalScale.z);
            }
            else
            {
                transform.localPosition = Vector3.Lerp(transform.localPosition, _restLocalPosition, 18f * Time.deltaTime);
                transform.localScale = Vector3.Lerp(transform.localScale, _restLocalScale, 18f * Time.deltaTime);
            }
        }

        private void BeginAction(Sprite[] frames, float frameTime, bool locks, int priority)
        {
            if (_dead && !locks) return;
            if (frames == null || frames.Length == 0) return;
            if (_action != null && Time.time < _actionEndsAt && priority < _actionPriority) return;
            _action = frames;
            _actionStarted = Time.time;
            _actionFrameTime = frameTime;
            _actionLocks = locks;
            _actionPriority = priority;
            _actionEndsAt = Time.time + frames.Length * frameTime;
            SetSprite(frames[0]);
        }

        private void SetSprite(Sprite sprite, bool smear = true)
        {
            if (sprite == null || _renderer.sprite == sprite) return;
            if (smear && _renderer.sprite != null)
            {
                _ghostRenderer.sprite = _renderer.sprite;
                _ghostRenderer.flipX = _renderer.flipX;
                _ghostFade = 1f;
            }

            _renderer.sprite = sprite;
        }

        private void CreateComicLayers()
        {
            GameObject ghost = new GameObject("Pose Smear");
            ghost.transform.SetParent(transform, false);
            _ghostRenderer = ghost.AddComponent<SpriteRenderer>();
            _ghostRenderer.color = Color.clear;

            Material comic = GetComicMaterial();
            if (comic != null)
            {
                _renderer.sharedMaterial = comic;
                _ghostRenderer.sharedMaterial = comic;
            }
        }

        private void CreateGrounding(Faction faction)
        {
            Transform root = transform.parent;

            GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadow.name = "Soft Contact Shadow";
            shadow.transform.SetParent(root, false);
            shadow.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localScale = new Vector3(1.30f, 0.52f, 1f);
            Destroy(shadow.GetComponent<Collider>());
            shadow.GetComponent<MeshRenderer>().sharedMaterial = GetShadowMaterial();

            // Один ясный маркер нужен герою; сорок стрелок под мобами только
            // превращают бой в визуальный шум.
            if (faction == Faction.Wole)
            {
                GameObject direction = GameObject.CreatePrimitive(PrimitiveType.Quad);
                direction.name = "Player Facing Ink Mark";
                direction.transform.SetParent(root, false);
                direction.transform.localScale = new Vector3(0.42f, 0.92f, 1f);
                Destroy(direction.GetComponent<Collider>());
                Color accent = new Color(0.16f, 0.88f, 0.92f, 0.70f);
                direction.GetComponent<MeshRenderer>().sharedMaterial = CreateGroundMaterial(GetDirectionTexture(), accent);
                _directionMark = direction.transform;
            }
        }

        private void ApplyShaderProperties(float dissolve)
        {
            if (_properties == null) return;
            _renderer.GetPropertyBlock(_properties);
            _properties.SetFloat(FlashId, _flash);
            _properties.SetFloat(DissolveId, dissolve);
            _renderer.SetPropertyBlock(_properties);
        }

        private static Material GetComicMaterial()
        {
            if (_comicMaterial != null) return _comicMaterial;
            Shader shader = Resources.Load<Shader>("Shaders/RazlomComicSprite");
            if (shader == null) shader = Shader.Find("Razlom/ComicSprite");
            if (shader == null) return null;
            _comicMaterial = new Material(shader) { name = "Runtime Razlom Comic Sprite" };
            return _comicMaterial;
        }

        private static Material GetShadowMaterial()
        {
            if (_shadowMaterial == null)
                _shadowMaterial = CreateGroundMaterial(GetSoftDiscTexture(), new Color(0.02f, 0.03f, 0.05f, 0.58f));
            return _shadowMaterial;
        }

        private static Material CreateGroundMaterial(Texture2D texture, Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            Material material = new Material(shader)
            {
                name = "Runtime Comic Ground FX",
                mainTexture = texture,
                color = color,
                renderQueue = (int)RenderQueue.Transparent
            };
            return material;
        }

        private static Texture2D GetSoftDiscTexture()
        {
            if (_softDiscTexture != null) return _softDiscTexture;
            _softDiscTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Soft Contact Shadow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x + 0.5f) / 32f - 1f;
                float dy = (y + 0.5f) / 32f - 1f;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.8f);
                _softDiscTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            _softDiscTexture.Apply(false, true);
            return _softDiscTexture;
        }

        private static Texture2D GetDirectionTexture()
        {
            if (_directionTexture != null) return _directionTexture;
            _directionTexture = new Texture2D(32, 64, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Facing Ink Mark",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 32; x++)
            {
                float v = y / 63f;
                float u = Mathf.Abs((x + 0.5f) / 16f - 1f);
                float halfWidth = Mathf.Lerp(0.12f, 0.92f, 1f - v);
                float edge = 1f - Mathf.SmoothStep(halfWidth - 0.12f, halfWidth, u);
                float alpha = edge * Mathf.SmoothStep(0f, 0.22f, v) * (1f - Mathf.SmoothStep(0.78f, 1f, v));
                _directionTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            _directionTexture.Apply(false, true);
            return _directionTexture;
        }

        private static Sprite[] Load(string character, params string[] names)
        {
            Sprite[] result = new Sprite[names.Length];
            for (int i = 0; i < names.Length; i++)
                result[i] = Resources.Load<Sprite>($"CharacterSprites/{character}/{names[i]}");
            return result;
        }
    }
}
