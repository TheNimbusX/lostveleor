using UnityEngine;
using UnityEngine.Rendering;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Отрисовка сущностей. Читает состояние симуляции и ничего в неё не пишет:
    /// это односторонний канал, и он должен таким остаться.
    ///
    /// Соответствие «индекс сущности → объект» задаётся один раз при привязке
    /// и больше не меняется: в симуляции индекс — это identity, сущности не
    /// удаляются и не переупорядочиваются, поэтому и здесь ничего не съезжает.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class ArenaView : MonoBehaviour
    {
        [Header("Прогрев пулов")]
        [Tooltip("Сколько объектов создать заранее. 0 — по вместимости EntityStore.")]
        public int PrewarmWole = 8;
        public int PrewarmOrvill = 64;

        [Header("Вид")]
        public Color WoleColor = new Color(0.92f, 0.40f, 0.46f);
        public Color OrvillColor = new Color(0.55f, 0.57f, 0.62f);
        public float WoleScale = 1.4f;
        public float OrvillScale = 1.0f;

        [Header("Модели персонажей")]
        [Tooltip("Путь модели в Resources. Пусто — рисованные спрайты, как было.")]
        public string WoleModel = "Characters/Pelag_Rodin01/Pelag_Rodin01";

        [Tooltip("Орвилл пока на спрайтах: его production-модель ещё не собрана.")]
        public string OrvillModel = "";

        [Tooltip("Контроллер анимаций в Resources. У сырой модели Animator приходит " +
                 "пустым, и без контроллера персонаж стоит столбом.")]
        public string WoleController = "Characters/Pelag_Rodin01/Pelag_Rodin01";

        public string OrvillController = "";

        [Tooltip("Материал персонажа в Resources. Материал внутри FBX пересоздаётся " +
                 "при каждом переимпорте, поэтому цвет берётся из отдельного ассета.")]
        public string WoleMaterial = "Characters/Pelag_Rodin01/Pelag_Rodin01";

        public string OrvillMaterial = "";

        [Tooltip("Текстура персонажа в Resources. Запасной путь: если готового " +
                 "материала нет, он собирается прямо в игре из этой картинки.")]
        public string WoleTexture = "Characters/Pelag_Rodin01/Pelag_Rodin01_BaseColor";

        public string OrvillTexture = "";

        [Tooltip("Доворот модели вокруг вертикали, градусы. Если персонаж бегает " +
                 "спиной вперёд — поставь 180. Зависит от того, куда смотрел " +
                 "оригинал при экспорте, и одинаково для всех клипов.")]
        public float ModelYaw = 0f;

        [Header("Снаряды")]
        public int PrewarmProjectiles = 16;
        public Color ProjectileColor = new Color(1.00f, 0.55f, 0.10f);
        public float ProjectileScale = 0.45f;
        public float ProjectileHeight = 0.8f;

        private TickDriver _driver;

        private ViewPool _wolePool;
        private ViewPool _orvillPool;
        private ViewPool _projectilePool;

        // Снаряд → его объект. Слоты снарядов переиспользуются, поэтому объект
        // берётся из пула при рождении и возвращается при смерти, а не висит
        // за индексом навсегда, как у сущностей.
        private Transform[] _projectileViews;

        // Индекс сущности → её объект. Массив, а не словарь: индексы плотные,
        // а искать по ним надо каждый кадр.
        private Transform[] _views;
        private ViewPool[] _viewPools;
        private float[] _groundOffset;
        private CharacterAnimatorView[] _animationViews;
        private float[] _deathUntil;
        private bool[] _deathStarted;
        private float[] _deathStartedAt;
        private Vector3[] _deathDirection;
        private int[] _lastDamageSource;
        private int _generation = -1;

        /// <summary>
        /// Глубина Разлома, под которую собраны привязки.
        ///
        /// Вход в следующий Разлом НЕ меняет поколение: симуляция та же самая,
        /// меняется только её содержимое. Признак «сущностей стало меньше»
        /// тут не работает — с глубиной врагов становится БОЛЬШЕ, и старые
        /// привязки молча остаются жить.
        /// </summary>
        private int _depthShown = -1;

        // Привязаны индексы [0, _boundCount): сущности не переупорядочиваются,
        // поэтому «привязанное» — всегда непрерывный префикс, флаги не нужны.
        private int _boundCount;

        private bool _initialized;

        // ---- реакция на попадание ----
        //
        // Отдача и сплющивание живут ЗДЕСЬ, а не в симуляции: положение
        // сущности решает тик, и трогать его ради картинки нельзя. Это
        // смещение поверх посчитанной позиции, и оно ни на что не влияет.
        //
        // Затухание идёт по ИГРОВОМУ времени, а не по реальному: hit-stop
        // замедляет время специально, и поза удара обязана замереть вместе
        // со всем остальным — в этом и весь смысл стопа.
        private Vector3[] _hitRecoil;
        private float[] _hitPunch;
        private Vector3[] _baseScale;
        private Renderer[][] _bodyRenderers;
        private MaterialPropertyBlock[] _materialBlocks;
        private float[] _hitFlash;
        private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");

        [Header("Реакция на попадание")]
        [Tooltip("На сколько метров тело отбрасывает визуально при полном ударе.")]
        public float RecoilDistance = 0.30f;

        [Tooltip("Насколько тело раздувается в момент попадания.")]
        public float PunchScale = 0.16f;

        [Tooltip("Во сколько раз в секунду затухают отдача и сплющивание.")]
        public float ReactionDecay = 11f;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        /// <summary>
        /// Пулы собираются ЛЕНИВО, при первой появившейся симуляции.
        ///
        /// Игра начинается в лагере, где рисовать нечего и симуляции нет вовсе.
        /// Собирать пулы в Start значило бы выключить компонент навсегда ещё
        /// до того, как игрок войдёт в Разлом.
        ///
        /// Размер берётся по самой большой симуляции сессии, а не по текущей:
        /// на Полигоне сущностей единицы, в Разломе сотни, а массивы привязок
        /// живут дольше и той и другой.
        /// </summary>
        private void Initialize()
        {
            _initialized = true;

            int capacity = TickDriver.MaxSimCapacity;
            _views = new Transform[capacity];
            _viewPools = new ViewPool[capacity];
            _groundOffset = new float[capacity];
            _animationViews = new CharacterAnimatorView[capacity];
            _deathUntil = new float[capacity];
            _deathStarted = new bool[capacity];
            _deathStartedAt = new float[capacity];
            _deathDirection = new Vector3[capacity];
            _lastDamageSource = new int[capacity];
            _hitRecoil = new Vector3[capacity];
            _hitPunch = new float[capacity];
            _baseScale = new Vector3[capacity];
            _bodyRenderers = new Renderer[capacity][];
            _materialBlocks = new MaterialPropertyBlock[capacity];
            _hitFlash = new float[capacity];

            Transform woleRoot = new GameObject("Пул: Wole").transform;
            Transform orvillRoot = new GameObject("Пул: Orvill").transform;
            woleRoot.SetParent(transform, false);
            orvillRoot.SetParent(transform, false);

            // Путь 3D-модели или рисованный спрайт — решается тем, лежит ли
            // модель по указанному пути. Спрайт остаётся запасным вариантом
            // намеренно: сломанный или недоделанный персонаж не должен
            // оставлять игрока без тела вообще.
            _wolePool = new ViewPool(woleRoot,
                BodyFactory(WoleModel, WoleController, WoleMaterial, WoleTexture,
                    Faction.Wole, WoleScale),
                PrewarmWole > 0 ? PrewarmWole : capacity);
            _orvillPool = new ViewPool(orvillRoot,
                BodyFactory(OrvillModel, OrvillController, OrvillMaterial, OrvillTexture,
                    Faction.Orvill, OrvillScale),
                PrewarmOrvill > 0 ? PrewarmOrvill : capacity);

            Transform projectileRoot = new GameObject("Пул: снаряды").transform;
            projectileRoot.SetParent(transform, false);

            Material projectileMat = ViewMaterials.CreateLit(ProjectileColor);
            _projectilePool = new ViewPool(projectileRoot,
                () => CreateBody(PrimitiveType.Sphere, projectileMat, ProjectileScale),
                PrewarmProjectiles);

            _projectileViews = new Transform[capacity];

            BindNewEntities();
        }

        private void LateUpdate()
        {
            // LateUpdate, а не Update: к этому моменту TickDriver уже сделал все
            // шаги кадра и выставил Alpha, по которой интерполируется отрисовка.
            Simulation sim = _driver.Sim;
            if (sim == null)
            {
                // Вышли в лагерь: рисовать нечего, и всё занятое надо вернуть,
                // иначе в лагере остались бы стоять враги прошлого Разлома.
                if (_initialized && _boundCount > 0) ReleaseEverything();
                return;
            }

            if (!_initialized) Initialize();

            // Три случая, когда старые привязки становятся ложью: другая
            // симуляция, вход в следующий Разлом и упавшее число сущностей.
            //
            // ГЛУБИНА ЗДЕСЬ ОБЯЗАТЕЛЬНА. Раньше признаком считалось только
            // упавшее число сущностей — и это ловило переход лишь тогда, когда
            // в новом Разломе врагов оказывалось меньше. А их с глубиной
            // становится больше: часть сущностей оставалась привязана к телам
            // прошлого Разлома, то есть к спрятанным трупам, и враги выходили
            // невидимыми.
            int depth = _driver.Run != null ? _driver.Run.Depth : -1;

            if (_generation != _driver.Generation
                || depth != _depthShown
                || _driver.Sim.Entities.Count < _boundCount)
            {
                ReleaseEverything();
                _depthShown = depth;
            }

            BindNewEntities();
            SyncAnimationEvents();
            SyncTransforms();
            SyncProjectiles();
        }

        /// <summary>
        /// Тело дёрнулось от удара. Зовёт CombatJuiceView, получив подтверждённое
        /// событие урона.
        ///
        /// Направление приходит от бьющего к цели и нормализуется здесь:
        /// звать это с ненормированным вектором — обычная ошибка, а цена ей
        /// улетевший через полкарты спрайт.
        /// </summary>
        public void ReactToHit(int entityId, Vector3 direction, float strength)
        {
            if (!_initialized) return;
            if ((uint)entityId >= (uint)_boundCount) return;
            if (_views[entityId] == null) return;

            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) direction.Normalize();
            else direction = Vector3.zero;

            strength = Mathf.Clamp01(strength);

            // Берётся МАКСИМУМ, а не сумма: двадцать попаданий по площади
            // в одном кадре — это один толчок, а не двадцать сложенных.
            Vector3 recoil = direction * (RecoilDistance * strength);
            if (recoil.sqrMagnitude > _hitRecoil[entityId].sqrMagnitude) _hitRecoil[entityId] = recoil;
            if (strength > _hitPunch[entityId]) _hitPunch[entityId] = strength;
            if (strength > _hitFlash[entityId]) _hitFlash[entityId] = strength;
        }

        /// <summary>
        /// Возвращает всё в пулы при перезапуске забега.
        ///
        /// У новой симуляции индексы сущностей начинаются заново, поэтому
        /// старые привязки указывали бы не на тех. Объекты при этом
        /// переиспользуются — Instantiate в бою запрещён и на рестарте тоже.
        /// </summary>
        private void ReleaseEverything()
        {
            for (int i = 0; i < _boundCount; i++)
            {
                if (_views[i] == null) continue;
                _viewPools[i].Release(_views[i].gameObject);
                _views[i] = null;
                _viewPools[i] = null;
                _animationViews[i] = null;
                _deathUntil[i] = 0f;
                _deathStarted[i] = false;
                _deathStartedAt[i] = 0f;
                _deathDirection[i] = Vector3.zero;
                _lastDamageSource[i] = -1;
                _hitRecoil[i] = Vector3.zero;
                _hitPunch[i] = 0f;
                _bodyRenderers[i] = null;
                _materialBlocks[i] = null;
                _hitFlash[i] = 0f;
            }

            for (int i = 0; i < _projectileViews.Length; i++)
            {
                if (_projectileViews[i] == null) continue;
                _projectilePool.Release(_projectileViews[i].gameObject);
                _projectileViews[i] = null;
            }

            _boundCount = 0;
            _generation = _driver.Generation;
            _depthShown = _driver.Run != null ? _driver.Run.Depth : -1;
        }

        /// <summary>
        /// Снаряды рисуются без интерполяции между тиками, в отличие от тел.
        /// Они летят быстро и живут секунду: сглаживание тут не читается,
        /// а лишний массив прошлых позиций стоил бы памяти на каждый слот.
        /// </summary>
        private void SyncProjectiles()
        {
            ProjectileStore projectiles = _driver.Sim.Projectiles;

            for (int i = 0; i < projectiles.HighWater; i++)
            {
                if (projectiles.Alive[i])
                {
                    if (_projectileViews[i] == null)
                        _projectileViews[i] = _projectilePool.Acquire().transform;

                    _projectileViews[i].position = new Vector3(
                        projectiles.Position[i].X.ToFloat(),
                        ProjectileHeight,
                        projectiles.Position[i].Y.ToFloat());
                }
                else if (_projectileViews[i] != null)
                {
                    _projectilePool.Release(_projectileViews[i].gameObject);
                    _projectileViews[i] = null;
                }
            }
        }

        /// <summary>
        /// Привязывает объекты к сущностям, появившимся с прошлого кадра.
        /// Сейчас сущности после расстановки не досоздаются, но опираться на это
        /// не стоит: волны и призыв придут, а код останется этот же.
        /// </summary>
        private void BindNewEntities()
        {
            EntityStore entities = _driver.Sim.Entities;

            for (int i = _boundCount; i < entities.Count; i++)
            {
                ViewPool pool = entities.Side[i] == Faction.Wole ? _wolePool : _orvillPool;
                GameObject go = pool.Acquire();
                go.name = $"{entities.Side[i]} #{i}";
                _views[i] = go.transform;
                _viewPools[i] = pool;
                _animationViews[i] = go.GetComponent<CharacterAnimatorView>();
                _animationViews[i]?.ResetForSpawn();
                _deathUntil[i] = 0f;
                _deathStarted[i] = false;
                _deathStartedAt[i] = 0f;
                _deathDirection[i] = Vector3.zero;
                _lastDamageSource[i] = -1;
                _hitRecoil[i] = Vector3.zero;
                _hitPunch[i] = 0f;
                _bodyRenderers[i] = go.GetComponentsInChildren<Renderer>(true);
                _materialBlocks[i] = new MaterialPropertyBlock();
                _hitFlash[i] = 0f;

                // Базовый масштаб запоминается ОДИН РАЗ при привязке: дальше
                // его каждый кадр перезаписывает сплющивание, и прочитать
                // «как было» уже неоткуда.
                _baseScale[i] = go.transform.localScale;

                _groundOffset[i] = _animationViews[i] != null
                    ? 0f
                    : GroundOffset(entities.Side[i], WoleScale, OrvillScale);
            }

            _boundCount = entities.Count;
        }

        private void SyncTransforms()
        {
            EntityStore entities = _driver.Sim.Entities;

            for (int i = 0; i < _boundCount; i++)
            {
                Transform view = _views[i];
                bool alive = entities.Alive[i];

                // Мировые координаты: симуляция считает в них же, и Bootstrap
                // держит корень сцены в начале координат ровно ради этого.
                Vector3 p = _driver.GetRenderPosition(i);
                p.y += _groundOffset[i];

                // Отдача и сплющивание затухают экспоненциально: удар должен
                // читаться как толчок, а не как отъезд тела в сторону.
                float decay = Mathf.Exp(-ReactionDecay * Time.deltaTime);
                _hitRecoil[i] *= decay;
                _hitPunch[i] *= decay;
                _hitFlash[i] *= Mathf.Exp(-24f * Time.deltaTime);

                Renderer[] bodyRenderers = _bodyRenderers[i];
                MaterialPropertyBlock block = _materialBlocks[i];
                if (bodyRenderers != null && block != null)
                {
                    block.SetFloat(HitFlashId, _hitFlash[i]);
                    for (int r = 0; r < bodyRenderers.Length; r++)
                        if (bodyRenderers[r] != null) bodyRenderers[r].SetPropertyBlock(block);
                }

                view.position = p + _hitRecoil[i];
                if (_baseScale[i] != Vector3.zero)
                    view.localScale = _baseScale[i] * (1f + _hitPunch[i] * PunchScale);

                // Разворот берётся из симуляции как есть, без сглаживания:
                // именно этот угол решает, куда уйдёт конус способности,
                // и картинка не должна показывать другой.
                FixVec2 facing = entities.Facing[i];
                if (facing.LengthSq.Raw != 0)
                {
                    Vector3 facingWorld = new Vector3(facing.X.ToFloat(), 0f, facing.Y.ToFloat());
                    CharacterAnimatorView animation = _animationViews[i];
                    if (animation != null && animation.UsesSprites)
                    {
                        view.rotation = Quaternion.identity;
                        animation.FaceCamera(facingWorld);
                    }
                    else
                    {
                        // Доворот на случай, если модель экспортировали лицом
                        // не туда: разворачивать сам меш дороже, чем повернуть
                        // корень одним числом.
                        view.rotation = Quaternion.LookRotation(facingWorld, Vector3.up)
                                        * Quaternion.Euler(0f, ModelYaw, 0f);
                    }
                }

                // Тело остаётся видимым до конца death-клипа. Симуляция уже
                // считает сущность мёртвой; эта задержка существует только в View.
                if (!alive)
                {
                    bool showDeath = _deathStarted[i] && Time.unscaledTime < _deathUntil[i];
                    if (view.gameObject.activeSelf != showDeath) view.gameObject.SetActive(showDeath);
                    if (showDeath)
                    {
                        float duration = _animationViews[i] != null
                            ? _animationViews[i].DeathDuration
                            : 1.5f;
                        float t = Mathf.Clamp01((Time.time - _deathStartedAt[i]) / duration);
                        float travel = 1f - (1f - t) * (1f - t);
                        view.position += _deathDirection[i] * (0.58f * travel)
                                         + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.44f);
                        Vector3 fallAxis = Vector3.Cross(Vector3.up, _deathDirection[i]);
                        if (fallAxis.sqrMagnitude < 0.001f) fallAxis = Vector3.forward;
                        view.rotation = Quaternion.AngleAxis(86f * t, fallAxis.normalized) * view.rotation;
                        float vanish = Mathf.InverseLerp(1f, 0.68f, t);
                        view.localScale = _baseScale[i] * Mathf.Lerp(0.12f, 1.08f, vanish);
                    }
                    continue;
                }

                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                _animationViews[i]?.SetMoving(entities.Velocity[i].LengthSq.Raw != 0);
            }
        }

        private void SyncAnimationEvents()
        {
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                switch (e.Type)
                {
                    case SimEventType.Attack:
                        AnimationOf(e.Source)?.PlayAttack();
                        break;
                    case SimEventType.AbilityCast:
                        AnimationOf(e.Source)?.PlayAbility(e.Amount);
                        break;
                    case SimEventType.Damage:
                        if ((uint)e.Target < (uint)_lastDamageSource.Length)
                            _lastDamageSource[e.Target] = e.Source;
                        AnimationOf(e.Target)?.PlayHit(e.Source ^ e.Target);
                        break;
                    case SimEventType.Death:
                        CharacterAnimatorView animation = AnimationOf(e.Target);
                        if (animation == null) break;
                        animation.PlayDeath();
                        _deathStarted[e.Target] = true;
                        _deathStartedAt[e.Target] = Time.time;
                        _deathUntil[e.Target] = Time.unscaledTime + animation.DeathDuration;
                        int source = _lastDamageSource[e.Target];
                        if (_driver.Sim != null
                            && (uint)source < (uint)_driver.Sim.Entities.Count)
                        {
                            FixVec2 from = _driver.Sim.Entities.Position[source];
                            FixVec2 to = _driver.Sim.Entities.Position[e.Target];
                            Vector3 away = new Vector3(to.X.ToFloat() - from.X.ToFloat(), 0f,
                                                       to.Y.ToFloat() - from.Y.ToFloat());
                            _deathDirection[e.Target] = away.sqrMagnitude > 0.001f
                                ? away.normalized
                                : Vector3.forward;
                        }
                        else
                        {
                            _deathDirection[e.Target] = Vector3.forward;
                        }
                        break;
                }
            }
        }

        private CharacterAnimatorView AnimationOf(int entity)
            => entity >= 0 && entity < _boundCount ? _animationViews[entity] : null;

        /// <summary>
        /// Чем создавать тела этой стороны: моделью из Resources или спрайтом.
        ///
        /// Модель ищется ОДИН раз, при сборке пула, а не на каждое тело:
        /// Resources.Load идёт по диску, и звать его сорок раз подряд —
        /// это заметная пауза ровно в момент входа в Разлом.
        /// </summary>
        private System.Func<GameObject> BodyFactory(string modelPath, string controllerPath,
            string materialPath, string texturePath, Faction faction, float scale)
        {
            GameObject prefab = string.IsNullOrEmpty(modelPath)
                ? null
                : Resources.Load<GameObject>(modelPath);

            if (prefab == null)
            {
                if (!string.IsNullOrEmpty(modelPath))
                    Debug.LogWarning($"[Разлом] Модель «{modelPath}» не найдена, {faction} рисуется спрайтом.");
                return () => CreateSpriteBody(faction, scale);
            }

            RuntimeAnimatorController controller = string.IsNullOrEmpty(controllerPath)
                ? null
                : Resources.Load<RuntimeAnimatorController>(controllerPath);

            if (controller == null && !string.IsNullOrEmpty(controllerPath))
                Debug.LogWarning($"[Разлом] Контроллер «{controllerPath}» не найден — " +
                                 $"{faction} будет стоять столбом. Собери его: меню Разлом.");

            Debug.Log($"[Разлом] {faction}: модель {modelPath}" +
                      (controller != null ? ", контроллер найден" : ", БЕЗ контроллера"));

            Material material = string.IsNullOrEmpty(materialPath)
                ? null
                : Resources.Load<Material>(materialPath);

            // ЗАПАСНОЙ ПУТЬ, и он важнее, чем кажется. Материал внутри FBX
            // пересоздаётся при переимпорте и теряет текстуру, готовый .mat
            // может быть собран раньше, чем появилась картинка, — а персонаж
            // должен быть цветным в любом случае.
            //
            // Проверяется именно ТЕКСТУРА, а не наличие материала: материал
            // без текстуры выглядит точно так же, как его отсутствие, —
            // белой фигурой, — и именно на этом мы уже один раз попались.
            if (material == null || material.mainTexture == null)
            {
                Material runtime = BuildRuntimeMaterial(texturePath, faction);
                if (runtime != null) material = runtime;
            }

            return () => CreateCharacterBody(prefab, faction, scale, material, controller);
        }

        /// <summary>
        /// Собирает материал персонажа в игре, из картинки в Resources.
        ///
        /// Ищет URP-шейдер, потому что проект на URP: встроенный Standard тут
        /// рисуется белым или розовым, и именно это выглядит как «модель есть,
        /// а раскраски нет».
        /// </summary>
        private static Material BuildRuntimeMaterial(string texturePath, Faction faction)
        {
            if (string.IsNullOrEmpty(texturePath)) return null;

            Texture2D texture = Resources.Load<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogWarning($"[Разлом] Текстуры «{texturePath}» нет — " +
                                 $"{faction} останется в том, что пришло из FBX.");
                return null;
            }

            Shader shader = Shader.Find("Razlom/Texture Toon")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[Разлом] Не найден ни один пригодный шейдер.");
                return null;
            }

            var material = new Material(shader) { name = "Runtime_" + faction };
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            // Мультяшной фигуре блик по всей поверхности мешает: он забивает
            // силуэт, а силуэт здесь главный канал распознавания.
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_ShadowColor"))
                material.SetColor("_ShadowColor", new Color(0.36f, 0.20f, 0.32f, 1f));
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0.007f);

            Debug.Log($"[Разлом] {faction}: материал собран в игре из «{texturePath}», шейдер {shader.name}.");
            return material;
        }

        private static GameObject CreateSpriteBody(Faction faction, float scale)
        {
            GameObject root = new GameObject(faction == Faction.Wole ? "Pelag Art" : "Orvill Art");
            // Рисованный силуэт должен читаться в игровом зуме, а не превращаться
            // в маленькое пятно между крупными тайлами арены.
            float comicScale = faction == Faction.Wole ? 1.55f : 1.34f;
            root.transform.localScale = Vector3.one * (scale * comicScale);

            GameObject artwork = new GameObject("Artwork");
            artwork.transform.SetParent(root.transform, false);
            SpriteRenderer renderer = artwork.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 10;

            SpriteCharacterVisual visual = artwork.AddComponent<SpriteCharacterVisual>();
            visual.Configure(faction, renderer);

            CharacterAnimatorView animation = root.AddComponent<CharacterAnimatorView>();
            animation.Configure(faction);
            return root;
        }

        private static GameObject CreateCharacterBody(GameObject prefab, Faction faction, float scale,
            Material fallbackMaterial, RuntimeAnimatorController controller)
        {
            GameObject go = Instantiate(prefab);
            go.transform.localScale = Vector3.one * scale;
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
                Destroy(collider);

            // Эти объекты случайно попали в production FBX из стартовой сцены
            // Blender. Они не являются частью персонажа и не должны создавать
            // дополнительные камеры/свет или закрывать модель кубом.
            foreach (Camera importedCamera in go.GetComponentsInChildren<Camera>(true))
                importedCamera.enabled = false;
            foreach (Light importedLight in go.GetComponentsInChildren<Light>(true))
                importedLight.enabled = false;

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer.gameObject.name == "Cube")
                {
                    renderer.enabled = false;
                    continue;
                }
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;

                // Палитра персонажа записана в vertex colors меша; материал
                // добавляет cel-тени и контур, не перекрашивая работу художника.
                if (fallbackMaterial != null)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++) materials[m] = fallbackMaterial;
                    renderer.sharedMaterials = materials;
                }
            }

            // Контроллер ставится ДО Configure: тот запоминает Animator и сразу
            // трогает его параметры, а у Animator без контроллера параметров нет.
            if (controller != null)
            {
                Animator animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
                if (animator.runtimeAnimatorController == null)
                    animator.runtimeAnimatorController = controller;

                // ROOT MOTION ВЫКЛЮЧЕН НАВСЕГДА. Положение сущности решает тик,
                // и клип, двигающий персонажа сам, увёл бы картинку от симуляции:
                // бил бы он там, где стоит по тику, а выглядел бы стоящим в другом
                // месте. Это прямое правило проекта, а не настройка вкуса.
                animator.applyRootMotion = false;
            }

            if (faction == Faction.Wole)
                EquipProp(go.transform, "PRP_Saber_0p92m", "Weapon_R");

            CharacterAnimatorView animation = go.GetComponent<CharacterAnimatorView>();
            if (animation == null) animation = go.AddComponent<CharacterAnimatorView>();
            animation.Configure(faction);
            return go;
        }

        private static void EquipProp(Transform root, string propName, string socketName)
        {
            Transform prop = FindChild(root, propName);
            Transform socket = FindChild(root, socketName);
            if (prop == null || socket == null)
            {
                Debug.LogWarning($"[Разлом] Не удалось экипировать {propName}: " +
                                 $"prop={(prop != null)}, socket={(socket != null)}.");
                return;
            }

            Vector3 importedScale = prop.localScale;
            prop.SetParent(socket, false);
            prop.localPosition = Vector3.zero;
            prop.localRotation = Quaternion.identity;
            prop.localScale = importedScale;
        }

        private static Transform FindChild(Transform root, string wantedName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == wantedName) return all[i];
            return null;
        }

        private static GameObject CreateBody(PrimitiveType type, Material material, float scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);

            // Коллайдеры не нужны: столкновения считает симуляция, физика Unity
            // к ним отношения не имеет и иметь не должна.
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.transform.localScale = Vector3.one * scale;

            // Капсула симметрична вокруг своей оси: без метки её разворот
            // на экране никак не читается. Маленький нос вперёд по +Z решает
            // это до появления настоящих моделей.
            if (type == PrimitiveType.Capsule) AddNose(go.transform, material);
            return go;
        }

        private static void AddNose(Transform body, Material material)
        {
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Нос";

            Collider collider = nose.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            nose.GetComponent<MeshRenderer>().sharedMaterial = material;
            nose.transform.SetParent(body, false);
            nose.transform.localScale = new Vector3(0.35f, 0.2f, 0.6f);
            nose.transform.localPosition = new Vector3(0f, 0f, 0.55f);
        }

        /// <summary>
        /// Подъём над полом: симуляция двумерная и даёт y = 0, а примитивы Unity
        /// заданы от центра. Без сдвига половина тела уходит под плоскость.
        /// Капсула примитива высотой 2 единицы, куб — 1.
        /// </summary>
        private static float GroundOffset(Faction side, float woleScale, float orvillScale)
            => side == Faction.Wole ? woleScale : orvillScale * 0.5f;
    }
}
