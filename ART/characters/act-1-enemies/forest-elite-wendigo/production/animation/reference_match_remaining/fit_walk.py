"""Шаги по видео: перенос таза, противоход плеч и фиксированные опорные стопы."""
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'reference_match_leap'))
from fit_leap import *
OUT=ROOT/'walk_work';OUT.mkdir(exist_ok=True)
stations={
36:dict(nose=(468,405),brow=(474,299),horn_R=(382,57),horn_L=(508,45),R_shoulder=(319,304),R_elbow=(226,457),R_wrist=(180,597),R_tip=(244,754),L_elbow=(603,459),L_wrist=(667,598),L_tip=(603,755),waist=(435,466),R_knee=(420,624),R_ankle=(342,799),R_toe=(332,917),L_knee=(572,576),L_ankle=(637,703),L_toe=(670,812)),
48:dict(nose=(445,352),brow=(453,237),R_shoulder=(274,256),R_elbow=(194,408),R_wrist=(160,576),R_tip=(231,756),L_elbow=(601,367),L_wrist=(679,477),L_tip=(619,644),waist=(427,430),R_knee=(398,617),R_ankle=(341,798),R_toe=(332,917),L_knee=(546,563),L_ankle=(594,749),L_toe=(670,943)),
60:dict(nose=(613,407),brow=(621,282),R_shoulder=(448,265),R_elbow=(333,445),R_wrist=(300,599),R_tip=(366,780),L_elbow=(747,466),L_wrist=(798,607),L_tip=(724,757),waist=(521,465),R_knee=(460,652),R_ankle=(351,784),R_toe=(333,917),L_knee=(625,604),L_ankle=(632,811),L_toe=(670,943)),
72:dict(nose=(631,352),brow=(642,222),R_shoulder=(430,196),R_elbow=(304,352),R_wrist=(224,483),R_tip=(225,664),L_elbow=(797,437),L_wrist=(825,592),L_tip=(739,801),waist=(562,426),R_knee=(440,613),R_ankle=(300,822),R_toe=(232,1050),L_knee=(639,615),L_ankle=(635,811),L_toe=(670,943)),
84:dict(nose=(550,549),brow=(561,419),horn_R=(449,100),horn_L=(587,99),R_shoulder=(343,381),R_elbow=(207,549),R_wrist=(125,706),R_tip=(150,931),L_elbow=(709,515),L_wrist=(792,676),L_tip=(754,875),waist=(500,591),R_knee=(409,683),R_ankle=(281,885),R_toe=(232,1050),L_knee=(657,660),L_ankle=(635,811),L_toe=(670,943))}
# Промежуточные переносы ног задаются отдельно от линейного перемещения корпуса.
for f in [42,54,66,78]:
 a=f-6;b=f+6;common=stations[a].keys()&stations[b].keys();stations[f]={k:tuple((np.array(stations[a][k])+stations[b][k])*.5) for k in common}
stations[42].update(L_knee=(529,520),L_ankle=(601,634),L_toe=(620,737))
stations[66].update(R_knee=(457,616),R_ankle=(343,739),R_toe=(327,850))
sol={};x=np.zeros(66);prev=x.copy()
contact_R0=on_floor((332,917),.045);contact_L1=on_floor((670,943),.045);contact_R1=on_floor((232,1050),.045)
for f,target in sorted(stations.items()):
 ix=[keys.index(k) for k in target];xy=np.array(list(target.values()));guide=np.zeros(66)
 phase=(f-36)/48*2*np.pi
 for n,ang in [('pelvis',5),('spine_01',3),('spine_02',4),('neck',-6),('head',-4)]:
  i=names.index(n);guide[3+(i-1)*3:3+i*3]=rests[i,:3,:3].T@(pitchaxis*np.deg2rad(ang))
 prior=np.array([4 if n in ['pelvis','spine_01','spine_02','neck','head'] else 2 for n in names[1:]])
 contacts=[]
 if f<=54:contacts.append(('R',contact_R0))
 if 48<=f<=78:contacts.append(('L',contact_L1))
 if f>=72:contacts.append(('R',contact_R1))
 def error(v):
  ts=fk(v);ma=ts@inverses;p=deform(v);z=np.einsum('vb,bj,vj->v',fw,ma[:,2,:],fv)
  e=[((project(p[ix])-xy)/9).ravel(),((v[3:]-guide[3:]).reshape(-1,3)*prior[:,None]).ravel(),(v-prev)*2,np.minimum(z-.006,0)*250]
  for side,pt in contacts:
   ki=keys.index(side+'_toe');e.append((p[ki]-pt)/.02);bi=names.index(side+'_foot');e.append(((ts[bi,:3,:3]-rests[bi,:3,:3])*4).ravel())
  for side in ['L','R']:
   bi=names.index(side+'_hand');e.append(((ts[bi,:3,:3]-rests[bi,:3,:3])*2).ravel())
  return np.concatenate(e)
 opt=least_squares(error,x,max_nfev=130,ftol=3e-5,xtol=3e-5);x=opt.x;prev=x.copy()
 sol[f-36]={'pose':x.tolist(),'rms_px':float(np.sqrt(np.mean((project(deform(x))[ix]-xy)**2)))}
 print(f,round(sol[f-36]['rms_px'],1),np.round(x[:3],2),flush=True)
 (OUT/'pose_solutions.json').write_text(json.dumps({'names':names,'camera':camera.tolist(),'source_first_frame':36,'frames':sol},indent=2))
(OUT/'reference_stations.json').write_text(json.dumps(stations,indent=2))
