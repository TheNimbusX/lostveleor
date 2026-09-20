using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class PelagAnchorSlamView
    {
        private bool _wreck, _returning;
        private int _weaponSerial, _wreckVariant;
        private float _returnAge, _returnPayout;
        private Vector3 _returnPosition;
        private Quaternion _returnRotation;
        public void ReturnFlyingAnchor(Vector3 position, Quaternion rotation)
        {
            if (Active || _equipment==null || _ring==null) return;
            _forward=transform.forward;_right=transform.right;_impact=transform.position;
            _equipment.SetSlamOwnership(true);
            _equipment.SlamHead.SetPositionAndRotation(position,rotation);
            _ring.gameObject.SetActive(true);Active=true;_wreck=false;
            _previousPayout=Mathf.Min(7.5f,Vector3.Distance(_equipment.SlamBeltPosition,_ring.position)+.65f);
            _previousGrip=_equipment.SlamBeltPosition;_previousRing=_ring.position;
            _peakStrain=_peakSolveMilliseconds=0;_solveTotal=0;_solveFrames=0;
            Release();
        }

        public void BeginWreck()
        {
            var sim=_driver!=null?_driver.Sim:null;
            if(sim==null || _equipment==null || _equipment.SlamHead==null || _ring==null)return;
            Vector3 previous=_equipment.SlamHead.position;Quaternion rotation=_equipment.SlamHead.rotation;
            Release(true);
            _wreck=true;_weaponSerial=sim.PlayerAction.Serial;_wreckVariant=sim.WreckStage;
            _startTick=sim.PlayerAction.StartTick;_contactTick=sim.PlayerAction.ContactTick;_endTick=sim.PlayerAction.EndTick;
            _forward=new Vector3(sim.WreckDirection.X.ToFloat(),0,sim.WreckDirection.Y.ToFloat());
            _right=Vector3.Cross(Vector3.up,_forward);
            _impact=transform.position+_forward*2.1f;
            _startPosition=previous;_startRotation=rotation;
            _equipment.SetSlamOwnership(true);_equipment.SlamHead.SetPositionAndRotation(previous,rotation);
            _ring.gameObject.SetActive(true);Active=true;_contact=false;ClipTime=0;
            _previousGrip=_equipment.ChainGripPosition;_previousRing=_ring.position;_previousPayout=.95f;
            _peakStrain=_peakSolveMilliseconds=0;_solveTotal=0;_solveFrames=0;
        }
        private float WreckPayout(float t)
        {
            // Выдача задаётся постановкой, а не текущим расстоянием между руками и головой.
            if (_wreckVariant >= 2)
            {
                if (t < .20f) return Mathf.Lerp(.95f,.72f,Smooth(t/.20f));
                if (t < .38f) return Mathf.Lerp(.72f,.90f,Smooth((t-.20f)/.18f));
                if (t < .50f) return Mathf.Lerp(.90f,1.65f,Mathf.Pow((t-.38f)/.12f,1.4f));
                if (t < .58f) return Mathf.Lerp(1.65f,1.78f,Smooth((t-.50f)/.08f));
                if (t < .70f) return Mathf.Lerp(1.78f,1.15f,Smooth((t-.58f)/.12f));
                return Mathf.Lerp(1.15f,.75f,Smooth((t-.70f)/.15f));
            }
            if (t < .20f) return Mathf.Lerp(.95f,.90f,Smooth(t/.20f));
            if (t < .40f) return Mathf.Lerp(.90f,1.74f,(t-.20f)/.20f);
            if (t < .50f) return Mathf.Lerp(1.74f,1.85f,(t-.40f)/.10f);
            if (t < .61f) return Mathf.Lerp(1.85f,1.98f,Smooth((t-.50f)/.11f));
            if (t < .71f) return Mathf.Lerp(1.98f,1.62f,Smooth((t-.61f)/.10f));
            return Mathf.Lerp(1.62f,.75f,Smooth((t-.71f)/.14f));
        }
        private void PoseWreck(float t)
        {
            Vector3 root=transform.position;
            float side=_wreckVariant==1?-1f:1f;
            Vector3 contact=root+_forward*2.25f+Vector3.up*(_wreckVariant>=2?.28f:.68f);
            Vector3 p;
            if(t<.20f)
            {
                Vector3 ready=_wreckVariant>=2 ? root-_right*.35f+_forward*.5f+Vector3.up*2.15f
                    : root+(_forward*Mathf.Cos(-75*side*Mathf.Deg2Rad)+_right*Mathf.Sin(-75*side*Mathf.Deg2Rad))*1.05f+Vector3.up*1.1f;
                float u=Smooth(t/.20f);
                Vector3 front=root+_forward*.55f+Vector3.up*1.15f+_right*.35f;
                p=_wreckVariant>=2 ? Vector3.Lerp(Vector3.Lerp(_startPosition,front,u),Vector3.Lerp(front,ready,u),u)
                    : Vector3.Lerp(_startPosition,ready,u);
            }
            else if(t<.5f)
            {
                float u=(t-.20f)/.30f;
                if(_wreckVariant>=2)
                    p=Vector3.Lerp(root-_right*.35f+_forward*.5f+Vector3.up*2.15f,contact,u*u);
                else
                {
                    float angle=Mathf.Lerp(-75*side,0,u*u)*Mathf.Deg2Rad;
                    p=root+(_forward*Mathf.Cos(angle)+_right*Mathf.Sin(angle))*Mathf.Lerp(1.05f,2.25f,u)
                        +Vector3.up*Mathf.Lerp(1.1f,.68f,u);
                }
            }
            else
            {
                if (_wreckVariant < 2 && t < .61f)
                {
                    float u = Smooth((t-.5f)/.11f), angle = 52f*side*u*Mathf.Deg2Rad;
                    p = root + (_forward*Mathf.Cos(angle)+_right*Mathf.Sin(angle))*2.25f + Vector3.up*.68f;
                }
                else
                {
                    float u=Smooth((t-(_wreckVariant>=2?.56f:.61f))/(_wreckVariant>=2?.29f:.24f));
                    Vector3 from = _wreckVariant>=2 ? contact : root + (_forward*.616f+_right*side*.788f)*2.25f+Vector3.up*.68f;
                    p=Vector3.Lerp(from,_equipment.SlamBeltPosition,u)+Vector3.up*.16f*Mathf.Sin(u*Mathf.PI);
                }
            }
            Quaternion q=Quaternion.LookRotation((p-root+Vector3.forward*.001f).normalized)*Quaternion.Euler(30,0,side*15);
            if(t>.55f)q=Quaternion.Slerp(q,_equipment.SlamBeltRotation,Smooth((t-.55f)/.3f));
            float lowest = float.PositiveInfinity;
            foreach (var corner in _headBounds) lowest = Mathf.Min(lowest, (q * (corner * _equipment.SlamHead.lossyScale.x)).y);
            float floor = _layout != null ? _layout.WeaponGroundHeight(p.x,p.z) : root.y;
            p.y = Mathf.Max(p.y, floor - lowest + .01f);
            _equipment.SlamHead.SetPositionAndRotation(p,q);
        }
    }
}
