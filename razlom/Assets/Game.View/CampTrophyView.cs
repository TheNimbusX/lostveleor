using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Трофеи побеждённых боссов у костра (решение 06.10): трофей i стоит в точке i,
    /// когда босс <see cref="RunBossKeys.At"/>(i) повержен хотя бы раз. Модели грузятся
    /// только при первой победе, до неё точка пустая и ничего не рисует.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Трофеи у костра")]
    public sealed class CampTrophyView : MonoBehaviour
    {
        [Tooltip("Точки трофеев по порядку боссов акта: 0 — Хозяин Чащи.")]
        public Transform[] Anchors = new Transform[RunBossKeys.Count];
        [Tooltip("Префабы в Resources по порядку боссов. Пустое имя — у босса пока нет трофея.")]
        public string[] Prefabs = { "Environment/Camp/Trophies/Trophy_Thicket", "", "" };

        readonly GameObject[] _shown = new GameObject[RunBossKeys.Count];
        TickDriver _driver;
        float _nextCheck;

        void Update()
        {
            // Победа приходит только из забега, поэтому опрос раз в полсекунды хватает с запасом.
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + .5f;
            if (_driver == null) _driver = FindAnyObjectByType<TickDriver>();
            Camp camp = _driver != null && _driver.Session != null ? _driver.Session.Camp : null;
            if (camp == null) return;
            for (int i = 0; i < RunBossKeys.Count; i++) Show(i, camp.BossDefeated(RunBossKeys.At(i)));
        }

        void Show(int index, bool defeated)
        {
            if (_shown[index] != null) { if (_shown[index].activeSelf != defeated) _shown[index].SetActive(defeated); return; }
            if (!defeated || Anchors == null || index >= Anchors.Length || Anchors[index] == null) return;
            string path = Prefabs != null && index < Prefabs.Length ? Prefabs[index] : null;
            if (string.IsNullOrEmpty(path)) return;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[camp-trophy] нет префаба " + path); Prefabs[index] = null; return; }
            _shown[index] = Instantiate(prefab, Anchors[index], false);
        }
    }
}
