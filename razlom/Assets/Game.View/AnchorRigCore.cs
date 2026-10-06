using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>Режим рига якоря (DESIGN §1.2). Значения только дописывать: идут в лог съёмки.</summary>
    public enum AnchorRigMode : byte
    {
        /// <summary>Голова на спине: ведомая, цель — крепление Spine2.</summary>
        OnBack = 0,
        /// <summary>Снятие со спины: живая физика, рукоять уже в левой.</summary>
        Draw = 1,
        /// <summary>Маятник на цепи L от хвата: живая физика без пружин.</summary>
        InHandLive = 2,
        /// <summary>Запечённый путь удара: ведомая, цель — запечка в осях корня по часам Sim.</summary>
        Baked = 3,
        /// <summary>Бросок: ведомая, цель — точка на линии Sim, цепь натянута и выдаётся из хвата.</summary>
        Thrown = 4,
        /// <summary>Рывок назад по той же линии: ведомая.</summary>
        Yank = 5,
        /// <summary>Поймал: живая физика, цепь выбирается рукой до L, голова перелетает хват маятником.</summary>
        Caught = 6,
        /// <summary>Уборка: живая физика, хват ведёт клип к спине.</summary>
        Stow = 7,
        /// <summary>Голову и цепь рисует чужой код (Абордаж, старый Удар якорем).</summary>
        External = 8,
    }

    /// <summary>Числа рига (DESIGN §0.1). Офлайн-запечка (tools/anchorbake) берёт этот же класс и этот же StepLive.</summary>
    public sealed class AnchorRigSettings
    {
        public float ChainLength = 1.60f;
        public float Gravity = 9.81f;
        public float Step = 1f / 240f;
        public float MaxAccumulated = .1f;
        public float AirDrag = .6f, SpinDrag = 1.5f;
        /// <summary>Отскок первого удара о мягкий грунт: скорость касания выше порога → упругость.</summary>
        public float BounceSpeed = 6f, BounceRestitution = .18f;
        /// <summary>Цепь считается натянутой, если кольцо дальше L − допуска от хвата.</summary>
        public float TautTolerance = .02f;
        /// <summary>Наименьшая скорость выбора цепи рукой после броска, м/с.</summary>
        public float MinReelSpeed = 6f;
    }

    /// <summary>
    /// Чистое ядро рига (System.Numerics, без Unity): один <see cref="AnchorHeadDynamics"/> на всё время навыка.
    /// Живые режимы (Draw, InHandLive, Caught, Stow) — только натяжение цепи, тяжесть, земля и тело героя
    /// (<see cref="AnchorRigBody"/>); PullEye/TurnTowards/Catch/LaunchHead здесь не вызываются никогда (проверка §5 #17).
    /// Ведомые режимы (OnBack, Baked, Thrown, Yank) показывают цель плюс затухающее смещение стыка
    /// (<see cref="AnchorBlend"/>, ускорение поправки ≤ 400 м/с²). Переход любой → любой сохраняет положение, поворот и обе скорости.
    /// </summary>
    public sealed class AnchorRigCore
    {
        public readonly AnchorHeadDynamics Body = new AnchorHeadDynamics();
        public readonly AnchorRigSettings Settings;
        private readonly AnchorBlend _blend = new AnchorBlend();
        private float _accumulator, _airborne, _hullRadius = -1f;
        private bool _bounceArmed = true;
        private int _segment = int.MinValue;
        private float _reelSpeed, _reelTarget;

        public AnchorRigCore(AnchorRigSettings settings = null)
        {
            Settings = settings ?? new AnchorRigSettings();
            CableLength = _reelTarget = Settings.ChainLength;
        }

        public AnchorRigMode Mode { get; private set; } = AnchorRigMode.OnBack;
        public AnchorPose Output { get; private set; }
        /// <summary>Длина цепи сейчас: L; после броска — больше L и выбирается до L; в уборке — сматывается к спине.</summary>
        public float CableLength { get; private set; }
        public float BlendError => _blend.Magnitude;
        public float BlendOmega => _blend.Omega;
        /// <summary>Ускорение поправки стыка в этом кадре, м/с² (проверка §5 #8: ≤ 400, кроме дёрга броска).</summary>
        public float CorrectionAccel => _blend.Acceleration;
        /// <summary>Остаток стыка к сроку не уложился в предел ускорения: запечённый контакт будет мимо (в лог).</summary>
        public float DeadlineResidual => _blend.DeadlineResidual;
        /// <summary>Скачок на последнем стыке режимов: положение и скорость показанного состояния (проверка §5 #9).</summary>
        public float SeamJump { get; private set; }
        public float SeamVelocityJump { get; private set; }
        public int Transitions { get; private set; }
        public int Bounces { get; private set; }
        /// <summary>Голова ещё в креплении спины: корпус (центр и кольцо) её не выталкивает, пока центр не вышел из капсулы корпуса.</summary>
        public bool IgnoreTorso { get; set; }
        /// <summary>Уборка на спину: корпус не толкает голову, пока риг не снимет флаг (голова идёт в крепление внутри корпуса).</summary>
        public bool HoldIgnoreTorso { get; set; }
        /// <summary>Глубина последнего выталкивания телом за кадр (для отчёта запечки), м.</summary>
        public float LastBodyPush { get; private set; }
        /// <summary>
        /// Крушение v4, пауза (якорь висит на короткой цепи, timing.json whip_chain_0610.hang): гашение скорости головы
        /// относительно хвата, 1/с (0 — нет; задаёт риг по возрасту паузы). Остальные навыки его не трогают.
        /// </summary>
        public float LiveExtraDrag { get; set; }
        /// <summary>Крушение v4: в живом режиме кольцо всегда смотрит на хват (веретено вдоль цепи), крен не ведётся — голова не крутится на цепи.</summary>
        public bool LiveAlignShank { get; set; }

        public static bool IsLive(AnchorRigMode mode) => mode == AnchorRigMode.Draw || mode == AnchorRigMode.InHandLive
                                                       || mode == AnchorRigMode.Caught || mode == AnchorRigMode.Stow;
        public bool Live => IsLive(Mode);

        /// <summary>Мгновенная постановка без сшивки — только появление тела (пул, смерть) и начало запечки офлайн.</summary>
        public void Teleport(AnchorRigMode mode, in AnchorPose pose)
        {
            Mode = mode; Output = pose; _segment = int.MinValue; _accumulator = 0; _blend.Clear();
            CableLength = _reelTarget = Settings.ChainLength; _reelSpeed = 0; IgnoreTorso = HoldIgnoreTorso = false;
            SetBody(pose);
        }

        /// <summary>
        /// Ведомый режим: показать <paramref name="target"/> плюс затухающее смещение стыка. Новый <paramref name="segment"/>
        /// (или вход из живого режима) засевает сшивку от текущего показанного состояния.
        /// <paramref name="deadline"/> — секунд до тика, к которому смещение обязано погаснуть (контакт − 1), &lt; 0 — нет срока.
        /// </summary>
        public void Drive(AnchorRigMode mode, int segment, in AnchorPose target, float dt, float deadline = -1f, float omega = AnchorBlend.DefaultOmega,
            bool carryVelocity = true)
        {
            if (IsLive(mode) || mode == AnchorRigMode.External) throw new ArgumentException("Drive: ведомый режим", nameof(mode));
            // Первый показ после создания (Teleport не звали): стыковать не с чем.
            if (Output.Rotation.LengthSquared() < .5f) Output = target;
            if (mode != Mode || segment != _segment)
            {
                AnchorPose before = Output;
                // Посадка (Крушение v4: уборка села на крепление): скорость прошлого кадра — не разница с целью этого кадра,
                // гаснет только зазор положения (иначе разница скоростей при пределе 400 м/с² уносит голову на метры).
                if (!carryVelocity) { before.Velocity = target.Velocity; before.AngularVelocity = target.AngularVelocity; }
                // Выпуск и рывок броска — удар руки (исключение §5 #8): их стык не ограничен по ускорению.
                _blend.Start(before, target, omega, limitAccel: mode != AnchorRigMode.Thrown && mode != AnchorRigMode.Yank);
                if (deadline > 0) _blend.MeetDeadline(deadline);
                Seam(before, _blend.Apply(target), mode);
                _segment = segment;
                Output = _blend.Apply(target);
                SetBody(Output);
                return;
            }
            _blend.Advance(dt);
            if (deadline > 0) _blend.MeetDeadline(deadline);
            Output = _blend.Apply(target);
            SetBody(Output);
        }

        /// <summary>
        /// Ведомый режим: кольцо не дальше <paramref name="length"/> от хвата (цепь не тянется). Показанное состояние
        /// сдвигается к хвату, выходящая наружу скорость снимается; сшивка продолжается от сдвинутого (смещение пересеяно).
        /// true — пришлось ограничить.
        /// </summary>
        public bool LimitReach(Vector3 grip, float length)
        {
            if (Live || Mode == AnchorRigMode.External || length <= 0) return false;
            Vector3 eye = Output.Position + Vector3.Transform(Body.EyeLocal, Output.Rotation);
            Vector3 delta = eye - grip;
            float distance = delta.Length();
            if (distance <= length || distance < 1e-5f) return false;
            Vector3 n = delta / distance, shift = -n * (distance - length);
            float outward = Vector3.Dot(Output.Velocity, n);
            Vector3 velocity = outward > 0 ? -n * outward : Vector3.Zero;
            _blend.Shift(shift, velocity);
            Output = new AnchorPose(Output.Position + shift, Output.Rotation, Output.Velocity + velocity, Output.AngularVelocity);
            SetBody(Output);
            return true;
        }

        /// <summary>Толчок смещения в ведомом режиме (дёрг броска в тик натяга): голова отскакивает и возвращается на линию.</summary>
        public void Kick(Vector3 velocity) => _blend.Kick(velocity);
        public float KickSpeedFor(float amplitude) => AnchorBlend.KickSpeedFor(amplitude, _blend.Omega);

        /// <summary>
        /// Войти в живой режим. Тело берёт показанное состояние как есть (положение, поворот, скорости).
        /// Из спины — голова ещё внутри корпуса: корпус её не толкает, пока она не выйдет (<see cref="IgnoreTorso"/>).
        /// </summary>
        public void EnterLive(AnchorRigMode mode, Vector3 grip)
        {
            if (!IsLive(mode)) throw new ArgumentException("EnterLive: живой режим", nameof(mode));
            if (Mode == mode) return;
            AnchorPose before = Output;
            bool fromThrow = Mode == AnchorRigMode.Yank || Mode == AnchorRigMode.Thrown;
            if (Mode == AnchorRigMode.OnBack) IgnoreTorso = true;
            SetBody(before);
            float span = Vector3.Distance(Body.Eye, grip);
            CableLength = Math.Max(Settings.ChainLength, span);
            _reelTarget = Settings.ChainLength;
            _reelSpeed = CableLength > Settings.ChainLength + 1e-4f
                ? Math.Max(Settings.MinReelSpeed, fromThrow ? before.Velocity.Length() : Settings.MinReelSpeed) : 0;
            _accumulator = 0; _segment = int.MinValue;
            Seam(before, before, mode);
            Output = before;
        }

        /// <summary>
        /// Смотать цепь до <paramref name="length"/> со скоростью <paramref name="speed"/> м/с (уборка: голову к спине тянет
        /// натяжение цепи, не пружина к позе). Длина не растёт: если цепь уже короче, ничего не делает.
        /// </summary>
        public void ReelTo(float length, float speed)
        {
            _reelTarget = Math.Max(.05f, length);
            _reelSpeed = CableLength > _reelTarget + 1e-4f ? Math.Max(.1f, speed) : 0;
        }

        /// <summary>Новое нажатие во время уборки: цепь снова L (выдача мгновенна — трос-неравенство просто провисает).</summary>
        public void ResetCable()
        {
            CableLength = _reelTarget = Settings.ChainLength; _reelSpeed = 0; HoldIgnoreTorso = false;
        }

        /// <summary>
        /// Шаг живой физики за кадр <paramref name="frameDt"/>: подшаги 1/240 с реального времени, хват от
        /// <paramref name="gripFrom"/> к <paramref name="gripTo"/> по кадру. Ни одной пружины к позе.
        /// </summary>
        public void StepLive(float frameDt, Vector3 gripFrom, Vector3 gripTo, Func<Vector3, float> ground,
            AnchorCapsule[] capsules, int capsuleCount)
        {
            if (!Live) throw new InvalidOperationException("StepLive вне живого режима: " + Mode);
            if (frameDt <= 0) return;
            if (_hullRadius < 0) _hullRadius = AnchorRigBody.HullRadius(Body.Hull);
            Vector3 gripVelocity = (gripTo - gripFrom) / frameDt;
            float h = Settings.Step;
            _accumulator = Math.Min(_accumulator + frameDt, Settings.MaxAccumulated);
            Vector3 gravity = new Vector3(0, -Settings.Gravity, 0);
            LastBodyPush = 0;
            while (_accumulator >= h)
            {
                _accumulator -= h;
                float alpha = Math.Clamp(1f - _accumulator / frameDt, 0f, 1f);
                Vector3 grip = Vector3.Lerp(gripFrom, gripTo, alpha);
                Body.Advance(h, gravity);
                float reel = 0;
                if (_reelSpeed > 0)
                {
                    float next = Math.Max(_reelTarget, CableLength - _reelSpeed * h);
                    reel = (next - CableLength) / h;
                    CableLength = next;
                    if (CableLength <= _reelTarget + 1e-5f) _reelSpeed = 0;
                }
                Body.ConstrainCable(grip, gripVelocity, CableLength, reel, h);
                Vector3 impact = Body.Velocity;
                if (ground != null)
                {
                    Body.CollideGround(ground, h);
                    Bounce(impact, h);
                }
                CollideBody(capsules, capsuleCount);
                Body.Velocity *= (float)Math.Exp(-Settings.AirDrag * h);
                Body.AngularVelocity *= (float)Math.Exp(-Settings.SpinDrag * h);
                if (LiveExtraDrag > 0)
                    Body.Velocity = gripVelocity + (Body.Velocity - gripVelocity) * (float)Math.Exp(-LiveExtraDrag * h);
                if (LiveAlignShank) AlignShank(grip);
            }
            Output = new AnchorPose(Body.Position, Body.Rotation, Body.Velocity, Body.AngularVelocity);
        }

        /// <summary>Веретено (центр → кольцо) — на хват по кратчайшей дуге, без крена и без своей угловой скорости.</summary>
        private void AlignShank(Vector3 grip)
        {
            Vector3 to = grip - Body.Position;
            if (to.LengthSquared() < 1e-6f || Body.EyeLocal.LengthSquared() < 1e-8f) return;
            Vector3 a = Vector3.Transform(Vector3.Normalize(Body.EyeLocal), Body.Rotation), b = Vector3.Normalize(to);
            float d = Vector3.Dot(a, b);
            if (d < -.9999f) return;   // ровно против хвата: ждём кадр, куда повернуть — неясно
            Body.Rotation = Quaternion.Normalize(Quaternion.Normalize(new Quaternion(Vector3.Cross(a, b), 1f + d)) * Body.Rotation);
            Body.AngularVelocity = Vector3.Zero;
        }

        private void CollideBody(AnchorCapsule[] capsules, int count)
        {
            if (capsules == null) return;
            if (IgnoreTorso && !HoldIgnoreTorso)
            {
                // Голова вышла из крепления, как только центр покинул корпус: с этого шага корпус снова твёрдый.
                bool inside = false;
                for (int i = 0; i < count; i++)
                    if (capsules[i].Kind == AnchorCapsuleKind.Center && capsules[i].AxisDistance(Body.Position, out _) < capsules[i].Radius) inside = true;
                if (!inside) IgnoreTorso = false;
            }
            for (int i = 0; i < count; i++)
            {
                var c = capsules[i];
                if ((IgnoreTorso || HoldIgnoreTorso) && c.Torso) continue;
                Vector3 before = Body.Position;
                switch (c.Kind)
                {
                    case AnchorCapsuleKind.Ring: Body.CollideEyeBody(c.A, c.B, c.Radius); break;
                    case AnchorCapsuleKind.Center: Body.CollideBody(c.A, c.B, c.Radius); break;
                    default: AnchorRigBody.PushHull(Body, c, _hullRadius); break;
                }
                LastBodyPush = Math.Max(LastBodyPush, Vector3.Distance(before, Body.Position));
            }
        }

        /// <summary>Сменить живой режим на другой живой (InHandLive → Stow и т. п.): тело не трогается.</summary>
        public void Relabel(AnchorRigMode mode)
        {
            if (!IsLive(mode) || !Live) throw new InvalidOperationException("Relabel только между живыми режимами");
            if (mode != Mode) { Mode = mode; Transitions++; }
        }

        public void MarkExternal()
        {
            if (Mode == AnchorRigMode.External) return;
            Mode = AnchorRigMode.External; _segment = int.MinValue; Transitions++;
        }

        /// <summary>Расстояние кольцо — хват и натяг по допуску.</summary>
        public bool IsTaut(Vector3 grip) => Vector3.Distance(Body.Eye, grip) >= CableLength - Settings.TautTolerance;

        private void Bounce(Vector3 before, float h)
        {
            if (!Body.Grounded)
            {
                _airborne += h;
                if (_airborne > .12f) _bounceArmed = true;
                return;
            }
            _airborne = 0;
            if (!_bounceArmed) return;
            _bounceArmed = false;
            float into = -before.Y;
            if (into < Settings.BounceSpeed) return;
            // Первый удар о мягкий грунт: голова подпрыгивает и кувыркается (импульс между центром и нижней точкой
            // оболочки — и подскок, и вращение), а не замирает на три кадра. Упругость контакта .04 уже дал CollideGround.
            Vector3 lowest = Body.Position;
            float y = float.PositiveInfinity;
            foreach (var local in Body.Hull)
            {
                Vector3 p = Body.Position + Vector3.Transform(local, Body.Rotation);
                if (p.Y < y) { y = p.Y; lowest = p; }
            }
            Body.Impulse(Vector3.UnitY * (Body.Mass * (Settings.BounceRestitution - .04f) * into), Vector3.Lerp(Body.Position, lowest, .35f));
            Bounces++;
        }

        private void SetBody(in AnchorPose pose)
        {
            Body.Position = pose.Position;
            Body.Rotation = pose.Rotation;
            Body.Velocity = pose.Velocity;
            Body.AngularVelocity = pose.AngularVelocity;
        }

        private void Seam(in AnchorPose before, in AnchorPose after, AnchorRigMode mode)
        {
            SeamJump = Vector3.Distance(before.Position, after.Position);
            SeamVelocityJump = Vector3.Distance(before.Velocity, after.Velocity);
            if (mode != Mode) Transitions++;
            Mode = mode;
        }
    }
}
