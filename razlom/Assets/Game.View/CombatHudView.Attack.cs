using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Плитка ЛКМ (владелец 02.10, DESIGN «Пелаг — новая структура набора»: «ЛКМ показывается в HUD слотом,
    /// как навык»). Базовая атака — всегда серия саблей на ЛКМ; позже у неё появятся свои формы и таланты, и
    /// значок будет меняться по форме. Плитка — та же, что у способностей (клякса, круглая иконка, огненное
    /// кольцо, кейкап на нижней кромке), первой слева от способности 1 с тем же зазором; на кейкапе — мышь с
    /// левой кнопкой из набора, не буквы. Перезарядки, нехватки ресурса и огонька усилений у неё нет.
    ///
    /// Иконка лежит в префабе (CombatHudWcBuilder); здесь — только готовность: как у способностей, кольцо горит,
    /// пока герой может бить, и тлеет, а иконка гаснет, пока герой без сознания. В лагере вне полигона плитка
    /// уходит вместе со способностями (CombatHudView.Camp).
    /// </summary>
    public sealed partial class CombatHudView
    {
        [Header("ЛКМ — серия саблей (02.10)")]
        [Tooltip("Необязательно: панель плитки ЛКМ слева от способностей; в лагере вне полигона уходит вместе с ними")]
        public RectTransform AttackPanel;
        [Tooltip("Плитка ЛКМ: иконка серии, огненное кольцо, кейкап с мышью")]
        public HudSlotWidget Attack;

        void RefreshAttack(Simulation sim)
        {
            if (Attack == null) return;
            // Готовность — по тем же правилам, что у способностей (HudAbilityAvailability): у серии нет ни
            // перезарядки, ни цены, ни корней, остаётся одно «герой без сознания».
            bool ready = HudAbilityAvailability.Evaluate(sim.Entities.Alive[Simulation.PlayerId], false, 0, 0, 0, false).Ready;
            if (Attack.Art != null)
            {
                Color art = ready ? Color.white : ArtDimmed;
                if (Attack.Art.color != art) Attack.Art.color = art;
            }
            if (Attack.ReadyGem != null) Attack.ReadyGem.SetReady(ready);
        }
    }
}
