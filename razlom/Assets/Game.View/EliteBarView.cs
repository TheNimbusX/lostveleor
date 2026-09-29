using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ПОЛОСА ЭЛИТЫ (план «Мобы леса v2», поток J). Заведена на этапе 0 одной строкой в ArenaView
    /// (EnsureOn), чтобы поток J не трогал ArenaView.
    ///
    /// Отдельной полосы у элиты НЕТ: её рисует HealthBars тем же мазком «Дыма и света», что и у
    /// обычных, — длиннее, с цифрами «1240 / 2000» внутри (EliteBarLayout.Numbers) и, по выбору
    /// владельца 29.09 «5 — Рога», с костяными рогами на обоих концах (Resources/UI/HUD/EliteBarAntler,
    /// tools/ui-kit/make-elite-bar-mark.py) и тёмно-алой заливкой. Рогатый череп у левого края
    /// владелец отверг — убран. Имя — табличка RunWorldView над полосой, между рогами.
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
