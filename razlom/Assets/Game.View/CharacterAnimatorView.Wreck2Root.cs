using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Крушение v4: корень героя (взгляд к направлению этапа, без сдвига тела), конец показа, срыв, сброс.</summary>
    public sealed partial class CharacterAnimatorView
    {
        private float _wreck2RecoverUntil, _wreck2ReleaseAt = -1f;
        private Vector3 _wreck2LastFacing, _wreck2LastShift, _wreck2ReleaseShift;

        /// <summary>
        /// Корень героя для ArenaView: взгляд и сдвиг тела от позиции Sim. lastShown — взгляд, показанный в прошлом
        /// кадре (с него начинается поворот замаха этапа), proposed — то, что ArenaView поставил бы сам.
        /// В окне идущему (корень у ArenaView) — его взгляд, сглаженный; false — Крушение корнем не правит.
        /// </summary>
        public bool TryGetWreck2Body(Vector3 lastShown, Vector3 proposed, out Vector3 facing, out Vector3 shift)
        {
            facing = proposed;
            shift = Vector3.zero;
            if (_faction != Faction.Wole) return false;
            Simulation sim = TempoSim;
            // Сменили в этом же кадре (событие пришло в LateUpdate): корень уже не наш.
            if (_wreck2Driven && !Wreck2OwnsPresentation) ReleaseWreck2("replaced", true);
            if (_wreck2Driven && sim != null && sim == _wreck2Sim && !IsDead)
            {
                float now = Wreck2Now(sim);
                PelagWreckTimeline line = _wreck2Feed.Timeline;
                ReadWreck2Events(sim);
                _wreck2Feed.Track(sim);
                if (line.NeedsStartYaw(now) && lastShown.sqrMagnitude > .25f)
                    line.SetStageStartYaw(PelagSquallClipRules.YawOf(lastShown.x, lastShown.z), now);
                PelagWreckPose pose = line.Sample(now, Wreck2Scale);
                if (pose.RootOwned)
                {
                    PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
                    facing = new Vector3(x, 0f, z);
                }
                else
                {
                    // Окно, герой идёт: взгляд — ArenaView (якорный навык смотрит в прицел), без рывка в кадр.
                    Vector3 from = _wreck2LastFacing.sqrMagnitude > .25f ? _wreck2LastFacing : lastShown;
                    facing = from.sqrMagnitude > .25f && proposed.sqrMagnitude > .25f
                        ? Vector3.Slerp(from, proposed, 1f - Mathf.Exp(-16f * Time.deltaTime)).normalized : proposed;
                }
                _wreck2LastFacing = facing;
                shift = new Vector3(pose.ShiftX, 0f, pose.ShiftY);
                _wreck2LastShift = shift;
                return true;
            }
            bool used = false;
            // Рывок разворачивает сразу и сам (ArenaView, RollActive) — ему не мешаем; Шквал и Абордаж правят корнем сами.
            if (_squallDriven || _abordageDriven) { _wreck2RecoverUntil = 0f; _wreck2ReleaseAt = -1f; return false; }
            if (Time.time < _wreck2RecoverUntil && !RollActive
                && proposed.sqrMagnitude > .25f && _wreck2LastFacing.sqrMagnitude > .25f)
            {
                // Конец серии: взгляд догоняет Sim за четверть секунды, а не прыжком в кадр.
                _wreck2LastFacing = Vector3.Slerp(_wreck2LastFacing, proposed, 1f - Mathf.Exp(-16f * Time.deltaTime)).normalized;
                facing = _wreck2LastFacing;
                used = true;
            }
            if (_wreck2ReleaseAt >= 0f)
            {
                float u = (Time.time - _wreck2ReleaseAt) / Wreck2ShiftReleaseSeconds;
                if (u >= 1f) _wreck2ReleaseAt = -1f;
                else { shift = _wreck2ReleaseShift * (1f - Mathf.SmoothStep(0f, 1f, u)); used = true; }
            }
            return used;
        }

        /// <summary>Показ доигран (уборка кончилась) или Sim сорвала серию: тело уходит в стойку или бег.</summary>
        private void EndWreck2(in PelagWreckPose pose)
        {
            if (!_wreck2Driven) return;
            _wreck2Driven = false;
            ReleaseWreck4Bind();
            PelagWreckTimeline line = _wreck2Feed.Timeline;
            bool cut = line.Ended && line.EndReason == WreckEnd.Interrupted;
            line.Stop();
            if (Wreck2OwnsPresentation)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _actionProtectedUntil = 0f;
            }
            PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
            if (pose.Stage < 0 || cut) _wreck2LastFacing = new Vector3(x, 0f, z);
            _wreck2RecoverUntil = Time.time + Wreck2FacingRecoverySeconds;
            StartWreck2ShiftRelease();
            Wreck2Trace($"end clip={_wreck2Clip} frame={pose.Frame:F2} reason={line.EndReason} moving={_locomotionMoving}");
            if (IsDead || _animator == null || !BaseInWreck2()) return;
            float blend = cut ? Wreck2CancelBlend : Wreck2ExitBlend;
            _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, blend, 0, 0f);
            // Stow кончается стойкой серии сабли — тот же шаг выхода в покой, что у Шквала и Абордажа.
            if (!_locomotionMoving && !cut) BeginSquallExitStep(blend);
        }

        /// <summary>
        /// Тело забрал кто-то другой: ленту не ведём, сдвиг гаснет за 0,08 с. replaced — сменило действие: взгляд
        /// догоняет его плавно, а через кадр, если базовый слой остался в клипе Крушения, он уходит в бег или стойку.
        /// Якорь — дело рига: без серии Sim он сам уходит в живую физику и на спину.
        /// </summary>
        private void ReleaseWreck2(string why, bool replaced)
        {
            if (!_wreck2Driven) return;
            _wreck2Driven = false;
            ReleaseWreck4Bind();
            _wreck2Feed.Timeline.Stop();
            _wreck2RecoverUntil = replaced ? Time.time + Wreck2FacingRecoverySeconds : 0f;
            _wreck2BaseCheckFrame = replaced ? Time.frameCount + 1 : -1;
            StartWreck2ShiftRelease();
            Wreck2Trace("release " + why);
        }

        private void StartWreck2ShiftRelease()
        {
            _wreck2ReleaseShift = _wreck2LastShift;
            _wreck2LastShift = Vector3.zero;
            _wreck2ReleaseAt = _wreck2ReleaseShift.sqrMagnitude > 1e-6f ? Time.time : -1f;
        }

        private void ResetWreck2()
        {
            _wreck2Driven = false;
            ReleaseWreck4Bind();
            _wreck2Support = -1;
            _wreck2Feed.Timeline.Stop();
            _wreck2Clip = PelagWreckClip.None;
            _wreck2Pose = default;
            _wreck2PoseFrame = -1;
            _wreck2Sim = null;
            _wreck2RecoverUntil = 0f;
            _wreck2ReleaseAt = -1f;
            _wreck2BaseCheckFrame = -1;
            _wreck2LastShift = Vector3.zero;
            _wreck2LastFacing = Vector3.zero;
        }
    }
}
