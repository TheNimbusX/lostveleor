using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Кисть держит остриё вдоль полосы выпада; перемещением владеет Sim.</summary>
    [DefaultExecutionOrder(1018)]
    public sealed class PelagMobilityPoseView : MonoBehaviour
    {
        private TickDriver _driver;
        private Transform _hand, _bladeRoot, _bladeTip;
        private void Start()
        {
            _driver=FindAnyObjectByType<TickDriver>();
            foreach(var t in GetComponentsInChildren<Transform>(true))
            {
                if(t.name=="mixamorig:RightHand")_hand=t;
                if(t.name=="BladeRoot")_bladeRoot=t;
                if(t.name=="BladeTip")_bladeTip=t;
            }
        }
        private void LateUpdate()
        {
            var sim=_driver!=null?_driver.Sim:null;
            if(sim==null||_hand==null||_bladeRoot==null||_bladeTip==null)return;
            var action=sim.PlayerAction;
            if(action.DefinitionId!=AbilityDefinition.SkewerId||action.Interrupted||!action.ActiveAt(sim.Tick-1))return;
            float tick=sim.Tick-1+_driver.Alpha;
            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(action.StartTick,action.StartTick+1,tick));
            Vector3 direction=new Vector3(sim.Entities.Facing[0].X.ToFloat(),-.035f,sim.Entities.Facing[0].Y.ToFloat()).normalized;
            var rotation=Quaternion.FromToRotation(_bladeTip.position-_bladeRoot.position,direction)*_hand.rotation;
            _hand.rotation=Quaternion.Slerp(_hand.rotation,rotation,weight);
        }
    }
}
