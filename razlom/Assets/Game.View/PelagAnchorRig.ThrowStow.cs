using UnityEngine;

namespace Game.View
{
    // Тело Броска якоря → риг якоря на цепи (artifacts/anchor-core). Кладётся ТОЛЬКО вместе с ригом (apply.py вида
    // анимации Броска: если в проекте есть PelagAnchorRig.Throw.cs).
    //
    // Голову на линии (Thrown → дёрг → Yank → Caught) ведёт ОДИН кормилец — мост VFX
    // (PelagVfxController.AnchorThrowRig: BeginLine/DriveLine, Гарпун «в теле цели», срыв, переключатель F8 «якорь на
    // риге»). Свою кормушку снимком (ReadThrowSnapshot) риг здесь НЕ получает: она шла бы после моста в том же кадре и
    // перебивала бы его (Гарпун, срыв, выключенный риг — две головы). Отсюда — только кадр «якорь на спину» клипа ловли.
    public sealed partial class PelagAnchorRig
    {
        /// <summary>
        /// Кадр «якорь на спину» клипа ловли (Catch 3, timing.json keys.stow_handoff) обрывает маятник ловли и сразу
        /// начинает уборку (рукоять на спину скольжением, цепь сматывается). false — риг сам: Caught 0,3 с, потом Stow.
        /// </summary>
        public static bool ThrowStowAtClipHandoff = true;

        /// <summary>
        /// Вид тела дошёл до кадра «якорь на спину» клипа ловли (CharacterAnimatorView.AnchorThrow): живой маятник ловли
        /// (Caught) кончается, уборка начинается сразу со скольжения рукояти — кисть клипа до крепления не тянется
        /// (timing.json keys.stow_handoff, компромисс DESIGN §4.3), ждать её 0,35 с незачем. Только для своего броска:
        /// риг держит голову, ловит её (не Абордаж) и уже отработал ловлю этого каста.
        /// </summary>
        public void CueThrowStow(int serial)
        {
            if (!ThrowStowAtClipHandoff || !_owned || !_catching || _catchFromAbordage || _lineDoneSerial != serial) return;
            _catching = false;
            _stow = StowStep.Hand;
            _stowAge = StowTimeout;
            if (CaptureRig.LiveSkill) Debug.Log($"{Log} throw stow cue serial={serial} caughtAge={_caughtAge:F2}");
        }
    }
}
