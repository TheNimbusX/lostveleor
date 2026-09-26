"""Постановка Leap по отмеченным кадрам; все исходники Claw неизменны."""
import json, argparse
from pathlib import Path
import numpy as np
from scipy.optimize import least_squares
from scipy.spatial.transform import Rotation
from scipy.spatial import ConvexHull

HERE=Path(__file__).resolve().parent
src=json.loads((HERE/'fit_source.json').read_text())
marks=json.loads((HERE/'landmarks.json').read_text())
camera=np.array(marks['camera']);keys=marks['keys'];ids=marks['vertex_ids']
verts=np.c_[np.array(src['vertices']),np.ones(len(src['vertices']))]
names=[b['name'] for b in src['bones']]
rests=np.array([b['matrix'] for b in src['bones']]);inverses=np.linalg.inv(rests)
parents=[names.index(b['parent']) if b['parent'] else -1 for b in src['bones']]
locals=np.array([inverses[p]@rests[i] if p>=0 else rests[i] for i,p in enumerate(parents)])
weights=np.zeros((len(verts),len(names)))
for i,gs in enumerate(src['weights']):
 for n,w in gs:
  if n in names:weights[i,names.index(n)]=w
pv=verts[ids];pw=weights[ids]
az,el,scale,cx,cy=camera
forward=np.array([np.sin(az)*np.cos(el),np.cos(az)*np.cos(el),np.sin(el)])
right=np.cross([0,0,1],forward);right/=np.linalg.norm(right);up=np.cross(forward,right)
axes=np.array([right,up]);center=np.array([0,0,1.55]);eye=center+forward*5.5

def project(points):
 d=points[...,:3]-center;depth=d@forward
 return (d@axes.T)*[960/scale,-960/scale]*5.5/(5.5-depth[...,None])+[cx,cy]

def on_floor(pixel,z):
 q=(np.array(pixel)-[cx,cy])*[scale/960,-scale/960]
 ray=-forward*5.5+right*q[0]+up*q[1]
 return eye+ray*((z-eye[2])/ray[2])

def fk(x):
 basis=np.tile(np.eye(4),(len(names),1,1))
 basis[1:,:3,:3]=Rotation.from_rotvec(x[3:].reshape(-1,3)).as_matrix()
 # Translation is authored in world metres, rotations remain bone-local.
 basis[1,:3,3]=rests[1,:3,:3].T@x[:3]
 pose=[]
 for i,p in enumerate(parents):pose.append((pose[p] if p>=0 else np.eye(4))@locals[i]@basis[i])
 return np.array(pose)

def deform(x,v=pv,w=pw):return np.einsum('vb,bij,vj->vi',w,fk(x)@inverses,v)[:,:3]

base={k:v for k,v in marks['base_targets'].items() if k!='L_shoulder'}
stations={0:base,6:{**base,'nose':(566,378),'brow':(576,283),'waist':(491,427)},
9:{**base,'nose':(568,389),'brow':(578,294),'horn_R':(514,52),'horn_L':(618,51),'waist':(491,440),'R_wrist':(269,576),'L_wrist':(716,570)},
12:dict(nose=(568,444),brow=(573,343),horn_R=(506,105),horn_L=(615,100),R_shoulder=(398,359),R_elbow=(297,531),R_wrist=(266,690),R_tip=(226,860),R_inner=(293,848),L_elbow=(685,510),L_wrist=(746,663),L_tip=(780,826),waist=(489,485),R_knee=(443,610),R_ankle=(343,790),R_toe=(323,908),L_knee=(590,596),L_ankle=(633,730),L_toe=(691,808)),
15:dict(nose=(558,571),brow=(571,471),horn_R=(482,256),horn_L=(586,247),R_shoulder=(394,493),R_elbow=(288,665),R_wrist=(265,792),R_tip=(224,901),R_inner=(399,853),L_elbow=(668,665),L_wrist=(726,762),L_tip=(858,837),waist=(475,584),R_knee=(416,640),R_ankle=(312,746),R_toe=(305,805),L_knee=(583,648),L_ankle=(605,739),L_toe=(637,783)),
18:dict(nose=(518,688),brow=(528,593),horn_R=(449,371),horn_L=(550,363),R_shoulder=(355,546),R_elbow=(234,665),R_wrist=(250,796),R_tip=(223,898),R_inner=(399,853),L_elbow=(647,665),L_wrist=(716,760),L_tip=(858,837),waist=(440,631),R_knee=(338,650),R_ankle=(166,751),R_toe=(143,813),L_knee=(549,658),L_ankle=(566,720),L_toe=(613,775)),
24:dict(nose=(488,725),brow=(497,626),horn_R=(431,401),horn_L=(528,395),R_shoulder=(350,575),R_elbow=(229,688),R_wrist=(250,796),R_tip=(223,898),R_inner=(399,853),L_elbow=(644,666),L_wrist=(716,760),L_tip=(858,837),waist=(431,647),R_knee=(327,650),L_knee=(540,652)),
30:dict(nose=(490,718),brow=(500,620),horn_R=(435,398),horn_L=(535,390),R_shoulder=(351,568),R_elbow=(231,683),R_wrist=(250,796),R_tip=(223,898),R_inner=(399,853),L_elbow=(645,664),L_wrist=(716,760),L_tip=(858,837),waist=(432,643),R_knee=(328,647),L_knee=(541,650)),
36:dict(nose=(489,710),brow=(499,614),horn_R=(428,394),horn_L=(533,389),R_shoulder=(350,565),R_elbow=(230,681),R_wrist=(250,796),R_tip=(223,898),R_inner=(399,853),L_elbow=(644,662),L_wrist=(716,760),L_tip=(858,837),waist=(432,640),R_knee=(327,645),L_knee=(541,648)),
42:dict(nose=(481,750),brow=(490,651),horn_R=(418,430),horn_L=(520,424),R_shoulder=(342,590),R_elbow=(224,694),R_wrist=(250,796),R_tip=(223,898),R_inner=(399,853),L_elbow=(637,675),L_wrist=(716,760),L_tip=(858,837),waist=(424,660),R_knee=(320,650),L_knee=(536,657)),
44:dict(nose=(484,723),brow=(494,625),horn_R=(422,412),horn_L=(523,408),R_shoulder=(345,576),R_elbow=(228,686),R_wrist=(253,783),R_tip=(224,889),R_inner=(394,845),L_elbow=(635,660),L_wrist=(700,756),L_tip=(840,830),waist=(427,651),R_knee=(322,649),L_knee=(535,652)),
45:dict(nose=(490,685),brow=(496,582),horn_R=(427,388),horn_L=(529,394),R_shoulder=(350,542),R_elbow=(235,660),R_wrist=(259,754),R_tip=(221,845),R_inner=(368,819),L_elbow=(631,644),L_wrist=(660,728),L_tip=(780,801),waist=(431,620),R_knee=(324,629),L_knee=(533,633)),
48:dict(nose=(520,337),brow=(526,231),horn_R=(445,74),horn_L=(539,81),R_shoulder=(382,216),R_elbow=(255,243),R_wrist=(264,305),R_tip=(282,454),R_inner=(327,415),L_elbow=(648,339),L_wrist=(666,439),L_tip=(622,580),waist=(431,407),R_knee=(333,491),R_ankle=(244,623),R_toe=(228,744),L_knee=(472,474),L_ankle=(468,617),L_toe=(456,732)),
51:dict(nose=(532,220),brow=(542,127),R_shoulder=(395,109),R_elbow=(285,111),R_wrist=(287,56),R_tip=(355,7),R_inner=(230,28),L_elbow=(687,146),L_wrist=(778,117),L_tip=(845,217),waist=(456,298),R_knee=(356,434),R_ankle=(285,545),R_toe=(266,631),L_knee=(525,428),L_ankle=(484,537),L_toe=(459,629)),
54:dict(nose=(553,233),brow=(568,139),R_shoulder=(398,121),R_elbow=(329,194),R_wrist=(312,159),R_tip=(286,82),R_inner=(226,179),L_elbow=(744,199),L_wrist=(843,161),L_tip=(932,95),waist=(458,291),R_knee=(366,390),R_ankle=(307,483),R_toe=(293,628),L_knee=(601,322),L_ankle=(568,454),L_toe=(579,613)),
57:dict(nose=(577,262),brow=(592,159),R_shoulder=(430,164),R_elbow=(334,306),R_wrist=(367,374),R_tip=(257,480),R_inner=(462,428),L_elbow=(778,359),L_wrist=(900,411),waist=(511,327),R_knee=(401,411),R_ankle=(327,572),R_toe=(305,698),L_knee=(606,380),L_ankle=(641,591),L_toe=(691,727)),
60:dict(nose=(636,538),brow=(641,430),horn_R=(520,232),horn_L=(621,224),R_shoulder=(453,471),R_elbow=(352,677),R_wrist=(396,846),R_tip=(393,947),R_inner=(560,907),L_elbow=(773,661),L_wrist=(868,806),L_tip=(942,903),waist=(535,584),R_knee=(426,679),R_ankle=(265,789),R_toe=(225,850),L_knee=(650,637),L_ankle=(668,755),L_toe=(710,810)),
63:dict(nose=(638,782),brow=(643,667),horn_R=(540,430),horn_L=(647,417),R_shoulder=(439,596),R_elbow=(321,741),R_wrist=(400,846),R_tip=(393,947),R_inner=(536,890),L_elbow=(770,712),L_wrist=(879,808),L_tip=(942,903),waist=(521,722),R_knee=(422,712),L_knee=(650,719)),
69:dict(nose=(620,797),brow=(629,693),horn_R=(531,451),horn_L=(638,434),R_shoulder=(433,619),R_elbow=(324,749),R_wrist=(400,846),R_tip=(393,947),R_inner=(536,890),L_elbow=(772,714),L_wrist=(879,808),L_tip=(942,903),waist=(515,721),R_knee=(416,713),L_knee=(645,718)),
78:dict(nose=(593,773),brow=(605,678),horn_R=(531,452),horn_L=(645,440),R_shoulder=(414,616),R_elbow=(313,748),R_wrist=(390,837),R_tip=(383,937),R_inner=(526,880),L_elbow=(756,714),L_wrist=(860,798),L_tip=(927,893),waist=(510,704),R_knee=(412,700),L_knee=(635,709)),
87:dict(nose=(569,735),brow=(583,646),horn_R=(535,422),horn_L=(650,412),R_shoulder=(407,581),R_elbow=(304,726),R_wrist=(356,818),R_tip=(352,917),R_inner=(496,864),L_elbow=(721,685),L_wrist=(811,783),L_tip=(910,870),waist=(501,674),R_knee=(402,678),L_knee=(610,679)),
96:dict(nose=(535,690),brow=(566,621),horn_R=(541,377),horn_L=(651,371),R_shoulder=(398,519),R_elbow=(307,682),R_wrist=(337,799),R_tip=(331,898),R_inner=(485,862),L_elbow=(693,659),L_wrist=(769,773),L_tip=(923,842),waist=(484,631),R_knee=(379,652),R_ankle=(230,751),R_toe=(181,814),L_knee=(586,655),L_ankle=(589,722),L_toe=(641,787))}

# Не повторяем ключи в выдержках: масса продолжает смещаться перед толчком.
pitch={0:(0,0,0),6:(1,0,0),9:(2,2,1),12:(6,4,5),15:(15,10,12),18:(24,16,16),24:(25,18,18),30:(24,18,18),36:(24,17,18),42:(27,20,19),44:(24,17,17),45:(20,14,14),48:(4,5,7),51:(3,3,5),54:(7,5,5),57:(10,7,5),60:(16,12,14),63:(27,18,18),69:(29,20,19),78:(29,22,21),87:(27,22,20),96:(24,22,18)}
pitchaxis=np.array([-.70710678,.70710678,0.0])
contact_ids={side:keys.index(side+'_toe') for side in ['R','L']}
floor_set=set()
for n in ['R_hand','L_hand','R_foot','L_foot','R_toe','L_toe']:
 group=np.where(weights[:,names.index(n)]>.65)[0]
 floor_set.update(group[ConvexHull(verts[group,:3]).vertices].tolist())
floor_ix=np.array(sorted(floor_set));fv=verts[floor_ix];fw=weights[floor_ix]
fv=verts[floor_ix];fw=weights[floor_ix]
hand_rows={side:np.where(fw[:,names.index(side+'_hand')]>.85)[0] for side in ['R','L']}
hand_pitch={0:0,6:0,9:-5,12:-30,15:-72,18:-85,24:-85,30:-85,36:-85,42:-85,44:-76,45:-60,60:-75,63:-85,69:-85,78:-85,87:-82,96:-77}
# После поворота ладони длинный наружный коготь оказывается справа в изображении.
# Ранее внутренний короткий коготь ошибочно сопоставлялся с противоположным краем.
for f in hand_pitch:
 if f>=15:
  stations[f].pop('R_inner',None)
  if 'R_tip' in stations[f]:stations[f]['R_tip']=(stations[f]['R_tip'][0]+87,stations[f]['R_tip'][1]-7)
for f,a,b in [(16,15,18),(17,15,18),(61,60,63),(62,60,63)]:
 t=(f-a)/(b-a);common=stations[a].keys()&stations[b].keys()
 stations[f]={k:tuple(np.array(stations[a][k])*(1-t)+np.array(stations[b][k])*t) for k in common}
 pitch[f]=tuple(np.array(pitch[a])*(1-t)+np.array(pitch[b])*t)
 hand_pitch[f]=hand_pitch[a]*(1-t)+hand_pitch[b]*t
stations[47]=dict(nose=(505,418),brow=(514,324),horn_R=(449,156),horn_L=(528,161),R_shoulder=(360,325),R_elbow=(257,337),R_wrist=(258,491),R_tip=(225,624),L_elbow=(632,456),L_wrist=(675,596),L_tip=(620,768),waist=(417,475),R_knee=(333,542),R_ankle=(244,651),R_toe=(226,743),L_knee=(487,563),L_ankle=(474,664),L_toe=(470,734))
pitch[47]=(8,7,8)
stations[59]=dict(nose=(618,407),brow=(620,299),horn_R=(512,115),horn_L=(617,105),R_shoulder=(450,370),R_elbow=(357,574),R_wrist=(408,748),R_tip=(490,874),L_elbow=(779,575),L_wrist=(872,763),L_tip=(945,835),waist=(518,489),R_knee=(426,625),R_ankle=(266,786),R_toe=(225,864),L_knee=(622,554),L_ankle=(668,719),L_toe=(710,816))
pitch[59]=(11,9,11);hand_pitch[59]=-74

def solve():
 arg=argparse.ArgumentParser();arg.add_argument('--frames',default='');arg.add_argument('--reuse',action='store_true');args=arg.parse_args()
 only={int(f) for f in args.frames.split(',')} if args.frames else None
 saved=json.loads((HERE/'pose_solutions.json').read_text())['frames'] if (HERE/'pose_solutions.json').exists() else {}
 x=np.zeros(3+(len(names)-1)*3);previous=x.copy();solutions={}
 prior=np.array([6 if n=='pelvis' else 5 if n in ['spine_01','spine_02'] else 4 if n in ['neck','head'] else 3 if 'clavicle' in n else 1.5 for n in names[1:]])
 limits=np.r_[np.repeat(2.0,3),np.repeat([1.1 if 'clavicle' in n else 1.45 if n in ['head','neck'] else 2.95 for n in names[1:]],3)]
 for frame,target in sorted(stations.items()):
  if only and frame not in only:
   if str(frame) in saved:solutions[frame]=saved[str(frame)];x=np.array(solutions[frame]['pose']);previous=x.copy()
   continue
  if args.reuse and str(frame) in saved and (frame==0 or only):x=np.array(saved[str(frame)]['pose'])
  ix=[keys.index(k) for k in target];xy=np.array(list(target.values()))
  guide=np.zeros_like(x)
  for n,angle in zip(['pelvis','spine_01','spine_02'],pitch[frame]):
   i=names.index(n);guide[3+(i-1)*3:3+i*3]=rests[i,:3,:3].T@(pitchaxis*np.deg2rad(angle))
  total=sum(pitch[frame])
  for n,fac in [('neck',-.38),('head',-.38)]:
   i=names.index(n);guide[3+(i-1)*3:3+i*3]=rests[i,:3,:3].T@(pitchaxis*np.deg2rad(total*fac))
  x[3:18]=guide[3:18]
  foot_targets=[]
  for side,ki in contact_ids.items():
   if frame<=12:point=verts[ids[ki],:3]
   elif 18<=frame<=45:point=on_floor(stations[18][side+'_toe'],verts[ids[ki],2])
   elif 60<=frame<=69:point=on_floor(stations[60][side+'_toe'],verts[ids[ki],2])
   elif frame>=78:
    t=(frame-69)/27;pixel=np.array(stations[60][side+'_toe'])*(1-t)+np.array(stations[96][side+'_toe'])*t
    point=on_floor(pixel,verts[ids[ki],2])
   elif frame in (15,16,17):point=on_floor(target[side+'_toe'],verts[ids[ki],2])
   else:continue
   foot_targets.append((ki,point))
  def error(v):
   transforms=fk(v);mats=transforms@inverses
   p=np.einsum('vb,bij,vj->vi',pw,mats,pv)[:,:3]
   screen=((project(p[ix])-xy)/8).ravel()
   reg=((v[3:]-guide[3:]).reshape(-1,3)*prior[:,None]).ravel()
   contact=np.r_[tuple(((p[i]-pt)/.035) for i,pt in foot_targets)].ravel() if foot_targets else np.array([])
   floor=np.einsum('vb,bj,vj->v',fw,mats[:,2,:],fv)
   ground=np.minimum(floor-.005,0)*250
   feetrot=[]
   if foot_targets:
    for side in ['R','L']:
     b=names.index(side+'_foot');feetrot.extend(((transforms[b,:3,:3]-rests[b,:3,:3])*3).ravel())
   handrot=[]
   if frame in hand_pitch:
    for side in ['R','L']:
     bi=names.index(side+'_hand')
     # Ладони исходной модели развёрнуты по-разному; общий поворот их выворачивает.
     end=(Rotation.from_euler('z',90,degrees=True)*Rotation.from_euler('x',-90,degrees=True)) if side=='R' else Rotation.from_euler('x',85,degrees=True)
     hand_turn=Rotation.from_rotvec(end.as_rotvec()*(-hand_pitch[frame]/85)).as_matrix()
     handrot.extend(((transforms[bi,:3,:3]-hand_turn@rests[bi,:3,:3])*28).ravel())
     if 15<=frame<=42 or frame>=60:handrot.append((np.min(floor[hand_rows[side]])-.008)*45)
   # Одноракурсный референс не должен вызывать скачки корпуса в глубину.
   depth_target=np.interp(frame,[0,42,48,60,96],[-.08,-.25,-.2,.20,.10])
   depth=(np.dot(v[:2],forward[:2])-depth_target)*24
   smooth=7.0 if frame in [6,9,24,30,36,42,44,69,78,87,96] else 2.0
   return np.r_[screen,reg,(v-previous)*smooth,contact,ground,feetrot,handrot,depth]
  best=least_squares(error,np.clip(x,-limits+1e-5,limits-1e-5),bounds=(-limits,limits),max_nfev=160,ftol=2e-5,xtol=2e-5)
  x=best.x;previous=x.copy();p=deform(x);px=project(p)
  solutions[frame]={'pose':x.tolist(),'rms_px':float(np.sqrt(np.mean((px[ix]-xy)**2))),'projected':dict(zip(keys,px.tolist()))}
  print('frame',frame,'rms',round(solutions[frame]['rms_px'],1),'pelvis',np.round(x[:3],2),flush=True)
  (HERE/'pose_solutions.json').write_text(json.dumps({'names':names,'camera':camera.tolist(),'keys':keys,'vertex_ids':ids,'translation_space':'WORLD','frames':solutions},indent=2))
 (HERE/'reference_stations.json').write_text(json.dumps(stations,indent=2))

if __name__=='__main__':solve()
