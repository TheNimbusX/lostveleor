"""Смерть: отдельная постановка по видео, с опорой кистей и коленей."""
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'reference_match_leap'))
from fit_leap import *
OUT=ROOT/'death_work';OUT.mkdir(exist_ok=True)
base={k:v for k,v in marks['base_targets'].items() if k!='L_shoulder'}
stations={0:base,12:{**base,'nose':(565,378),'waist':(491,429)},18:{**base,'nose':(567,391),'brow':(576,298),'waist':(490,440)},
24:dict(nose=(578,453),brow=(590,351),horn_R=(515,115),horn_L=(625,110),R_shoulder=(405,327),R_elbow=(306,473),R_wrist=(277,598),R_tip=(230,817),L_elbow=(691,489),L_wrist=(729,631),L_tip=(813,818),waist=(484,484),R_knee=(464,632),R_ankle=(349,782),R_toe=(324,894),L_knee=(619,595),L_ankle=(629,714),L_toe=(665,793)),
30:dict(nose=(584,553),brow=(596,457),horn_R=(513,220),horn_L=(625,222),R_shoulder=(407,418),R_elbow=(326,593),R_wrist=(310,724),R_tip=(309,941),L_elbow=(675,580),L_wrist=(726,739),L_tip=(871,885),waist=(463,547),R_knee=(454,675),R_ankle=(365,741),R_toe=(339,818),L_knee=(616,643),L_ankle=(610,725),L_toe=(655,779)),
36:dict(nose=(585,652),brow=(596,555),horn_R=(523,343),horn_L=(630,333),R_shoulder=(417,499),R_elbow=(313,657),R_wrist=(333,803),R_tip=(365,948),L_elbow=(679,639),L_wrist=(709,776),L_tip=(881,870),waist=(454,608),R_knee=(415,757),R_ankle=(285,724),R_toe=(192,792),L_knee=(600,717),L_ankle=(606,674),L_toe=(652,729)),
48:dict(nose=(579,707),brow=(590,609),horn_R=(522,348),horn_L=(624,332),R_shoulder=(413,521),R_elbow=(309,660),R_wrist=(331,800),R_tip=(365,948),L_elbow=(677,645),L_wrist=(709,776),L_tip=(881,870),waist=(449,646),R_knee=(402,783),R_ankle=(265,726),R_toe=(191,795),L_knee=(587,751),L_ankle=(604,673),L_toe=(653,729)),
60:dict(nose=(575,774),brow=(586,679),horn_R=(515,424),horn_L=(621,429),R_shoulder=(405,607),R_elbow=(292,662),R_wrist=(329,803),R_tip=(365,948),L_elbow=(666,672),L_wrist=(709,777),L_tip=(881,870),waist=(438,674),R_knee=(402,783),R_ankle=(257,714),R_toe=(161,770),L_knee=(584,748)),
72:dict(nose=(572,814),brow=(584,720),horn_R=(510,457),horn_L=(621,458),R_shoulder=(401,638),R_elbow=(284,670),R_wrist=(329,803),R_tip=(365,948),L_elbow=(665,686),L_wrist=(709,777),L_tip=(881,870),waist=(438,679),R_knee=(402,783),R_ankle=(253,714),R_toe=(154,757),L_knee=(582,749)),
84:dict(nose=(573,826),brow=(584,733),horn_R=(513,474),horn_L=(622,473),R_shoulder=(400,645),R_elbow=(282,672),R_wrist=(329,803),R_tip=(365,948),L_elbow=(664,690),L_wrist=(709,777),L_tip=(881,870),waist=(438,680),R_knee=(402,783),R_ankle=(253,714),R_toe=(154,757),L_knee=(582,749))}
stations[96]=stations[84].copy()
pitch={0:(0,0,0),12:(0,0,0),18:(2,1,0),24:(7,4,3),30:(18,9,8),36:(30,13,12),48:(35,18,18),60:(43,22,23),72:(46,23,26),84:(46,23,27),96:(46,23,27)}
x=np.zeros(66);prev=x.copy();solutions={}
for f,target in stations.items():
 ix=[keys.index(k) for k in target];xy=np.array(list(target.values()));guide=np.zeros(66)
 for n,ang in zip(['pelvis','spine_01','spine_02'],pitch[f]):
  i=names.index(n);guide[3+(i-1)*3:3+i*3]=rests[i,:3,:3].T@(pitchaxis*np.deg2rad(ang))
 for n,fac in [('neck',-.2),('head',-.12)]:
  i=names.index(n);guide[3+(i-1)*3:3+i*3]=rests[i,:3,:3].T@(pitchaxis*np.deg2rad(sum(pitch[f])*fac))
 x[3:18]=guide[3:18]
 prior=np.array([7 if n in ['pelvis','spine_01','spine_02'] else 4 if n in ['neck','head'] else 2.3 for n in names[1:]])
 def error(v):
  ts=fk(v);ma=ts@inverses;p=deform(v);z=np.einsum('vb,bj,vj->v',fw,ma[:,2,:],fv)
  e=[((project(p[ix])-xy)/7).ravel(),((v[3:]-guide[3:]).reshape(-1,3)*prior[:,None]).ravel(),(v-prev)*3,np.minimum(z-.006,0)*280]
  for side in ['R','L']:
   if f<=24:
    ki=keys.index(side+'_toe');e.append((p[ki]-verts[ids[ki],:3])/.035)
   if f>=30:
    bi=names.index(side+'_hand');end=(Rotation.from_euler('z',90,degrees=True)*Rotation.from_euler('x',-90,degrees=True)) if side=='R' else Rotation.from_euler('x',85,degrees=True)
    a=np.interp(f,[24,30,36],[0,.8,1]);turn=Rotation.from_rotvec(end.as_rotvec()*a).as_matrix()
    e.append(((ts[bi,:3,:3]-turn@rests[bi,:3,:3])*25).ravel());e.append(np.array([(np.min(z[hand_rows[side]])-.01)*50]))
   if f>=36:
    ki=keys.index(side+'_wrist');pt=on_floor(stations[36][side+'_wrist'],.22);e.append((p[ki]-pt)/.07)
   if f>=48:
    ki=keys.index(side+'_knee');e.append(np.array([(p[ki,2]-.14)*25]))
  e.append(np.array([(np.dot(v[:2],forward[:2])-np.interp(f,[0,36,96],[-.08,-.02,-.06]))*20]))
  return np.concatenate(e)
 opt=least_squares(error,x,max_nfev=140,ftol=3e-5,xtol=3e-5);x=opt.x;prev=x.copy()
 solutions[f]={'pose':x.tolist(),'rms_px':float(np.sqrt(np.mean((project(deform(x))[ix]-xy)**2)))}
 print(f,round(solutions[f]['rms_px'],2),flush=True)
 (OUT/'pose_solutions.json').write_text(json.dumps({'names':names,'camera':camera.tolist(),'frames':solutions},indent=2))
(OUT/'reference_stations.json').write_text(json.dumps(stations,indent=2))
