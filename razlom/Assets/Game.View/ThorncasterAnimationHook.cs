using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХУК КЛИПОВ ШИПОМЁТА ДЛЯ АРТ-КОНВЕЙЕРА.
    ///
    /// ArenaView на EnemyActionStarted(ThornShot) зовёт <see cref="TryPlayShot"/>
    /// для тела без заглушки. У собранного тела (ThorncasterBuilder) позу ведёт
    /// ThorncasterAnimatorView по фазам Sim — выстрел, линию и всплеск разом, —
    /// и хук только подтверждает: клип есть, играть его отдельно не нужно.
    /// Для тела с клипом <see cref="ShotClip"/> без этого вида (старый
    /// контроллер) хук, как раньше, играет выстрел с начала замаха.
    ///
    /// Клип обязан выпускать шип на кадре <see cref="ShotReleaseFrame"/> при
    /// <see cref="ClipFramesPerSecond"/> кадрах в секунду: это ровно тик выпуска
    /// Sim (Simulation.ThornShotWindupTicks), и шип вылетает из руки, а не из
    /// воздуха. Стойка после выпуска — Simulation.ThornShotRecoveryTicks.
    /// </summary>
    public static class ThorncasterAnimationHook
    {
        public const string ShotClip = "ForestThorncaster_Shot";
        public const int ShotReleaseFrame = Simulation.ThornShotWindupTicks;
        public const float ClipFramesPerSecond = Simulation.TicksPerSecond;

        private static readonly int ShotState = Animator.StringToHash(ShotClip);

        /// <summary>
        /// Выстрел в теле body. ticksLate — сколько тиков Sim прошло от начала
        /// выстрела к этому кадру (события кадра приходят пачкой): клип
        /// начинается с того же места, и выпуск совпадает с Sim. true — выстрел
        /// показан (своим видом или клипом), false — показать нечем (заглушка).
        /// </summary>
        public static bool TryPlayShot(Transform body, float ticksLate = 0f)
        {
            if (body == null) return false;
            // Фазы ведёт вид: Play поверх него сорвал бы параметр времени.
            if (body.GetComponent<ThorncasterAnimatorView>() != null) return true;
            var animator = body.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null || !animator.HasState(0, ShotState))
                return false;
            float length = 0f;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip != null && clip.name == ShotClip) { length = clip.length; break; }
            float normalized = length > 0f ? Mathf.Clamp01(Mathf.Max(0f, ticksLate) / ClipFramesPerSecond / length) : 0f;
            animator.Play(ShotState, 0, normalized);
            return true;
        }
    }

    /// <summary>
    /// ТОЧКА ВХОДА ШИПОМЁТА ДЛЯ ARENAVIEW. Тело (префаб ThorncasterBuilder)
    /// приходит из пула семьи 0 (ArenaView.PrepareForestMob); здесь оно
    /// получает свою сущность, а арена — вид эффектов линии, всплеска и
    /// выстрела (ThorncasterCombatView).
    ///
    ///   • ArenaView.PrepareForestMob, семья 0, рядом с ForestThornShotView:
    ///       ThorncasterViewInstaller.Prepare(gameObject);
    ///   • ArenaView.BindNewEntities, после _placeholderViews[i]?.Bind(...):
    ///       ThorncasterViewInstaller.Attach(go, _driver, i);
    ///
    /// Оба вызова необязательны для работы: без Prepare вид эффектов заводится
    /// с первым телом, без Attach тело само находит свою сущность через
    /// ArenaView.TryGetEntityView (на кадр позже). С ними — без кадра в Idle и
    /// без прогрева пулов посреди боя.
    /// </summary>
    public static class ThorncasterViewInstaller
    {
        /// <summary>
        /// Заводит на объекте арены (там же TickDriver, LayoutView, ArenaView)
        /// вид эффектов Шипомёта. Повторный вызов ничего не делает.
        /// </summary>
        public static ThorncasterCombatView Prepare(GameObject arena)
        {
            if (arena == null || arena.GetComponent<TickDriver>() == null) return null;
            var view = arena.GetComponent<ThorncasterCombatView>();
            return view != null ? view : arena.AddComponent<ThorncasterCombatView>();
        }

        /// <summary>
        /// Привязывает тело Шипомёта к сущности entity. false — у тела нет
        /// ThorncasterAnimatorView (заглушка или чужое тело): ничего не сделано.
        /// </summary>
        public static bool Attach(GameObject body, TickDriver driver, int entity)
        {
            if (body == null || driver == null) return false;
            var view = body.GetComponent<ThorncasterAnimatorView>();
            if (view == null) return false;
            view.Bind(driver, entity);
            return true;
        }

        /// <summary>Вид тела Шипомёта или null (заглушка, другой моб).</summary>
        public static ThorncasterAnimatorView ViewOf(Transform body)
            => body != null ? body.GetComponent<ThorncasterAnimatorView>() : null;
    }
}
