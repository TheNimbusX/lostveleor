using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Отклик на находки (концепт 2Б, владелец 25 сентября): вещь или артефакт из тайника и с элиты,
    /// золото — всплывашкой над портретом; «Разлом зачищен» — узким баннером сверху. Награда,
    /// выбранная на экране выбора, всплывашкой не повторяется. Заказы Лео сняты 06.10.
    /// </summary>
    public sealed partial class CombatHudView
    {
        [Header("Всплывашки и объявление (концепт 2Б)")]
        public HudToasts Toasts;
        public HudAnnounce Announce;
        // Заказов алхимика больше нет (06.10); поле остаётся, чтобы префаб не терял ссылки.
        [Tooltip("Значки бывших заказов алхимика (сняты 06.10): Живица, Порыв")] public Texture2D[] OrderIcons = new Texture2D[2];
        public Texture2D GoldIcon;

        RiftRun _toastRun;
        int _toastTaken, _toastGold;
        RunPhase _toastPhase;

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
                    // Значок золота — белый глиф набора забега: кремовый, как текст.
                    Toasts.Push(GoldIcon, UiTheme.Role.Coins, null, null, UiTheme.Role.TextMuted, "gold", run.Gold - _toastGold, n => "+" + n + " золота",
                        glyph: true);
                }
                _toastGold = run.Gold;
                _toastPhase = run.Phase;
            }
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
    }
}
