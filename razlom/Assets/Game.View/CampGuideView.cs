using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Значки «здесь есть дело» (владелец 24 сентября: «нужен ненавязчивый указатель: у Лео есть
    /// заказ, у кузнеца можно перековать»). Значок висит над жителем, только пока у него есть дело:
    /// у Лео новый или готовый к сдаче заказ, Эни может что-то перековать на имеющиеся деньги,
    /// у Вена свежий товар после босса. Прячется, когда герой рядом или открыто окно.
    /// Знакомства с лагерем нет — владелец его отверг. Добавляется CampPlayerView сам.
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
        bool _smithWork, _alchemistWork, _traderWork;
        readonly List<RectTransform> _markers = new List<RectTransform>();
        readonly List<float> _markerAlpha = new List<float>();
        readonly List<(Vector3 at, Icon icon)> _targets = new List<(Vector3, Icon)>();
        CampServiceNpc[] _npcs;

        void Start()
        {
            _driver = GetComponent<TickDriver>() ?? FindAnyObjectByType<TickDriver>();
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampGuideWc");
            if (prefab == null) { enabled = false; return; }
            _panel = Instantiate(prefab).GetComponent<CampGuidePanel>();
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
                if (_npcs == null) _npcs = FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Exclude);
                WorkTargets();
            }
            UpdateMarkers(player, busy);
        }

        void WorkTargets()
        {
            if (Time.unscaledTime >= _checkAt)
            {
                _checkAt = Time.unscaledTime + 1f;
                var camp = _driver.Session.Camp;
                _alchemistWork = camp.Has(CampService.Alchemist) && (Pending(camp, AlchemistOrder.Resin) || Pending(camp, AlchemistOrder.Surge));
                _traderWork = camp.Has(CampService.Trader) && camp.TraderBossStock;
                _smithWork = camp.Has(CampService.Smith) && CanReforge(camp);
            }
            foreach (var npc in _npcs)
            {
                if (npc == null) continue;
                if (npc.Kind == CampServiceKind.Smith && _smithWork) _targets.Add((Head(npc), Icon.Smith));
                else if (npc.Kind == CampServiceKind.Trader && _traderWork) _targets.Add((Head(npc), Icon.Trader));
                else if (npc.Kind == CampServiceKind.Alchemist && _alchemistWork) _targets.Add((Head(npc), Icon.Alchemist));
            }
        }

        static bool Pending(Camp camp, AlchemistOrder order)
        {
            var status = camp.AlchemyStatus(order);
            return status == AlchemistOrderStatus.Available || status == AlchemistOrderStatus.Ready;
        }

        /// <summary>Есть ли вещь (надетая или в сумке), которую Эни перекуёт на имеющиеся деньги.</summary>
        static bool CanReforge(Camp camp)
        {
            int gold = camp.Money(CurrencyType.Gold), shards = camp.Money(CurrencyType.Shards);
            for (int s = 0; s < (int)EquipSlot.Count; s++)
            {
                var item = camp.Worn.Worn((EquipSlot)s);
                if (!item.IsEmpty && camp.ReforgeRange((EquipSlot)s, 0, out _, out _) == SmithResult.Success
                    && gold >= Camp.ReforgeGold(item) && shards >= Camp.ReforgeShards(item)) return true;
            }
            for (int i = 0; i < camp.Bag.Capacity; i++)
            {
                if (camp.Bag.IsEmpty(i)) continue;
                var item = camp.Bag.At(i);
                if (camp.ReforgeRange(i, 0, out _, out _) == SmithResult.Success
                    && gold >= Camp.ReforgeGold(item) && shards >= Camp.ReforgeShards(item)) return true;
            }
            return false;
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
                _markerAlpha.Add(0f);
            }
            for (int i = 0; i < _markers.Count; i++)
            {
                var marker = _markers[i];
                bool show = i < _targets.Count && !busy && camera != null;
                Vector3 screen = Vector3.zero;
                if (show)
                {
                    var (at, icon) = _targets[i];
                    Vector3 flat = at - player.Position; flat.y = 0f;
                    show = flat.sqrMagnitude > HideNear * HideNear;
                    screen = camera.WorldToScreenPoint(at);
                    show &= screen.z > 0f;
                    var art = marker.GetComponentInChildren<UnityEngine.UI.RawImage>();
                    int index = (int)icon;
                    if (art != null && index < _panel.Icons.Length && art.texture != _panel.Icons[index]) art.texture = _panel.Icons[index];
                }
                _markerAlpha[i] = Mathf.MoveTowards(_markerAlpha[i], show ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.05f, Fade));
                bool visible = _markerAlpha[i] > 0f;
                if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
                if (!visible) continue;
                var group = marker.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = _markerAlpha[i];
                if (show)
                {
                    float scale = marker.lossyScale.y > 0f ? marker.lossyScale.y : 1f;
                    float bob = Mathf.Sin(Time.unscaledTime * 2.2f + i * 1.3f) * Bob * scale;
                    marker.position = new Vector3(screen.x, screen.y + bob, 0f);
                }
            }
        }
    }
}
