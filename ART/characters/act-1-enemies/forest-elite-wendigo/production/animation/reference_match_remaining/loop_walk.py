"""Выделение полного шага из референса, замыкание и удаление поступательного root motion."""
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'reference_match_leap'))
from fit_leap import *
from scipy.interpolate import CubicSpline
from scipy.spatial.transform import Slerp
OUT=ROOT/'walk_work'
d=json.loads((OUT/'pose_solutions.json').read_text());times=np.array([float(k) for k in d['frames']]);poses=np.array([v['pose'] for v in d['frames'].values()])
stride=poses[-1,:2]-poses[0,:2];travel=np.r_[stride,0.0]
poses[:,:2]-=(times/times[-1])[:,None]*stride
# Концы — одна фаза шага. Коррекция ограничена двумя соседними ключами, внутри шаг не выравнивается.
for bone in range(21):
 j=3+bone*3;q=Rotation.from_rotvec(poses[:,j:j+3]);endpoint=Slerp([0,1],Rotation.from_quat([q[0].as_quat(),q[-1].as_quat()]))([.5])[0]
 poses[0,j:j+3]=poses[-1,j:j+3]=endpoint.as_rotvec()
poses[0,:3]=poses[-1,:3]=(poses[0,:3]+poses[-1,:3])*.5
spline=CubicSpline(times,poses,bc_type='periodic')
samples=spline(np.arange(193)/4)
# Привязываем опорную стопу к миру: при движении root вперёд её локальная позиция идёт назад.
forward_walk=travel/max(np.linalg.norm(travel),1e-8);stride_length=1.8;speed=forward_walk*stride_length/48
# В видео короткие шаги на месте. Для игровой скорости сохраняем фазы и работу корпуса,
# но даём ногам рабочий шаг; затем скорость проигрывания определяется пройденными метрами.
centres={side:verts[ids[keys.index(side+'_toe')],:3].copy() for side in ['R','L']}
for side in centres:
 centres[side]-=forward_walk*np.dot(centres[side],forward_walk)
 centres[side]+=forward_walk*.1
for k,x in enumerate(samples):
 f=k/4
 for side,start in [('R',36),('L',12)]:
  elapsed=(f-start)%48
  ki=keys.index(side+'_toe')
  if elapsed<=30:offset=.5625-stride_length*elapsed/48;lift=0
  else:
   t=(elapsed-30)/18;ease=t*t*(3-2*t);offset=-.5625+1.125*ease;lift=.26*np.sin(np.pi*t)**2
  target=centres[side]+forward_walk*offset;target[2]=.045+lift
  bs=[names.index(side+'_'+n) for n in ['leg_upper','leg_lower','foot']]
  columns=np.concatenate([np.arange(3+(bi-1)*3,3+bi*3) for bi in bs]);v0=x[columns].copy()
  def err(v):
   pose=x.copy();pose[columns]=v;p=deform(pose);ts=fk(pose);bi=bs[-1]
   return np.r_[(p[ki]-target)/.006,(v-v0)*1.8,((ts[bi,:3,:3]-rests[bi,:3,:3])*4).ravel()]
  opt=least_squares(err,v0,max_nfev=35,ftol=2e-5,xtol=2e-5);x[columns]=opt.x
 samples[k]=x
samples[-1]=samples[0]
(OUT/'animation_samples.json').write_text(json.dumps(samples.tolist()))
(OUT/'loop_contract.json').write_text(json.dumps({'source_frames':[36,84],'cycle_frames':48,'fps':24,'stride_metres':stride_length,'travel_source':travel.tolist(),'root_motion':'removed; simulation moves model','stance':{'R':[36,66],'L':[12,42]}},indent=2))
print('stride',stride_length,'m')
