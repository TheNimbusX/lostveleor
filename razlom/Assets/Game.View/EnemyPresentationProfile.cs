using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Из чего сделан моб — во что он рассыпается в момент убийства (поток I, 29.09).</summary>
    public enum DeathMaterial : byte
    {
        /// <summary>Кора и щепки, листья с головы — Хранитель, Камнекопыт.</summary>
        Bark = 0,
        /// <summary>Труха, волокна корней, сухие листья, земля — Корнеполз, Корнехват.</summary>
        Rot = 1,
        /// <summary>Золотые споры, капли сока, рваные лепестки — Плюй-плод.</summary>
        Spore = 2,
        /// <summary>Сколы панциря поверх своего раскола — Расщепень и детёныш.</summary>
        Shell = 3,
        /// <summary>Шипы и тёмная кора — Шипомёт.</summary>
        Thorn = 4,
        /// <summary>Костяные осколки рогов и мох — Вендиго.</summary>
        Bone = 5,
    }

    /// <summary>
    /// ТАКТ УБИЙСТВА одного моба (поток I). Все времена — секунды от тика
    /// события Death; эффекты считают возраст от тика Sim, тело — от кадра, в
    /// котором ArenaView увидел смерть (расходятся не больше чем на кадр).
    ///
    ///   0            — вспышка на теле (_HitFlash, KillFlashSeconds, потом гаснет за 1–2 кадра);
    ///   0…HitStop    — стоп-кадр тяжёлого: тело держит отдачу, распад ждёт;
    ///   BurstAt      — залп материала вида и начало распада тела (тряска у тяжёлых);
    ///   MotesAt      — первые огоньки сущности летят в героя;
    ///   BodyGoneAt   — тело рассыпалось и возвращается в пул.
    ///
    /// Звук (поток K): слоёная смерть — на BurstAt, «огоньки долетели» — на
    /// FirstMoteArrivesAt…LastMoteArrivesAt. Такт считает
    /// <see cref="EnemyDeathFxView.BeatFor"/> в кадре события смерти.
    /// </summary>
    public readonly struct EnemyKillBeat
    {
        public readonly EnemyKind Kind;
        public readonly DeathMaterial Material;
        /// <summary>Крупный, элита или последний в волне: стоп-кадр и лёгкая тряска.</summary>
        public readonly bool Heavy;
        /// <summary>Убийство зачистило поле (последний в волне).</summary>
        public readonly bool LastOfWave;
        public readonly float HitStopSeconds;
        public readonly float BurstAt;
        public readonly float CrumbleSeconds;
        public readonly float MotesAt;
        public readonly int MoteCount;
        /// <summary>Масштаб залпа: единица — Хранитель.</summary>
        public readonly float Scale;
        /// <summary>Размер куска, на которые трескается тело, м.</summary>
        public readonly float ChunkMetres;
        /// <summary>
        /// Тело оседает и сжимается. Расщепень — нет: он раскалывается сам (SplitterCombatView).
        /// Корнехват — нет: тело URP Lit не трескается, он падает клипом и уходит в землю сам
        /// (RootSnarerAnimatorView).
        /// </summary>
        public readonly bool Crumbles;
        public readonly float ShakeTrauma, ShakeZoom;

        /// <summary>
        /// Тело ложится на землю в этот миг (с от смерти) — второй, низкий залп материала у груди
        /// (EnemyDeathFxView). 0 — тело не падает, а рассыпается стоя (все, кроме Корнехвата).
        /// </summary>
        public readonly float LandsAt;

        public EnemyKillBeat(EnemyKind kind, DeathMaterial material, bool heavy, bool lastOfWave, float hitStop,
            float burstAt, float crumbleSeconds, int motes, float scale, float chunkMetres,
            bool crumbles, float shakeTrauma, float shakeZoom, float landsAt = 0f)
        {
            Kind = kind; Material = material; Heavy = heavy; LastOfWave = lastOfWave;
            HitStopSeconds = hitStop; BurstAt = burstAt; CrumbleSeconds = crumbleSeconds;
            MotesAt = burstAt + EnemyPresentationProfile.MoteDelaySeconds; MoteCount = motes;
            Scale = scale; ChunkMetres = chunkMetres; Crumbles = crumbles;
            ShakeTrauma = shakeTrauma; ShakeZoom = shakeZoom; LandsAt = landsAt;
        }

        /// <summary>Центр залпа над землёй, м: префабы Resources/VFX/Death собраны с центром на 0,8 при масштабе 1.</summary>
        public float CentreHeight => EnemyPresentationProfile.BurstCentreUnit * Scale;
        public float BodyGoneAt => BurstAt + CrumbleSeconds;
        public float FirstMoteArrivesAt => MotesAt + EnemyPresentationProfile.MoteFlightMinSeconds;
        public float LastMoteArrivesAt => MotesAt + EnemyPresentationProfile.MoteLaunchSpreadSeconds
                                          + EnemyPresentationProfile.MoteFlightMaxSeconds;
    }

    [Serializable]
    public sealed class EnemyDeathPresentation
    {
        [Min(0.01f)] public float ClipSeconds = 73f / 30f;
        [Range(0f, 1f)] public float StartNormalized = 24f / 73f;
        [Range(0f, 1f)] public float RestNormalized = 0.72f;
        [Min(0.01f)] public float StateSpeed = 1f;
        [Min(0f)] public float BlendSeconds = 0.09f;
        [Min(0f)] public float RestSeconds = 0.18f;
        [Min(0.01f)] public float DissolveSeconds = 0.38f;
        [Range(0f, 0.5f)] public float RecoilMeters = 0.12f;
        [Range(0f, 1f)] public float EdgeGlow = 0.10f;
        public Color EdgeColor = new Color(0.38f, 0.29f, 0.15f, 1f);

        /// <summary>
        /// За сколько клип доходит до «лёг». Теперь это только скорость
        /// проигрывания клипа смерти (Шипомёт, заглушка): тело рассыпается раньше,
        /// чем ляжет, — см. <see cref="DissolveAt"/>.
        /// </summary>
        public float FallSeconds => Mathf.Max(0.01f,
            (RestNormalized - StartNormalized) * ClipSeconds / Mathf.Max(0.01f, StateSpeed));

        /// <summary>
        /// Начало распада лёгкого убийства. БЫЛО «упал — полежал — осыпался»
        /// (FallSeconds + RestSeconds, у Хранителя 1,13 с): выплата за удар
        /// приходила через секунду после удара, и убийство не ощущалось (ревью
        /// 29.09). Теперь тело трескается и рассыпается в материал сразу после
        /// вспышки, клип смерти даёт лишь оседание. Тяжёлым ArenaView добавляет
        /// стоп-кадр (<see cref="EnemyKillBeat.HitStopSeconds"/>).
        /// </summary>
        public float DissolveAt => EnemyPresentationProfile.KillBreakSeconds;

        /// <summary>Тело рассыпалось (лёгкое убийство); DissolveSeconds — длительность распада.</summary>
        public float TotalSeconds => DissolveAt + DissolveSeconds;
    }

    [CreateAssetMenu(menuName = "Разлом/Профиль реакций врагов")]
    public sealed class EnemyPresentationProfile : ScriptableObject
    {
        public EnemyDeathPresentation Guardian = new EnemyDeathPresentation();
        public EnemyDeathPresentation RootSwarm = new EnemyDeathPresentation
        {
            ClipSeconds = 66f / 30f, StartNormalized = 0f,
            RestNormalized = 50f / 66f, StateSpeed = 2f,
            BlendSeconds = 0.055f, RestSeconds = 0.10f, DissolveSeconds = 0.28f,
            RecoilMeters = 0.07f, EdgeGlow = 0.06f,
            EdgeColor = new Color(0.27f, 0.32f, 0.13f, 1f)
        };

        public EnemyDeathPresentation ForestBud = new EnemyDeathPresentation
        {
            ClipSeconds = 1.2f, StartNormalized = 0f, RestNormalized = 1f,
            StateSpeed = 1f, BlendSeconds = .09f, RestSeconds = .45f,
            DissolveSeconds = .45f, RecoilMeters = .035f, EdgeGlow = .03f,
            EdgeColor = new Color(.38f, .30f, .13f, 1f)
        };

        public EnemyDeathPresentation ForestStonehoof = new EnemyDeathPresentation {
            ClipSeconds = 2f, StartNormalized = 0, RestNormalized = 1, StateSpeed = 1,
            BlendSeconds = .09f, RestSeconds = .55f, DissolveSeconds = .55f,
            RecoilMeters = 0, EdgeGlow = .025f, EdgeColor = new Color(.36f,.3f,.19f,1)
        };
        private static EnemyPresentationProfile _current;
        // Кромка трещин Вендиго — цвет кости на изломе, не коры: он рассыпается в рога.
        public EnemyDeathPresentation ForestWendigo = new EnemyDeathPresentation {
            ClipSeconds = 4f, StartNormalized = 18f/96f, RestNormalized = 1f,
            StateSpeed = 1f, BlendSeconds = .07f, RestSeconds = .6f,
            DissolveSeconds = .65f, RecoilMeters = .02f, EdgeGlow = .03f,
            EdgeColor = new Color(.62f, .57f, .46f, 1f)
        };
        // Шипомёт (клип Death, 48 кадров): касание земли на 39-м — ThorncasterAnimatorView
        // играет кадры 0–39 за время падения этого профиля, 39–48 за стойку.
        public EnemyDeathPresentation ForestThorncaster = new EnemyDeathPresentation {
            ClipSeconds = 48f / 30f, StartNormalized = 0f, RestNormalized = 39f / 48f,
            StateSpeed = 1f, BlendSeconds = .07f, RestSeconds = .45f,
            DissolveSeconds = .5f, RecoilMeters = .04f, EdgeGlow = .03f,
            EdgeColor = new Color(.36f, .30f, .17f, 1f)
        };
        // Корнехват (Death, 45 кадров): брюхом в землю на 25-м, с 32-го лежит. Тело под URP Lit
        // без растворения и без трещин — ревью 01.10 «анимации и VFX смерти никакой»: раньше
        // распад за 0,4 с сжимал и топил тело, клип доходил до 15-го кадра. Теперь он падает
        // целиком: кадры 0–25 — до FallSeconds (0,62 с от смерти, после стоп-кадра), 25–45 — за
        // RestSeconds, потом RootSnarerAnimatorView уводит лежащее тело в землю до конца показа
        // (BurstAt + DissolveSeconds ≈ 1,1 с). Выплата убийства (вспышка, залп, огоньки, звук) —
        // по-прежнему на залпе, сразу.
        public EnemyDeathPresentation ForestRootSnarer = new EnemyDeathPresentation {
            ClipSeconds = 45f / 30f, StartNormalized = 0f, RestNormalized = 25f / 45f,
            StateSpeed = 1.35f, BlendSeconds = .08f, RestSeconds = .42f,
            DissolveSeconds = 1f, RecoilMeters = .04f, EdgeGlow = .03f,
            EdgeColor = new Color(.30f, .26f, .16f, 1f)
        };
        /// <summary>
        /// Смерть вида. Расщепень не падает, а раскалывается (SplitterCombatView): тело
        /// прячется через 0,2 с, профиль Хранителя лишь держит слот до конца распада;
        /// детёныш — по корнеползу.
        /// </summary>
        public static EnemyDeathPresentation Death(EnemyKind kind)
        {
            if (_current == null)
                _current = Resources.Load<EnemyPresentationProfile>("Combat/EnemyPresentation")
                    ?? CreateInstance<EnemyPresentationProfile>();
            switch (kind)
            {
                case EnemyKind.ForestStonehoof: return _current.ForestStonehoof;
                case EnemyKind.ForestWendigo: return _current.ForestWendigo;
                case EnemyKind.ForestBud: return _current.ForestBud;
                case EnemyKind.ForestThorncaster: return _current.ForestThorncaster;
                case EnemyKind.ForestRootSnarer: return _current.ForestRootSnarer;
                case EnemyKind.ForestRootSwarm:
                case EnemyKind.ForestSplitling: return _current.RootSwarm;
                default: return _current.Guardian;
            }
        }

        // ------------------------------------------------------------ момент убийства
        //
        // Решение владельца 29.09: распад в материал по виду, вспышка на теле 2–3
        // кадра, лёгкая тряска и стоп-кадр — только у крупных и у последнего в волне,
        // огоньки сущности летят в героя. Числа ниже — общий такт для тела
        // (ArenaView), залпа (EnemyDeathFxView), огоньков (EssenceMotesView) и
        // звука (поток K). Меняешь — меняется у всех разом.

        /// <summary>Вспышка на теле держится на пике столько (2 кадра при 60 fps), потом гаснет за 1–2 кадра.</summary>
        public const float KillFlashSeconds = .035f;

        /// <summary>Сила вспышки добивания: _HitFlash тела, тёплый lerp Texture Toon. Не постоянное высветление.</summary>
        public const float KillFlashPeak = .92f;

        /// <summary>
        /// Стоп-кадр тяжёлого убийства: тело держит отдачу, залп и распад ждут.
        /// Глобальное время бой не трогает (правило CombatJuiceView): стоп — у тела.
        /// </summary>
        public const float KillHitStopSeconds = .075f;

        /// <summary>Лёгкое убийство: залп материала и распад — сразу после пика вспышки.</summary>
        public const float KillBreakSeconds = KillFlashSeconds;

        /// <summary>Огоньки вылетают после залпа — из облака обломков, а не из целого тела.</summary>
        public const float MoteDelaySeconds = .06f;

        /// <summary>Разброс вылета огоньков одного убийства.</summary>
        public const float MoteLaunchSpreadSeconds = .12f;

        /// <summary>Полёт огонька до героя.</summary>
        public const float MoteFlightMinSeconds = .6f, MoteFlightMaxSeconds = .9f;

        /// <summary>Потолок летящих огоньков: лишние на массовом убийстве не рождаются.</summary>
        public const int MotePoolSize = 64;

        /// <summary>Высота центра залпа при масштабе 1, м (Хранитель).</summary>
        public const float BurstCentreUnit = .8f;

        /// <summary>
        /// Такт убийства вида. elite — элита забега (Sim.IsElite), lastOfWave —
        /// убийство зачистило поле. Тяжёлое — крупный вид (Камнекопыт, Вендиго,
        /// Шипомёт, Корнехват, Расщепень), элита или последний в волне.
        /// </summary>
        public static EnemyKillBeat Kill(EnemyKind kind, bool elite, bool lastOfWave)
        {
            DeathMaterial material;
            // ownBreak — тело раскалывается своим видом (Расщепень): залп ждёт его раскола.
            bool big, crumbles = true, ownBreak = false;
            int motes;
            float scale, chunk;
            switch (kind)
            {
                case EnemyKind.ForestRootSwarm:
                    material = DeathMaterial.Rot; big = false; motes = 3; scale = .6f; chunk = .09f; break;
                case EnemyKind.ForestBud:
                    material = DeathMaterial.Spore; big = false; motes = 4; scale = .75f; chunk = .11f; break;
                case EnemyKind.ForestWendigo:
                    material = DeathMaterial.Bone; big = true; motes = 12; scale = 1.35f; chunk = .22f; break;
                case EnemyKind.ForestStonehoof:
                    material = DeathMaterial.Bark; big = true; motes = 8; scale = 1.15f; chunk = .18f; break;
                case EnemyKind.ForestThorncaster:
                    material = DeathMaterial.Thorn; big = true; motes = 7; scale = 1f; chunk = .15f; break;
                // Корнехват не трескается (URP Lit): падает клипом и уходит в землю — см. профиль.
                case EnemyKind.ForestRootSnarer:
                    material = DeathMaterial.Rot; big = true; motes = 7; scale = .95f; chunk = .15f; crumbles = false; break;
                case EnemyKind.ForestSplitter:
                    material = DeathMaterial.Shell; big = true; motes = 6; scale = 1f; chunk = .15f; crumbles = false; ownBreak = true; break;
                case EnemyKind.ForestSplitling:
                    material = DeathMaterial.Shell; big = false; motes = 3; scale = .6f; chunk = .09f; crumbles = false; ownBreak = true; break;
                default:
                    material = DeathMaterial.Bark; big = false; motes = 6; scale = 1f; chunk = .16f; break;
            }
            bool heavy = big || elite || lastOfWave;
            float stop = heavy ? KillHitStopSeconds : 0f;
            // Расщепень раскалывается по своей трещине (SplitterCombatView): сколы — в тот же тик.
            float burst = !ownBreak ? KillBreakSeconds + stop
                : Mathf.Max(SplitterCombatView.BreakDelaySeconds, KillBreakSeconds + stop);
            if (elite) motes = motes * 3 / 2;
            float trauma = !heavy ? 0f : lastOfWave ? .6f : .5f;
            float zoom = !heavy ? 0f : lastOfWave ? .75f : .55f;
            // Падающее тело ложится к концу падения профиля (тот же миг, что «упал» в CombatAudio).
            float lands = kind == EnemyKind.ForestRootSnarer ? Mathf.Max(stop + .05f, Death(kind).FallSeconds) : 0f;
            return new EnemyKillBeat(kind, material, heavy, lastOfWave, stop, burst,
                Death(kind).DissolveSeconds, motes, scale, chunk, crumbles, trauma, zoom, lands);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _current = null;
    }
}
