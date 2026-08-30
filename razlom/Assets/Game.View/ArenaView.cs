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
        // Меш пелага приехал ростом 0.98 единицы (Tripo отдаёт модель в своём
        // масштабе, а не в метрах). Художник задал рост 1.78 м, симуляция
        // считает в метрах — отсюда множитель 1.78 / 0.98.
        public float WoleScale = 1.82f;
        public float OrvillScale = 1.0f;

        [Header("Модели персонажей")]
        [Tooltip("Путь модели в Resources. Пусто — рисованные спрайты, как было.")]
        public string WoleModel = "Characters/Pelag_v5/Runtime/Pelag_v5_MixamoRig";

        // ПУСТО — значит рисованный спрайт, и это сознательный выбор, а не
        // недоделка. Проверено съёмкой 29.08.2026: префаб
        // Orvill_ShieldInfantry_01_Validated загружается, анимируется и слушается
        // контроллера FullCombat, но его меш — блокаут для проверки рига: шар
        // вместо головы, коробки вместо конечностей, плоскость вместо щита.
        // Анимационный конвейер им доказан, а МОДЕЛИ ещё нет; ставить сюда
        // блокаут значило бы выдать техническую заглушку за персонажа.
        [Tooltip("Орвилл пока на спрайтах: его production-модель ещё не собрана.")]
        public string OrvillModel = "";

        // Игровая модель и клипы используют один Mixamo-скелет. Generic выбран
        // намеренно: так ноги, кисти и пальцы проигрываются без ретаргета.
        [Tooltip("Контроллер анимаций в Resources. У сырой модели Animator приходит " +
                 "пустым, и без контроллера персонаж стоит столбом.")]
        public string WoleController = "Characters/Pelag_v5/Pelag_v5_FullCombat";

        public string OrvillController = "";

        // Пусто: раскраска приехала внутри FBX отдельным материалом на каждую
        // часть тела, и постпроцессор импорта уже перевёл их на тун-шейдер.
        // Подстановка одного материала на всю модель стёрла бы это.
        [Tooltip("Материал персонажа в Resources. Пусто — материал берётся из модели.")]
        public string WoleMaterial = "";

        public string OrvillMaterial = "";

        // Картинка ОТДЕЛЬНЫМ файлом рядом с моделью, а не вшитая в FBX.
        //
        // Проверено 29.08.2026: материалу, который импортёр кладёт внутрь
        // префаба, ссылку на текстуру присвоить не удаётся — шейдер и цвет
        // сохраняются, а текстура молча теряется при сериализации. Материал без
        // текстуры выглядит ровно как её отсутствие: одноцветная фигура.
        // Поэтому материал собирается в игре из этого файла.
        [Tooltip("Текстура персонажа в Resources. Запасной путь: если готового " +
                 "материала нет, он собирается прямо в игре из этой картинки.")]
        public string WoleTexture = "Characters/Pelag_v4/Pelag_v4_BaseColor";

        public string OrvillTexture = "";

        [Header("Модульное снаряжение героя")]
        [Tooltip("Отдельный prefab оружия. Он не связан с мешем тела и меняется через сокет.")]
        public string WoleWeaponPrefab = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber";
        public string WoleWeaponBaseColor = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_BaseColor";
        public string WoleWeaponNormal = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_Normal";
        public string WoleWeaponMetallic = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_Metallic";
        public string WoleWeaponSocket = "mixamorig:RightHand";
        // Экспорт нормализован: pivot находится в центре рукояти, клинок идёт
        // по локальной +Y сокета. Небольшой сдвиг ставит хват в центр ладони,
        // а не в сустав запястья.
        public Vector3 WoleWeaponLocalPosition = new Vector3(0f, 0.040f, 0f);
        // Измерено по линии Pinky1 -> Index1 в rest pose Mixamo и переведено
        // в локальные оси RightHand. Рукоять проходит поперёк ладони, клинок
        // выходит со стороны большого/указательного пальца.
        public Vector3 WoleWeaponLocalDirection = new Vector3(0.882f, 0.470f, 0.035f);
        // Тело Pelag увеличено в 1.82 раза из-за масштаба исходного FBX, а
        // новая сабля уже приходит в метрах. Компенсируем масштаб родителя:
        // 0.55 * 1.82 ~= 1, поэтому клинок остаётся длиной около метра.
        public float WoleWeaponLocalScale = 0.55f;

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

        private Transform _playerBladeRoot;
        private Transform _playerBladeTip;

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
        // Только presentation-offset: capture/demo может показать рывок или
        // сопротивление цепи, не меняя детерминированную позицию в Sim.
        private Vector3[] _presentationOffset;
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
            _presentationOffset = new Vector3[capacity];

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

        /// <summary>Опорные точки фактически установленной сабли Pelag.</summary>
        public bool TryGetPlayerBlade(out Transform bladeRoot, out Transform bladeTip)
        {
            bladeRoot = _playerBladeRoot;
            bladeTip = _playerBladeTip;
            return bladeRoot != null && bladeTip != null;
        }

        /// <summary>Фактически привязанный объект сущности в presentation-слое.</summary>
        public bool TryGetEntityView(int entityId, out Transform view)
        {
            view = null;
            if (!_initialized || (uint)entityId >= (uint)_boundCount) return false;
            view = _views[entityId];
            return view != null;
        }

        /// <summary>
        /// Визуальное смещение поверх позиции Sim. Никакого gameplay-состояния
        /// этот метод не меняет; он нужен для рывка/натяжения в VFX QA.
        /// </summary>
        public void SetPresentationOffset(int entityId, Vector3 offset)
        {
            if (_presentationOffset == null || (uint)entityId >= (uint)_boundCount) return;
            _presentationOffset[entityId] = offset;
        }

        public void ClearPresentationOffsets()
        {
            if (_presentationOffset == null) return;
            for (int i = 0; i < _boundCount; i++) _presentationOffset[i] = Vector3.zero;
        }

        public void PlayPlayerAttackPresentation()
        {
            if (!_initialized || _boundCount <= Simulation.PlayerId) return;
            _animationViews[Simulation.PlayerId]?.PlayAttack();
        }

        public void PlayPlayerAbilityPresentation(int slot)
        {
            if (!_initialized || _boundCount <= Simulation.PlayerId) return;
            _animationViews[Simulation.PlayerId]?.PlayAbility(slot);
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
                _presentationOffset[i] = Vector3.zero;

            }

            _playerBladeRoot = null;
            _playerBladeTip = null;

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

                if (i == Simulation.PlayerId)
                {
                    _playerBladeRoot = FindChild(go.transform, "BladeRoot");
                    _playerBladeTip = FindChild(go.transform, "BladeTip");
                }

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
                p += _presentationOffset[i];

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

            GameObject weaponPrefab = faction == Faction.Wole &&
                                      !string.IsNullOrEmpty(WoleWeaponPrefab)
                ? Resources.Load<GameObject>(WoleWeaponPrefab)
                : null;
            Material weaponMaterial = faction == Faction.Wole
                ? BuildWeaponMaterial(WoleWeaponBaseColor, WoleWeaponNormal, WoleWeaponMetallic)
                : null;

            // ЗАПАСНОЙ ПУТЬ, и он важнее, чем кажется. Материал внутри FBX
            // пересоздаётся при переимпорте и теряет текстуру, готовый .mat
            // может быть собран раньше, чем появилась картинка, — а персонаж
            // должен быть цветным в любом случае.
            //
            // Проверяется именно ТЕКСТУРА, а не наличие материала: материал
            // без текстуры выглядит точно так же, как его отсутствие, —
            // белой фигурой, — и именно на этом мы уже один раз попались.
            if (material == null || !HasBaseTexture(material))
            {
                Material runtime = BuildRuntimeMaterial(texturePath, faction);
                if (runtime != null) material = runtime;
            }

            return () => CreateCharacterBody(prefab, faction, scale, material, controller,
                weaponPrefab, WoleWeaponSocket,
                WoleWeaponLocalPosition, WoleWeaponLocalDirection, WoleWeaponLocalScale,
                weaponMaterial);
        }

        // Имя слота базовой текстуры зависит от шейдера: URP и наш тун-шейдер
        // зовут его _BaseMap, встроенный конвейер — _MainTex. Свойство
        // Material.mainTexture жёстко читает _MainTex, поэтому на URP-шейдере
        // оно НЕ «возвращает null», а пишет ошибку в лог и возвращает null.
        //
        // Из-за этого проверка «есть ли у материала текстура» всегда говорила
        // «нет»: готовый .mat отбрасывался, материал собирался заново каждый
        // раз, а в консоль на каждого персонажа падала ошибка. Отсюда правило:
        // слот выбирается по тому, что шейдер объявил, а не по удобному
        // короткому свойству.
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private static bool HasBaseTexture(Material material)
        {
            if (material.HasProperty(BaseMapId) && material.GetTexture(BaseMapId) != null) return true;
            return material.HasProperty(MainTexId) && material.GetTexture(MainTexId) != null;
        }

        private static void AssignBaseTexture(Material material, Texture texture)
        {
            if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, texture);
            if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, texture);
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

            // Pelag's 4K atlas already contains painted form and fine ink. A
            // second two-band toon pass flattened that form and made the dense
            // UV islands shimmer at gameplay distance. Lit restores the volume
            // from the imported vertex normals, closer to the source 3D view.
            Shader shader = (faction == Faction.Wole
                                ? Shader.Find("Universal Render Pipeline/Lit")
                                : Shader.Find("Razlom/Texture Toon"))
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[Разлом] Не найден ни один пригодный шейдер.");
                return null;
            }

            var material = new Material(shader) { name = "Runtime_" + faction };
            AssignBaseTexture(material, texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            // Мультяшной фигуре блик по всей поверхности мешает: он забивает
            // силуэт, а силуэт здесь главный канал распознавания.
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", faction == Faction.Wole ? 0.22f : 0.08f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_ShadowColor"))
                material.SetColor("_ShadowColor", ViewMaterials.ToonShadow);
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0.0035f);

            Debug.Log($"[Разлом] {faction}: материал собран в игре из «{texturePath}», шейдер {shader.name}.");
            return material;
        }

        private static GameObject CreateSpriteBody(Faction faction, float scale)
        {
            GameObject root = new GameObject(faction == Faction.Wole ? "Pelag Art" : "Orvill Art");
            // Рисованный силуэт должен читаться в игровом зуме, а не превращаться
            // в маленькое пятно между крупными тайлами арены.
            // Оба участника целевого среза происходят из одной 2.5D-серии.
            // Масштаб выравнивает их видимую высоту примерно до 2.4 метра:
            // текстуры имеют большие прозрачные поля, поэтому единица была бы
            // заметно меньше настоящего тела в кадре.
            float comicScale = faction == Faction.Wole ? 0.78f : 0.82f;
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
            Material fallbackMaterial, RuntimeAnimatorController controller,
            GameObject weaponPrefab, string weaponSocket,
            Vector3 weaponLocalPosition, Vector3 weaponLocalDirection, float weaponLocalScale,
            Material weaponMaterial)
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
            {
                if (!MountRigidProp(go.transform, weaponPrefab, weaponSocket,
                        weaponLocalPosition, weaponLocalDirection, weaponLocalScale, weaponMaterial))
                    Debug.LogWarning($"[Разлом] Сабля не установлена: prefab={(weaponPrefab != null)}, " +
                                     $"socket={weaponSocket}.");
            }

            CharacterAnimatorView animation = go.GetComponent<CharacterAnimatorView>();
            if (animation == null) animation = go.AddComponent<CharacterAnimatorView>();
            animation.Configure(faction);
            return go;
        }

        private static bool MountRigidProp(Transform character, GameObject weaponPrefab,
            string socketName, Vector3 localPosition,
            Vector3 localDirection, float localScale, Material materialOverride)
        {
            if (weaponPrefab == null) return false;

            Transform socket = FindChild(character, socketName);
            if (socket == null) return false;

            GameObject mounted = Instantiate(weaponPrefab, socket, false);
            mounted.name = "Pelag_FantasySaber_Equipped";
            mounted.transform.localPosition = localPosition;
            mounted.transform.localRotation = localDirection.sqrMagnitude > 0.0001f
                ? Quaternion.FromToRotation(Vector3.up, localDirection.normalized)
                : Quaternion.identity;
            mounted.transform.localScale = Vector3.one * localScale;
            mounted.SetActive(true);

            // Trail строится не вокруг персонажа, а по реальному клинку.
            // Точки создаются в локальном пространстве самого длинного измерения
            // меша, поэтому продолжают следовать за рукой и всеми костями клипа.
            MeshFilter bladeMesh = mounted.GetComponentInChildren<MeshFilter>(true);
            if (bladeMesh != null && bladeMesh.sharedMesh != null)
            {
                Bounds bounds = bladeMesh.sharedMesh.bounds;
                // Нормализованный weapon asset всегда направлен по +Y, а pivot
                // лежит в хвате. Начало trail ставим уже за гардой, а не в
                // помеле/ладони; конец — чуть до кончика, чтобы не дрожал.
                float length = bounds.size.y;

                Transform bladeRoot = new GameObject("BladeRoot").transform;
                bladeRoot.SetParent(bladeMesh.transform, false);
                bladeRoot.localPosition = new Vector3(0f, bounds.min.y + length * 0.40f, 0f);

                Transform bladeTip = new GameObject("BladeTip").transform;
                bladeTip.SetParent(bladeMesh.transform, false);
                bladeTip.localPosition = new Vector3(0f, bounds.min.y + length * 0.985f, 0f);
            }

            if (materialOverride != null)
            {
                Renderer[] renderers = mounted.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] materials = renderers[i].sharedMaterials;
                    for (int m = 0; m < materials.Length; m++) materials[m] = materialOverride;
                    renderers[i].sharedMaterials = materials;
                }
            }

            return true;
        }

        private static Transform FindChild(Transform root, string wantedName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == wantedName) return all[i];
            return null;
        }

        private static Material BuildWeaponMaterial(string baseColorPath, string normalPath,
            string metallicPath)
        {
            Texture2D baseColor = Resources.Load<Texture2D>(baseColorPath);
            Texture2D normal = Resources.Load<Texture2D>(normalPath);
            Texture2D metallic = Resources.Load<Texture2D>(metallicPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                Debug.LogWarning("[Разлом] Не найден URP/Lit — материал новой сабли не собран.");
                return null;
            }

            var material = new Material(shader) { name = "Runtime_Pelag_FantasySaber" };
            if (baseColor != null) AssignBaseTexture(material, baseColor);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            if (metallic != null && material.HasProperty("_MetallicGlossMap"))
            {
                material.SetTexture("_MetallicGlossMap", metallic);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.72f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.38f);
            if (baseColor == null)
                Debug.LogWarning($"[Разлом] BaseColor новой сабли не найден: {baseColorPath}");
            return material;
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
