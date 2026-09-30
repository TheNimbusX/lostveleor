using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Авторские дополнения рабочего места по рангу 0..3, без создания моделей.
    /// Ссылки задаются на отдельные дочерние группы инструментов или сменные варианты.
    /// Пропущенный ранг сохраняет предыдущую доступную группу.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Вид рабочего места по рангу")]
    public sealed class CampWorkstationStage : MonoBehaviour
    {
        public CampResident Resident;
        [Tooltip("Необязательные дочерние группы. Не назначать сам корень рабочего места или NPC.")]
        public GameObject Rank0, Rank1, Rank2, Rank3;
        [Tooltip("Каждый ранг добавляет группу к предыдущим. Выключить для сменных вариантов.")]
        public bool Cumulative = true;
        readonly Dictionary<GameObject, bool> _original = new Dictionary<GameObject, bool>();
        TickDriver _driver;

        /// <summary>Добавляет только связующий компонент; новые меши и объекты не создаёт.</summary>
        public static CampWorkstationStage RuntimeInstall(CampResident resident, Transform context)
        {
            if (!Application.isPlaying || context == null) return null;
            var stage = context.GetComponent<CampWorkstationStage>() ?? context.gameObject.AddComponent<CampWorkstationStage>();
            stage.Resident = resident;
            return stage;
        }

        /// <summary>Точные авторские контексты; запасной путь — только соответствующий CampServiceNpc.</summary>
        public static void InstallCurrentContexts(Transform campRoot)
        {
            if (!Application.isPlaying || campRoot == null) return;
            InstallContext(campRoot, "Smith Shelter", CampResident.Smith, CampServiceKind.Smith);
            InstallContext(campRoot, "Tent - Trader", CampResident.Trader, CampServiceKind.Trader);
            InstallContext(campRoot, "Alchemist Shelter", CampResident.Alchemist, CampServiceKind.Alchemist);
        }

        static void InstallContext(Transform campRoot, string name, CampResident resident, CampServiceKind kind)
        {
            foreach (var context in campRoot.GetComponentsInChildren<Transform>(true))
                if (context.name == name) { RuntimeInstall(resident, context); return; }
            foreach (var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
                if (npc.Kind == kind) { RuntimeInstall(resident, npc.transform); return; }
        }

        void Update() => Refresh();

        /// <summary>Немедленно применяет текущий ранг к уже назначенным авторским группам.</summary>
        public void Refresh()
        {
            if (_driver == null) _driver = FindAnyObjectByType<TickDriver>();
            Camp camp = _driver != null && _driver.Session != null ? _driver.Session.Camp : null;
            if (camp == null) return;
            bool available = camp.HasResident(Resident);
            int rank = Mathf.Clamp(camp.Rank(Resident), 0, 3);
            int chosen = -1;
            if (!Cumulative)
                for (int i = rank; i >= 0; i--) if (Valid(Group(i))) { chosen = i; break; }
            for (int i = 0; i < 4; i++)
            {
                var group = Group(i);
                if (!Valid(group)) continue;
                if (!_original.ContainsKey(group)) _original.Add(group, group.activeSelf);
                bool active = available && (Cumulative ? i <= rank : i == chosen);
                if (group.activeSelf != active) group.SetActive(active);
            }
        }

        GameObject Group(int rank) => rank == 0 ? Rank0 : rank == 1 ? Rank1 : rank == 2 ? Rank2 : Rank3;
        bool Valid(GameObject group) => group != null && group != gameObject && group.transform.IsChildOf(transform);

        void OnDisable()
        {
            foreach (var pair in _original)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);
            _original.Clear();
        }
    }
}
