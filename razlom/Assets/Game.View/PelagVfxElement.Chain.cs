using UnityEngine;
using N = System.Numerics.Vector3;

namespace Game.View
{
    public sealed partial class PelagVfxElement
    {
        private readonly AnchorChainSolver _paidChain = new AnchorChainSolver(.135f);
        private float _paidAccumulator, _lastPayout;
        private Vector3 _lastGrip, _lastRing;
        private LayoutView _chainLayout;
        private void PreparePaidChain()
        {
            if(!DynamicLine)return;
            _chainLayout=FindAnyObjectByType<LayoutView>();
            _paidChain.GroundHeight=ChainGround;
        }
        private float ChainGround(N point)=>_chainLayout!=null?_chainLayout.WeaponGroundHeight(point.X,point.Z):0;
        public void SetPaidChain(Vector3 grip,Vector3 ring,float payout,Vector3 hero)
        {
            if(_chainLinks==null)return;
            if(_paidChain.Count==0){_lastGrip=grip;_lastRing=ring;_lastPayout=payout;}
            Vector3 bottom=hero+Vector3.up*.9f,top=hero+Vector3.up*1.28f;
            _paidAccumulator=Mathf.Min(_paidAccumulator+Time.deltaTime,.1f);
            while(_paidAccumulator>=AnchorChainSolver.Step)
            {
                float u=Mathf.Clamp01(1-(_paidAccumulator-AnchorChainSolver.Step)/Mathf.Max(.0001f,Time.deltaTime));
                _paidChain.Advance(Nv(Vector3.Lerp(_lastGrip,grip,u)),Nv(Vector3.Lerp(_lastRing,ring,u)),
                    Mathf.Lerp(_lastPayout,payout,u),ChainGround(Nv(hero)),Nv(bottom),Nv(top),.18f,true);
                _paidAccumulator-=AnchorChainSolver.Step;
            }
            _paidChain.Advance(Nv(grip),Nv(ring),payout,ChainGround(Nv(hero)),Nv(bottom),Nv(top),.18f,false);
            _lastGrip=grip;_lastRing=ring;_lastPayout=payout;
            _chainLinks.SetSolved(_paidChain);
            if(PrimaryLine!=null)PrimaryLine.enabled=false;
            if(_chainGlint!=null)_chainGlint.gameObject.SetActive(false);
            if(CaptureRig.LiveSkill)Debug.Log($"[anchor-leap-chain] strain={_paidChain.MaxStrain:F5} grip={_paidChain.AttachmentError:F6} span={Vector3.Distance(grip,ring):F3} payout={payout:F3}");
        }
        private static N Nv(Vector3 p)=>new N(p.x,p.y,p.z);
    }
}
