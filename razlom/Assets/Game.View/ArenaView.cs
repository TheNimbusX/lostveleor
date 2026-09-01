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
        // МНОЖИТЕЛЬ РОСТА. Симуляция считает в метрах, художник задал 1.78 м,
        // bind pose приходит ростом 0.978 единицы — отсюда 1.82.
        //
        // ЭТО ЧИСЛО МЕНЯТЬ НЕ НАДО, даже когда приезжает новое тело. Подгонять
        // следует множитель импорта в RazlomCharacterImport так, чтобы meshHeight
        // становился 0.978; сюда трогать не нужно ничего.
        //
        // Причина: всё, что висит на костях — сабля, якорь, цепь, VFX — наследует
        // этот масштаб, а их собственные размеры и смещения заданы в единицах
        // модели. Сдвинешь корень — поедут все висюльки разом, и подкручивать
        // придётся каждую. На v6 это уже случилось: тело пришло ростом 1.8, я
        // компенсировал здесь, и сабля выросла в полсотни раз.
        public float WoleScale = 1.82f;
        public float OrvillScale = 1.0f;

        [Header("Модели персонажей")]
        [Tooltip("Путь модели в Resources. Пусто — рисованные спрайты, как было.")]
        public string WoleModel = "Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig";

        // v3 — модель по утверждённому концепту: чёрное с золотом, красный ромб,
        // длинный плащ. Пришла ростом 0.98 м на риге AccuRig и приведена под
        // движок: рост выставлен в 1.88 ровно, как у v2, кости переименованы под
        // Humanoid, добавлены сокеты Weapon_R и Shield_L.
        //
        // РУКИ У НЕЁ ПУСТЫЕ, и это осознанно: в v2 меч и щит были частью одного
        // меша и не снимались. Пока на сокеты ничего не надето, моб ходит
        // безоружным — это видно сразу и чинится навеской пропсов, а не правкой
        // модели. Откат на прежнюю: вернуть сюда Orvill_v2/Orvill_v2_CombatRig
        // и OrvillTexture на Orvill_v2_BaseColor.
        [Tooltip("Анимируемая 3D-модель Орвилла в Resources.")]
        public string OrvillModel = "Characters/Orvill_v3/Orvill_v3_CombatRig";

        // Игровая модель и клипы используют один Mixamo-скелет. Generic выбран
        // намеренно: так ноги, кисти и пальцы проигрываются без ретаргета.
        [Tooltip("Контроллер анимаций в Resources. У сырой модели Animator приходит " +
                 "пустым, и без контроллера персонаж стоит столбом.")]
        public string WoleController = "Characters/Pelag_v5/Pelag_v5_FullCombat";

        public string OrvillController =
            "Characters/Orvill_ShieldInfantry_01/Orvill_FullCombat";

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
        public string WoleTexture = "Characters/Pelag_v6/Pelag_v6_BaseColor";

        public string OrvillTexture = "Characters/Orvill_v3/Orvill_v3_BaseColor";

        [Header("Модульное снаряжение героя")]
        [Tooltip("Отдельный prefab оружия. Он не связан с мешем тела и меняется через сокет.")]
        public string WoleWeaponPrefab = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber";
        public string WoleWeaponBaseColor = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_BaseColor";
        public string WoleWeaponNormal = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_Normal";
        public string WoleWeaponMetallic = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_Metallic";
        public string WoleWeaponSocket = "mixamorig:RightHand";
        // Экспорт нормализован: pivot находится в центре рукояти, клинок идёт
        // по локальной +Y сокета. RightHand начинается в суставе запястья.
        // Значение 0.076 ставило центр рукояти на
        // линию оснований пальцев, поэтому гарда визуально висела снаружи
        // кулака. Сдвиг к 0.045 кладёт рукоять в середину сжатой ладони.
        public Vector3 WoleWeaponLocalPosition = new Vector3(0f, 0.045f, -0.002f);
        // Измерено по линии Pinky1 -> Index1 в rest pose Mixamo и переведено
        // в локальные оси RightHand. Рукоять проходит поперёк ладони, клинок
        // выходит со стороны большого/указательного пальца.
        // Продольная ось развёрнута: +Y оружейного asset теперь выводит
        // остриё вверх от кулака, а не в пол при рукояти наверху.
        // Геометрия сабли идёт вдоль локальной +Y. Этот вектор даёт почти
        // горизонтальный клинок с лёгким наклоном острия К ПОЛУ; прежний
        // отрицательный Y после преобразования сокета поднимал острие к лицу.
        public Vector3 WoleWeaponLocalDirection = new Vector3(-0.982f, 0.180f, -0.020f);
        // Direction задаёт только линию оружия. Roll отдельно переворачивает
        // поперечник вокруг клинка, чтобы режущая сторона смотрела вниз.
        public float WoleWeaponLocalRoll = 180f;
        // Тело Pelag увеличено в 1.82 раза из-за масштаба исходного FBX, а
        // новая сабля уже приходит в метрах. Компенсируем масштаб родителя:
        // 0.55 * 1.82 ~= 1, поэтому клинок остаётся длиной около метра.
        public float WoleWeaponLocalScale = 0.55f;

        // ЯКОРЬ НА ПОЯСЕ. До 1 сентября его на персонаже не было вовсе: голова
        // якоря существовала только внутри VFX-префабов, поэтому в момент каста
        // он материализовался из воздуха и так же исчезал. Вырезанная рукоять
        // со свёрнутой цепью лежала в проекте, но нигде не создавалась.
        //
        // Всё вынесено в инспектор намеренно: положение пропса на поясе — это
        // то, что подбирают глазом за один заход, а не считают. Правится
        // ползунками на живом персонаже, без перекомпиляции.
        [Header("Якорь на поясе")]
        [Tooltip("Рукоять с намотанной цепью. Пусто — якоря на теле не будет.")]
        public string WoleAnchorPrefab = "Weapons/Pelag/AnchorChain/Pelag_AnchorGrip";

        public string WoleAnchorBaseColor = "Weapons/Pelag/AnchorChain/Pelag_AnchorChain_BaseColor";

        [Tooltip("Кость, к которой крепится. Таз — якорь висит на поясе и " +
                 "качается вместе с корпусом, а не с рукой.")]
        public string WoleAnchorSocket = "mixamorig:Hips";

        [Tooltip("Смещение от кости таза: влево, вверх, назад. Стартовые числа " +
                 "прикидочные — доводить в Play Mode.")]
        public Vector3 WoleAnchorLocalPosition = new Vector3(0.13f, -0.02f, -0.09f);

        public Vector3 WoleAnchorLocalDirection = new Vector3(0f, -1f, 0f);
        public float WoleAnchorLocalRoll = 0f;
        public float WoleAnchorLocalScale = 0.55f;

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
        // Отдача живёт ЗДЕСЬ, а не в симуляции: положение
        // сущности решает тик, и трогать его ради картинки нельзя. Это
        // смещение поверх посчитанной позиции, и оно ни на что не влияет.
        //
        // Затухание идёт по ИГРОВОМУ времени, а не по реальному: hit-stop
        // замедляет время специально, и поза удара обязана замереть вместе
        // со всем остальным — в этом и весь смысл стопа.
        private Vector3[] _hitRecoil;
        private Vector3[] _deathLaunch;
        private Vector3[] _deathSpinAxis;

        /// <summary>Кого уже тащили в прошлом кадре — чтобы клип запускался один раз.</summary>
        private bool[] _wasDragged;
        private Vector3[] _baseScale;
        private Renderer[][] _bodyRenderers;
        private int[][] _bodyMaterialSlotCounts;
        private SpriteRenderer[] _contactShadows;
        private Color[] _contactShadowBaseColors;
        private MaterialPropertyBlock[] _materialBlocks;
        private float[] _hitFlash;
        private float[] _lastVelocityMagnitude;
        private bool[] _locomotionMoving;
        private Vector3[] _lastFacingWorld;
        private Vector3[] _visualFacingWorld;
        private float[] _turnVisualUntil;
        private float[] _turnVisualDirection;
        // Только presentation-offset: capture/demo может показать рывок или
        // сопротивление цепи, не меняя детерминированную позицию в Sim.
        private Vector3[] _presentationOffset;
        private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        private static readonly int DeathFadeId = Shader.PropertyToID("_DeathFade");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static Sprite _contactShadowSprite;
        private const string ContactShadowName = "Contact Shadow";
        private const float WoleSpriteScaleMultiplier = 0.78f;
        private const float OrvillSpriteScaleMultiplier = 0.82f;
        // СМЕРТЬ РЯДОВОГО МОБА — ЭТО ВЫБРОС, А НЕ ПАДЕНИЕ.
        //
        // Раньше здесь стояло 1.6 + 0.18 + 0.42 = 2.2 секунды: полноценная
        // анимация умирания, потом пауза на позе, потом растворение. Для
        // одного врага это красиво; для гриндилки, где за забег их сотни, это
        // мешок, который валится две секунды и всё это время занимает экран.
        // Никакие искры поверх такого не спасают — они кончаются, а мешок ещё
        // падает.
        //
        // 0.16 + 0.30 = меньше половины секунды. Тело выбрасывает, крутит и
        // растворяет; к моменту, когда игрок довёл прицел до следующего врага,
        // предыдущего уже нет. Именно это читается как «разлетелся».
        private const float OrvillDeathAnimationDuration = 0.16f;
        private const float OrvillDeathPoseHoldDuration = 0f;
        private const float OrvillDeathFadeDuration = 0.30f;

        // Выброс тела: горизонтальная скорость от удара плюс подброс вверх.
        // Через тело проходит парабола, а не прямая — прямая читается как
        // скольжение по льду.
        private const float DeathLaunchSpeed = 4.6f;
        private const float DeathLaunchLift = 3.1f;
        private const float DeathGravity = 11.5f;
        private const float DeathSpinSpeed = 520f;
        private const float OrvillDeathPresentationDuration = OrvillDeathAnimationDuration
                                                              + OrvillDeathPoseHoldDuration
                                                              + OrvillDeathFadeDuration;
        private const float OrvillTurnSharpness = 20f;
        private int _hoveredEntity = -1;

        [Header("Реакция на попадание")]
        [Tooltip("На сколько метров тело отбрасывает визуально при полном ударе.")]
        public float RecoilDistance = 0.30f;

        [Tooltip("Во сколько раз в секунду затухает отдача.")]
        public float ReactionDecay = 11f;

        [Tooltip("Максимальная вспышка героя: сохраняет палитру при одновременных ударах толпы.")]
        [Range(0f, 1f)] public float PlayerHitFlashMax = 0.42f;

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
            _hitRecoil = new Vector3[capacity];
            _deathLaunch = new Vector3[capacity];
            _deathSpinAxis = new Vector3[capacity];
            _wasDragged = new bool[capacity];
            _baseScale = new Vector3[capacity];
            _bodyRenderers = new Renderer[capacity][];
            _bodyMaterialSlotCounts = new int[capacity][];
            _contactShadows = new SpriteRenderer[capacity];
            _contactShadowBaseColors = new Color[capacity];
            _materialBlocks = new MaterialPropertyBlock[capacity];
            _hitFlash = new float[capacity];
            _presentationOffset = new Vector3[capacity];
            _lastVelocityMagnitude = new float[capacity];
            _locomotionMoving = new bool[capacity];
            _lastFacingWorld = new Vector3[capacity];
            _visualFacingWorld = new Vector3[capacity];
            _turnVisualUntil = new float[capacity];
            _turnVisualDirection = new float[capacity];

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

            bool alive = _driver.Sim != null
                         && (uint)entityId < (uint)_driver.Sim.Entities.Count
                         && _driver.Sim.Entities.Alive[entityId];

            // ИГРОКА ОТДАЧА НЕ ДВИГАЕТ НИКОГДА.
            //
            // Толчок читается как подтверждение удара только у того, кем игрок
            // не управляет. Своё тело он ведёт сам, и камера привязана к нему:
            // сдвиг на треть метра уезжает вместе со всем экраном и читается не
            // как «мне попали», а как «у меня отобрали управление». Получение
            // урона игрок узнаёт по вспышке, полоске здоровья и звуку — по всему,
            // что не трогает позицию.
            //
            // Берётся МАКСИМУМ, а не сумма: двадцать попаданий по площади
            // в одном кадре — это один толчок, а не двадцать сложенных.
            // Летальный Damage уже относится к DeathBack: труп не должен перед
            // падением получать ещё один процедурный толчок. Вспышка контакта
            // остаётся, чтобы последний удар не потерял визуальное подтверждение.
            if (alive && entityId != Simulation.PlayerId)
            {
                Vector3 recoil = direction * (RecoilDistance * strength);
                if (recoil.sqrMagnitude > _hitRecoil[entityId].sqrMagnitude)
                    _hitRecoil[entityId] = recoil;
            }

            float flashStrength = entityId == Simulation.PlayerId
                ? Mathf.Min(strength, PlayerHitFlashMax)
                : strength;
            if (flashStrength > _hitFlash[entityId]) _hitFlash[entityId] = flashStrength;
        }

        /// <summary>
        /// Летальный presentation-импульс. В отличие от обычной отдачи он не
        /// затухает обратно к исходной точке: тело заканчивает death-анимацию
        /// там, куда его действительно визуально вытолкнул последний удар.
        /// Gameplay-позиция и столкновения Sim не меняются.
        /// </summary>
        public void ReactToDeath(int entityId, Vector3 direction, float strength)
        {
            if (!_initialized || (uint)entityId >= (uint)_boundCount) return;
            if (_views[entityId] == null) return;

            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) direction.Normalize();
            else direction = _lastFacingWorld[entityId].sqrMagnitude > 0.0001f
                ? _lastFacingWorld[entityId]
                : Vector3.forward;

            // Смерть должна отбрасывать, а не покачивать. 0.24–0.38 м — это
            // меньше ширины самого тела: труп оседал на месте, и убийство
            // терялось среди обычных попаданий. Полметра-метр уже читаются как
            // «улетел», и именно это отличает убийство от очередного удара.
            // Не смещение, а СКОРОСТЬ: дальше её интегрирует SyncTransforms,
            // и тело идёт по параболе, а не переставляется в новую точку.
            _deathLaunch[entityId] =
                direction * (DeathLaunchSpeed * Mathf.Lerp(0.75f, 1.25f, strength))
                + Vector3.up * DeathLaunchLift;

            // Ось вращения поперёк удара: тело кувыркается через голову в ту
            // сторону, куда его отправили, а не крутится волчком на месте.
            _deathSpinAxis[entityId] = Vector3.Cross(Vector3.up, direction).normalized;
            if (_deathSpinAxis[entityId].sqrMagnitude < 0.5f)
                _deathSpinAxis[entityId] = Vector3.right;

            _presentationOffset[entityId] = Vector3.zero;
            _hitRecoil[entityId] = Vector3.zero;
            _hitFlash[entityId] = Mathf.Max(_hitFlash[entityId], 0.92f);
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

        /// <summary>Выделяет только врага под курсором; gameplay не меняет.</summary>
        public void SetHoveredEntity(int entityId)
        {
            _hoveredEntity = entityId;
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
        /// Игровой presentation по стабильному определению способности.
        /// Слот сохраняется в API для трассировки вызывающего каста, но выбор
        /// клипа никогда не делается по его номеру: пользователь может
        /// переставить способности в панели как угодно.
        /// </summary>
        public void PlayPlayerAbilityPresentation(int slot, int definitionId)
        {
            if (!_initialized || _boundCount <= Simulation.PlayerId) return;
            _animationViews[Simulation.PlayerId]?.PlayAbilityDefinition(definitionId);
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
            if (_views == null || _viewPools == null)
            {
                _boundCount = 0;
                _hoveredEntity = -1;
                _playerBladeRoot = null;
                _playerBladeTip = null;
                return;
            }

            for (int i = 0; i < _boundCount; i++)
                ReleaseEntityView(i);

            _playerBladeRoot = null;
            _playerBladeTip = null;
            _hoveredEntity = -1;

            for (int i = 0; _projectileViews != null && i < _projectileViews.Length; i++)
            {
                if (_projectileViews[i] == null) continue;
                if (_projectilePool != null) _projectilePool.Release(_projectileViews[i].gameObject);
                else _projectileViews[i].gameObject.SetActive(false);
                _projectileViews[i] = null;
            }

            _boundCount = 0;
            _generation = _driver.Generation;
            _depthShown = _driver.Run != null ? _driver.Run.Depth : -1;
        }

        private void ReleaseEntityView(int entityId)
        {
            Transform view = _views[entityId];
            if (view != null)
            {
                // MPB живёт на Renderer дольше одной привязки. Сбрасываем fade
                // до возврата, чтобы следующий владелец слота не появился уже
                // растворённым даже на один render-кадр.
                ResetRendererPresentation(entityId);
                if (_baseScale[entityId] != Vector3.zero)
                    view.localScale = _baseScale[entityId];

                // Managed ViewPool не переживает forced script reload, а ссылки
                // на созданные Transform Unity успевает восстановить. Во время
                // teardown объект достаточно спрятать: новый Awake соберёт пул.
                if (_viewPools[entityId] != null) _viewPools[entityId].Release(view.gameObject);
                else view.gameObject.SetActive(false);
            }

            _views[entityId] = null;
            _viewPools[entityId] = null;
            _animationViews[entityId] = null;
            _deathUntil[entityId] = 0f;
            _deathStarted[entityId] = false;
            _deathStartedAt[entityId] = 0f;
            _hitRecoil[entityId] = Vector3.zero;
            _baseScale[entityId] = Vector3.zero;
            _bodyRenderers[entityId] = null;
            _bodyMaterialSlotCounts[entityId] = null;
            _contactShadows[entityId] = null;
            _contactShadowBaseColors[entityId] = Color.clear;
            _materialBlocks[entityId] = null;
            _hitFlash[entityId] = 0f;
            _lastVelocityMagnitude[entityId] = 0f;
            _locomotionMoving[entityId] = false;
            _lastFacingWorld[entityId] = Vector3.zero;
            _visualFacingWorld[entityId] = Vector3.zero;
            _turnVisualUntil[entityId] = 0f;
            _turnVisualDirection[entityId] = 0f;
            _presentationOffset[entityId] = Vector3.zero;
            _groundOffset[entityId] = 0f;
            // Слот переиспользуется под нового врага: остаточный выброс от
            // прошлого покойника отправил бы живого в полёт при первой смерти.
            _deathLaunch[entityId] = Vector3.zero;
            _deathSpinAxis[entityId] = Vector3.zero;

            if (_hoveredEntity == entityId) _hoveredEntity = -1;
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
                if (i == Simulation.PlayerId)
                    AlignSaberCuttingEdgeToGround(go.transform);
                _deathUntil[i] = 0f;
                _deathStarted[i] = false;
                _deathStartedAt[i] = 0f;
                _hitRecoil[i] = Vector3.zero;
                _deathLaunch[i] = Vector3.zero;
                _deathSpinAxis[i] = Vector3.zero;
                _bodyRenderers[i] = CacheBodyRenderers(go, out SpriteRenderer contactShadow);
                _bodyMaterialSlotCounts[i] = CacheMaterialSlotCounts(_bodyRenderers[i]);
                _contactShadows[i] = contactShadow;
                _contactShadowBaseColors[i] = contactShadow != null
                    ? contactShadow.color
                    : Color.clear;
                _materialBlocks[i] = new MaterialPropertyBlock();
                ResetRendererPresentation(i);
                _hitFlash[i] = 0f;
                _lastVelocityMagnitude[i] = 0f;
                _locomotionMoving[i] = false;
                FixVec2 initialFacing = entities.Facing[i];
                _lastFacingWorld[i] = initialFacing.LengthSq.Raw == 0
                    ? Vector3.zero
                    : new Vector3(initialFacing.X.ToFloat(), 0f, initialFacing.Y.ToFloat()).normalized;
                _visualFacingWorld[i] = _lastFacingWorld[i];
                _turnVisualUntil[i] = 0f;
                _turnVisualDirection[i] = 0f;

                if (i == Simulation.PlayerId)
                {
                    _playerBladeRoot = FindChild(go.transform, "BladeRoot");
                    _playerBladeTip = FindChild(go.transform, "BladeTip");
                }

                // Сначала сбрасываем пульный объект в масштаб из конфига, и только
                // потом снимаем базу: иначе остаток последнего hit/death-кадра
                // станет «нормальным» размером врага в следующем Разломе.
                go.transform.localScale = ExpectedBaseScale(entities.Side[i], _animationViews[i]);
                _baseScale[i] = go.transform.localScale;

                _groundOffset[i] = _animationViews[i] != null
                    ? 0f
                    : GroundOffset(entities.Side[i], WoleScale, OrvillScale);
            }

            _boundCount = entities.Count;
        }

        private Vector3 ExpectedBaseScale(Faction faction, CharacterAnimatorView animation)
        {
            float scale = faction == Faction.Wole ? WoleScale : OrvillScale;
            if (animation != null && animation.UsesSprites)
                scale *= SpriteScaleMultiplier(faction);
            return Vector3.one * scale;
        }

        private static float SpriteScaleMultiplier(Faction faction)
            => faction == Faction.Wole ? WoleSpriteScaleMultiplier : OrvillSpriteScaleMultiplier;

        private static void AlignSaberCuttingEdgeToGround(Transform character)
        {
            Transform saber = FindChild(character, "Pelag_FantasySaber_Equipped");
            if (saber == null) return;

            // Геометрический аудит FBX: оружие идёт вдоль локальной +Y,
            // широкая ось клинка — X. Игровой просмотр показал, что видимая
            // режущая сторона asset находится на +X (а не на -X, как читалось
            // по ортографическому аудиту без руки).
            // Продольный хват уже выставлен отдельно. Здесь меняется ТОЛЬКО
            // roll вокруг клинка: +X совмещается с ближайшим к мировому низу
            // направлением, возможным при неизменной оси острия.
            Vector3 bladeAxis = saber.TransformDirection(Vector3.up).normalized;
            Vector3 currentEdge = Vector3.ProjectOnPlane(
                saber.TransformDirection(Vector3.right), bladeAxis);
            Vector3 groundEdge = Vector3.ProjectOnPlane(Vector3.down, bladeAxis);
            if (currentEdge.sqrMagnitude < 0.000001f || groundEdge.sqrMagnitude < 0.000001f)
                return;

            float correction = Vector3.SignedAngle(
                currentEdge.normalized, groundEdge.normalized, bladeAxis);
            saber.rotation = Quaternion.AngleAxis(correction, bladeAxis) * saber.rotation;
        }

        private static int[] CacheMaterialSlotCounts(Renderer[] renderers)
        {
            if (renderers == null) return null;

            var counts = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                counts[i] = renderer != null ? renderer.sharedMaterials.Length : 0;
            }
            return counts;
        }

        private static Renderer[] CacheBodyRenderers(GameObject body,
            out SpriteRenderer contactShadow)
        {
            Renderer[] all = body.GetComponentsInChildren<Renderer>(true);
            contactShadow = null;
            int bodyCount = 0;

            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (IsContactShadow(renderer, out SpriteRenderer sprite))
                {
                    contactShadow = sprite;
                    continue;
                }

                bodyCount++;
            }

            if (bodyCount == all.Length) return all;

            var renderers = new Renderer[bodyCount];
            int write = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (IsContactShadow(renderer, out _)) continue;
                renderers[write++] = renderer;
            }

            return renderers;
        }

        private static bool IsContactShadow(Renderer renderer, out SpriteRenderer shadow)
        {
            shadow = renderer as SpriteRenderer;
            return shadow != null && renderer.gameObject.name == ContactShadowName;
        }

        private void ResetRendererPresentation(int entityId)
        {
            Renderer[] renderers = _bodyRenderers[entityId];
            MaterialPropertyBlock block = _materialBlocks[entityId];
            ResetContactShadowPresentation(entityId);
            if (renderers == null || block == null) return;

            block.Clear();
            block.SetFloat(HitFlashId, 0f);
            block.SetFloat(DeathFadeId, 0f);
            block.SetFloat(OutlineWidthId, 1.18f);
            block.SetColor(OutlineColorId, new Color(0.025f, 0.13f, 0.17f, 1f));

            bool usesSprites = _animationViews[entityId] != null
                               && _animationViews[entityId].UsesSprites;
            if (usesSprites)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer != null)
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            ApplyRendererPropertyBlock(renderers, _bodyMaterialSlotCounts[entityId], block);
        }

        private void ResetContactShadowPresentation(int entityId)
        {
            SpriteRenderer shadow = _contactShadows[entityId];
            if (shadow == null) return;
            shadow.color = _contactShadowBaseColors[entityId];
            shadow.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void SetContactShadowFade(int entityId, float fade)
        {
            SpriteRenderer shadow = _contactShadows[entityId];
            if (shadow == null) return;

            Color color = _contactShadowBaseColors[entityId];
            color.a *= 1f - Mathf.Clamp01(fade);
            shadow.color = color;
        }

        private static void ApplyRendererPropertyBlock(Renderer[] renderers, int[] materialSlotCounts,
            MaterialPropertyBlock block)
        {
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null) continue;

                // Renderer-level block может быть перекрыт block-ом отдельного
                // material slot. Пишем значения в каждый submesh, чтобы весь
                // силуэт реагировал одинаково.
                int slotCount = materialSlotCounts != null && r < materialSlotCounts.Length
                    ? materialSlotCounts[r]
                    : 0;
                if (slotCount == 0)
                {
                    renderer.SetPropertyBlock(block);
                    continue;
                }

                for (int m = 0; m < slotCount; m++)
                    renderer.SetPropertyBlock(block, m);
            }
        }

        private void SyncTransforms()
        {
            EntityStore entities = _driver.Sim.Entities;

            for (int i = 0; i < _boundCount; i++)
            {
                Transform view = _views[i];
                if (view == null) continue;

                bool alive = entities.Alive[i];
                bool orvill = entities.Side[i] == Faction.Orvill;

                // Начало волока — один раз на попадание в тягу, а не каждый
                // кадр: триггер, дёрнутый десять раз подряд, перезапускает
                // клип с нуля и тело дёргается на месте вместо одной реакции.
                bool dragged = alive
                               && entities.ForcedTicksLeft[i] > 0
                               && entities.ForcedKind[i] == (byte)ForcedMotionKind.Dragged;
                if (dragged && !_wasDragged[i]) _animationViews[i]?.PlayDragged();
                _wasDragged[i] = dragged;
                float deathElapsed = !alive && _deathStarted[i]
                    ? Mathf.Max(0f, Time.time - _deathStartedAt[i])
                    : 0f;
                float deathFade = orvill && !alive
                    ? Mathf.InverseLerp(
                        OrvillDeathAnimationDuration + OrvillDeathPoseHoldDuration,
                        OrvillDeathPresentationDuration,
                        deathElapsed)
                    : 0f;
                if (deathFade > 0f) SetContactShadowFade(i, deathFade);

                // Мировые координаты: симуляция считает в них же, и Bootstrap
                // держит корень сцены в начале координат ровно ради этого.
                Vector3 p = _driver.GetRenderPosition(i);
                p.y += _groundOffset[i];
                p += _presentationOffset[i];

                // Старт locomotion должен отвечать на первый ненулевой тик,
                // а остановка — происходить до последнего микрошажка торможения.
                // Проверка только velocity != 0 держала Run ещё несколько
                // кадров после того, как тело визуально уже приехало.
                FixVec2 velocity = entities.Velocity[i];
                float velocityMagnitude = Mathf.Sqrt(
                    velocity.X.ToFloat() * velocity.X.ToFloat()
                    + velocity.Y.ToFloat() * velocity.Y.ToFloat());
                float fullStep = entities.MoveStep[i].ToFloat();
                bool moving = _locomotionMoving[i];
                if (velocityMagnitude <= 0.0001f)
                    moving = false;
                else if (!moving)
                    moving = true;
                else if (velocityMagnitude < _lastVelocityMagnitude[i]
                         && velocityMagnitude <= fullStep * 0.38f)
                    moving = false;
                _lastVelocityMagnitude[i] = velocityMagnitude;
                _locomotionMoving[i] = moving;
                float normalizedMoveSpeed = fullStep > 0.0001f
                    ? Mathf.Clamp01(velocityMagnitude / fullStep)
                    : 0f;
                float turnDirection = 0f;
                float localMoveX = 0f;
                float localMoveY = moving ? 1f : 0f;

                // Отдача затухает экспоненциально: удар должен
                // читаться как толчок, а не как отъезд тела в сторону.
                float decay = Mathf.Exp(-ReactionDecay * Time.deltaTime);
                _hitRecoil[i] *= decay;
                // Roughly 60 ms above the visible 0.1 threshold at 60 FPS.
                _hitFlash[i] *= Mathf.Exp(-36f * Time.deltaTime);

                Renderer[] bodyRenderers = _bodyRenderers[i];
                int[] materialSlotCounts = _bodyMaterialSlotCounts[i];
                MaterialPropertyBlock block = _materialBlocks[i];
                if (bodyRenderers != null && block != null)
                {
                    block.SetFloat(HitFlashId, _hitFlash[i]);
                    block.SetFloat(DeathFadeId, deathFade);
                    bool hovered = alive && i == _hoveredEntity;
                    // Faction contours keep overlapping bodies separable on a
                    // bright floor. Orvill gets a restrained red silhouette and
                    // Pelag a blue-green one; only the hovered target becomes a
                    // strong red contour. The width stays compact so armour
                    // details do not turn into a dirty halo.
                    bool hostile = entities.Side[i] == Faction.Orvill;
                    block.SetFloat(OutlineWidthId, hovered ? 1.55f : hostile ? 1.24f : 1.18f);
                    block.SetColor(OutlineColorId, hovered
                        ? new Color(1f, 0.055f, 0.035f, 1f)
                        : hostile
                            ? new Color(0.52f, 0.035f, 0.055f, 1f)
                            : new Color(0.025f, 0.13f, 0.17f, 1f));

                    ApplyRendererPropertyBlock(bodyRenderers, materialSlotCounts, block);
                }

                // Парабола выброса. Считается от времени смерти, а не
                // накапливается по кадрам: накопление разъезжается при просадке
                // кадров и при hit-stop, а тут важно, чтобы тело и растворение
                // шли по одним часам.
                bool inDeathFlight = !alive && orvill && _deathStarted[i]
                                     && _deathLaunch[i].sqrMagnitude > 0.0001f;
                if (inDeathFlight)
                {
                    float t = deathElapsed;
                    Vector3 flight = _deathLaunch[i] * t;
                    flight.y -= 0.5f * DeathGravity * t * t;
                    // Под землю не проваливаемся: тело гаснет в воздухе или
                    // у самой земли, но никогда не уходит сквозь пол.
                    if (flight.y < 0f) flight.y = 0f;
                    p += flight;
                }

                view.position = p + _hitRecoil[i];
                if (_baseScale[i] != Vector3.zero)
                    view.localScale = _baseScale[i];

                if (inDeathFlight)
                {
                    // Кувырок через голову в сторону удара. Клип умирания при
                    // такой длительности всё равно не успевает прочитаться —
                    // силуэт в полёте несёт всю информацию сам.
                    view.rotation = Quaternion.AngleAxis(
                        DeathSpinSpeed * deathElapsed, _deathSpinAxis[i])
                        * Quaternion.LookRotation(
                            _lastFacingWorld[i].sqrMagnitude > 0.0001f
                                ? _lastFacingWorld[i]
                                : Vector3.forward, Vector3.up);
                    continue;
                }

                // Gameplay-facing остаётся мгновенным и живёт в Sim. Только
                // корень живого ORVILL мягко догоняет новый yaw: 30 Hz повороты
                // больше не выглядят ступенчатыми, но атаки по-прежнему решаются
                // по авторитетному направлению без presentation-задержки.
                FixVec2 facing = entities.Facing[i];
                if (facing.LengthSq.Raw != 0)
                {
                    Vector3 facingWorld = new Vector3(
                        facing.X.ToFloat(), 0f, facing.Y.ToFloat()).normalized;
                    if (moving && velocityMagnitude > 0.0001f)
                    {
                        Vector3 velocityWorld = new Vector3(
                            velocity.X.ToFloat(), 0f, velocity.Y.ToFloat()) / velocityMagnitude;
                        Vector3 rightWorld = Vector3.Cross(Vector3.up, facingWorld);
                        localMoveX = Vector3.Dot(velocityWorld, rightWorld);
                        localMoveY = Vector3.Dot(velocityWorld, facingWorld);
                    }
                    CharacterAnimatorView animation = _animationViews[i];
                    if (animation != null && animation.UsesSprites)
                    {
                        view.rotation = Quaternion.identity;
                        animation.FaceCamera(facingWorld);
                    }
                    else
                    {
                        Vector3 previousFacing = _lastFacingWorld[i];
                        if (!moving && previousFacing.sqrMagnitude > 0.5f)
                        {
                            float delta = Vector3.SignedAngle(previousFacing, facingWorld, Vector3.up);
                            if (Mathf.Abs(delta) > 0.1f)
                            {
                                _turnVisualDirection[i] = Mathf.Sign(delta);
                                // Симуляция обновляется 30 раз/с, View чаще.
                                // Hold перекрывает промежуточные render-кадры,
                                // чтобы Turn-параметр не мигал 1/0/1/0.
                                _turnVisualUntil[i] = Time.time + 0.08f;
                            }
                        }
                        if (moving) _turnVisualUntil[i] = 0f;
                        if (Time.time < _turnVisualUntil[i])
                            turnDirection = _turnVisualDirection[i];
                        _lastFacingWorld[i] = facingWorld;

                        Vector3 visualFacing = facingWorld;
                        if (orvill)
                        {
                            Vector3 previousVisual = _visualFacingWorld[i];
                            if (!alive && previousVisual.sqrMagnitude > 0.5f)
                            {
                                // Смерть фиксирует ориентацию кадра контакта:
                                // труп не доворачивается к уже сменившейся цели.
                                visualFacing = previousVisual;
                            }
                            else if (previousVisual.sqrMagnitude > 0.5f)
                            {
                                float turnBlend = 1f - Mathf.Exp(-OrvillTurnSharpness * Time.deltaTime);
                                visualFacing = Vector3.Slerp(
                                    previousVisual, facingWorld, turnBlend).normalized;
                            }
                            _visualFacingWorld[i] = visualFacing;
                        }
                        else
                        {
                            _visualFacingWorld[i] = facingWorld;
                        }

                        // Доворот на случай, если модель экспортировали лицом
                        // не туда: разворачивать сам меш дороже, чем повернуть
                        // корень одним числом.
                        view.rotation = Quaternion.LookRotation(visualFacing, Vector3.up)
                                        * Quaternion.Euler(0f, ModelYaw, 0f);
                    }
                }

                // Тело остаётся видимым до конца death-клипа. Симуляция уже
                // считает сущность мёртвой; эта задержка существует только в View.
                if (!alive)
                {
                    bool showDeath = _deathStarted[i] && Time.time < _deathUntil[i];
                    if (!showDeath)
                    {
                        ReleaseEntityView(i);
                        continue;
                    }

                    if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                    continue;
                }

                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                _animationViews[i]?.SetLocomotion(
                    moving, turnDirection, normalizedMoveSpeed, localMoveX, localMoveY);
            }
        }

        private void SyncAnimationEvents()
        {
            var events = _driver.FrameEvents;
            var eventContexts = _driver.FrameEventContexts;
            EntityStore entities = _driver.Sim.Entities;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                switch (e.Type)
                {
                    case SimEventType.Attack:
                        AnimationOf(e.Source)?.PlayAttack(e.Amount);
                        break;
                    case SimEventType.AbilityCast:
                        if ((uint)e.Amount < Simulation.AbilitySlots)
                        {
                            AbilityBuild build = _driver.Sim.GetAbility(e.Amount);
                            if (build != null)
                                AnimationOf(e.Source)?.PlayAbilityDefinition(build.DefinitionId);
                        }
                        break;
                    case SimEventType.Damage:
                        // На летальном тике состояние Sim уже финальное. Не
                        // запускаем Hit за несколько строк до DeathBack: иначе
                        // Animator успевает показать неправильный recoil-кадр.
                        if ((uint)e.Target < (uint)entities.Count && entities.Alive[e.Target])
                            AnimationOf(e.Target)?.PlayHit(HitVariantFor(in e, entities));
                        // A basic attack's contact pose is confirmed by the
                        // same Damage event that drives hit VFX and hit-stop.
                        // This keeps the authored blade pose and the actual
                        // health change on one presentation boundary.
                        if (e.Source == Simulation.PlayerId
                            && e.DamageOrigin == DamageOrigin.BasicAttack)
                            AnimationOf(e.Source)?.PlayAttackContact(e.ActionVariant);
                        // ChainStep's five-tick clip is one hop, not the whole
                        // chain. Simulation has already scheduled the next
                        // Lunge by the time this contact event reaches View;
                        // restart only when that authoritative next hop exists,
                        // so the final hit cannot create a phantom fifth jump.
                        FrameEventContext context = i < eventContexts.Count
                            ? eventContexts[i]
                            : default;
                        if (e.Source == Simulation.PlayerId
                            && e.DamageOrigin == DamageOrigin.Ability
                            && IsAbilityDefinition(e.ActionVariant, AbilityDefinition.ChainStepId)
                            && context.Event.Type == e.Type
                            && context.Event.Source == e.Source
                            && context.Event.Target == e.Target
                            && context.SourceForcedTicksLeft > 0
                            && context.SourceForcedKind == (byte)ForcedMotionKind.Lunge)
                            AnimationOf(e.Source)?.PlayChainStepRepeat();
                        break;
                    case SimEventType.Death:
                        CharacterAnimatorView animation = AnimationOf(e.Target);
                        if (animation == null) break;
                        animation.PlayDeath();
                        _deathStarted[e.Target] = true;
                        _deathStartedAt[e.Target] = Time.time;
                        float presentationDuration = entities.Side[e.Target] == Faction.Orvill
                            ? OrvillDeathPresentationDuration
                            : animation.DeathDuration;
                        _deathUntil[e.Target] = Time.time + presentationDuration;
                        break;
                }
            }
        }

        private CharacterAnimatorView AnimationOf(int entity)
            => entity >= 0 && entity < _boundCount ? _animationViews[entity] : null;

        private bool IsAbilityDefinition(int slot, int definitionId)
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots) return false;
            AbilityBuild build = sim.GetAbility(slot);
            return build != null && build.DefinitionId == definitionId;
        }

        private static int HitVariantFor(in SimEvent hit, EntityStore entities)
        {
            int fallback = hit.Source ^ hit.Target;
            if ((uint)hit.Source >= (uint)entities.Count
                || (uint)hit.Target >= (uint)entities.Count)
                return fallback;

            FixVec2 facing = entities.Facing[hit.Target];
            FixVec2 source = entities.Position[hit.Source];
            FixVec2 target = entities.Position[hit.Target];
            float toSourceX = source.X.ToFloat() - target.X.ToFloat();
            float toSourceY = source.Y.ToFloat() - target.Y.ToFloat();
            float facingX = facing.X.ToFloat();
            float facingY = facing.Y.ToFloat();
            if (facingX * facingX + facingY * facingY < 0.0001f
                || toSourceX * toSourceX + toSourceY * toSourceY < 0.0001f)
                return fallback;

            // Positive 2D cross means the source is on the target's left.
            // Directly front/back is geometrically ambiguous, so dot supplies
            // a stable choice instead of flickering around a zero cross value.
            float cross = facingX * toSourceY - facingY * toSourceX;
            if (Mathf.Abs(cross) > 0.0001f) return cross > 0f ? 0 : 1;

            float dot = facingX * toSourceX + facingY * toSourceY;
            return dot >= 0f ? 0 : 1;
        }

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

            GameObject anchorPrefab = faction == Faction.Wole
                ? Resources.Load<GameObject>(WoleAnchorPrefab)
                : null;
            Material anchorMaterial = anchorPrefab != null
                ? BuildRuntimeMaterial(WoleAnchorBaseColor, faction)
                : null;

            return () =>
            {
                GameObject body = CreateCharacterBody(prefab, faction, scale, material, controller,
                    weaponPrefab, WoleWeaponSocket,
                    WoleWeaponLocalPosition, WoleWeaponLocalDirection, WoleWeaponLocalRoll,
                    WoleWeaponLocalScale,
                    weaponMaterial);

                // Якорь вешается ВТОРЫМ пропсом на ту же механику, что и сабля.
                // Отдельного кода крепления нет и не надо: MountRigidProp уже
                // умеет искать кость и сажать на неё меш.
                if (body != null && anchorPrefab != null
                    && !MountRigidProp(body.transform, anchorPrefab, WoleAnchorSocket,
                        WoleAnchorLocalPosition, WoleAnchorLocalDirection,
                        WoleAnchorLocalRoll, WoleAnchorLocalScale, anchorMaterial))
                {
                    Debug.LogWarning($"[Разлом] Якорь не сел на «{WoleAnchorSocket}»: " +
                                     "кости с таким именем в риге нет.");
                }

                return body;
            };
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

            // Pelag's 4K atlas already contains painted form and fine ink. The
            // character shader therefore uses restrained three-tone lighting
            // plus a screen-space outline instead of the old dark two-band
            // world-space hull that crushed detail and shimmered in motion.
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
            AssignBaseTexture(material, texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            // Мультяшной фигуре блик по всей поверхности мешает: он забивает
            // силуэт, а силуэт здесь главный канал распознавания.
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", faction == Faction.Wole ? 0.22f : 0.08f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_ShadowColor"))
                material.SetColor("_ShadowColor", ViewMaterials.ToonShadow);
            if (material.HasProperty("_MidColor"))
                material.SetColor("_MidColor", new Color(0.94f, 0.90f, 0.91f, 1f));
            if (material.HasProperty("_MidThreshold")) material.SetFloat("_MidThreshold", 0.24f);
            if (material.HasProperty("_LightThreshold")) material.SetFloat("_LightThreshold", 0.62f);
            if (material.HasProperty("_LightFeather")) material.SetFloat("_LightFeather", 0.045f);
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 1.10f);

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
            float comicScale = SpriteScaleMultiplier(faction);
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
            Vector3 weaponLocalPosition, Vector3 weaponLocalDirection, float weaponLocalRoll,
            float weaponLocalScale,
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
                        weaponLocalPosition, weaponLocalDirection, weaponLocalRoll,
                        weaponLocalScale, weaponMaterial))
                    Debug.LogWarning($"[Разлом] Сабля не установлена: prefab={(weaponPrefab != null)}, " +
                                     $"socket={weaponSocket}.");
            }

            // Даже при мягкой directional-тени маленькая изометрическая модель
            // выглядела подвешенной над светлым полом. Небольшая контактная
            // тень возвращает ногам опору и делает направление света заметным,
            // не вмешиваясь в физику или детерминированное положение тела.
            CreateContactShadow(go.transform, faction, scale);

            CharacterAnimatorView animation = go.GetComponent<CharacterAnimatorView>();
            if (animation == null) animation = go.AddComponent<CharacterAnimatorView>();
            animation.Configure(faction);
            return go;
        }

        private static void CreateContactShadow(Transform character, Faction faction, float rootScale)
        {
            if (_contactShadowSprite == null)
            {
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Runtime Contact Shadow",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = 1f - Mathf.SmoothStep(0.16f, 1f, radius);
                    pixels[y * size + x] = new Color32(17, 19, 25,
                        (byte)Mathf.RoundToInt(alpha * 150f));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _contactShadowSprite = Sprite.Create(texture,
                    new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
                _contactShadowSprite.name = "Runtime Contact Shadow";
            }

            GameObject shadow = new GameObject(ContactShadowName);
            shadow.transform.SetParent(character, false);
            shadow.transform.localPosition = new Vector3(0f, 0.014f / rootScale, 0f);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float worldDiameter = faction == Faction.Wole ? 1.05f : 0.88f;
            shadow.transform.localScale = Vector3.one * (worldDiameter / rootScale);
            SpriteRenderer renderer = shadow.AddComponent<SpriteRenderer>();
            renderer.sprite = _contactShadowSprite;
            renderer.color = new Color(0.17f, 0.18f, 0.23f, 0.56f);
            renderer.sortingOrder = -200;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static bool MountRigidProp(Transform character, GameObject weaponPrefab,
            string socketName, Vector3 localPosition,
            Vector3 localDirection, float localRoll, float localScale, Material materialOverride)
        {
            if (weaponPrefab == null) return false;

            Transform socket = FindChild(character, socketName);
            if (socket == null) return false;

            GameObject mounted = Instantiate(weaponPrefab, socket, false);
            mounted.name = weaponPrefab.name + "_Equipped";
            mounted.transform.localPosition = localPosition;
            Quaternion aim = localDirection.sqrMagnitude > 0.0001f
                ? Quaternion.FromToRotation(Vector3.up, localDirection.normalized)
                : Quaternion.identity;
            mounted.transform.localRotation = aim * Quaternion.AngleAxis(localRoll, Vector3.up);
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
