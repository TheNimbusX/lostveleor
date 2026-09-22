using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Шаги Пелага в лагере (владелец, 22 сентября). Звук — в кадр касания ступни
    /// (PelagFootPlantView, тот же, от которого летит пыль), а не по пройденному пути:
    /// шаг должен совпадать с ногой. Поверхность: мост — доски и изредка скрип
    /// настила; дорожка (красный канал карты грунта, CampGroundStudy.IsDustyPath) —
    /// шаги владельца из боя step_pt1–3 по очереди; остальное — мягкая трава.
    /// Вешается на объект CampPlayerView в игре.
    /// </summary>
    // После PelagFootPlantView (1000): касание ступни отмечается в его LateUpdate этого же кадра.
    [DefaultExecutionOrder(1020)]
    public sealed class CampFootsteps : MonoBehaviour
    {
        [Range(0f, 1f)] public float GrassVolume = .3f;
        [Range(0f, 1f)] public float PathVolume = .32f;
        [Range(0f, 1f)] public float WoodVolume = .42f;
        [Tooltip("Доля шагов по мосту со скрипом досок")] [Range(0f, 1f)] public float CreakChance = .12f;

        CampPlayerView _player;
        CampRiverPassage _passage;
        CampGroundStudy _ground;
        ArenaView _arena;
        Transform _body;
        PelagFootPlantView _feet;
        AudioClip[] _pathSteps;
        int _pathPart;

        /// <summary>Счёт шагов по поверхностям для проверки без слуха (CampServicesProbe).</summary>
        public static int GrassSteps, PathSteps, WoodSteps;

        void Start()
        {
            _player = GetComponent<CampPlayerView>();
            _passage = FindAnyObjectByType<CampRiverPassage>();
            _ground = FindAnyObjectByType<CampGroundStudy>(FindObjectsInactive.Include);
            _arena = FindAnyObjectByType<ArenaView>();
            _pathSteps = Resources.LoadAll<AudioClip>("Audio/Combat/Footstep");
            System.Array.Sort(_pathSteps, (a, b) => string.CompareOrdinal(a.name, b.name));
        }

        void LateUpdate()
        {
            if (_player == null || !_player.Active || _arena == null) return;
            if (!_arena.TryGetEntityView(Game.Sim.Simulation.PlayerId, out Transform body)) return;
            if (body != _body) { _body = body; _feet = body.GetComponentInChildren<PelagFootPlantView>(); }
            if (_feet == null) return;
            for (int side = 0; side < 2; side++)
                if (_feet.TryGetStepContact(side == 0, out Vector3 at)) Step(at);
        }

        void Step(Vector3 at)
        {
            if (OnBridge(at))
            {
                WoodSteps++;
                GameSound.Play("camp_step_wood", WoodVolume, .08f, .1f);
                if (Random.value < CreakChance) GameSound.Play("camp_bridge_creak", .3f, .05f, 1.5f);
                return;
            }
            if (_ground != null && _ground.IsDustyPath(at) && _pathSteps.Length > 0)
            {
                PathSteps++;
                // Части поступи владельца идут подряд, как в бою: вместе они звучат связно.
                GameSound.PlayClip(_pathSteps[_pathPart++ % _pathSteps.Length], PathVolume, .04f);
                return;
            }
            GrassSteps++;
            GameSound.Play("camp_step_grass", GrassVolume, .08f, .1f);
        }

        bool OnBridge(Vector3 p)
        {
            if (_passage == null) return false;
            Bounds b = _passage.BridgeBounds;
            return p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z;
        }
    }
}
