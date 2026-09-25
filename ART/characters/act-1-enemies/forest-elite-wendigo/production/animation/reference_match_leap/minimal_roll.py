"""Сохраняем дуги суставов и ладони, устраняем полный оборот предплечья в посадке."""
from fit_leap import *
samples=np.array(json.loads((HERE/'polished_samples.json').read_text()));out=[];transport={}
def align(a,b):
 a=a/np.linalg.norm(a);b=b/np.linalg.norm(b);v=np.cross(a,b);q=np.r_[v,1+np.dot(a,b)];q/=np.linalg.norm(q);return Rotation.from_quat(q).as_matrix()
def assign(x,i,w):
 p=fk(x)[parents[i],:3,:3];local=(p@locals[i,:3,:3]).T@w;x[3+(i-1)*3:3+i*3]=Rotation.from_matrix(local).as_rotvec()
for old in samples:
 x=old.copy();p=fk(old)
 for side in ['R','L']:
  ui,li,hi=[names.index(side+n) for n in ['_arm_upper','_arm_lower','_hand']]
  for i,child in [(ui,li),(li,hi)]:
   restaxis=rests[child,:3,3]-rests[i,:3,3];axis=p[child,:3,3]-p[i,:3,3]
   if i in transport:
    previous_axis,previous_world=transport[i];world=align(previous_axis,axis)@previous_world
   else:world=align(restaxis,axis)@rests[i,:3,:3]
   transport[i]=(axis.copy(),world.copy());assign(x,i,world)
  assign(x,hi,p[hi,:3,:3])
 out.append(x.tolist())
(HERE/'minimal_roll_samples.json').write_text(json.dumps(out))
audit=[];previous=None
for k,x in enumerate(out):
 x=np.array(x);xyz=deform(x,verts,weights);pose=fk(x);old=fk(samples[k]);delta=0
 if previous is not None:
  delta=max(Rotation.from_matrix(pose[i,:3,:3]@previous[i,:3,:3].T).magnitude()*180/np.pi for i in [7,8,11,12])
 previous=pose
 audit.append({'frame':k/4,'min_z':float(xyz[:,2].min()),'joint_error':float(np.linalg.norm(pose[:,:3,3]-old[:,:3,3],axis=1).max()),'arm_step_deg':float(delta)})
(HERE/'minimal_roll_audit.json').write_text(json.dumps(audit,indent=2))
(HERE/'final_samples.json').write_text(json.dumps(out))
print('min ground',min(a['min_z'] for a in audit),'max joint error',max(a['joint_error'] for a in audit),'max arm step',max(a['arm_step_deg'] for a in audit))
print('saved',len(out))
