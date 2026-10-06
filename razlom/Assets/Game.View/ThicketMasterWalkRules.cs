using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ: ЛАПЫ НА ХОДУ (владелец 08.10: «ноги у босса немного проскальзывают при ходьбе»). Без UnityEngine —
    /// проверяет tools/Combat.Presentation.Tests/ThicketMasterWalkFeetTests.cs; замер и стенд скольжения —
    /// artifacts/tools/boss-feet (walk_feet.py, foot_table.py, slide/).
    ///
    /// Замер 08.10. Клип Walk, как его играет Unity (Walk.anim, тело ×1,203), ведёт стоящие лапы назад ровно на
    /// 2,148 м за цикл — столько же, сколько _walkStride префаба: по прямой на 2,0 и 2,8 м/с лапы стоят (1–2 см за
    /// стойку), шаг и фаза по пути верны. Скользят лапы на ходу с поворотом: Sim вертит тело вокруг центра сущности,
    /// а передние лапы стоят на 2,5 м впереди него (задние — на 0,8 м позади, обе пары — на 1,4 м в стороны). Доворот
    /// на ходу 30–75°/с тащит стоящую переднюю лапу вбок на 1,3–3 м/с; на живой Sim, где герой бегает вбок или кружит,
    /// — 0,2–0,6 м за стойку (медиана), до 2 м. Клип ведёт лапы только прямо назад — одной фазой это не убрать:
    /// • шаг под поворот: смещение стопы от её стоп-центра (<see cref="StanceX"/>, <see cref="StanceZ"/>) повёрнуто туда,
    ///   куда под этой лапой едет земля при ходе и повороте тела, −(v + ω × r), не больше <see cref="MaxWarpDegrees"/>, и
    ///   растянуто/укорочено под её ход; фаза Walk — по самой «быстрой» лапе (не чаще <see cref="MaxCyclesPerSecond"/>),
    ///   прямо — ровно путь / шаг, как было;
    /// • замок стопы: в окне опоры клипа (<see cref="Planted"/>) лапа держит точку касания на земле и свой разворот,
    ///   пока корпус идёт и крутится (дальше <see cref="MaxLockMetres"/> от шага замок скользит следом); на отрыве
    ///   поправка гаснет за <see cref="ReleaseCycles"/> цикла уже в воздухе.
    /// Вид (ThicketMasterAnimatorView) ставит лапу поверх клипа двухкостной ИК (<see cref="ThicketLegIk"/>), вес — вес
    /// состояния Walk в аниматоре: смесь в ход и из хода поправку проявляет и гасит.
    ///
    /// Оси — корня вида (ArenaView): x — вправо, z — вперёд, м; поворот — по часовой сверху (Vector3.SignedAngle вокруг
    /// вверх), как у вида. Лапы: 0 — передняя левая, 1 — передняя правая, 2 — задняя левая, 3 — задняя правая.
    /// </summary>
    public static class ThicketWalkRules
    {
        public const int PawCount = 4;
        public const int FrontLeft = 0, FrontRight = 1, HindLeft = 2, HindRight = 3;

        /// <summary>Тело префаба при замере (ThicketMasterBuilder: 4,14 м / рост модели) — стоп-центры ниже даны в масштабе модели 1.</summary>
        public const float MeasuredBodyScale = 1.2026992f;

        // Стоп-центры: середина отрезка, который стопа проходит стоя (передние — костяшка leg_front_*_toe, задние — скакательный
        // сустав leg_hind_*_foot), масштаб модели 1. Замер foot_table.py 08.10: перед ±1,2201 / 2,0538, зад ±1,1799 / −0,6702.
        private static readonly float[] CentreX = { -1.2201f, 1.2201f, -1.1799f, 1.1799f };
        private static readonly float[] CentreZ = { 2.0538f, 2.0538f, -0.6702f, -0.6702f };

        // Окна опоры для замка, доли цикла (clip_walk.py: касание HR 0, FR 0,44, HL 0,5, FL 0,94; опора передних 0,5 цикла,
        // задних 0,56, из них последние 0,22 — перекат на пальцах). Замок — без посадки (лапа ещё оседает 0,04 цикла) и без
        // переката задних (скакательный сустав там поднимается вокруг пальцев): перед [касание + 0,04, касание + 0,48] (костяшка
        // на земле до +0,49), зад [касание + 0,04, касание + 0,33]. Замер 08.10: внутри окна стопа клипа едет назад 2,15 м/с
        // (на краях 2,1–2,4), высота ±6 мм.
        private static readonly float[] LockFrom = { .98f, .48f, .54f, .04f };
        private static readonly float[] LockTo = { .42f, .92f, .83f, .33f };

        /// <summary>
        /// Чаще стольких циклов Walk в секунду лапы не переступают (прямо 2,0 м/с — 0,93, 2,8 м/с — 1,30): на крутом
        /// повороте на ходу — не больше чем ×1,23 к темпу дальнего хода; остаток держит замок (стенд 08.10: 2,2 цикла/с
        /// держали лапу на 1–2 см лучше, но частили).
        /// </summary>
        public const float MaxCyclesPerSecond = 1.6f;

        /// <summary>Шаг лапы поворачивается к ходу земли под ней не больше чем на столько градусов.</summary>
        public const float MaxWarpDegrees = 75f;

        /// <summary>
        /// Шаг лапы длиннее клипа не больше чем во столько раз (стенд 08.10 с досягаемостью ноги: передняя у края шага почти
        /// прямая — 97% длины, при ×1,3 ИК дотягивается в 99,9% кадров).
        /// </summary>
        public const float MaxStretch = 1.3f;

        /// <summary>Замок держит стопу не дальше стольких метров (масштаб модели 1) от шага под поворот; дальше — скользит следом.</summary>
        public const float MaxLockMetres = .3f;

        /// <summary>
        /// Поправка замка гаснет за столько цикла после отрыва (передняя уже в воздухе, задняя докатывается на пальцах) — до
        /// посадки (в воздухе передняя 0,5 цикла, задняя 0,44) поправки нет, лапа встаёт шагом под поворот.
        /// </summary>
        public const float ReleaseCycles = .3f;

        /// <summary>Стоящая стопа держит свой разворот на земле не больше стольких градусов (дальше крутится с телом).</summary>
        public const float MaxPawYawDegrees = 25f;

        /// <summary>Сглаживание скорости поворота, с: кадр со скачком взгляда не дёргает шаг.</summary>
        public const float YawSmoothSeconds = .05f;

        /// <summary>Стоп-центр лапы, м, при масштабе тела scale (корень вида).</summary>
        public static float StanceX(int paw, float scale) => CentreX[paw] * scale;
        public static float StanceZ(int paw, float scale) => CentreZ[paw] * scale;

        /// <summary>Передняя лапа (точка касания — костяшка leg_front_*_toe; у задних — сам скакательный сустав).</summary>
        public static bool IsFront(int paw) => paw == FrontLeft || paw == FrontRight;

        /// <summary>Лапа в окне опоры (замок держит её на земле) на доле цикла phase.</summary>
        public static bool Planted(int paw, float phase) => Since(LockFrom[paw], phase) < LockSpan(paw);

        /// <summary>Сколько цикла прошло после отрыва (конца окна опоры) к доле phase: 0…1.</summary>
        public static float SinceLift(int paw, float phase) => Since(LockTo[paw], phase);

        /// <summary>Начало окна опоры лапы (доля цикла) и его длина (доля цикла).</summary>
        public static float LockStart(int paw) => LockFrom[paw];
        public static float LockSpan(int paw) => Since(LockFrom[paw], LockTo[paw]);

        /// <summary>Сколько цикла от начала окна опоры лапы до доли phase: 0…1.</summary>
        public static float SinceLock(int paw, float phase) => Since(LockFrom[paw], phase);

        private static float Since(float from, float phase)
        {
            float d = phase - from;
            return d - (float)Math.Floor(d);
        }

        /// <summary>
        /// Ход земли под точкой (x, z) тела, м/с в осях корня: тело идёт forward / side (м/с) и поворачивается yawRate
        /// (°/с, по часовой сверху), стоящая там лапа должна ехать по телу так: −(v + ω × r). Поворот по часовой ведёт точку
        /// (x, z) тела со скоростью ω·(z, −x).
        /// </summary>
        public static void GroundVelocity(float x, float z, float forward, float side, float yawRate, out float ux, out float uz)
        {
            float w = yawRate * (float)(Math.PI / 180.0);
            ux = -(side + w * z);
            uz = -(forward - w * x);
        }

        /// <summary>
        /// Циклов Walk в секунду: самой «быстрой» лапе — её ход земли за шаг клипа stride (м/цикл в осях корня), не чаще
        /// <see cref="MaxCyclesPerSecond"/>. Прямо (без поворота и сноса) — ровно forward / stride, как раньше.
        /// </summary>
        public static float CyclesPerSecond(float forward, float side, float yawRate, float stride, float scale)
        {
            float need = 0f;
            for (int paw = 0; paw < PawCount; paw++)
            {
                GroundVelocity(StanceX(paw, scale), StanceZ(paw, scale), forward, side, yawRate, out float ux, out float uz);
                need = Math.Max(need, Length(ux, uz));
            }
            return Math.Min(need / Math.Max(.05f, stride), MaxCyclesPerSecond);
        }

        /// <summary>
        /// Шаг под поворот: стопа лапы (x, z) клипа → (wx, wz). Её смещение от стоп-центра по оси шага растянуто во столько
        /// раз, во сколько ход земли под лапой длиннее шага клипа при cyclesPerSecond (не больше <see cref="MaxStretch"/>), и
        /// повёрнуто к этому ходу (клип ведёт стоящую лапу назад, −z; не больше <see cref="MaxWarpDegrees"/>).
        /// </summary>
        public static void Warp(int paw, float x, float z, float forward, float side, float yawRate, float cyclesPerSecond,
            float stride, float scale, out float wx, out float wz)
        {
            float cx = StanceX(paw, scale), cz = StanceZ(paw, scale);
            GroundVelocity(cx, cz, forward, side, yawRate, out float ux, out float uz);
            float u = Length(ux, uz), step = cyclesPerSecond * stride;
            if (u < 1e-3f || step < 1e-3f) { wx = x; wz = z; return; }
            float k = Math.Min(MaxStretch, u / step);
            double limit = MaxWarpDegrees * (Math.PI / 180.0);
            double angle = Math.Atan2(ux, -uz);
            if (angle > limit) angle = limit;
            else if (angle < -limit) angle = -limit;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            float dx = x - cx, dz = (z - cz) * k;
            wx = cx + dx * cos - dz * sin;
            wz = cz + dx * sin + dz * cos;
        }

        /// <summary>Точка (x, z) осей корня после того, как корень сдвинулся на (side, forward) в прежних осях и повернулся на yaw°.</summary>
        public static void Follow(ref float x, ref float z, float forward, float side, float yawDegrees)
        {
            float px = x - side, pz = z - forward;
            double a = -yawDegrees * (Math.PI / 180.0);
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            // Поворот вокруг вверх в осях Unity (по часовой сверху при a > 0): (x, z) → (x·cos + z·sin, −x·sin + z·cos).
            x = px * cos + pz * sin;
            z = -px * sin + pz * cos;
        }

        internal static float Length(float x, float z) => (float)Math.Sqrt(x * x + z * z);

        internal static float Smooth01(float t) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Лапы одного тела на ходу (<see cref="ThicketWalkRules"/>): в Update — фаза Walk кадра (<see cref="Advance"/>), в
    /// LateUpdate — сдвиг корня для замков (<see cref="Follow"/>) и цель каждой лапы (<see cref="Place"/>). Массивы — один
    /// раз на тело, в бою без выделений.
    /// </summary>
    public sealed class ThicketWalkFeet
    {
        /// <summary>Ход корня этого кадра (м/с вперёд и вправо, как есть) и поворот (°/с по часовой сверху, сглажен YawSmoothSeconds).</summary>
        public float Forward, Side, YawRate;

        /// <summary>Циклов Walk в секунду этого кадра.</summary>
        public float CyclesPerSecond;

        /// <summary>Шаг клипа (м/цикл в осях корня) и масштаб тела (стоп-центры), с которыми считан кадр.</summary>
        public float Stride = ThicketMasterClipRules.DefaultWalkStride, Scale = 1f;

        private readonly bool[] _locked = new bool[ThicketWalkRules.PawCount];
        private readonly float[] _lockX = new float[ThicketWalkRules.PawCount], _lockZ = new float[ThicketWalkRules.PawCount];
        private readonly float[] _lockYaw = new float[ThicketWalkRules.PawCount];
        private readonly float[] _offX = new float[ThicketWalkRules.PawCount], _offZ = new float[ThicketWalkRules.PawCount];
        private readonly float[] _offYaw = new float[ThicketWalkRules.PawCount];

        /// <summary>Поправка замка в последнем кадре опоры (замок − шаг): с неё начинается затухание после отрыва.</summary>
        private readonly float[] _devX = new float[ThicketWalkRules.PawCount], _devZ = new float[ThicketWalkRules.PawCount];
        private bool _warm;

        /// <summary>Ход начался заново (или тело из пула): замков и поправок нет, поворот не сглажен.</summary>
        public void Reset()
        {
            Array.Clear(_locked, 0, _locked.Length);
            Array.Clear(_offX, 0, _offX.Length);
            Array.Clear(_offZ, 0, _offZ.Length);
            Array.Clear(_offYaw, 0, _offYaw.Length);
            Array.Clear(_devX, 0, _devX.Length);
            Array.Clear(_devZ, 0, _devZ.Length);
            Forward = Side = YawRate = WarpForward = WarpSide = CyclesPerSecond = 0f;
            _warm = false;
        }

        /// <summary>
        /// Ход корня для шага под поворот, м/с: сглажен как поворот (Sim меняет скорость тиками по 0,67 м/с — шаг лап в
        /// воздухе не дёргается). Фаза идёт по несглаженному <see cref="Forward"/>: по прямой путь / шаг ровно.
        /// </summary>
        public float WarpForward, WarpSide;

        /// <summary>
        /// Кадр хода (Update): показанный корень прошёл forwardMetres вперёд и sideMetres вправо (оси тела) и повернулся на
        /// yawDegrees за dt. Возвращает, на сколько циклов сдвинуть фазу Walk. stride — шаг клипа в осях корня (м/цикл).
        /// </summary>
        public float Advance(float dt, float forwardMetres, float sideMetres, float yawDegrees, float stride, float scale)
        {
            if (dt <= 1e-5f) return 0f;
            Stride = Math.Max(.05f, stride);
            Scale = scale;
            Forward = forwardMetres / dt;
            Side = sideMetres / dt;
            float rate = yawDegrees / dt;
            float follow = _warm ? 1f - (float)Math.Exp(-dt / ThicketWalkRules.YawSmoothSeconds) : 1f;
            YawRate += (rate - YawRate) * follow;
            WarpForward += (Forward - WarpForward) * follow;
            WarpSide += (Side - WarpSide) * follow;
            _warm = true;
            CyclesPerSecond = ThicketWalkRules.CyclesPerSecond(Forward, Side, YawRate, Stride, Scale);
            return CyclesPerSecond * dt;
        }

        /// <summary>
        /// LateUpdate: корень сдвинулся на (sideMetres, forwardMetres) в прежних своих осях и повернулся на yawDegrees с
        /// прошлого кадра — стоящие лапы остаются на земле: их точки в осях корня едут обратно, а разворот стопы — против.
        /// </summary>
        public void Follow(float forwardMetres, float sideMetres, float yawDegrees)
        {
            for (int paw = 0; paw < ThicketWalkRules.PawCount; paw++)
            {
                if (!_locked[paw]) continue;
                ThicketWalkRules.Follow(ref _lockX[paw], ref _lockZ[paw], forwardMetres, sideMetres, yawDegrees);
                _lockYaw[paw] -= yawDegrees;
            }
        }

        /// <summary>
        /// Цель лапы paw на доле цикла phase: (x, z) — точка касания клипа в осях корня (костяшка передней, скакательный
        /// сустав задней). Возвращает цель (tx, tz) и разворот стопы yaw (°, по часовой сверху) до веса Walk.
        /// </summary>
        public void Place(int paw, float phase, float x, float z, out float tx, out float tz, out float yaw)
        {
            ThicketWalkRules.Warp(paw, x, z, WarpForward, WarpSide, YawRate, CyclesPerSecond, Stride, Scale, out float wx, out float wz);
            float reach = ThicketWalkRules.MaxLockMetres * Scale;
            if (ThicketWalkRules.Planted(paw, phase))
            {
                if (!_locked[paw])
                {
                    // Касание: замок берёт точку там, где стопа встала (уже под поворот), — без скачка.
                    _locked[paw] = true;
                    _lockX[paw] = wx;
                    _lockZ[paw] = wz;
                    _lockYaw[paw] = 0f;
                }
                float dx = _lockX[paw] - wx, dz = _lockZ[paw] - wz, d = ThicketWalkRules.Length(dx, dz);
                if (d > reach)
                {
                    // Дальше досягаемого: замок скользит следом за шагом, а не рвёт ногу.
                    _lockX[paw] = wx + dx * reach / d;
                    _lockZ[paw] = wz + dz * reach / d;
                }
                float limit = ThicketWalkRules.MaxPawYawDegrees;
                if (_lockYaw[paw] > limit) _lockYaw[paw] = limit;
                else if (_lockYaw[paw] < -limit) _lockYaw[paw] = -limit;
                tx = _lockX[paw];
                tz = _lockZ[paw];
                yaw = _lockYaw[paw];
                _devX[paw] = tx - wx;
                _devZ[paw] = tz - wz;
                return;
            }
            if (_locked[paw])
            {
                // Отрыв: поправка замка последнего кадра опоры уходит за ReleaseCycles цикла, лапа уже в воздухе. Не «замок −
                // шаг сейчас»: стопа клипа в этом кадре уже пошла вперёд, и прямой ход получил бы поправку из ничего.
                _locked[paw] = false;
                _offX[paw] = _devX[paw];
                _offZ[paw] = _devZ[paw];
                _offYaw[paw] = _lockYaw[paw];
            }
            float left = 1f - ThicketWalkRules.Smooth01(ThicketWalkRules.SinceLift(paw, phase) / ThicketWalkRules.ReleaseCycles);
            if (left <= 0f) _offX[paw] = _offZ[paw] = _offYaw[paw] = 0f;
            tx = wx + _offX[paw] * left;
            tz = wz + _offZ[paw] * left;
            yaw = _offYaw[paw] * left;
        }

        /// <summary>Лапа сейчас держит землю замком.</summary>
        public bool Locked(int paw) => _locked[paw];
    }

    /// <summary>
    /// Двухкостная ИК лапы (бедро/плечо → колено/локоть → конец): мировые повороты, которые вид домножает слева на
    /// мировые повороты костей. Колено гнётся в своей плоскости клипа (ось — нормаль плоскости a, b, c), потом вся нога
    /// доворачивается к цели кратчайшим поворотом. Цель дальше досягаемого — нога почти прямая (<see cref="MaxReach"/>).
    /// </summary>
    public static class ThicketLegIk
    {
        /// <summary>Нога не выпрямляется больше этой доли длины: прямое колено теряет плоскость сгиба.</summary>
        public const float MaxReach = .995f;

        /// <summary>
        /// a, b, c — плечо (бедро), колено (локоть), конец ноги; target — куда поставить конец. bend — поворот нижней кости
        /// вокруг колена, aim — поворот всей ноги вокруг a (мировые, домножать слева: сначала bend на нижнюю, потом aim на
        /// верхнюю). False — нога вырождена (нулевая длина или прямая без плоскости) — кости не трогать.
        /// </summary>
        public static bool Solve(Vector3 a, Vector3 b, Vector3 c, Vector3 target, out Quaternion bend, out Quaternion aim)
        {
            bend = aim = Quaternion.Identity;
            Vector3 ab = b - a, cb = c - b, ba = a - b;
            float lab = ab.Length(), lcb = cb.Length();
            if (lab < 1e-5f || lcb < 1e-5f) return false;
            Vector3 normal = Vector3.Cross(ba, cb);
            float n = normal.Length();
            if (n < 1e-6f * lab * lcb) return false;
            normal /= n;
            float lat = (target - a).Length();
            float max = (lab + lcb) * MaxReach, min = Math.Abs(lab - lcb) + 1e-4f;
            if (lat > max) lat = max;
            if (lat < min) lat = min;
            // Внутренний угол колена: сейчас и нужный (теорема косинусов).
            float now = Angle(ba, cb);
            float want = (float)Math.Acos(Clamp((lab * lab + lcb * lcb - lat * lat) / (2f * lab * lcb), -1f, 1f));
            // Поворот вокруг normal = ba × cb на +θ уводит cb от ba — угол растёт.
            bend = Quaternion.CreateFromAxisAngle(normal, want - now);
            Vector3 bent = b + Vector3.Transform(cb, bend);
            aim = FromTo(bent - a, target - a);
            return true;
        }

        /// <summary>Кратчайший поворот от направления from к направлению to.</summary>
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            float lf = from.Length(), lt = to.Length();
            if (lf < 1e-6f || lt < 1e-6f) return Quaternion.Identity;
            from /= lf;
            to /= lt;
            float dot = Clamp(Vector3.Dot(from, to), -1f, 1f);
            if (dot > .999999f) return Quaternion.Identity;
            Vector3 axis = Vector3.Cross(from, to);
            if (axis.Length() < 1e-6f)
            {
                // Строго назад: любая ось поперёк.
                axis = Vector3.Cross(from, Math.Abs(from.X) < .9f ? Vector3.UnitX : Vector3.UnitY);
            }
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)Math.Acos(dot));
        }

        private static float Angle(Vector3 u, Vector3 v)
            => (float)Math.Acos(Clamp(Vector3.Dot(u, v) / (u.Length() * v.Length()), -1f, 1f));

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
