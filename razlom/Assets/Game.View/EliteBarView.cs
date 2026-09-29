using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ПОЛОСА ЭЛИТЫ (план «Мобы леса v2», поток J). Заведена на этапе 0 одной строкой в ArenaView
    /// (EnsureOn), чтобы поток J не трогал ArenaView.
    ///
    /// Отдельной полосы у элиты НЕТ: владелец 29.09 (выбор G8) отверг все три концепта новой полосы
    /// («даже то что щас — лучше»). Полосу элиты рисует HealthBars в прежнем виде «Дыма и света»,
    /// крупнее — с цифрами «1240 / 2000» внутри (EliteBarLayout.Numbers) и маленьким знаком элиты
    /// (рогатый череп, Resources/UI/HUD/EliteBarMark*). Имя — табличка RunWorldView над полосой.
    /// Этот компонент остаётся пустым крючком: место для будущих эффектов полосы элиты (вспышка
    /// при смене фазы и т. п.), не требующих правки ArenaView.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class EliteBarView : MonoBehaviour
    {
        public static EliteBarView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<EliteBarView>();
            return view != null ? view : host.AddComponent<EliteBarView>();
        }
    }
}
