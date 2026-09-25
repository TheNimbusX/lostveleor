"""Reference-space pose fitting; editable FK keys, no game export.

Landmarks are manually identified on the actual mesh and approved video frames.
The solve preserves bone lengths. It is a blocking aid, not artistic acceptance.
"""
import json, argparse
from pathlib import Path
import numpy as np
from scipy.optimize import least_squares
from scipy.spatial.transform import Rotation

HERE = Path(__file__).resolve().parent
src=json.loads((HERE/'fit_source.json').read_text())
verts=np.c_[np.array(src['vertices']),np.ones(len(src['vertices']))]
projected=np.array(src['projected'])
names=[b['name'] for b in src['bones']]
rests=np.array([b['matrix'] for b in src['bones']])
inverses=np.linalg.inv(rests)
parents=[names.index(b['parent']) if b['parent'] else -1 for b in src['bones']]
locals=np.array([inverses[p]@rests[i] if p>=0 else rests[i] for i,p in enumerate(parents)])
weights=np.zeros((len(verts),len(names)))
for i,gs in enumerate(src['weights']):
 for n,w in gs:
  if n in names:weights[i,names.index(n)]=w

# Stable surface landmarks. Left/right names refer to the rig, not the screen.
mark_xy={
 'nose':(565,445),'brow':(573,351),'horn_R':(510,126),'horn_L':(613,116),
 'R_shoulder':(392,333),'R_elbow':(310,488),'R_wrist':(244,629),
 'R_tip':(245,811),'R_inner':(299,768),
 'L_shoulder':(595,366),'L_elbow':(674,474),'L_wrist':(731,634),
 'L_tip':(706,821),'waist':(497,481),
 'R_knee':(446,665),'R_ankle':(337,854),'R_toe':(326,958),
 'L_knee':(590,641),'L_ankle':(651,836),'L_toe':(727,910)
}
keys=list(mark_xy)
ids=[int(np.argmin(np.sum((projected-np.array(mark_xy[k]))**2,axis=1))) for k in keys]
if (HERE/'visible_landmarks.json').exists():
 visible=json.loads((HERE/'visible_landmarks.json').read_text())
 ids=[visible[k] for k in keys]
pv=verts[ids];pw=weights[ids]

def cam_matrix(par):
 az,el,scale,cx,cy=par
 forward=np.array([np.sin(az)*np.cos(el),np.cos(az)*np.cos(el),np.sin(el)])
 right=np.cross([0,0,1],forward);right/=np.linalg.norm(right)
 up=np.cross(forward,right)
 return np.array([right,up]),scale,np.array([cx,cy])

def project(points,par):
 axes,scale,offset=cam_matrix(par)
 centered=points[...,:3]-[0,0,1.55]
 q=centered@axes.T
 forward=np.cross(axes[0],axes[1])
 depth=centered@forward
 return q*np.array([960/scale,-960/scale])*5.5/(5.5-depth[...,None])+offset

base_targets={
 'nose':(565,375),'brow':(575,280),'horn_R':(512,42),'horn_L':(616,40),
 'R_shoulder':(397,275),'R_elbow':(310,410),'R_wrist':(270,549),
 'R_tip':(238,741),'R_inner':(309,690),
 'L_shoulder':(601,317),'L_elbow':(669,412),'L_wrist':(711,542),
 'L_tip':(685,739),'waist':(491,423),
 'R_knee':(443,600),'R_ankle':(343,790),'R_toe':(323,908),
 'L_knee':(584,570),'L_ankle':(627,728),'L_toe':(691,808)
}
cam0=np.array([np.arctan2(1,.28),np.arctan2(.3,np.hypot(1,.28)),3.4,496.32,480])
camkeys=['nose','brow','horn_R','horn_L','waist','R_toe','L_toe']
ci=[keys.index(k) for k in camkeys]
ct=np.array([base_targets[k] for k in camkeys])
fit=least_squares(lambda c:(project(pv[ci],c)-ct).ravel(),cam0,bounds=([.4,0,2.8,300,300],[1.8,.6,4.2,650,650]))
camera=fit.x
(HERE/'landmarks.json').write_text(json.dumps({'keys':keys,'vertex_ids':ids,'camera':camera.tolist(),'base_targets':base_targets,'camera_rms':float(np.sqrt(np.mean(fit.fun**2)))},indent=2))

if __name__=='__main__':
 print('camera',np.round(camera,4),'rms',round(np.sqrt(np.mean(fit.fun**2)),2))

def fk(x):
 basis=np.tile(np.eye(4),(len(names),1,1))
 basis[1:,:3,:3]=Rotation.from_rotvec(x[3:].reshape(-1,3)).as_matrix()
 basis[1,:3,3]=x[:3]
 pose=[]
 for i,p in enumerate(parents):
  pose.append((pose[p] if p>=0 else np.eye(4))@locals[i]@basis[i])
 return np.array(pose)

def deform(x):
 mats=fk(x)@inverses
 return np.einsum('vb,bij,vj->vi',pw,mats,pv)[:,:3]

stations={0:base_targets,24:{
 'nose':(430,438),'brow':(458,343),'horn_R':(403,110),'horn_L':(506,123),
 'R_elbow':(267,298),'R_wrist':(175,364),'R_tip':(162,528),'R_inner':(208,497),
 'L_shoulder':(542,337),'L_elbow':(629,436),'L_wrist':(618,566),'L_tip':(510,706),
 'waist':(461,483),'R_knee':(435,609),'L_knee':(572,599)},36:{
 'nose':(372,395),'brow':(410,308),'horn_R':(402,77),'horn_L':(503,100),
 'R_elbow':(254,213),'R_wrist':(156,209),'R_tip':(79,325),'R_inner':(174,309),
 'L_shoulder':(511,310),'L_elbow':(615,405),'L_wrist':(607,528),'L_tip':(498,663),
 'waist':(445,462),'R_knee':(415,587),'L_knee':(568,598)},50:{
 'nose':(477,449),'brow':(483,346),'horn_R':(417,115),'horn_L':(529,105),
 'R_shoulder':(367,300),'R_elbow':(251,260),'R_wrist':(88,186),
 'R_tip':(-40,86),'R_inner':(-35,182),
 'L_shoulder':(573,326),'L_elbow':(681,381),'L_wrist':(724,492),'L_tip':(704,648),
 'waist':(472,474),'R_knee':(456,614),'L_knee':(584,611)},52:{
 'nose':(684,511),'brow':(686,412),'horn_R':(596,178),'horn_L':(705,161),
 'R_shoulder':(531,399),'R_elbow':(412,562),'R_wrist':(269,688),
 'R_tip':(5,551),'R_inner':(3,624),
 'L_shoulder':(716,367),'L_elbow':(741,376),'L_wrist':(809,405),'L_tip':(890,523),
 'waist':(549,520),'R_knee':(519,677),'L_knee':(651,623)},57:{
 'nose':(712,414),'brow':(745,330),'horn_R':(665,104),'horn_L':(744,103),
 'R_shoulder':(639,360),'R_elbow':(752,478),'R_wrist':(904,492),
 'L_shoulder':(524,289),'L_elbow':(402,356),'L_wrist':(370,436),'L_tip':(405,485),
 'waist':(552,469),'R_knee':(528,656),'L_knee':(654,588)}
}
stations[16]=base_targets.copy()
stations[40]={
 'nose':(371,378),'brow':(403,286),'horn_R':(393,59),'horn_L':(492,79),
 'R_elbow':(253,181),'R_wrist':(160,169),'R_tip':(71,274),'R_inner':(173,267),
 'L_elbow':(613,386),'L_wrist':(622,509),'L_tip':(531,650),
 'waist':(444,452),'R_knee':(412,581),'L_knee':(570,596)}
stations[44]={
 'nose':(370,365),'brow':(398,270),'horn_R':(379,46),'horn_L':(480,63),
 'R_elbow':(251,153),'R_wrist':(164,137),'R_tip':(69,233),'R_inner':(174,236),
 'L_elbow':(612,375),'L_wrist':(636,494),'L_tip':(556,637),
 'waist':(444,442),'R_knee':(410,577),'L_knee':(573,594)}
stations[48]={
 'nose':(371,360),'brow':(396,264),'horn_R':(374,42),'horn_L':(473,56),
 'R_elbow':(250,135),'R_wrist':(167,118),'R_tip':(67,212),'R_inner':(175,222),
 'L_elbow':(613,368),'L_wrist':(643,485),'L_tip':(573,628),
 'waist':(444,436),'R_knee':(410,574),'L_knee':(576,591)}
stations[61]={
 'nose':(704,392),'brow':(728,311),'horn_R':(652,81),'horn_L':(722,79),
 'R_shoulder':(632,345),'R_elbow':(744,451),'R_wrist':(844,430),
 'L_elbow':(388,368),'L_wrist':(367,450),'L_tip':(402,497),
 'waist':(548,460),'R_knee':(523,648),'L_knee':(648,585)}
stations[65]={
 'nose':(699,385),'brow':(720,300),'horn_R':(646,73),'horn_L':(714,74),
 'R_shoulder':(627,341),'R_elbow':(739,445),'R_wrist':(824,418),
 'L_elbow':(389,371),'L_wrist':(372,455),'L_tip':(407,505),
 'waist':(546,455),'R_knee':(520,639),'L_knee':(646,584)}
stations[69]={
 'nose':(696,381),'brow':(718,296),'horn_R':(640,69),'horn_L':(710,72),
 'R_shoulder':(624,340),'R_elbow':(736,441),'R_wrist':(810,412),
 'L_elbow':(392,374),'L_wrist':(378,457),'L_tip':(414,510),
 'waist':(545,452),'R_knee':(518,634),'L_knee':(644,583)}
stations[84]=base_targets.copy()
stations[96]=base_targets.copy()
stations[19]={
 'nose':(528,408),'brow':(550,302),'horn_R':(472,74),'horn_L':(578,72),
 'R_shoulder':(380,284),'R_elbow':(280,402),'R_wrist':(263,528),'R_tip':(275,709),'R_inner':(307,665),
 'L_shoulder':(591,332),'L_elbow':(654,431),'L_wrist':(702,566),'L_tip':(654,744),
 'waist':(485,456),'R_knee':(447,614),'L_knee':(584,584)}
stations[54]={
 'nose':(725,458),'brow':(755,362),'horn_R':(673,136),'horn_L':(764,130),
 'R_shoulder':(629,383),'R_elbow':(747,548),'R_wrist':(888,659),
 'L_shoulder':(542,307),'L_elbow':(479,323),'L_wrist':(487,390),'L_tip':(512,447),
 'waist':(552,487),'R_knee':(531,667),'L_knee':(665,604)}
stations[75]={
 'nose':(662,416),'brow':(680,317),'horn_R':(595,89),'horn_L':(689,87),
 'R_shoulder':(522,344),'R_elbow':(504,491),'R_wrist':(683,557),'R_tip':(844,582),'R_inner':(833,524),
 'L_shoulder':(590,360),'L_elbow':(627,433),'L_wrist':(654,508),'L_tip':(688,600),
 'waist':(525,469),'R_knee':(497,633),'L_knee':(613,584)}
stations[78]={
 'nose':(608,410),'brow':(609,310),'horn_R':(544,73),'horn_L':(648,76),
 'R_shoulder':(451,310),'R_elbow':(395,473),'R_wrist':(464,610),'R_tip':(608,705),'R_inner':(578,644),
 'L_shoulder':(589,340),'L_elbow':(646,440),'L_wrist':(699,558),'L_tip':(628,704),
 'waist':(493,456),'R_knee':(465,622),'L_knee':(588,584)}
stations[30]={
 'nose':(392,421),'brow':(430,336),'horn_R':(414,95),'horn_L':(505,118),
 'R_elbow':(251,246),'R_wrist':(168,273),'R_tip':(103,414),'R_inner':(189,381),
 'L_elbow':(620,422),'L_wrist':(581,540),'L_tip':(475,678),
 'waist':(450,483),'R_knee':(425,611),'L_knee':(565,604)}
stations[51]={
 'nose':(575,492),'brow':(579,397),'horn_R':(484,159),'horn_L':(597,146),
 'R_shoulder':(437,350),'R_elbow':(254,404),'R_wrist':(100,365),
 'L_elbow':(737,399),'L_wrist':(817,472),'L_tip':(855,618),
 'waist':(523,507),'R_knee':(493,662),'L_knee':(620,610)}
stations[53]={
 'nose':(725,493),'brow':(742,401),'horn_R':(647,168),'horn_L':(751,153),
 'R_shoulder':(592,413),'R_elbow':(626,589),'R_wrist':(695,802),
 'L_elbow':(739,354),'L_wrist':(752,380),'L_tip':(781,471),
 'waist':(555,501),'R_knee':(532,668),'L_knee':(661,614)}

def solve(only_frames=None):
 x=np.zeros(3+(len(names)-1)*3)
 solutions={};previous=x.copy()
 cached=json.loads((HERE/'pose_solutions.json').read_text())['frames'] if only_frames else {}
 toe_ids=[keys.index(n) for n in ['R_toe','L_toe','R_ankle','L_ankle']]
 feet=deform(x)[toe_ids].copy()
 prior=np.array([12 if n=='pelvis' else 8 if n in ['spine_01','spine_02'] else 3 if n in ['neck','head'] else 5 if 'clavicle' in n else 4 if any(a in n for a in ['foot','toe']) else 2.2 for n in names[1:]])
 yaw={0:(0,0,0),16:(0,0,0),19:(4,4,4),24:(12,16,14),36:(18,22,22),48:(18,22,22),50:(7,10,12),52:(-10,-10,-10),54:(-16,-17,-17),57:(-16,-17,-17),69:(-16,-17,-17),75:(-10,-10,-10),78:(-4,-3,-3),84:(0,0,0),96:(0,0,0)}
 yaw.update({30:(15,20,18),51:(-2,-3,-3),53:(-13,-14,-14)})
 yaw.update({40:(19,23,23),44:(20,24,24),48:(20,24,24),61:(-15,-16,-16),65:(-14,-15,-15),69:(-13,-14,-14)})
 limits=np.r_[np.repeat(.8,3),np.repeat([.9 if 'clavicle' in n else 1.2 if n in ['head','neck'] else 2.8 for n in names[1:]],3)]
 for frame,partial in sorted(stations.items()):
  if only_frames and frame not in only_frames:
   solutions[frame]=cached[str(frame)]
   x=np.array(solutions[frame]['pose']);previous=x.copy()
   continue
  target={**base_targets,**partial}
  # This rest-view mark is on the neck over the occluded left shoulder;
  # it cannot be used as a shoulder correspondence after a body turn.
  target.pop('L_shoulder',None)
  if frame not in (0,16,84,96):
   # Hidden shoulder/claw points must not be treated as observed positions.
   for n in ['R_shoulder','R_tip','R_inner']:
    if n not in partial:target.pop(n,None)
  ix=[keys.index(k) for k in target];xy=np.array(list(target.values()))
  guide=np.zeros_like(x)
  for n,angle in zip(['pelvis','spine_01','spine_02'],yaw[frame]):
   i=names.index(n);guide[3+(i-1)*3:3+i*3]=rests[i,:3,:3].T@np.array([0,0,np.deg2rad(-angle)])
  if frame in (54,57,61,65,69,75):
   for n,angle in [('neck',-10),('head',-20)]:
    i=names.index(n);guide[3+(i-1)*3:3+i*3]=rests[i,:3,:3].T@np.array([0,0,np.deg2rad(angle)])
  # Constrain the head near its anatomical orientation; 2-D points alone
  # permit the visually wrong backwards-facing solution.
  x[3:18]=guide[3:18]
  def error(v):
   p=deform(v)
   data=((project(p[ix],camera)-xy)/8).ravel()
   reg=((v[3:]-guide[3:]).reshape(-1,3)*prior[:,None]).ravel()
   continuity=(v-previous)*(.35 if frame not in (0,84,96) else 0)
   contact=((p[toe_ids]-feet)/.025).ravel()
   transforms=fk(v)
   planted=[]
   for n in ['R_foot','L_foot','R_toe','L_toe']:
    bi=names.index(n)
    planted.extend(((transforms[bi,:3,3]-rests[bi,:3,3])/.015).ravel())
    planted.extend(((transforms[bi,:3,:3]-rests[bi,:3,:3])*16).ravel())
   return np.r_[data,reg,contact,continuity,v[:3]*4,planted]
  best=least_squares(error,np.clip(x,-limits+1e-4,limits-1e-4),bounds=(-limits,limits),max_nfev=150,ftol=1e-5,xtol=1e-5)
  x=best.x;previous=x.copy()
  px=project(deform(x),camera)
  solutions[frame]={'pose':x.tolist(),'rms_px':float(np.sqrt(np.mean((px[ix]-xy)**2))),'projected':dict(zip(keys,px.tolist()))}
  print('frame',frame,'rms',round(solutions[frame]['rms_px'],1),flush=True)
 (HERE/'pose_solutions.json').write_text(json.dumps({'names':names,'camera':camera.tolist(),'keys':keys,'vertex_ids':ids,'frames':solutions},indent=2))

if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--frames',default='')
 arg=parser.parse_args()
 solve({int(f) for f in arg.frames.split(',')} if arg.frames else None)
