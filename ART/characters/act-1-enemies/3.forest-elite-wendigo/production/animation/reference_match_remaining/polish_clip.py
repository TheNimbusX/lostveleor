"""Постоянная плоскость локтя, ограничение скручивания кисти и субкадровая опора."""
import sys,argparse
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'reference_match_leap'))
from fit_leap import *
arg=argparse.ArgumentParser();arg.add_argument('kind');args=arg.parse_args()
kind=args.kind;out=ROOT/(kind.lower()+'_work') if kind!='Leap' else ROOT.parent/'reference_match_leap'
samples=np.array(json.loads((out/('polished_samples.json' if kind=='Leap' else 'animation_samples.json')).read_text()))
fixed=[];audit=[];prev_delta=np.zeros(66);previous_normal={}
def unit(v):return v/max(np.linalg.norm(v),1e-9)
def axes_frame(y,n):
 y=unit(y);z=unit(n-y*np.dot(y,n));x=unit(np.cross(y,z));return np.stack([x,y,z],axis=1)
def assign(x,i,world):
 parent=fk(x)[parents[i],:3,:3];local=(parent@locals[i,:3,:3]).T@world
 x[3+(i-1)*3:3+i*3]=Rotation.from_matrix(local).as_rotvec()
for k,old in enumerate(samples):
 x=old.copy();p=fk(x)
 if kind=='Leap':
  for side in ['L']:
   ui,li,hi=[names.index(side+n) for n in ['_arm_upper','_arm_lower','_hand']]
   # Не вращаем плечо вслед за плоскостью локтя: это разворачивало плоскую кору ребром.
   # Меняется только лишний продольный roll предплечья; центры суставов остаются на месте.
   j=3+(li-1)*3;q=Rotation.from_rotvec(x[j:j+3]).as_quat();tw=unit(np.array([0,q[1],0,q[3]]));ang=(2*np.arctan2(tw[1],tw[3])+np.pi)%(2*np.pi)-np.pi
   limit=np.deg2rad(40 if side=='L' else 70)
   x[j:j+3]=(Rotation.from_quat(q)*Rotation.from_quat(tw).inv()*Rotation.from_rotvec([0,np.clip(ang,-limit,limit),0])).as_rotvec()
   assign(x,hi,p[hi,:3,:3])
   v=x[3+(hi-1)*3:3+hi*3];q=Rotation.from_rotvec(v).as_quat();tw=unit(np.array([0,q[1],0,q[3]]));angle=(2*np.arctan2(tw[1],tw[3])+np.pi)%(2*np.pi)-np.pi
   swing=Rotation.from_quat(q)*Rotation.from_quat(tw).inv()
   x[3+(hi-1)*3:3+hi*3]=(swing*Rotation.from_rotvec([0,np.clip(angle,-1.31,1.31),0])).as_rotvec()
 original=x.copy();xyz=deform(x,verts,weights);critical=np.unique(np.r_[floor_ix,np.where(xyz[:,2]<.06)[0]])
 sv,sw=verts[critical],weights[critical];marker=deform(x)
 if xyz[:,2].min()<.005:
  def error(v):
   ma=fk(v)@inverses;z=np.einsum('vb,bj,vj->v',sw,ma[:,2,:],sv)
   return np.r_[(v-original)*np.r_[[180,180,100],np.repeat(40,63)],np.minimum(z-.006,0)*900,((deform(v)-marker)/.035).ravel(),(v-original-prev_delta)*7]
  opt=least_squares(error,x,max_nfev=24,ftol=1e-5,xtol=1e-5);x=opt.x
 prev_delta=x-original;fixed.append(x.tolist());final=deform(x,verts,weights)
 audit.append({'frame':k/4,'min_z':float(final[:,2].min()),'correction_deg':float(np.linalg.norm((x-original)[3:].reshape(-1,3),axis=1).max()*180/np.pi)})
 if k%80==0:print(kind,k,'/',len(samples),flush=True)
(out/'final_samples.json').write_text(json.dumps(fixed));(out/'final_contacts.json').write_text(json.dumps(audit,indent=2))
print('minimum',min(x['min_z'] for x in audit),flush=True)
