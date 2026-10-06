using System.Collections.Generic;
using Game.Sim;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Ненавязчивые значки текущего дела: готовая часть главы или новый, ещё не увиденный ранг лагеря.
    /// В песочнице без прогрессии остаётся указатель свежего товара Вена.
    /// При наведении короткая подпись поясняет дело; значок скрывается рядом с NPC или окном.
    /// Добавляется CampPlayerView; представление новых прибытий ведёт CampArrivalPresentation.
    /// </summary>
    public sealed class CampGuideView : MonoBehaviour
    {
        enum Icon { Smith = 0, Trader = 1, Alchemist = 2 }

        [Tooltip("Ближе этого значок над целью прячется, метры")] public float HideNear = 3.5f;
        [Tooltip("Высота значка над макушкой, метры")] public float Lift = .7f;
        [Tooltip("Размах покачивания, пикселей Canvas")] public float Bob = 6f;
        public float Fade = .25f;

        CampGuidePanel _panel;
        TickDriver _driver;
        float _checkAt;
        readonly string[] _tasks = new string[3];
        Camp _checkedCamp;
        readonly List<RectTransform> _markers = new List<RectTransform>();
        readonly List<TMP_Text> _labels = new List<TMP_Text>();
        readonly List<float> _markerAlpha = new List<float>();
        readonly List<(Vector3 at, Icon icon, string note)> _targets = new List<(Vector3, Icon, string)>();
        CampServiceNpc[] _npcs;

        void Start()
        {
            _driver = GetComponent<TickDriver>() ?? FindAnyObjectByType<TickDriver>();
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampGuideWc");
            if (prefab == null) { enabled = false; return; }
            _panel = Instantiate(prefab).GetComponent<CampGuidePanel>();
            if (_panel != null) UiScaleFollower.Attach(_panel.gameObject);
            _panel.name = "Дела в лагере";
            if (_panel.MarkerTemplate != null) _panel.MarkerTemplate.gameObject.SetActive(false);
        }

        void OnDestroy() { if (_panel != null) Destroy(_panel.gameObject); }

        void LateUpdate()
        {
            if (_panel == null || _driver?.Session == null) return;
            var player = CampPlayerView.Instance;
            bool active = player != null && player.Active;
            bool busy = !active || player.InputBlocked || _driver.GameplayPaused || MainMenuView.IsOpen;
            _targets.Clear();
            if (active)
            {
                if (_npcs == null) _npcs = FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include);
                WorkTargets();
            }
            UpdateMarkers(player, busy);
        }

        void WorkTargets()
        {
            var camp = _driver.Session.Camp;
            if (Time.unscaledTime >= _checkAt || !ReferenceEquals(camp, _checkedCamp))
            {
                _checkAt = Time.unscaledTime + 1f;
                _checkedCamp = camp;
                for (int i = 0; i < _tasks.Length; i++) _tasks[i] = TaskHint(camp, (CampResident)i);
            }
            foreach (var npc in _npcs)
            {
                if (npc == null || !npc.isActiveAndEnabled) continue;
                int index = npc.Kind == CampServiceKind.Smith ? 0 : npc.Kind == CampServiceKind.Trader ? 1
                    : npc.Kind == CampServiceKind.Alchemist ? 2 : -1;
                if (index < 0 || !camp.HasResident((CampResident)index) || string.IsNullOrEmpty(_tasks[index])) continue;
                _targets.Add((Head(npc), (Icon)index, _tasks[index]));
            }
        }

        /// <summary>Настоящее доступное дело, без проверки старой перековки за золото и осколки.</summary>
        public static string TaskHint(Camp camp, CampResident resident)
        {
            if (camp == null || !camp.HasResident(resident)) return null;
            if (camp.ChapterStatus(resident) == CampChapterStatus.Ready)
                return resident == CampResident.Smith ? "Завершить первую часть главы «Наладить жизнь»"
                    : resident == CampResident.Trader ? "Завершить вторую часть главы «Наладить жизнь»"
                    : "Завершить главу «Наладить жизнь»";
            // Ранг открывается сам (босс + уровень); значок держится, пока игрок не заглянет
            // в «Развитие лагеря» — там флаг и снимается (CampServicesView.Progression).
            if ((camp.PendingUnlocks & CampServicesView.RankUnlocks) != 0) return "Новый ранг · загляни";
            if (!camp.UsesCampProgression && resident == CampResident.Trader && camp.TraderBossStock) return "В лавке появился свежий товар";
            return null;
        }

        Vector3 Head(CampServiceNpc npc)
        {
            float top = npc.transform.position.y + 1.8f;
            var animator = npc.GetComponentInChildren<Animator>();
            var renderer = animator != null ? animator.GetComponentInChildren<Renderer>() : null;
            if (renderer != null) top = renderer.bounds.max.y;
            Vector3 at = renderer != null ? renderer.bounds.center : npc.transform.position;
            return new Vector3(at.x, top + Lift, at.z);
        }

        void UpdateMarkers(CampPlayerView player, bool busy)
        {
            var camera = Camera.main;
            while (_markers.Count < _targets.Count && _panel.MarkerTemplate != null)
            {
                var marker = Instantiate(_panel.MarkerTemplate, _panel.MarkerTemplate.parent);
                marker.name = "Значок дела " + (_markers.Count + 1);
                _markers.Add(marker);
                _labels.Add(null);
                _markerAlpha.Add(0f);
            }
            for (int i = 0; i < _markers.Count; i++)
            {
                var marker = _markers[i];
                bool show = i < _targets.Count && !busy && camera != null;
                Vector3 screen = Vector3.zero;
                if (show)
                {
                    var (at, icon, note) = _targets[i];
                    Vector3 flat = at - player.Position; flat.y = 0f;
                    show = flat.sqrMagnitude > HideNear * HideNear;
                    screen = camera.WorldToScreenPoint(at);
                    show &= screen.z > 0f;
                    var art = marker.GetComponentInChildren<UnityEngine.UI.RawImage>();
                    int index = (int)icon;
                    if (art != null && index < _panel.Icons.Length && art.texture != _panel.Icons[index]) art.texture = _panel.Icons[index];
                    EnsureLabel(i);
                    if (_labels[i] != null) _labels[i].text = note;
                }
                _markerAlpha[i] = Mathf.MoveTowards(_markerAlpha[i], show ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.05f, Fade));
                bool visible = _markerAlpha[i] > 0f;
                if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
                if (!visible) continue;
                var group = marker.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = _markerAlpha[i];
                if (_labels[i] != null) _labels[i].gameObject.SetActive(show && Hovering(marker));
                if (show)
                {
                    float scale = marker.lossyScale.y > 0f ? marker.lossyScale.y : 1f;
                    float bob = Mathf.Sin(Time.unscaledTime * 2.2f + i * 1.3f) * Bob * scale;
                    marker.position = new Vector3(screen.x, screen.y + bob, 0f);
                }
            }
        }

        void EnsureLabel(int index)
        {
            if (_labels[index] != null) return;
            var shop = FindAnyObjectByType<CampShopView>(FindObjectsInactive.Include);
            if (shop == null || shop.HintNote == null) return;
            // Берём шрифт, материал и цвет из действующей подсказки лагеря.
            TMP_Text label = Instantiate(shop.HintNote, _markers[index]);
            label.name = "Пояснение дела"; label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 16;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, -8);
            rect.sizeDelta = new Vector2(320, 44); rect.localScale = Vector3.one;
            label.gameObject.SetActive(false); _labels[index] = label;
        }

        static bool Hovering(RectTransform marker)
        {
            if (TickDriver.GamepadLastUsed) return false;
#if ENABLE_INPUT_SYSTEM
            Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1, -1);
#else
            Vector2 pointer = Input.mousePosition;
#endif
            return RectTransformUtility.RectangleContainsScreenPoint(marker, pointer, null);
        }
    }
}
