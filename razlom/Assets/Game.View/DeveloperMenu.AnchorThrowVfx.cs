#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Бросок якоря: якорь на риге» в F8 (вкладка «Пелаг», секция «Формы», order 71 — сразу под выбором формы
    /// Броска, 70). Включено — голову и цепь ведёт система якоря на цепи (PelagAnchorRig), если она стоит в
    /// проекте; выключено (или рига нет) — временный путь Абордажа v2 (спека §5.1). Своим файлом: оболочка F8
    /// и пункты других навыков не трогаются.
    /// </summary>
    internal static class AnchorThrowVfxDevEntries
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            DevMenu.Toggle("pelag.forms.anchor_throw_rig", DevTab.Pelag, "Формы", "Бросок якоря: якорь на риге",
                c => AnchorThrowVfxSwitches.UseRig,
                (c, on) => AnchorThrowVfxSwitches.UseRig = on,
                hint: "Вкл — голову и цепь ведёт якорь на цепи (риг anchor-core), если он стоит в проекте; " +
                      "выкл или рига нет — временный путь Абордажа. Действует со следующего броска.",
                order: 71);
        }
    }
}
#endif
