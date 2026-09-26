using Game.Sim;
using TMPro;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ЗАГЛУШКА НОВОГО МОБА ЛЕСА (план мобов от 26.09): Шипомёт, Корнехват, Расщепень
    /// и его детёныш, пока у них нет утверждённой модели в Resources/Characters.
    ///
    /// Серая капсула по радиусу тела из Sim, нос вперёд и подпись «[заглушка] …» над
    /// головой. Никакого чужого меша: вид, у которого нет своего тела, раньше молча
    /// рисовался Хранителем, и такой бой нельзя было ни проверить, ни показать.
    /// Заглушка живёт только в редакторе и dev-сборке (съёмка); в релизе без префаба
    /// ArenaView пишет ошибку и тела не даёт вовсе (<see cref="Allowed"/>).
    ///
    /// ПОЗА ИЗ ФАЗ SIM, а не из клипов. Замах Расщепеня и его детёныша — общий
    /// замах (TryGetEnemySwing); Шипомёт и Корнехват — события EnemyAction* и тики
    /// их действий (Simulation.ThornLine*, RootSnarer*). Подготовка — отклон назад
    /// и приседание, контакт — рывок вперёд, наказание — наклонённая стойка, которая
    /// к концу восстановления выпрямляется; каждый шип или удар корнями — толчок.
    /// Часы — часы Sim (тик − 1 + Alpha): пауза и хит-стоп держат позу сами.
    ///
    /// Всё, что двигает поза, висит на дочернем «Поза»: корень принадлежит ArenaView
    /// (позиция, поворот, базовый масштаб), и трогать его здесь нельзя.
    /// </summary>
    [DefaultExecutionOrder(640)]
    public sealed class ForestMobPlaceholderView : MonoBehaviour
    {
        /// <summary>Имя подписи: ArenaView не берёт её в тело (вспышка, растворение, контур).</summary>
        public const string LabelName = "Подпись заглушки";

        /// <summary>Заглушка разрешена: редактор и dev-сборка (съёмка собирается Development).</summary>
        public static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        // Рост капсулы в радиусах тела: элита выше всех, детёныш — Расщепень в 0,6.
        private const float ThorncasterHeightPerRadius = 3f, RootSnarerHeightPerRadius = 2.8f, SplitterHeightPerRadius = 2.4f;
        private const float LabelLift = .28f, LabelFontSize = 2.4f;
        private static readonly Color BodyColor = new Color(.56f, .57f, .6f, 1f);
        private static readonly Color LabelColor = new Color(1f, .95f, .86f, 1f);

        /// <summary>Сглаживание позы, 1/с: контакт резкий, но без ступенек 30 Гц.</summary>
        private const float PoseSharpness = 22f;

        /// <summary>Сколько тиков идёт рывок контакта из отклона в наклон.</summary>
        private const float StrikeTicks = 3f;

        private static Material _material, _labelMaterial;
        private static readonly string[] Captions = new string[16];

        private Transform _pose, _capsule, _nose, _label;
        private TextMeshPro _text;
        private Transform _camera;
        private TickDriver _driver;
        private int _entity = -1, _health;
        private EnemyKind _kind;
        private float _height;

        // Действие Шипомёта или Корнехвата по событиям EnemyAction*, тики Sim.
        private EnemyActionKind _action;
        private float _actionStart, _actionImpact, _actionHold, _actionEnd;
        private float _lastImpact = -1000f, _hitAt = -1000f;

        private float _lean, _squash;
        private bool _dying;
        private float _deathClock, _deathFall, _deathFrom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { _material = null; _labelMaterial = null; }

        /// <summary>Тело заглушки для пула ArenaView. Размер ставит <see cref="Bind"/> по сущности.</summary>
        public static GameObject Create(EnemyKind kind)
        {
            // Собирается включённым: TextMeshPro ставит свой рендер в Awake, а свойства до
            // Awake на выключенном объекте надёжно не ложатся. Выключает тело пул сразу же.
            var root = new GameObject("Заглушка: " + EnemyTexts.Name(kind));
            var view = root.AddComponent<ForestMobPlaceholderView>();

            view._pose = new GameObject("Поза").transform;
            view._pose.SetParent(root.transform, false);
            Material material = BodyMaterial();
            view._capsule = Primitive(PrimitiveType.Capsule, "Капсула", view._pose, material);
            // Капсула симметрична: без носа разворот моба на экране не читается.
            view._nose = Primitive(PrimitiveType.Cube, "Нос", view._pose, material);

            TMP_FontAsset font = UiTheme.Current.Body;
            if (font != null)
            {
                var label = new GameObject(LabelName);
                label.transform.SetParent(root.transform, false);
                var text = label.AddComponent<TextMeshPro>();
                text.font = font;
                Material styled = LabelMaterial(font);
                if (styled != null) text.fontSharedMaterial = styled;
                text.fontSize = LabelFontSize;
                text.alignment = TextAlignmentOptions.Bottom;
                text.fontStyle = FontStyles.Bold;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.rectTransform.sizeDelta = new Vector2(4f, .6f);
                text.rectTransform.pivot = new Vector2(.5f, 0f);
                text.color = LabelColor;
                text.text = Caption(kind);
                var renderer = label.GetComponent<MeshRenderer>();
                renderer.sortingOrder = 6000;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                view._label = label.transform;
                view._text = text;
            }
            else Debug.LogWarning("[Разлом] Заглушка моба: в теме UI нет шрифта Body, подписи не будет.");
            root.SetActive(false);
            return root;
        }

        /// <summary>Подпись ли это заглушки: её рендер не часть тела.</summary>
        public static bool IsLabel(Renderer renderer) => renderer != null && renderer.gameObject.name == LabelName;

        /// <summary>
        /// Привязка к сущности. Зовётся после того, как ArenaView выставил базовый
        /// масштаб корня: размеры капсулы — метры Sim, поделённые на этот масштаб.
        /// </summary>
        public void Bind(TickDriver driver, int entity, float rootScale)
        {
            _driver = driver; _entity = entity;
            var entities = driver.Sim.Entities;
            _kind = entities.Kind[entity];
            _health = entities.Health[entity];
            _action = EnemyActionKind.None;
            _lastImpact = _hitAt = -1000f;
            _lean = _squash = 0f;
            _dying = false; _deathClock = 0f;

            float scale = Mathf.Max(.01f, rootScale);
            float radius = Mathf.Max(.1f, entities.BodyRadius[entity].ToFloat());
            _height = radius * HeightPerRadius(_kind);
            _capsule.localScale = new Vector3(radius * 2f, _height * .5f, radius * 2f) / scale;
            _capsule.localPosition = new Vector3(0f, _height * .5f, 0f) / scale;
            _nose.localScale = new Vector3(radius * .55f, radius * .3f, radius * .7f) / scale;
            _nose.localPosition = new Vector3(0f, _height * .72f, radius * .95f) / scale;
            _pose.localRotation = Quaternion.identity;
            _pose.localScale = Vector3.one;

            if (_label != null)
            {
                // Подпись одного размера в мире у всех, кроме детёныша: над метровым телом
                // полная подпись шире своры, ей хватает трёх четвертей.
                _label.localScale = Vector3.one * (_kind == EnemyKind.ForestSplitling ? .75f : 1f) / scale;
                string caption = Caption(_kind);
                if (_text.text != caption) _text.text = caption;
                _label.gameObject.SetActive(true);
            }
        }

        /// <summary>Событие действия Шипомёта или Корнехвата. tick — тик Sim, в котором оно родилось.</summary>
        public void OnEnemyAction(SimEventType type, EnemyActionKind kind, int tick)
        {
            if (_dying) return;
            switch (type)
            {
                case SimEventType.EnemyActionStarted:
                    _action = kind; _actionStart = tick;
                    switch (kind)
                    {
                        case EnemyActionKind.ThornLine:
                            _actionImpact = tick + Simulation.ThornLineWindupTicks;
                            _actionHold = tick + Simulation.ThornLineLastImpactTicks;
                            _actionEnd = _actionHold + Simulation.ThornLineRecoveryTicks;
                            break;
                        case EnemyActionKind.ThornBurst:
                            _actionImpact = _actionHold = tick + Simulation.ThornBurstWindupTicks;
                            _actionEnd = _actionHold + Simulation.ThornBurstRecoveryTicks;
                            break;
                        case EnemyActionKind.SnarerSlam:
                            // Контакт позы — удар корнями о землю; корни выходят позже, у героя.
                            _actionImpact = tick + Simulation.RootSnarerSlamTicks;
                            _actionHold = _actionImpact + Simulation.RootSnarerImpactDelayTicks;
                            _actionEnd = _actionHold + Simulation.RootSnarerRecoveryTicks;
                            break;
                        default: _action = EnemyActionKind.None; break;
                    }
                    break;
                case SimEventType.EnemyActionImpact:
                    _lastImpact = tick;
                    break;
                case SimEventType.EnemyActionCancelled:
                    _action = EnemyActionKind.None;
                    break;
            }
        }

        /// <summary>Смерть: заглушка валится на спину за время падения из профиля вида.</summary>
        public void PlayDeath()
        {
            if (_dying) return;
            _dying = true; _deathClock = 0f; _deathFrom = _lean;
            _deathFall = Mathf.Max(.05f, EnemyPresentationProfile.Death(_kind).FallSeconds);
            _action = EnemyActionKind.None;
            if (_label != null) _label.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || _entity < 0 || _entity >= sim.Entities.Count) return;
            var entities = sim.Entities;
            float dt = _driver.GameplayPaused ? 0f : Time.deltaTime;
            bool alive = entities.Alive[_entity];

            float lean = _lean, squash = _squash;
            if (_dying)
            {
                _deathClock += dt;
                float fall = Mathf.Clamp01(_deathClock / _deathFall);
                lean = Mathf.Lerp(_deathFrom, -84f, fall * fall);
                squash = 0f;
            }
            else if (alive) Target(sim, out lean, out squash);

            float blend = 1f - Mathf.Exp(-PoseSharpness * dt);
            _lean = _dying ? lean : Mathf.Lerp(_lean, lean, blend);
            _squash = Mathf.Lerp(_squash, squash, blend);
            _pose.localRotation = Quaternion.Euler(_lean, 0f, 0f);
            _pose.localScale = new Vector3(1f + _squash * .6f, 1f - _squash, 1f + _squash * .6f);

            if (_label == null) return;
            // Под землёй (выход волны) подписи нет: иначе она висит над пустой поляной.
            bool show = alive && !_dying && !sim.IsEmerging(_entity);
            if (_label.gameObject.activeSelf != show) _label.gameObject.SetActive(show);
            if (!show) return;
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            _label.position = transform.position + Vector3.up * (_height + LabelLift);
            if (_camera != null) _label.rotation = _camera.rotation;
        }

        /// <summary>Поза, к которой тянется тело в этом кадре: наклон, градусы (+ вперёд), и приседание.</summary>
        private void Target(Simulation sim, out float lean, out float squash)
        {
            var entities = sim.Entities;
            float now = sim.Tick - 1 + _driver.Alpha;
            lean = 0f; squash = 0f;

            // Ход — лёгкий наклон по скорости, стоячий моб прямой.
            FixVec2 velocity = entities.Velocity[_entity];
            float speed = Mathf.Sqrt(velocity.X.ToFloat() * velocity.X.ToFloat() + velocity.Y.ToFloat() * velocity.Y.ToFloat())
                * Simulation.TicksPerSecond;
            lean += Mathf.Clamp(speed * 1.6f, 0f, 7f);

            if (sim.TryGetEnemySwing(_entity, out EnemySwingState swing) && now <= swing.RecoverUntil)
            {
                bool bite = _kind == EnemyKind.ForestSplitling;
                Phase(now, swing.StartTick, swing.ImpactTick, swing.ImpactTick, swing.RecoverUntil,
                    bite ? 8f : 12f, bite ? 14f : 18f, .1f, .08f, ref lean, ref squash);
            }
            else if (_action != EnemyActionKind.None)
            {
                // Действие кончилось или снято без события (смерть по концу выживания) — выпрямляемся.
                bool acting = _kind == EnemyKind.ForestThorncaster ? sim.TryGetThorncasterAction(_entity, out _)
                    : _kind == EnemyKind.ForestRootSnarer && sim.TryGetRootSnarerAction(_entity, out _);
                if (now > _actionEnd || (!acting && now > _actionStart + 1f)) _action = EnemyActionKind.None;
                else if (_action == EnemyActionKind.ThornBurst)
                    Phase(now, _actionStart, _actionImpact, _actionHold, _actionEnd, 0f, 4f, .2f, .16f, ref lean, ref squash);
                else if (_action == EnemyActionKind.ThornLine)
                    Phase(now, _actionStart, _actionImpact, _actionHold, _actionEnd, 10f, 16f, .12f, .05f, ref lean, ref squash);
                else
                    Phase(now, _actionStart, _actionImpact, _actionHold, _actionEnd, 14f, 22f, .14f, .1f, ref lean, ref squash);
            }

            // Шип, всплеск, корни — короткий толчок в тело.
            float sinceImpact = now - _lastImpact;
            if (sinceImpact >= 0f && sinceImpact < 10f) squash += .09f * Mathf.Exp(-sinceImpact / 2.2f);

            // Попадание по заглушке: сжатие на пару кадров (отдачу корня ведёт ArenaView).
            int health = entities.Health[_entity];
            if (health < _health) _hitAt = Time.time;
            _health = health;
            float sinceHit = Time.time - _hitAt;
            if (sinceHit >= 0f && sinceHit < .3f) squash += .1f * Mathf.Exp(-sinceHit / .06f);

            // Детёныша выбрасывает из тела родителя: вытянут, пока летит.
            if (entities.ForcedTicksLeft[_entity] > 0 && entities.ForcedKind[_entity] == (byte)ForcedMotionKind.SplitPop)
                squash -= .16f * Mathf.Clamp01(entities.ForcedTicksLeft[_entity] / (float)Simulation.SplitPopTicks);
        }

        /// <summary>
        /// Подготовка: отклон назад на back градусов и приседание squash к контакту.
        /// Контакт: за StrikeTicks рывок в наклон forward и вытяжку stretch. До hold
        /// стойка держится (шипы линии, ожидание корней), к end — выпрямляется.
        /// </summary>
        private static void Phase(float now, float start, float impact, float hold, float end,
            float back, float forward, float crouch, float stretch, ref float lean, ref float squash)
        {
            if (now < impact)
            {
                float w = Mathf.Clamp01((now - start) / Mathf.Max(1f, impact - start));
                float eased = w * w * (3f - 2f * w);
                lean += -back * eased;
                squash += crouch * eased;
                return;
            }
            float strike = Mathf.Clamp01((now - impact) / StrikeTicks);
            float settle = now <= hold ? 0f : Mathf.Clamp01((now - hold) / Mathf.Max(1f, end - hold));
            float keep = 1f - settle * settle * (3f - 2f * settle);
            lean += Mathf.Lerp(-back, forward, strike) * keep;
            squash += Mathf.Lerp(crouch, -stretch, strike) * keep;
        }

        private static float HeightPerRadius(EnemyKind kind) => kind switch
        {
            EnemyKind.ForestThorncaster => ThorncasterHeightPerRadius,
            EnemyKind.ForestRootSnarer => RootSnarerHeightPerRadius,
            _ => SplitterHeightPerRadius,
        };

        private static string Caption(EnemyKind kind)
        {
            int index = (int)kind;
            if ((uint)index >= (uint)Captions.Length) return "[заглушка] " + EnemyTexts.Name(kind);
            return Captions[index] ??= "[заглушка] " + EnemyTexts.Name(kind);
        }

        private static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            // Столкновения считает Sim: коллайдер примитива тут лишний.
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var renderer = go.GetComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>
        /// Серый тун-материал врагов: тот же шейдер, что у Хранителя, — значит работают
        /// вспышка попадания, растворение смерти и контур из ArenaView. Один на все заглушки.
        /// </summary>
        private static Material BodyMaterial()
        {
            if (_material != null) return _material;
            Shader shader = Shader.Find("Razlom/Texture Toon") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Разлом] Заглушка моба: не найден ни тун-шейдер, ни URP/Lit.");
                return null;
            }
            _material = new Material(shader) { name = "Runtime_ForestMobPlaceholder" };
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", BodyColor);
            if (_material.HasProperty("_Smoothness")) _material.SetFloat("_Smoothness", .08f);
            if (_material.HasProperty("_Metallic")) _material.SetFloat("_Metallic", 0f);
            if (_material.HasProperty("_ShadowColor")) _material.SetColor("_ShadowColor", ViewMaterials.ToonShadow);
            if (_material.HasProperty("_MidColor")) _material.SetColor("_MidColor", new Color(.94f, .9f, .91f, 1f));
            if (_material.HasProperty("_MidThreshold")) _material.SetFloat("_MidThreshold", .24f);
            if (_material.HasProperty("_LightThreshold")) _material.SetFloat("_LightThreshold", .62f);
            if (_material.HasProperty("_LightFeather")) _material.SetFloat("_LightFeather", .045f);
            if (_material.HasProperty("_OutlineWidth")) _material.SetFloat("_OutlineWidth", 0f);
            return _material;
        }

        /// <summary>Подпись: светлый текст с тёмной обводкой, как цифры урона. Один материал на все.</summary>
        private static Material LabelMaterial(TMP_FontAsset font)
        {
            if (_labelMaterial != null) return _labelMaterial;
            if (font.material == null) return null;
            _labelMaterial = new Material(font.material) { name = font.name + " Placeholder" };
            _labelMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            _labelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.05f, .06f, .09f, 1f));
            _labelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, .24f);
            ShaderUtilities.GetShaderPropertyIDs();
            ShaderUtilities.UpdateShaderRatios(_labelMaterial);
            return _labelMaterial;
        }
    }
}
