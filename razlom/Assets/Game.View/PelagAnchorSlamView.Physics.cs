using UnityEngine;
using NQuaternion = System.Numerics.Quaternion;

namespace Game.View
{
    public sealed partial class PelagAnchorSlamView
    {
        private readonly AnchorHeadDynamics _headBody = new AnchorHeadDynamics();
        private bool _physicalSlam, _headLaunched, _headPlanted;
        private float _headTime, _headCable = 1.05f;
        private Vector3 _headCenterLocal, _headEyeLocal, _headPreviousGrip, _headPreviousBelt;
        private AnchorHeadShape _headShape;
        private static NQuaternion NQ(Quaternion q) => new NQuaternion(q.x,q.y,q.z,q.w);
        private static Quaternion UQ(NQuaternion q) => new Quaternion(q.X,q.Y,q.Z,q.W);

        private void BeginHeadPhysics()
        {
            Vector3 scale = _equipment.SlamHead.lossyScale;
            _headCenterLocal = Vector3.zero;
            foreach(var p in _headBounds) _headCenterLocal += Vector3.Scale(p,scale);
            _headCenterLocal /= Mathf.Max(1,_headBounds.Length);
            _headEyeLocal = Vector3.Scale(_ring.localPosition,scale);
            if(_headShape==null)_headShape=Resources.Load<AnchorHeadShape>("Weapons/Pelag/AnchorChain/AnchorHeadShape");
            Vector3[] points=_headShape!=null?_headShape.Points:_headBounds;
            _headBody.Hull = new System.Numerics.Vector3[points.Length];
            for(int i=0;i<points.Length;i++) _headBody.Hull[i]=N(Vector3.Scale(points[i],scale)-_headCenterLocal);
            _headBody.EyeLocal = N(_headEyeLocal-_headCenterLocal);
            _headBody.Reset(N(_startPosition+_startRotation*_headCenterLocal),NQ(_startRotation));
            _physicalSlam=true;_headLaunched=_headPlanted=false;_headTime=0;_headCable=1.05f;
            _headPreviousGrip=_equipment.ChainGripPosition;_headPreviousBelt=_equipment.SlamBeltPosition;
        }

        private void LaunchHead()
        {
            _headLaunched=true;
            Quaternion wanted = Quaternion.LookRotation(_forward)*Quaternion.Euler(-20,0,-15);
            float lowest=0,furthest=0;
            foreach(var p in _headBody.Hull)
            {
                Vector3 offset=wanted*U(p);
                lowest=Mathf.Min(lowest,offset.y);furthest=Mathf.Max(furthest,Vector3.Dot(offset,_forward));
            }
            Vector3 destination=_impact-_forward*furthest-Vector3.up*(lowest-.012f);
            const float flight=.18f;
            // Однократный импульс броска. Далее нет позиционной траектории или притягивания к цели.
            _headBody.Velocity=N((destination-U(_headBody.Position))/flight-Vector3.down*(30f*.5f*flight));
            Quaternion delta=wanted*Quaternion.Inverse(UQ(_headBody.Rotation));
            delta.ToAngleAxis(out float angle,out Vector3 axis);
            if(angle>180)angle-=360;
            _headBody.AngularVelocity=N(axis*(angle*Mathf.Deg2Rad/flight));
        }

        private void PosePhysicalHead(float time)
        {
            Vector3 grip=_equipment.ChainGripPosition, belt=_equipment.SlamBeltPosition;
            float span=Mathf.Max(.0001f,time-_headTime), begin=_headTime;
            Vector3 gripVelocity=(grip-_headPreviousGrip)/span;
            while(_headTime<time-.000001f)
            {
                float dt=Mathf.Min(1f/240f,time-_headTime);
                if(_headTime<.32f-.00001f)dt=Mathf.Min(dt,.32f-_headTime);
                if(_headTime<.50f-.00001f && _headTime+dt>.50f)dt=.50f-_headTime;
                if(dt<.000001f){_headTime=time;break;}
                float next=_headTime+dt, blend=(next-begin)/span;
                Vector3 atGrip=Vector3.Lerp(_headPreviousGrip,grip,blend);
                Vector3 atBelt=Vector3.Lerp(_headPreviousBelt,belt,blend);
                bool caught=_headTime>.74f && Vector3.Distance(U(_headBody.Eye),atBelt)<.8f;
                if(!_headLaunched && _headTime>=.32f-.00001f)LaunchHead();
                if(!_headLaunched)
                {
                    // Рука разгоняет массу за кольцо; голова отстаёт и разворачивается под тягой.
                    _headBody.PullEye(N(atGrip-_forward*.08f),N(gripVelocity),25f,dt);
                    _headBody.TurnTowards(NQ(Quaternion.LookRotation(_forward)*Quaternion.Euler(105,0,-20)),7f,dt);
                }
                else if(_headTime>=.56f)
                {
                    float catchBlend=Smooth((_headTime-.70f)/.17f);
                    Vector3 target=Vector3.Lerp(atGrip,atBelt+_equipment.SlamBeltRotation*_headEyeLocal,catchBlend);
                    if(caught)
                        _headBody.Catch(N(atBelt+_equipment.SlamBeltRotation*_headCenterLocal),N((belt-_headPreviousBelt)/span),NQ(_equipment.SlamBeltRotation),dt);
                    else
                    {
                        _headBody.PullEye(N(target),System.Numerics.Vector3.Zero,32f,dt);
                        _headBody.TurnTowards(NQ(_equipment.SlamBeltRotation),18f,dt);
                    }
                }
                float cable=PhysicalPayout(next);
                _headBody.Advance(dt,N(Vector3.down*(_headLaunched?30f:9.81f)));
                _headBody.ConstrainCable(N(atGrip),N(gripVelocity),cable-.06f,(cable-_headCable)/dt,dt);
                if(!caught)_headBody.CollideGround(SampleFloor,dt);
                if(_headLaunched && !_headPlanted && _headBody.Grounded)
                {
                    // Лапы врезаются в мягкий грунт: неупругий удар гасит скольжение, без фиксации позиции.
                    _headPlanted=true;
                    _headBody.Impulse(-_headBody.Velocity*(_headBody.Mass*.90f),_headBody.Position);
                    _headBody.AngularVelocity*=.18f;
                    if(CaptureRig.LiveSkill)Debug.Log($"[anchor-physical-contact] phase={next:F5} velocity={_headBody.Velocity.Length():F3} position={U(_headBody.Position)}");
                }
                if(!caught)_headBody.CollideBody(N(transform.position+Vector3.up*.65f),N(transform.position+Vector3.up*1.35f),.38f);
                _headCable=cable;_headTime=next;
            }
            _headPreviousGrip=grip;_headPreviousBelt=belt;
            ApplyHeadBody();
        }

        private float PhysicalPayout(float time)
        {
            if(time<.32f)return Mathf.Lerp(1.05f,.55f,Smooth(time/.20f));
            if(time<.50f)return Mathf.Lerp(.55f,4.20f,(time-.32f)/.18f);
            if(time<.56f)return Mathf.Lerp(4.20f,4.35f,Smooth((time-.50f)/.06f));
            return Mathf.Lerp(4.35f,.78f,Smooth((time-.56f)/.29f));
        }

        private bool ReturnPhysicalHead()
        {
            float remaining=Mathf.Min(Time.deltaTime,.05f),duration=Mathf.Max(.0001f,remaining);
            Vector3 belt=_equipment.SlamBeltPosition;
            Vector3 beltVelocity=(belt-_headPreviousBelt)/Mathf.Max(.001f,Time.deltaTime);
            float cable=Mathf.Lerp(_returnPayout,.75f,Smooth(_returnAge/.5f));
            while(remaining>.000001f)
            {
                float dt=Mathf.Min(1f/240f,remaining);remaining-=dt;
                float blend=1-remaining/duration;
                Vector3 atBelt=Vector3.Lerp(_headPreviousBelt,belt,blend);
                Vector3 eye=atBelt+_equipment.SlamBeltRotation*_headEyeLocal;
                bool caught=Vector3.Distance(U(_headBody.Eye),eye)<.8f;
                if(caught)
                    _headBody.Catch(N(atBelt+_equipment.SlamBeltRotation*_headCenterLocal),N(beltVelocity),NQ(_equipment.SlamBeltRotation),dt);
                else
                {
                    _headBody.PullEye(N(eye),N(beltVelocity),38f,dt);
                    _headBody.TurnTowards(NQ(_equipment.SlamBeltRotation),24f,dt);
                }
                _headBody.Advance(dt,System.Numerics.Vector3.Zero);
                _headBody.ConstrainCable(N(atBelt),N(beltVelocity),Mathf.Lerp(_previousPayout,cable,blend)-.06f,
                    (cable-_previousPayout)/duration,dt);
                // После захвата управляет крепление: контакт свободного тела не должен мешать подвесу.
                if(!caught)_headBody.CollideGround(SampleFloor,dt);
            }
            ApplyHeadBody();
            _headPreviousBelt=_equipment.SlamBeltPosition;
            return _returnAge>.08f && Vector3.Distance(_equipment.SlamHead.position,_equipment.SlamBeltPosition)<.025f
                && Quaternion.Angle(_equipment.SlamHead.rotation,_equipment.SlamBeltRotation)<4f;
        }

        private void ApplyHeadBody()
        {
            Quaternion rotation=UQ(_headBody.Rotation);
            _equipment.SlamHead.SetPositionAndRotation(U(_headBody.Position)-rotation*_headCenterLocal,rotation);
        }
    }
}
