using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v4: бёдра в местах привязки клипов Pelag_AN_Wreck4_* (06.10, правка «мах 2 скручивает корпус»).
    /// Клипы v4 пишут только повороты костей и место таза, состояния Wreck4_* — без Write Defaults, поэтому места костей
    /// остаются от прошлого клипа. Стойка сабли, KnifeIdle, повороты на месте и Blaze (скелет v5) ставят LeftUpLeg
    /// (−0,043; −0,035; 0,002) и RightUpLeg (0,043; −0,035; 0,021): правое бедро на 2 см впереди левого. Клипы v4 собраны
    /// на модели v6 — (∓0,052; −0,028; −0,008 / −0,012). Таз (ось Hips) шёл ровно по клипу, но линия бёдер — на −15°, ноги
    /// крепились к тазу со сдвигом: скрутка груди и бёдер в махе 2 доходила до 41° вместо 27° клипа (съёмка 06.10,
    /// artifacts/wreck/v4/bugs/tools/twist.py, cliptwist.py). Пока лента v4 ведёт тело, у бёдер места по осям X и Z — из
    /// привязки (Awake: экземпляр префаба до первого шага аниматора), высота Y — прежняя (опора стоп, подобранная под игру,
    /// не меняется). В конце серии бёдрам возвращаются прежние места: вне Крушения ничего не меняется.
    /// </summary>
    public sealed partial class CharacterAnimatorView
    {
        private Transform _wreck4LeftUpLeg, _wreck4RightUpLeg;
        private Vector3 _wreck4LeftBind, _wreck4RightBind, _wreck4LeftSaved, _wreck4RightSaved;
        private bool _wreck4BindHeld;

        private void Awake()
        {
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "mixamorig:LeftUpLeg") { _wreck4LeftUpLeg = bone; _wreck4LeftBind = bone.localPosition; }
                else if (bone.name == "mixamorig:RightUpLeg") { _wreck4RightUpLeg = bone; _wreck4RightBind = bone.localPosition; }
            }
        }

        /// <summary>
        /// Кадр ленты v4 (Update, до шага аниматора): места бёдер по X и Z — из привязки. Аниматор их в Wreck4_* не пишет
        /// (без Write Defaults), так что они держатся до конца кадра.
        /// </summary>
        private void HoldWreck4Bind()
        {
            if (_wreck4LeftUpLeg == null || _wreck4RightUpLeg == null) return;
            if (!_wreck4BindHeld)
            {
                _wreck4BindHeld = true;
                _wreck4LeftSaved = _wreck4LeftUpLeg.localPosition;
                _wreck4RightSaved = _wreck4RightUpLeg.localPosition;
            }
            Vector3 left = _wreck4LeftUpLeg.localPosition, right = _wreck4RightUpLeg.localPosition;
            _wreck4LeftUpLeg.localPosition = new Vector3(_wreck4LeftBind.x, left.y, _wreck4LeftBind.z);
            _wreck4RightUpLeg.localPosition = new Vector3(_wreck4RightBind.x, right.y, _wreck4RightBind.z);
        }

        /// <summary>Серия кончилась или сорвана: бёдрам — места, что были до неё.</summary>
        private void ReleaseWreck4Bind()
        {
            if (!_wreck4BindHeld) return;
            _wreck4BindHeld = false;
            if (_wreck4LeftUpLeg != null) _wreck4LeftUpLeg.localPosition = _wreck4LeftSaved;
            if (_wreck4RightUpLeg != null) _wreck4RightUpLeg.localPosition = _wreck4RightSaved;
        }
    }
}
