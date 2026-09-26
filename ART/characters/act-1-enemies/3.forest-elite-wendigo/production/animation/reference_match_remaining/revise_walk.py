"""Walk r04: ref-derived torso arcs, planted feet, toe-off and distance-matched stride.

The old fitted walk is retained as the reference, never overwritten. All rotations
use constant bone lengths. Travel is removed from the root; Unity advances phase
by actual travel / 2.4 metres. The 48-frame source can be inspected at any speed.
"""
import sys, json
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
from scipy.interpolate import CubicHermiteSpline
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent / 'reference_match_leap'))
from fit_leap import names, rests, parents, locals, fk, deform, verts, weights
OUT = ROOT / 'walk_revision_r04'; OUT.mkdir(exist_ok=True)
old = np.array(json.loads((ROOT/'walk_work/final_samples.json').read_text()))
F = np.array([1.,1.,0.]); F /= np.linalg.norm(F)
SIDE = np.cross(F, [0.,0.,1.]); UP = np.array([0.,0.,1.])
STRIDE = 2.4
STANCE = .59
angles = np.arange(192)/192*2*np.pi
def design(t):
    return np.array([1, np.cos(t), np.sin(t), np.cos(2*t), np.sin(2*t)])
coeff = np.linalg.lstsq(np.array([design(t) for t in angles]),old[:-1,3:],rcond=None)[0]
def sl(n):
    i=names.index(n); return slice(3+(i-1)*3,3+i*3)
def assign(x,n,world):
    i=names.index(n); p=fk(x)[parents[i],:3,:3]
    x[sl(n)] = Rotation.from_matrix((p@locals[i,:3,:3]).T@world).as_rotvec()
def align(a,b):
    a=a/np.linalg.norm(a); b=b/np.linalg.norm(b)
    return Rotation.from_rotvec(np.cross(a,b)/max(np.linalg.norm(np.cross(a,b)),1e-9)*np.arccos(np.clip(a@b,-1,1))).as_matrix()
def world_delta(x,n,axis,degrees):
    assign(x,n,Rotation.from_rotvec(axis*np.deg2rad(degrees)).as_matrix()@fk(x)[names.index(n),:3,:3])

# Swing meets the moving stance with the same backwards velocity: no stop/snap
# at touchdown or toe-off. Mid-swing is deliberately quicker than the planted part.
half=STRIDE*STANCE*.5
swing=CubicHermiteSpline([0,.5,1],[-half,.04,half],[-STRIDE*(1-STANCE),2.6,-STRIDE*(1-STANCE)])
foot_pitch=CubicHermiteSpline([0,.11,STANCE-.12,STANCE,.78,1],[9,0,0,-20,-9,9],[0,0,0,0,100,0])
def foot_curve(p):
    pitch=float(foot_pitch(p))
    if p<STANCE:
        y=half-STRIDE*p
        lift=0
    else:
        t=(p-STANCE)/(1-STANCE)
        y=float(swing(t)); lift=.23*np.sin(np.pi*t)**2
    return y,lift,pitch

samples=[]; audit=[]
for k in range(193):
    phase=k/192; theta=phase*2*np.pi
    # Rephase the existing video fit to right-foot contact. Retain its torso
    # asymmetry and arm overlap, strip camera drift and the folded last pose.
    x=np.r_[np.zeros(3),design(theta+1.5*np.pi)@coeff]
    x[:3]=SIDE*(.055*np.sin(theta-.4))+F*.02+UP*(-.27-.025*np.sin(2*theta-.35))
    # A stable pelvis carries mass; shoulders counter-turn, the head follows later.
    for n,rv in [('pelvis',F*np.deg2rad(3*np.sin(theta)) + UP*np.deg2rad(6*np.cos(theta)) - SIDE*np.deg2rad(4)),
                 ('spine_01',UP*np.deg2rad(-4*np.cos(theta-.15))-SIDE*np.deg2rad(3)),
                 ('spine_02',UP*np.deg2rad(-5*np.cos(theta-.3))+F*np.deg2rad(-2*np.sin(theta-.2))),
                 ('neck',SIDE*np.deg2rad(3)+UP*np.deg2rad(2*np.cos(theta-.4))),
                 ('head',SIDE*np.deg2rad(2)+F*np.deg2rad(1.5*np.sin(theta-.5)))]:
        i=names.index(n);x[sl(n)]=rests[i,:3,:3].T@rv
    # Long arms have a low pendular path; wrists lag the shoulder. The video fit
    # supplies elbow shape, while extra swing makes propulsion legible in game.
    for side,sign in [('R',1),('L',-1)]:
        world_delta(x,side+'_arm_upper',SIDE,sign*13*np.cos(theta-.25))
        world_delta(x,side+'_arm_lower',SIDE,sign*5*np.cos(theta-.65))
        world_delta(x,side+'_hand',SIDE,sign*4*np.cos(theta-.95))

    targets={}
    for side,offset,sign in [('R',0,1),('L',.5,-1)]:
        p=(phase+offset)%1; y,lift,pitch=foot_curve(p)
        ui,li,fi,ti=[names.index(side+'_'+n) for n in ['leg_upper','leg_lower','foot','toe']]
        footrot=Rotation.from_rotvec(SIDE*np.deg2rad(pitch)).as_matrix()@rests[fi,:3,:3]
        # Lower edge of the rigid foot, including the toe. Ground contact uses
        # geometry rather than a marker floating above the actual sole.
        group=np.where(weights[:,fi]+weights[:,ti]>.8)[0]
        rel=(verts[group,:3]-rests[fi,:3,3])@rests[fi,:3,:3]
        local=rel@footrot.T
        ankle=F*(y-.025)+SIDE*(sign*.39)+UP*(.009-float(local[:,2].min())+lift)
        hip=fk(x)[ui,:3,3]; delta=ankle-hip; dist=np.linalg.norm(delta)
        l1=np.linalg.norm(rests[li,:3,3]-rests[ui,:3,3]); l2=np.linalg.norm(rests[fi,:3,3]-rests[li,:3,3])
        if dist>l1+l2-.008:
            # Never stretch: shorten only an unreachable airborne reach.
            ankle=hip+delta/dist*(l1+l2-.008);delta=ankle-hip;dist=np.linalg.norm(delta)
        axis=delta/dist; pole=F+SIDE*(sign*.13);pole-=axis*(pole@axis);pole/=np.linalg.norm(pole)
        along=(l1*l1-l2*l2+dist*dist)/(2*dist)
        knee=hip+axis*along+pole*np.sqrt(max(0,l1*l1-along*along))
        assign(x,names[ui],align(rests[li,:3,3]-rests[ui,:3,3],knee-hip)@rests[ui,:3,:3])
        assign(x,names[li],align(rests[fi,:3,3]-rests[li,:3,3],ankle-knee)@rests[li,:3,:3])
        assign(x,names[fi],footrot)
        x[sl(names[ti])]=0
        targets[side]={'stance':p<STANCE,'ankle':ankle.tolist(),'phase':p,'pitch':pitch}
    xyz=deform(x,verts,weights)
    samples.append(x.tolist());audit.append({'frame':k/4,'min_z':float(xyz[:,2].min()),'feet':targets})
samples[-1]=samples[0]
(OUT/'final_samples.json').write_text(json.dumps(samples))
(OUT/'contact_audit.json').write_text(json.dumps(audit,indent=2))
(OUT/'loop_contract.json').write_text(json.dumps({'stride_metres':STRIDE,'cycle_frames':48,'fps':24,'root_motion':False,'metres_per_second':3,'runtime_cycle_seconds':STRIDE/3,'source':'Higgsfield idle_walk 36..84; torso fit cleaned, feet reauthored with fixed-length IK','stance_fraction':STANCE},indent=2))
print('walk r04',len(samples),'minimum ground',min(a['min_z'] for a in audit))
