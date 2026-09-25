"""Убирает скрытое встречное скручивание плеча и предплечья, сохраняя дугу руки."""
from fit_leap import *
samples=np.array(json.loads((HERE/'polished_samples.json').read_text()))
fixed=[];audit=[];previous_normal={}
def unit(v):return v/max(np.linalg.norm(v),1e-8)
def frame(y,n):
 y=unit(y);z=unit(n-y*np.dot(y,n));x=unit(np.cross(y,z));return np.stack([x,y,z],axis=1)
def assign(x,b,world):
 i=names.index(b);parent=fk(x)[parents[i],:3,:3]
 local=(parent@locals[i,:3,:3]).T@world
 x[3+(i-1)*3:3+i*3]=Rotation.from_matrix(local).as_rotvec()
for k,old in enumerate(samples):
 x=old.copy();p=fk(old)
 # Остальное принятое движение сохраняется; меняется только левая рука.
 for side in ['L']:
  ui=names.index(side+'_arm_upper');li=names.index(side+'_arm_lower');hi=names.index(side+'_hand')
  ur=rests[li,:3,3]-rests[ui,:3,3];lr=rests[hi,:3,3]-rests[li,:3,3];nr=unit(np.cross(ur,lr))
  u=p[li,:3,3]-p[ui,:3,3];l=p[hi,:3,3]-p[li,:3,3];n=unit(np.cross(u,l))
  expected=previous_normal.get(side,p[ui,:3,:3]@rests[ui,:3,:3].T@nr)
  if np.dot(n,expected)<0:n=-n
  if np.linalg.norm(np.cross(unit(u),unit(l)))<.04:n=expected
  previous_normal[side]=n
  for bone,i,rest_axis,axis in [(side+'_arm_upper',ui,ur,u),(side+'_arm_lower',li,lr,l)]:
   world=frame(axis,n)@frame(rest_axis,nr).T@rests[i,:3,:3]
   assign(x,bone,world)
  # Кисть сохраняет направление. Продольное скручивание ограничено анатомическим диапазоном.
  assign(x,side+'_hand',p[hi,:3,:3])
  v=x[3+(hi-1)*3:3+hi*3];q=Rotation.from_rotvec(v).as_quat();twist=unit(np.array([0,q[1],0,q[3]]))
  angle=2*np.arctan2(twist[1],twist[3]);angle=(angle+np.pi)%(2*np.pi)-np.pi
  swing=Rotation.from_quat(q)*Rotation.from_quat(twist).inv()
  limited=np.clip(angle,-np.deg2rad(75),np.deg2rad(75))
  x[3+(hi-1)*3:3+hi*3]=(swing*Rotation.from_rotvec([0,limited,0])).as_rotvec()
  audit.append({'frame':k/4,'side':side,'wrist_twist_before_deg':float(np.rad2deg(angle)),'wrist_twist_after_deg':float(np.rad2deg(limited))})
 fixed.append(x.tolist())
(HERE/'leap_r02_samples.json').write_text(json.dumps(fixed))
(HERE/'arm_roll_audit.json').write_text(json.dumps(audit,indent=2))
print('Arm roll corrected:',len(fixed),'samples')
