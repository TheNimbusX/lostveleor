using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Серия сабли «морская пена» (вариант Б 01.10, сборка эффектов —
    /// PelagSabreComboVfxSetup). Всё рождается от событий Sim, а не опросом
    /// тиков (в сборке плеера опрос однажды не рисовал слои Рассекающего):
    ///  • SabreContact — волна удара в любом случае, даже при промахе:
    ///    лёгкий удар — горизонтальный полумесяц на высоте клинка во весь
    ///    сектор, удар 2 — его зеркало; добивающий — стоячая волна к камере
    ///    и клочья пены по земле у края досягаемости. При попадании —
    ///    стоп-кадр героя;
    ///  • Damage обычной атакой — всплеск пены на теле цели по ходу клинка и
    ///    стоп-кадр цели; у добивающего и крита всплеск крупнее.
    /// Тряска и зум — CombatJuiceView, лента клинка — там же.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        // Высота волны над землёй, м: удар 1 идёт клинком на уровне плеч,
        // удар 2 (с отмаха) — у пояса (съёмка V2: кончик 1,65 и 0,75 м на контакте).
        private static readonly float[] SabreWaveHeight = { 1.25f, .80f };
        // Крен плоскости волны вокруг оси удара, град: у удара 1 голова ниже хвоста, у удара 2 — выше.
        private static readonly float[] SabreWaveRoll = { 8f, 6f };
        /// <summary>
        /// Внешний край волны — доля досягаемости сектора. Урон достаёт тело
        /// цели на 2,5 м, а гребень на 0,96 проходил сквозь середины тел
        /// (съёмка V2): на 0,82 вода идёт перед целями и касается их.
        /// </summary>
        private const float SabreWaveReach = .82f;
        /// <summary>Центр волны чуть позади героя: острые концы обнимают его бока.</summary>
        private const float SabreWaveBack = .15f;
        // Стоп-кадр контакта, с: лёгкий удар и добивающий (герой — только при попадании).
        private const float SabreLightHold = .05f, SabreFinisherHold = .085f;
        // Стоячая волна добивающего — как серп Рассекающего: центр впереди и
        // выше, подтянут к камере, верх чуть уходит от камеры.
        private const float SabreCrashForward = .45f;
        private const float SabreCrashHeight = 1.15f;
        private const float SabreCrashTowardCamera = .7f;
        private const float SabreCrashTilt = 18f;
        /// <summary>Внешний радиус стоячей волны, м, до подгонки под досягаемость.</summary>
        private const float SabreCrashSize = 2.2f;
        // Дуга внешнего края стоячей волны в осях корня, град (полумесяц 170°,
        // повёрнут на 198° в префабе): по ней подгоняется досягаемость.
        private const float SabreCrashRimFrom = -67f, SabreCrashRimTo = 103f;
        // Всплеск на теле: лёгкий удар, добивающий, множитель крита.
        private const float SabreSplashHeavyScale = 1.4f, SabreSplashCritScale = 1.2f;

        /// <summary>Контакт удара серии: волна по сектору и стоп-кадр героя при попадании.</summary>
        private void PlaySabreContact(in SimEvent e)
        {
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            SabreSwingState swing = sim.SabreSwing;
            bool sameSwing = swing.Serial > 0 && swing.Hit == e.ActionVariant;
            bool finisher = e.Flag;
            int hit = Mathf.Clamp(e.ActionVariant, 0, 2);
            // Промах не «залипает»: стоп только если в секторе кто-то был.
            if (e.Amount > 0 && sameSwing && !CaptureRig.NoVfx)
                _arena.ConfirmSabreContact(swing.Serial, finisher ? SabreFinisherHold : SabreLightHold);
            if (CaptureRig.NoVfx) return;

            Vector3 forward = sameSwing ? SabreForward(swing.Direction) : PlayerFacing();
            // Центр — позиция героя в Sim на тике контакта: вершина сектора урона.
            Vector3 origin = new Vector3(e.Position.X.ToFloat(), PlayerPosition().y, e.Position.Y.ToFloat());
            float reach = Simulation.SabreReach.ToFloat();
            if (finisher)
            {
                PlaySabreCrash(origin, forward, reach);
                PlaySabreWash(origin, forward, reach);
            }
            else PlaySabreWave(origin, forward, hit, reach);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[sabre-contact] tick={sim.Tick} hit={hit} targets={e.Amount} serial={swing.Serial} forward={forward.ToString("F2")}");
        }

        private static Vector3 SabreForward(FixVec2 direction)
        {
            var forward = new Vector3(direction.X.ToFloat(), 0f, direction.Y.ToFloat());
            return forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Волна лёгкого удара. Оси корня: −X — удар, +Y — куда уходит клинок
        /// (голова волны), +Z — нормаль. Удар 1 справа налево: голова слева,
        /// нормаль вверх; удар 2 — зеркало: голова справа, нормаль вниз (тот
        /// же поворот на 180° вокруг оси удара, без отрицательного масштаба).
        /// </summary>
        private void PlaySabreWave(Vector3 origin, Vector3 forward, int hit, float reach)
        {
            if (!TryAcquire(PelagVfxId.SabreWave, out GameObject go, out PelagVfxElement element)) return;
            int side = hit == 1 ? 1 : 0;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 headSide = side == 1 ? right : -right;
            Vector3 normal = side == 1 ? Vector3.down : Vector3.up;
            Quaternion roll = Quaternion.AngleAxis(SabreWaveRoll[side], forward);
            Quaternion rotation = Quaternion.LookRotation(roll * normal, roll * headSide);
            Vector3 center = origin + Vector3.up * SabreWaveHeight[side] - forward * SabreWaveBack;
            int index = ReserveActive();
            element.Begin(center, rotation);
            go.transform.localScale = Vector3.one * (reach * SabreWaveReach + SabreWaveBack);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.SabreWave, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = center, End = center,
                Motion = Motion.Static, FollowIndex = -1
            };
        }

        /// <summary>
        /// Стоячая волна добивающего: билборд к камере, повёрнутый в плоскости
        /// экрана по проекции удара (вертикальная плоскость вдоль удара при
        /// взгляде вдоль неё схлопнулась бы в линию). Размер подгоняется так,
        /// чтобы гребень на земле доходил до края досягаемости.
        /// </summary>
        private void PlaySabreCrash(Vector3 origin, Vector3 forward, float reach)
        {
            if (!TryAcquire(PelagVfxId.SabreCrash, out GameObject go, out PelagVfxElement element)) return;
            Camera camera = Camera.main;
            Vector3 center = origin + forward * SabreCrashForward + Vector3.up * SabreCrashHeight;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(forward, Vector3.up), Vector3.up);
            float scale = SabreCrashSize;
            if (camera != null)
            {
                Vector3 a = camera.WorldToScreenPoint(center);
                Vector3 b = camera.WorldToScreenPoint(center + forward);
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, angle) * Quaternion.Euler(SabreCrashTilt, 0f, 0f);
                center += (camera.transform.position - center).normalized * SabreCrashTowardCamera;
                if (camera.orthographic) scale = FitSabreCrash(camera, center, rotation, forward, origin, reach);
            }
            int index = ReserveActive();
            element.Begin(center, rotation);
            go.transform.localScale = Vector3.one * scale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.SabreCrash, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = center, End = center,
                Motion = Motion.Static, FollowIndex = -1
            };
        }

        /// <summary>
        /// Масштаб стоячей волны: проекция внешнего края вдоль взгляда
        /// орто-камеры на землю должна дойти до досягаемости удара. Проекция
        /// линейна — решается одним делением; зажато, чтобы к камере волна не
        /// раздувалась (как FitCleaveSplitToReach).
        /// </summary>
        private static float FitSabreCrash(Camera camera, Vector3 center, Quaternion rotation, Vector3 forward, Vector3 origin, float reach)
        {
            Vector3 view = camera.transform.forward;
            if (view.y > -.05f) return SabreCrashSize;
            float ground = origin.y;
            Vector3 Ground(Vector3 w) => w + view * ((ground - w.y) / view.y);
            float baseReach = Vector3.Dot(Ground(center) - origin, forward);
            float rim = 0f;
            for (int i = 0; i <= 16; i++)
            {
                float t = Mathf.Lerp(SabreCrashRimFrom, SabreCrashRimTo, i / 16f) * Mathf.Deg2Rad;
                Vector3 point = center + rotation * new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0f);
                rim = Mathf.Max(rim, Vector3.Dot(Ground(point) - Ground(center), forward));
            }
            if (rim < .05f) return SabreCrashSize;
            float fitted = (reach + .2f - baseReach) / rim;
            return Mathf.Clamp(fitted, SabreCrashSize * .75f, SabreCrashSize * 1.15f);
        }

        /// <summary>Пена добивающего по земле: клочья и брызги по краю сектора (своей дуги нет — волна одна, стоячая).</summary>
        private void PlaySabreWash(Vector3 origin, Vector3 forward, float reach)
        {
            if (!TryAcquire(PelagVfxId.SabreWash, out GameObject go, out PelagVfxElement element)) return;
            Vector3 left = -Vector3.Cross(Vector3.up, forward);
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, left);
            Vector3 center = origin + Vector3.up * .06f - forward * SabreWaveBack;
            int index = ReserveActive();
            element.Begin(center, rotation);
            go.transform.localScale = Vector3.one * (reach + SabreWaveBack);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.SabreWash, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = center, End = center,
                Motion = Motion.Static, FollowIndex = -1
            };
        }

        /// <summary>
        /// Всплеск пены на теле цели и её стоп-кадр. Брызги уходят по ходу
        /// клинка в точке цели: удар 1 — справа налево, удар 2 — слева
        /// направо, добивающий — вперёд и вверх от удара сверху.
        /// </summary>
        private void PlaySabreSplash(in SimEvent e)
        {
            if (CaptureRig.NoVfx) return;
            bool finisher = e.ActionVariant == 2;
            _arena.HoldEntityPose(e.Target, finisher ? SabreFinisherHold : SabreLightHold);
            if (!TryAcquire(PelagVfxId.SabreSplash, out GameObject go, out PelagVfxElement element)) return;
            Vector3 body = EntityPosition(e.Target, e.Position);
            Vector3 position = body + Vector3.up * .9f;
            Camera camera = Camera.main;
            // Перед поверхностью тела, иначе всплеск тонет в модели.
            if (camera != null) position += (camera.transform.position - position).normalized * .45f;
            Vector3 radial = Vector3.ProjectOnPlane(body - PlayerPosition(), Vector3.up);
            radial = radial.sqrMagnitude > .0001f ? radial.normalized : PlayerFacing();
            Vector3 right = Vector3.Cross(Vector3.up, radial);
            Vector3 along = finisher
                ? radial + Vector3.up * .6f
                : (e.ActionVariant == 1 ? right : -right) + radial * .35f + Vector3.up * .35f;
            float scale = finisher ? SabreSplashHeavyScale : 1f;
            if (e.Flag) scale *= SabreSplashCritScale;
            int index = ReserveActive();
            element.Begin(position, Quaternion.LookRotation(along.normalized));
            go.transform.localScale = Vector3.one * scale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.SabreSplash, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = position, End = position,
                Motion = Motion.Static, FollowIndex = -1
            };
        }
    }
}
