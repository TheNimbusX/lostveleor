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
        public int PrewarmRootSwarm = 6;

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

        // ЗАМЕРЕНО, А НЕ ПОДОБРАНО. Меш Лесного стража приходит высотой 0.98
        // единицы — ровно как тело Пелага, — а стояло здесь 1.0, и моб выходил
        // ростом в метр против геройских 1.78.
        //
        // 2.0 давало 1.96 м — на десятую выше героя. По просьбе владельца ещё
        // +20 %: 2.4 это 2.35 м, страж заметно возвышается над Пелагом.
        // Меняешь модель — дели желаемый рост в метрах на высоту меша, а не
        // крути на глаз.
        //
        // ВНИМАНИЕ: радиус тела в симуляции остался 0.62 м и с масштабом не
        // связан. Чем крупнее модель, тем сильнее толпа налезает друг на друга
        // в кадре. Лечится либо радиусом в Sim (правка ядра, с тестами), либо
        // масштабом обратно — это решение владельца, а не автора правки.
        public float OrvillScale = 2.4f;

        [Header("Корнеполз")]
        // Bind pose измерена в FBX: 0.661713 м. Бестиарий задаёт рост 1.05 м.
        public float RootSwarmScale = 1.05f / 0.661713f;
        // Основной FBX без костей; выгрузка Idle содержит то же тело с ригом.
        public string RootSwarmModel = "Characters/Forest_RootSwarm/Forest_RootSwarm@Idle";
        public string RootSwarmController = "Characters/Forest_RootSwarm/Forest_RootSwarm_Combat";
        public string RootSwarmMaterial = "Characters/Forest_RootSwarm/Forest_RootSwarm_Material";
        public string RootSwarmTexture = "Characters/Forest_RootSwarm/Forest_RootSwarm_BaseColor";

        [Header("Модели персонажей")]
        [Tooltip("Путь модели в Resources. Пусто — рисованные спрайты, как было.")]
        public string WoleModel = "Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig";

        // ПЕРВЫЙ МОБ ПАКА — Лесной страж, 3 сентября. Орвилл снят: модель
        // получилась кривой, и держать её ради истории смысла нет.
        //
        // Страж приехал конвейером Tripo → Mixamo → Unity: тело Humanoid на
        // стандартном 65-костном скелете Mixamo, клипы лежат рядом файлами
        // «Forest_Guardian@<Роль>.fbx», контроллер собирает
        // RazlomMobAnimatorBuilder из того, что нашлось.
        //
        // ВАЖНО ПРО ФРАКЦИЮ: `Faction.Orvill` — это сторона в симуляции, а не
        // этот конкретный персонаж. Она остаётся; сменилось только тело,
        // которое ею рисуется. Поля ниже поэтому и зовутся Orvill*.
        [Tooltip("Анимируемая 3D-модель моба в Resources.")]
        public string OrvillModel = "Characters/Forest_Guardian/Forest_Guardian";

        // Игровая модель и клипы используют один Mixamo-скелет. Generic выбран
        // намеренно: так ноги, кисти и пальцы проигрываются без ретаргета.
        [Tooltip("Контроллер анимаций в Resources. У сырой модели Animator приходит " +
                 "пустым, и без контроллера персонаж стоит столбом.")]
        public string WoleController = "Characters/Pelag_v5/Pelag_v5_FullCombat";

        // Собирается скриптом, как и у Пелага: меню «Разлом → Собрать
        // контроллеры мобов». Руками его править бесполезно — пересоберётся.
        public string OrvillController =
            "Characters/Forest_Guardian/Forest_Guardian_Combat";

        // Пусто: раскраска приехала внутри FBX отдельным материалом на каждую
        // часть тела, и постпроцессор импорта уже перевёл их на тун-шейдер.
        // Подстановка одного материала на всю модель стёрла бы это.
        [Tooltip("Материал персонажа в Resources. Пусто — материал берётся из модели.")]
        public string WoleMaterial = "";

        // МАТЕРИАЛ МОБА — АССЕТ, А НЕ СБОРКА В КОДЕ.
        //
        // Пусто здесь означало «собери материал на лету из текстуры», и это
        // было удобно ровно до тех пор, пока у шейдера не появились ручки
        // растворения. Собранный в коде материал нельзя ни выделить, ни
        // покрутить: владелец открывал .shader и закономерно не находил в нём
        // ни одного слайдера — свойства живут на материале, а материала не
        // существовало до старта игры.
        //
        // Теперь игра берёт готовый ассет. Что покрутил в инспекторе — то и
        // будет в бою. Вернуть прежнее поведение: поставить сюда пустую строку.
        public string OrvillMaterial = "Characters/Forest_Guardian/Forest_Guardian_Material";

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

        public string OrvillTexture = "Characters/Forest_Guardian/Forest_Guardian_BaseColor";

        [Header("Модульное снаряжение героя")]
        [Tooltip("Отдельный prefab оружия. Он не связан с мешем тела и меняется через сокет.")]
        public string WoleWeaponPrefab = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber";
        public string WoleWeaponBaseColor = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_BaseColor";
        public string WoleWeaponNormal = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_Normal";
        public string WoleWeaponMetallic = "Weapons/Pelag/FantasySaber/Pelag_FantasySaber_Metallic";
        public string WoleWeaponSocket = "mixamorig:RightHand";
        // ВСЕ ТРИ ЧИСЛА СНЯТЫ С ЖИВОГО ПЕРСОНАЖА в Play Mode на теле v6 и
        // перенесены из инспектора как есть. Тело сменилось — руки другой
        // длины, и прежние значения, подобранные под v5, промахивались.
        //
        // Правится так же: запусти игру, найди в иерархии
        // Pelag_FantasySaber_Equipped, потаскай гизмо, перепиши сюда.
        public Vector3 WoleWeaponLocalPosition = new Vector3(-0.019f, 0.08f, 0.019f);
        public Vector3 WoleWeaponLocalRotation = new Vector3(4.778f, -0.701f, 77.315f);
        // Масштаб НЕРАВНОМЕРНЫЙ — так получилось при подгонке гизмо на
        // повёрнутом объекте. Клинок слегка сплющен поперёк; на изометрии это
        // не читается. Если понадобится ровный — 0.5 по всем осям.
        public Vector3 WoleWeaponLocalScale =
            new Vector3(0.5722176f, 0.4797959f, 0.4378099f);

        [Tooltip("Кость, на которой сабля лежит в бытовом idle. Ножен в ассете нет, " +
                 "поэтому это голый клинок за кушаком.")]
        public string WoleWeaponStoredSocket = "mixamorig:Hips";
        public Vector3 WoleWeaponStoredLocalPosition =
            new Vector3(-0.12f, 0.015f, 0.065f);
        public Vector3 WoleWeaponStoredLocalRotation =
            new Vector3(0.435f, 89.205f, 237.383f);
        public Vector3 WoleWeaponStoredLocalScale =
            new Vector3(0.5722176f, 0.4797959f, 0.4378099f);

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

        // Тоже снято с тела v6 в Play Mode. Инспектор показывал углы
        // (-30.773, -355.468, 393.829) — здесь они приведены в привычный
        // диапазон, поворот от этого не меняется: -355.468 это те же 4.532,
        // а 393.829 — те же 33.829.
        [Tooltip("Смещение от кости таза. Правится в Play Mode на объекте " +
                 "Pelag_AnchorGrip_Equipped, потом переписывается сюда.")]
        public Vector3 WoleAnchorLocalPosition = new Vector3(0.114f, -0.0545f, -0.0722f);
        public Vector3 WoleAnchorLocalRotation = new Vector3(-30.773f, 4.532f, 33.829f);
        public Vector3 WoleAnchorLocalScale = new Vector3(0.55f, 0.55f, 0.55f);

        [Tooltip("Кость левой руки для короткого anchor-use окна. Грип один и тот же " +
                 "объект: он перепривязывается сюда и затем возвращается на пояс.")]
        public string WoleAnchorEquippedSocket = "mixamorig:LeftHand";
        public Vector3 WoleAnchorEquippedLocalPosition =
            new Vector3(0.012f, 0.035f, -0.012f);
        public Vector3 WoleAnchorEquippedLocalRotation =
            new Vector3(270.02f, 0f, 0f);
        public Vector3 WoleAnchorEquippedLocalScale = new Vector3(0.55f, 0.55f, 0.55f);

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
        private ViewPool _rootSwarmPool;
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
        private Vector3 _playerAbilityFacing;
        private PelagEquipmentView[] _equipmentViews;
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
        private Vector3 _previousPlayerPosition;
        private bool _hasPreviousPlayerPosition;
        private Vector3[] _lastFacingWorld;
        private Vector3[] _visualFacingWorld;

        // Куда тело поставили в прошлом кадре. Нужно только сторожу скачков:
        // симуляция уже проверена и не прыгает, значит рывок — здесь, и его
        // надо поймать с разбором на слагаемые, а не на глаз.
        private Vector3[] _lastRenderPosition;
        private bool[] _hasLastRenderPosition;
        private float[] _turnVisualUntil;
        private float[] _turnVisualDirection;
        private const float CombatThreatDistance = 6.5f;
        private const float CombatGraceSeconds = 3f;
        private const float AnchorFallbackSeconds = 2.4f;
        private float _playerCombatUntil;
        private float _playerAnchorFallbackUntil;
        private bool _playerCombatReady;
        private bool _anchorSaberSuppressed;
        // Только presentation-offset: capture/demo может показать рывок или
        // сопротивление цепи, не меняя детерминированную позицию в Sim.
        private Vector3[] _presentationOffset;
        private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        private static readonly int DeathFadeId = Shader.PropertyToID("_DeathFade");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        // КОНТУР ВРАГА. Ширина задана в пикселях экрана, а не в метрах: на
        // ортокамере метр — это фиксированное число пикселей, но кромка должна
        // остаться одинаковой и при зуме удара, и при смене разрешения.
        //
        // Цвета лежат за единицей намеренно. Порог Bloom в CombatLook — 1.05,
        // и наружный ореол вокруг силуэта рисует именно блум; опусти эти числа
        // под порог, и вместо свечения останется плоская цветная рамка.
        //
        // ЗА ПОРОГ ВЫХОДИТ ТОЛЬКО КРАСНЫЙ. Первый заход поднял и зелёный —
        // 1.35 при пороге 1.05, — и светиться начали оба канала сразу.
        // Tonemapping здесь Neutral: он давит вырвавшийся красный сильнее,
        // чем зелёный, тот догоняет, и тёплое свечение выходит жёлтым.
        // Держим G и B под порогом — тогда ореол остаётся красно-оранжевым.
        // Контур должен отделять силуэт, а не становиться главным объектом
        // кадра. Раньше три shell-прохода на ширине 1.6/2.0 px давали
        // горячую HDR-рамку и слипались между соседними мобами. Теперь
        // обычный враг получает спокойный тёплый акцент, а hover лишь слегка
        // усиливает его для выбора цели.
        private const float HeroOutlineWidth = 1.15f;
        private const float HostileOutlineWidth = 1.25f;
        private const float HoveredOutlineWidth = 1.65f;
        private static readonly Color HeroOutlineColor = new Color(0.024f, 0.012f, 0.008f, 1f);
        // CombatLook поднимает насыщенность на 46: приглушённый исходник
        // после цветокоррекции становится охристо-оранжевым, как в концепте.
        private static readonly Color HostileOutlineColor = new Color(0.70f, 0.46f, 0.30f, 1f);
        private static readonly Color HoveredOutlineColor = new Color(0.90f, 0.62f, 0.38f, 1f);
        private static Sprite _contactShadowSprite;
        private const string ContactShadowName = "Contact Shadow";
        private const float WoleSpriteScaleMultiplier = 0.78f;
        private const float OrvillSpriteScaleMultiplier = 0.82f;
        // СМЕРТЬ МОБА: УПАЛ — ПОЛЕЖАЛ — ОСЫПАЛСЯ. Порядок задан владельцем и
        // ровно в таком порядке эти три числа и стоят.
        //
        // ЧИСЛА ВЗЯТЫ ИЗ ЗАМЕРА КЛИПА, А НЕ НА ГЛАЗ. Forest_Guardian@Mutant
        // Dying прогнан покадрово в Blender по высоте таза:
        //
        //     кадры  1–9   таз на 0.430 — существо просто СТОИТ (0.27 с);
        //     кадры  9–24  оседание, таз 0.430 → 0.388;
        //     кадры 24–42  собственно падение, 0.388 → 0.091;
        //     кадры 42–73  таз 0.090, голова 0.130 — мёртвая заморозка (1.0 с).
        //
        // Отсюда и показ: начинаем клип с 24-го кадра (см. OrvillDeathClipStart
        // в CharacterAnimatorView) и держим 22 кадра до приземления — это
        // 0.73 с при 30 fps. Ни стоячего вступления, ни замороженного хвоста в
        // кадре нет: обе эти части и создавали «застыл в непонятной позе».
        //
        // Прежние 0.16 + 0.30 не позволяли увидеть падение в принципе: за
        // 0.16 с клип на скорости 0.67 доходил до девятого кадра, где тело ещё
        // стоит. Всё «падение» в кадре делал выброс, которого больше нет.
        private const float OrvillDeathAnimationDuration = 0.73f;

        /// <summary>Доля секунды на приземлившейся позе, прежде чем осыпаться.</summary>
        private const float OrvillDeathPoseHoldDuration = 0.14f;

        /// <summary>
        /// Растворение. ДЛИНА СВЯЗАНА СО ЗВУКОМ: осыпание в CombatAudio длится
        /// столько же и стартует ровно в начале этого окна. Меняешь здесь —
        /// перережь клип и поправь CombatAudio.DissolveDelay.
        /// </summary>
        private const float OrvillDeathFadeDuration = 0.50f;

        // Единая точка синхронизации View и CombatAudio. Звук осыпания
        // стартует в тот же момент, когда DeathFade становится больше нуля.
        public const float OrvillDeathDissolveStartDelay =
            OrvillDeathAnimationDuration + OrvillDeathPoseHoldDuration;

        public static float DeathDissolveStartDelay(EnemyKind kind)
            => (kind == EnemyKind.ForestRootSwarm ? 50f / 30f / 2f : OrvillDeathAnimationDuration)
               + OrvillDeathPoseHoldDuration;

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
            _equipmentViews = new PelagEquipmentView[capacity];
            _deathUntil = new float[capacity];
            _deathStarted = new bool[capacity];
            _deathStartedAt = new float[capacity];
            _hitRecoil = new Vector3[capacity];
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
            _lastRenderPosition = new Vector3[capacity];
            _hasLastRenderPosition = new bool[capacity];
            _turnVisualUntil = new float[capacity];
            _turnVisualDirection = new float[capacity];

            Transform woleRoot = new GameObject("Пул: Wole").transform;
            Transform orvillRoot = new GameObject("Пул: Orvill").transform;
            Transform swarmRoot = new GameObject("Пул: Forest_RootSwarm").transform;
            woleRoot.SetParent(transform, false);
            orvillRoot.SetParent(transform, false);
            swarmRoot.SetParent(transform, false);

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
            _rootSwarmPool = new ViewPool(swarmRoot,
                BodyFactory(RootSwarmModel, RootSwarmController, RootSwarmMaterial, RootSwarmTexture,
                    Faction.Orvill, RootSwarmScale),
                PrewarmRootSwarm > 0 ? PrewarmRootSwarm : capacity);

            Transform projectileRoot = new GameObject("Пул: снаряды").transform;
            projectileRoot.SetParent(transform, false);

            Material projectileMat = ViewMaterials.CreateLit(ProjectileColor);
            _projectilePool = new ViewPool(projectileRoot,
                () => CreateBody(PrimitiveType.Sphere, projectileMat, ProjectileScale),
                PrewarmProjectiles);

            _projectileViews = new Transform[capacity];

            BindNewEntities();
        }

        /// <summary>
        /// Догревает пулы по нескольку объектов за кадр.
        ///
        /// Прогрев остаётся прогревом — он просто перестал занимать треть
        /// секунды одним куском. Четыре за кадр это около 20 мс на шестьдесят
        /// объектов, размазанных по шестнадцати кадрам.
        /// </summary>
        private void StepPrewarm()
        {
            const int PerFrame = 4;
            if (_orvillPool != null && _orvillPool.NeedsPrewarm) _orvillPool.PrewarmStep(PerFrame);
            else if (_rootSwarmPool != null && _rootSwarmPool.NeedsPrewarm) _rootSwarmPool.PrewarmStep(PerFrame);
            else if (_wolePool != null && _wolePool.NeedsPrewarm) _wolePool.PrewarmStep(PerFrame);
            else if (_projectilePool != null && _projectilePool.NeedsPrewarm)
                _projectilePool.PrewarmStep(PerFrame);
        }

        private void LateUpdate()
        {
            // LateUpdate, а не Update: к этому моменту TickDriver уже сделал все
            // шаги кадра и выставил Alpha, по которой интерполируется отрисовка.
            StepPrewarm();

            Simulation sim = _driver.Sim;
            if (sim == null)
            {
                // Вышли в лагерь: рисовать нечего, и всё занятое надо вернуть,
                // иначе в лагере остались бы стоять враги прошлого Разлома.
                if (_initialized && _boundCount > 0) ReleaseEverything();
                _playerCombatUntil = 0f;
                _playerAnchorFallbackUntil = 0f;
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
            UpdatePlayerEquipmentIntent();
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

            // ВЫБРОС И КУВЫРОК УБРАНЫ. Они добавляли убийству вес, пока показ
            // смерти шёл 0.46 с и падения в кадре не было вовсе. Теперь падение
            // играется целиком, и вес несёт оно; полёт поверх него читался как
            // «завис в воздухе и растворился непонятно где».
            //
            // Направление всё ещё считается: по нему разворачивается вспышка, и
            // оно же остаётся точкой расширения, если выброс когда-нибудь
            // вернут — но уже как короткий толчок ДО падения, а не вместо него.
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

        /// <summary>
        /// Presentation-only combat intent. This never changes Simulation: it
        /// only selects Pelag's idle state and the matching saber mount.
        /// Explicit action callers use this method to make the hand pose and
        /// equipment respond in the same render frame as the action request.
        /// </summary>
        public void SetPlayerCombatReady(bool ready)
        {
            ApplyPlayerCombatReady(ready, force: true);
        }

        public void BeginPlayerAnchorUse(bool leap = false)
        {
            _playerAnchorFallbackUntil = Time.unscaledTime + AnchorFallbackSeconds;
            _anchorSaberSuppressed = true;
            ApplyPlayerCombatReady(false, force: true);
            if (_equipmentViews != null && Simulation.PlayerId < _boundCount)
                _equipmentViews[Simulation.PlayerId]?.BeginAnchorUse(leap);
        }

        public void EndPlayerAnchorUse()
        {
            _playerAnchorFallbackUntil = 0f;
            if (_equipmentViews != null && Simulation.PlayerId < _boundCount)
                _equipmentViews[Simulation.PlayerId]?.EndAnchorUse();
            // EndAnchorUse uses the current intent. Re-apply it in case the
            // player became quiet while the anchor motion was in flight.
            ApplyPlayerCombatReady(_playerCombatReady, force: true);
        }

        public void SetPlayerAbilityFacing(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f) _playerAbilityFacing = direction.normalized;
        }

        public Vector3 PlayerChainHandPosition
        {
            get
            {
                Transform hand = _equipmentViews != null && Simulation.PlayerId < _boundCount
                    ? _equipmentViews[Simulation.PlayerId]?.ChainHand : null;
                return hand != null ? hand.position : Vector3.zero;
            }
        }

        public Vector3 PlayerAnchorHeadPosition => _equipmentViews != null && Simulation.PlayerId < _boundCount
            && _equipmentViews[Simulation.PlayerId] != null ? _equipmentViews[Simulation.PlayerId].AnchorHeadPosition
            : PlayerChainHandPosition;

        public void PlayPlayerAttackPresentation()
        {
            if (!_initialized || _boundCount <= Simulation.PlayerId) return;
            MarkPlayerCombatActivity();
            _animationViews[Simulation.PlayerId]?.PlayAttack();
        }

        public void PlayPlayerAbilityPresentation(int slot)
        {
            if (!_initialized || _boundCount <= Simulation.PlayerId) return;
            AbilityBuild build = _driver != null && _driver.Sim != null
                && (uint)slot < Simulation.AbilitySlots
                ? _driver.Sim.GetAbility(slot)
                : null;
            if (build != null)
                PlayPlayerAbilityPresentation(slot, build.DefinitionId);
            else
            {
                MarkPlayerCombatActivity();
                _animationViews[Simulation.PlayerId]?.PlayAbility(slot);
            }
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
            if (definitionId == AbilityDefinition.AnchorLeapId
                || definitionId == AbilityDefinition.ChainCycloneId)
                BeginPlayerAnchorUse(definitionId == AbilityDefinition.AnchorLeapId);
            else { _anchorSaberSuppressed = false; EndPlayerAnchorUse(); MarkPlayerCombatActivity(); }
            _animationViews[Simulation.PlayerId]?.PlayAbilityDefinition(definitionId);
        }

        private void MarkPlayerCombatActivity()
        {
            _playerCombatUntil = Mathf.Max(_playerCombatUntil,
                Time.unscaledTime + CombatGraceSeconds);
            if (!_anchorSaberSuppressed) ApplyPlayerCombatReady(true, force: true);
        }

        private void ApplyPlayerCombatReady(bool ready, bool force)
        {
            _playerCombatReady = ready;
            if (!_initialized || _animationViews == null || _boundCount <= Simulation.PlayerId)
                return;

            CharacterAnimatorView animation = _animationViews[Simulation.PlayerId];
            PelagEquipmentView equipment = _equipmentViews != null
                ? _equipmentViews[Simulation.PlayerId]
                : null;
            if (!force && animation != null && animation.CombatReady == ready
                && (equipment == null || equipment.CombatReady == ready))
                return;

            animation?.SetCombatReady(ready);
            equipment?.SetCombatReady(ready);
        }

        private void UpdatePlayerEquipmentIntent()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            EntityStore entities = sim != null ? sim.Entities : null;
            if (entities == null || entities.Count <= Simulation.PlayerId
                || !entities.Alive[Simulation.PlayerId])
            {
                _playerCombatUntil = 0f;
                // Death/teardown must clear the anchor even when its fallback
                // timer was already consumed or was never started.
                EndPlayerAnchorUse();
                ApplyPlayerCombatReady(false, force: true);
                return;
            }

            if (_playerAnchorFallbackUntil > 0f
                && Time.unscaledTime >= _playerAnchorFallbackUntil)
                EndPlayerAnchorUse();

            bool threatened = (_driver != null && _driver.AttackHeld)
                              || HasPlayerCombatThreat(sim, entities);
            bool ready = CaptureRig.EquipmentShowcase ? CaptureRig.EquipmentReady
                : !string.IsNullOrEmpty(CaptureRig.PoseShowcase) ? CaptureRig.PoseShowcase == "combat-idle"
                : !_anchorSaberSuppressed && (threatened || Time.unscaledTime < _playerCombatUntil);
            ApplyPlayerCombatReady(ready, force: false);
        }

        private static bool HasPlayerCombatThreat(Simulation sim, EntityStore entities)
        {
            int player = Simulation.PlayerId;
            int target = sim.AttackTarget;
            if (target > player && target < entities.Count
                && entities.Alive[target]
                && entities.Side[target] != entities.Side[player])
                return true;

            FixVec2 playerPosition = entities.Position[player];
            const float threatDistanceSq = CombatThreatDistance * CombatThreatDistance;
            for (int i = 0; i < entities.Count; i++)
            {
                if (i == player || !entities.Alive[i]) continue;
                if (entities.Side[i] == entities.Side[player]) continue;

                if (entities.PendingAttackTarget[i] == player)
                    return true;

                FixVec2 delta = entities.Position[i] - playerPosition;
                float distanceSq = delta.X.ToFloat() * delta.X.ToFloat()
                                   + delta.Y.ToFloat() * delta.Y.ToFloat();
                if (distanceSq <= threatDistanceSq) return true;
            }
            return false;
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
                _playerCombatUntil = 0f;
                _playerAnchorFallbackUntil = 0f;
                _playerCombatReady = false;
                return;
            }

            for (int i = 0; i < _boundCount; i++)
                ReleaseEntityView(i);

            _playerBladeRoot = null;
            _playerBladeTip = null;
            _hoveredEntity = -1;
            _playerCombatUntil = 0f;
            _playerAnchorFallbackUntil = 0f;
            _playerCombatReady = false;

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

                // Props are parented to animated bones and survive pooling.
                // Always return them to their authored resting mounts before the
                // body becomes available to another entity.
                if (_equipmentViews != null && (uint)entityId < (uint)_equipmentViews.Length)
                    _equipmentViews[entityId]?.ResetForSpawn();

                // Managed ViewPool не переживает forced script reload, а ссылки
                // на созданные Transform Unity успевает восстановить. Во время
                // teardown объект достаточно спрятать: новый Awake соберёт пул.
                if (_viewPools[entityId] != null) _viewPools[entityId].Release(view.gameObject);
                else view.gameObject.SetActive(false);
            }

            _views[entityId] = null;
            _viewPools[entityId] = null;
            _animationViews[entityId] = null;
            if (_equipmentViews != null && (uint)entityId < (uint)_equipmentViews.Length)
                _equipmentViews[entityId] = null;
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
            if (entityId == Simulation.PlayerId) _hasPreviousPlayerPosition = false;
            _lastFacingWorld[entityId] = Vector3.zero;
            _hasLastRenderPosition[entityId] = false;
            _visualFacingWorld[entityId] = Vector3.zero;
            _turnVisualUntil[entityId] = 0f;
            _turnVisualDirection[entityId] = 0f;
            _presentationOffset[entityId] = Vector3.zero;
            _groundOffset[entityId] = 0f;

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
                ViewPool pool = entities.Side[i] == Faction.Wole ? _wolePool
                    : entities.Kind[i] == EnemyKind.ForestRootSwarm ? _rootSwarmPool : _orvillPool;
                GameObject go = pool.Acquire();
                go.name = entities.Kind[i] == EnemyKind.None
                    ? $"{entities.Side[i]} #{i}" : $"{entities.Kind[i]} #{i}";
                _views[i] = go.transform;
                _viewPools[i] = pool;
                _animationViews[i] = go.GetComponent<CharacterAnimatorView>();
                _animationViews[i]?.SetEnemyKind(entities.Kind[i]);
                _equipmentViews[i] = go.GetComponent<PelagEquipmentView>();
                _animationViews[i]?.ResetForSpawn();
                _equipmentViews[i]?.ResetForSpawn();
                _deathUntil[i] = 0f;
                _deathStarted[i] = false;
                _deathStartedAt[i] = 0f;
                _hitRecoil[i] = Vector3.zero;
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
                if (i == Simulation.PlayerId) _hasPreviousPlayerPosition = false;
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
                    // A pooled body may have been created while the previous
                    // player intent was active. Re-apply the current state once
                    // the new component references are cached.
                    ApplyPlayerCombatReady(_playerCombatReady, force: true);
                }

                // Сначала сбрасываем пульный объект в масштаб из конфига, и только
                // потом снимаем базу: иначе остаток последнего hit/death-кадра
                // станет «нормальным» размером врага в следующем Разломе.
                go.transform.localScale = ExpectedBaseScale(entities.Side[i], entities.Kind[i], _animationViews[i]);
                _baseScale[i] = go.transform.localScale;

                _groundOffset[i] = _animationViews[i] != null
                    ? 0f
                    : GroundOffset(entities.Side[i], WoleScale, OrvillScale);
            }

            _boundCount = entities.Count;
        }

        private Vector3 ExpectedBaseScale(Faction faction, EnemyKind kind, CharacterAnimatorView animation)
        {
            float scale = faction == Faction.Wole ? WoleScale
                : kind == EnemyKind.ForestRootSwarm ? RootSwarmScale : OrvillScale;
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
            block.SetFloat(OutlineWidthId, 0f);
            block.SetColor(OutlineColorId, Color.clear);

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

        /// <summary>
        /// Ловит скачок НАРИСОВАННОГО тела между кадрами и разбирает его на
        /// слагаемые.
        ///
        /// Сторож в TickDriver уже показал, что симуляция не прыгает ни разу.
        /// Значит рывок рождается здесь, между позицией из тика и тем, что
        /// реально попало в transform: интерполяция, отдача, смещения подачи.
        /// Печатаем все три, иначе опять придётся гадать.
        /// </summary>
        private void ReportRenderJump(int entityId, Vector3 drawn, Vector3 interpolated,
            float velocityMagnitude)
        {
            if (_hasLastRenderPosition[entityId])
            {
                float jumped = Vector3.Distance(drawn, _lastRenderPosition[entityId]);
                // За кадр тело не может проехать больше тика движения. Порог с
                // запасом втрое, плюс пол-метра на мелкие кадры и отдачу.
                float limit = Mathf.Max(velocityMagnitude * 3f, 0.5f);
                if (jumped > limit)
                {
                    Debug.LogWarning($"[Разлом][рывок кадра] сущность {entityId}: "
                                     + $"{jumped:0.00} м за кадр при скорости {velocityMagnitude:0.000} м/тик, "
                                     + $"alpha={_driver.Alpha:0.00}, dt={Time.deltaTime * 1000f:0.0} мс, "
                                     + $"интерполяция={interpolated}, отдача={_hitRecoil[entityId]}, "
                                     + $"подача={_presentationOffset[entityId]}");
                }
            }

            _lastRenderPosition[entityId] = drawn;
            _hasLastRenderPosition[entityId] = true;
        }

        /// <summary>
        /// Полная построчная выписка по одному мобу: что решил тик, что попало
        /// в transform и что в этот момент делает Animator.
        ///
        /// Догадки кончились. Владелец описал симптом как «анимация проходит,
        /// заканчивается, и он начинает бежать с другого места» — такую вещь
        /// нельзя опознать ни по позиции, ни по кадрам отдельно. Нужны обе
        /// колонки рядом плюс состояние и его нормализованное время.
        /// </summary>
        private void TraceMob(int entityId, Vector3 drawn, EntityStore entities)
        {
            CharacterAnimatorView animation = _animationViews[entityId];
            Animator animator = animation != null ? animation.Animator : null;

            string state = "нет";
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                state = $"хеш={info.shortNameHash} t={info.normalizedTime:0.00} "
                        + $"скорость={animator.speed:0.00} "
                        + $"MoveSpeed={animator.GetFloat("MoveSpeed"):0.00} "
                        + $"переход={animator.IsInTransition(0)}";
            }

            FixVec2 sim = entities.Position[entityId];
            Debug.Log($"[Разлом][моб] тик={_driver.Sim.Tick} alpha={_driver.Alpha:0.00} "
                      + $"sim=({sim.X.ToFloat():0.00},{sim.Y.ToFloat():0.00}) "
                      + $"кадр=({drawn.x:0.00},{drawn.z:0.00}) {state}");
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
                        DeathDissolveStartDelay(entities.Kind[i]),
                        DeathDissolveStartDelay(entities.Kind[i]) + OrvillDeathFadeDuration,
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
                //
                // РЕШЕНИЕ ОБ ОСТАНОВКЕ ОДНОРАЗОВОЕ, И ЭТО ГЛАВНОЕ ЗДЕСЬ.
                // Раньше ветка «не идём — значит пошли» стояла ПЕРЕД проверкой
                // торможения и снимала защёлку на первом же кадре: тело гасило
                // скорость четыре тика, и на каждом из них Run срывался в
                // RunStop и тут же возвращался обратно. Четыре рывка за 130 мс
                // в конце КАЖДОГО приказа — это и есть та самая дрожь.
                // Обратно в бег — только когда тело действительно поехало:
                // скорость выросла или снова выше порога торможения.
                FixVec2 velocity = entities.Velocity[i];
                float velocityMagnitude = Mathf.Sqrt(
                    velocity.X.ToFloat() * velocity.X.ToFloat()
                    + velocity.Y.ToFloat() * velocity.Y.ToFloat());
                float fullStep = entities.MoveStep[i].ToFloat();
                float worldSpeed = velocityMagnitude * Simulation.TicksPerSecond;
                if (i == Simulation.PlayerId)
                {
                    Vector3 playerPosition = _driver.GetRenderPosition(i);
                    if (_hasPreviousPlayerPosition && Time.deltaTime > 0.000001f)
                        worldSpeed = Vector3.Distance(playerPosition, _previousPlayerPosition) / Time.deltaTime;
                    else worldSpeed = 0f;
                    _previousPlayerPosition = playerPosition;
                    _hasPreviousPlayerPosition = true;
                }
                float brakeThreshold = fullStep * 0.38f;
                bool moving = _locomotionMoving[i];
                if (velocityMagnitude <= 0.0001f)
                    moving = false;
                else if (moving)
                {
                    if (velocityMagnitude < _lastVelocityMagnitude[i]
                        && velocityMagnitude <= brakeThreshold)
                        moving = false;
                }
                else
                {
                    moving = velocityMagnitude > brakeThreshold
                             || velocityMagnitude > _lastVelocityMagnitude[i];
                }
                _lastVelocityMagnitude[i] = velocityMagnitude;
                // The hero now blends directly out of the current run pose.
                // Keep that pose advancing until the rendered body stops.
                if (i == Simulation.PlayerId) moving = worldSpeed > 0.02f;
                _locomotionMoving[i] = moving;
                float normalizedMoveSpeed = fullStep > 0.0001f
                    ? Mathf.Clamp(velocityMagnitude / fullStep, 0f,
                        entities.Kind[i] == EnemyKind.ForestRootSwarm
                            ? Simulation.RootSwarmRushSpeed.ToFloat() / Simulation.RootSwarmMoveSpeed.ToFloat() : 1f)
                    : 0f;
                float turnDirection = 0f;
                float turnDelta = 0f;
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
                    // Цвет отделяет врага от фона постоянно. Маска видимого
                    // силуэта даёт ровную кромку без внутренних швов меша;
                    // ширина задана в пикселях при 1080p, независимо от роста.
                    bool hostile = entities.Side[i] == Faction.Orvill;
                    bool hoveredHostile = hovered && hostile;
                    // Свечение гаснет вместе с телом: иначе над осыпающимся
                    // трупом ещё полсекунды висит контур живого врага.
                    float outlineFade = Mathf.Clamp01(1f - deathFade);
                    block.SetFloat(OutlineWidthId,
                        (hostile ? (hoveredHostile ? HoveredOutlineWidth : HostileOutlineWidth)
                            : HeroOutlineWidth) * outlineFade);
                    block.SetColor(OutlineColorId, hostile
                        ? (hoveredHostile ? HoveredOutlineColor : HostileOutlineColor)
                        : HeroOutlineColor);

                    ApplyRendererPropertyBlock(bodyRenderers, materialSlotCounts, block);
                }

                // ВЫБРОСА БОЛЬШЕ НЕТ, И ЭТО НЕ ПОТЕРЯ. Здесь тело улетало по
                // параболе и кувыркалось через голову, а показ
                // смерти длился 0.46 с — то есть клип умирания успевал дойти
                // всего до девятого кадра из семидесяти трёх, где существо ещё
                // СТОИТ. Владелец описал это точно: «застывает в позе смерти в
                // воздухе и растворяется в непонятной позиции». Так и было:
                // в кадре не было падения вообще, только полёт и стоячая поза.
                //
                // Теперь тело падает там, где стояло, доигрывает падение и
                // только потом осыпается — см. OrvillDeathAnimationDuration.
                view.position = p + _hitRecoil[i];
                if (_baseScale[i] != Vector3.zero)
                    view.localScale = _baseScale[i];

                if (_driver.WatchTeleports)
                {
                    ReportRenderJump(i, view.position, p, velocityMagnitude);
                    if (i == 1) TraceMob(i, view.position, entities);
                }

                // Труп поворот больше не трогает: он падает в ту сторону, куда
                // смотрел, и остаток кадров этим занимается общая ветка ниже.
                // Кувырка через голову тут стояло 240° за показ — вместе с
                // выбросом он и подменял собой падение.

                // Gameplay-facing остаётся мгновенным и живёт в Sim. Только
                // корень живого ORVILL мягко догоняет новый yaw: 30 Hz повороты
                // больше не выглядят ступенчатыми, но атаки по-прежнему решаются
                // по авторитетному направлению без presentation-задержки.
                FixVec2 facing = entities.Facing[i];
                if (facing.LengthSq.Raw != 0)
                {
                    // Направление берётся ИНТЕРПОЛИРОВАННЫМ, из того же места,
                    // что и позиция. Мгновенный facing остаётся боевой правдой
                    // и живёт в Sim; на экране 30 Гц поворота без этого читались
                    // ступенями — при 120 кадрах особенно.
                    Vector3 facingWorld = _driver.GetRenderFacing(i);
                    if (facingWorld.sqrMagnitude < 0.0001f)
                        facingWorld = new Vector3(
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
                        if (previousFacing.sqrMagnitude > 0.5f)
                            turnDelta = Vector3.SignedAngle(previousFacing, facingWorld, Vector3.up);
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
                            CharacterAnimatorView presentation = _animationViews[i];
                            Vector3 previousVisual = _visualFacingWorld[i];
                            if (i == Simulation.PlayerId && presentation != null && presentation.AnchorAbilityActive
                                && _playerAbilityFacing.sqrMagnitude > 0.5f)
                                visualFacing = Vector3.Slerp(previousVisual,
                                    Vector3.Slerp(facingWorld, _playerAbilityFacing, presentation.AnchorFacingWeight),
                                    1f - Mathf.Exp(-28f * Time.deltaTime)).normalized;
                            if (presentation != null && presentation.WhirlwindActive
                                && previousVisual.sqrMagnitude > 0.5f)
                            {
                                // Preserve the spin axis while input changes.
                                // The final step turns back toward locomotion.
                                float recover = Mathf.InverseLerp(0.53f, 0.80f, presentation.WhirlwindElapsed);
                                visualFacing = Vector3.Slerp(previousVisual, facingWorld,
                                    1f - Mathf.Exp(-25f * recover * Time.deltaTime)).normalized;
                            }
                            _visualFacingWorld[i] = visualFacing;
                        }

                        // Доворот на случай, если модель экспортировали лицом
                        // не туда: разворачивать сам меш дороже, чем повернуть
                        // корень одним числом.
                        view.rotation = Quaternion.LookRotation(visualFacing, Vector3.up)
                                        * Quaternion.Euler(0f, ModelYaw, 0f);
                        if (i == Simulation.PlayerId && moving && velocityMagnitude > .0001f)
                        {
                            // Хук смотрит в прицел независимо от движения. Цикл ног выбирается
                            // относительно показанного тела, иначе боковой бег становится скольжением.
                            Vector3 travel = new Vector3(velocity.X.ToFloat(), 0f, velocity.Y.ToFloat()) / velocityMagnitude;
                            localMoveX = Vector3.Dot(travel, Vector3.Cross(Vector3.up, visualFacing));
                            localMoveY = Vector3.Dot(travel, visualFacing);
                        }
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
                    moving, turnDirection, normalizedMoveSpeed, localMoveX, localMoveY, worldSpeed, turnDelta);
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
                // Combat intent is presentation-only. Refresh the grace window
                // from authoritative events so a hostile swing remains visible
                // even after PendingAttackTarget is cleared on its impact tick.
                if ((e.Type == SimEventType.Attack &&
                     (e.Source == Simulation.PlayerId || e.Target == Simulation.PlayerId))
                    || (e.Type == SimEventType.AbilityCast && e.Source == Simulation.PlayerId)
                    || ((e.Type == SimEventType.Damage || e.Type == SimEventType.DamageOverTime)
                        && (e.Source == Simulation.PlayerId || e.Target == Simulation.PlayerId)))
                    MarkPlayerCombatActivity();

                switch (e.Type)
                {
                    case SimEventType.Attack:
                        if (e.Source == Simulation.PlayerId)
                        { _anchorSaberSuppressed = false; MarkPlayerCombatActivity(); }
                        AnimationOf(e.Source)?.PlayAttack(e.Amount);
                        break;
                    case SimEventType.AbilityCast:
                        if ((uint)e.Amount < Simulation.AbilitySlots)
                        {
                            AbilityBuild build = _driver.Sim.GetAbility(e.Amount);
                            if (build != null)
                            {
                                if (e.Source == Simulation.PlayerId)
                                    PlayPlayerAbilityPresentation(e.Amount, build.DefinitionId);
                                else
                                    AnimationOf(e.Source)?.PlayAbilityDefinition(build.DefinitionId);
                            }
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
                        if (e.Target == Simulation.PlayerId)
                        {
                            _playerCombatUntil = 0f;
                            ApplyPlayerCombatReady(false, force: true);
                            EndPlayerAnchorUse();
                        }
                        CharacterAnimatorView animation = AnimationOf(e.Target);
                        if (animation == null) break;
                        animation.PlayDeath();
                        _deathStarted[e.Target] = true;
                        _deathStartedAt[e.Target] = Time.time;
                        float presentationDuration = entities.Side[e.Target] == Faction.Orvill
                            ? DeathDissolveStartDelay(entities.Kind[e.Target]) + OrvillDeathFadeDuration
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
        public GameObject CreateCampPlayer()
        {
            return BodyFactory(WoleModel, WoleController, WoleMaterial, WoleTexture,
                Faction.Wole, WoleScale)();
        }

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
            // Вражеский материал из FBX может быть обычным URP/Lit и тогда
            // MaterialPropertyBlock с _OutlineWidth не имеет UnitOutlineMask-pass.
            // Для Orvill гарантированно собираем материал на нашем toon
            // shader, сохраняя исходную текстуру.
            if (faction == Faction.Orvill || material == null || !HasBaseTexture(material))
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
                GameObject body = CreateCharacterBody(prefab, faction, scale, material, controller);
                if (body != null && faction == Faction.Wole)
                    ConfigurePelagEquipment(body, weaponPrefab, weaponMaterial,
                        anchorPrefab, anchorMaterial);

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
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0f);

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
            Material fallbackMaterial, RuntimeAnimatorController controller)
        {
            GameObject go = Instantiate(prefab);
            go.transform.localScale = Vector3.one * scale;
            // Сохраняем существующий слой врагов для настроек камер. Обводка
            // выбирает юнитов по проходу UnitOutlineMask и ширине в материале.
            if (faction == Faction.Orvill)
                SetLayerRecursively(go, LayerMask.NameToLayer("EnemyOutline"));
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

            // Даже при мягкой directional-тени маленькая изометрическая модель
            // выглядела подвешенной над светлым полом. Небольшая контактная
            // тень возвращает ногам опору и делает направление света заметным,
            // не вмешиваясь в физику или детерминированное положение тела.
            CreateContactShadow(go.transform, faction, scale);

            CharacterAnimatorView animation = go.GetComponent<CharacterAnimatorView>();
            if (animation == null) animation = go.AddComponent<CharacterAnimatorView>();
            animation.Configure(faction);
            if (faction == Faction.Wole && controller != null)
                go.AddComponent<PelagFootPlantView>();
            return go;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0) return;
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        private void ConfigurePelagEquipment(GameObject body, GameObject saberPrefab,
            Material saberMaterial, GameObject anchorPrefab, Material anchorMaterial)
        {
            if (body == null) return;

            Transform root = body.transform;
            Transform saberStoredSocket = FindChild(root, WoleWeaponStoredSocket);
            Transform saberEquippedSocket = FindChild(root, WoleWeaponSocket);
            Transform anchorStoredSocket = FindChild(root, WoleAnchorSocket);
            Transform anchorEquippedSocket = FindChild(root, WoleAnchorEquippedSocket);
            // Older scenes serialized both weapons on the same hip. Keep the
            // authored height/depth but put the anchor opposite the saber.
            Vector3 anchorBeltPosition = WoleAnchorLocalPosition;
            if (anchorStoredSocket == saberStoredSocket)
                anchorBeltPosition.x = -Mathf.Sign(WoleWeaponStoredLocalPosition.x)
                    * Mathf.Max(0.114f, Mathf.Abs(anchorBeltPosition.x));

            if (saberPrefab != null && saberStoredSocket == null)
                Debug.LogWarning($"[Разлом] Сабля не может быть убрана: кость «{WoleWeaponStoredSocket}» не найдена.");
            if (saberPrefab != null && saberEquippedSocket == null)
                Debug.LogWarning($"[Разлом] Сабля не может быть взята: кость «{WoleWeaponSocket}» не найдена.");
            if (anchorPrefab != null && anchorStoredSocket == null)
                Debug.LogWarning($"[Разлом] Якорь не сел на пояс: кость «{WoleAnchorSocket}» не найдена.");
            if (anchorPrefab != null && anchorEquippedSocket == null)
                Debug.LogWarning($"[Разлом] Якорь не может перейти в левую руку: кость «{WoleAnchorEquippedSocket}» не найдена.");

            Transform saber = InstantiateEquipmentProp(root, saberPrefab,
                "Pelag_FantasySaber_Equipped", saberMaterial, addBladeMarkers: true);
            Transform anchor = InstantiateEquipmentProp(root, anchorPrefab,
                "Pelag_AnchorGrip_Equipped", anchorMaterial, addBladeMarkers: false);

            PelagEquipmentView equipment = body.GetComponent<PelagEquipmentView>();
            if (equipment == null) equipment = body.AddComponent<PelagEquipmentView>();
            equipment.Configure(saber,
                new PelagEquipmentView.MountPoint(saberStoredSocket,
                    WoleWeaponStoredLocalPosition, WoleWeaponStoredLocalRotation,
                    WoleWeaponStoredLocalScale),
                new PelagEquipmentView.MountPoint(saberEquippedSocket,
                    WoleWeaponLocalPosition, WoleWeaponLocalRotation,
                    WoleWeaponLocalScale),
                anchor,
                new PelagEquipmentView.MountPoint(anchorStoredSocket,
                    anchorBeltPosition, WoleAnchorLocalRotation,
                    WoleAnchorLocalScale),
                new PelagEquipmentView.MountPoint(anchorEquippedSocket,
                    WoleAnchorEquippedLocalPosition, WoleAnchorEquippedLocalRotation,
                    WoleAnchorEquippedLocalScale));
        }

        private static Transform InstantiateEquipmentProp(Transform body, GameObject prefab,
            string objectName, Material materialOverride, bool addBladeMarkers)
        {
            if (prefab == null) return null;

            GameObject mounted = Instantiate(prefab, body, false);
            mounted.name = objectName;
            mounted.SetActive(true);
            ApplyMaterialOverride(mounted, materialOverride);

            if (addBladeMarkers)
                AddBladeMarkers(mounted);

            return mounted.transform;
        }

        private static void ApplyMaterialOverride(GameObject mounted, Material materialOverride)
        {
            if (mounted == null || materialOverride == null) return;
            Renderer[] renderers = mounted.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int m = 0; m < materials.Length; m++) materials[m] = materialOverride;
                renderers[i].sharedMaterials = materials;
            }
        }

        private static void AddBladeMarkers(GameObject mounted)
        {
            MeshFilter bladeMesh = mounted.GetComponentInChildren<MeshFilter>(true);
            if (bladeMesh == null || bladeMesh.sharedMesh == null) return;

            Bounds bounds = bladeMesh.sharedMesh.bounds;
            float length = bounds.size.y;
            Transform bladeRoot = new GameObject("BladeRoot").transform;
            bladeRoot.SetParent(bladeMesh.transform, false);
            bladeRoot.localPosition = new Vector3(0f, bounds.min.y + length * 0.40f, 0f);

            Transform bladeTip = new GameObject("BladeTip").transform;
            bladeTip.SetParent(bladeMesh.transform, false);
            bladeTip.localPosition = new Vector3(0f, bounds.min.y + length * 0.985f, 0f);
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


