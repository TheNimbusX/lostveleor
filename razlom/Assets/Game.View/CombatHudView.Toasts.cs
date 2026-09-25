using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Отклик на находки (концепт 2Б, владелец 25 сентября): вещь или артефакт из тайника и с элиты,
    /// золото, выполненный заказ жителя — всплывашкой над портретом; «Разлом зачищен» — узким
    /// баннером сверху. Награда, выбранная на экране выбора, всплывашкой не повторяется.
    /// </summary>
    public sealed partial class CombatHudView
    {
        [Header("Всплывашки и объявление (концепт 2Б)")]
        public HudToasts Toasts;
        public HudAnnounce Announce;
        [Tooltip("Значки заказов алхимика по порядку AlchemistOrder: Живица, Порыв")] public Texture2D[] OrderIcons = new Texture2D[2];
        public Texture2D GoldIcon;

        RiftRun _toastRun;
        int _toastTaken, _toastGold;
        RunPhase _toastPhase;
        readonly AlchemistOrderStatus[] _orders = new AlchemistOrderStatus[2];
        bool _ordersKnown;

        void RefreshToasts(TickDriver driver)
        {
            if (Toasts == null) return;
            GameSession session = driver.Session;
            RiftRun run = session != null && session.Mode == GameMode.Rift ? driver.Run : null;
            // Новый забег (или тот же объект после сброса) — счёт с текущего места, без всплывашек за прошлое.
            if (run != _toastRun || run == null || run.TakenRewardCount < _toastTaken || run.Gold < _toastGold)
            {
                _toastRun = run;
                _toastTaken = run != null ? run.TakenRewardCount : 0;
                _toastGold = run != null ? run.Gold : 0;
                _toastPhase = run != null ? run.Phase : RunPhase.Idle;
            }
            else
            {
                while (_toastTaken < run.TakenRewardCount)
                {
                    RewardOffer offer = run.GetTaken(_toastTaken++);
                    if (_toastPhase != RunPhase.ChoosingReward) ToastReward(offer);
                }
                if (run.Gold > _toastGold)
                {
                    GameSound.Play("toast_gold", .6f, .03f, .3f);
                    Toasts.Push(GoldIcon, UiTheme.Role.Coins, null, null, UiTheme.Role.TextMuted, "gold", run.Gold - _toastGold, n => "+" + n + " золота");
                }
                _toastGold = run.Gold;
                _toastPhase = run.Phase;
            }
            RefreshOrders(session != null ? session.Camp : null);
        }

        void ToastReward(RewardOffer offer)
        {
            if (offer.Kind == RewardKind.Item)
            {
                WcRarity.Tier tier = WcRarity.FromItem((int)offer.Item.Rarity);
                GameSound.Play(tier >= WcRarity.Tier.Rare ? "toast_rare" : "toast_item", .7f, .03f, .15f);
                Toasts.Push(ItemTexts.Icon(offer.Item.BaseId), WcRarity.RoleFor(tier), ItemTexts.Name(offer.Item.BaseId),
                    WcRarity.Name(tier) + " · ур. " + offer.Item.ItemLevel, WcRarity.RoleFor(tier));
            }
            else if (offer.Kind == RewardKind.Artifact)
            {
                GameSound.Play("toast_rare", .75f, .02f, .15f);
                Toasts.Push(RunArtifactTexts.Icon(offer.Artifact), UiTheme.Role.Unique, RunArtifactTexts.Name(offer.Artifact),
                    "Артефакт забега", UiTheme.Role.Unique);
            }
        }

        /// <summary>Заказ жителя выполнен в забеге — «Заказ Лео выполнен · Живица».</summary>
        void RefreshOrders(Camp camp)
        {
            if (camp == null) { _ordersKnown = false; return; }
            for (int i = 0; i < _orders.Length; i++)
            {
                var order = (AlchemistOrder)i;
                AlchemistOrderStatus status = camp.AlchemyStatus(order);
                if (_ordersKnown && status == AlchemistOrderStatus.Ready && _orders[i] == AlchemistOrderStatus.Accepted)
                {
                    GameSound.Play("toast_rare", .7f, .02f, .3f);
                    Toasts.Push(i < OrderIcons.Length ? OrderIcons[i] : null, UiTheme.Role.Epic,
                        "Заказ " + CampServiceText.Get("npc.alchemist") + " выполнен",
                        order == AlchemistOrder.Resin ? "Живица" : "Лавидиевый порыв", UiTheme.Role.TextMuted);
                }
                _orders[i] = status;
            }
            _ordersKnown = true;
        }
    }
}
