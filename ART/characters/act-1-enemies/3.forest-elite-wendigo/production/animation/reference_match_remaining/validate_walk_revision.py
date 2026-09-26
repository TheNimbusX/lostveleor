import json,sys,numpy as np
from pathlib import Path
from scipy.spatial.transform import Rotation
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'reference_match_leap'))
from fit_leap import names, rests, parents, fk, deform, verts, weights
out=ROOT/'walk_revision_r04'
x=np.array(json.loads((out/'final_samples.json').read_text()))
records=json.loads((out/'contact_audit.json').read_text())
world=np.array([fk(q) for q in x]);bone_error=0;plant_gap=0
for i,p in enumerate(parents):
    if p>0:
        length=np.linalg.norm(world[:,i,:3,3]-world[:,p,:3,3],axis=1)
        bone_error=max(bone_error,float(abs(length-np.linalg.norm(rests[i,:3,3]-rests[p,:3,3])).max()))
for j,pose in enumerate(x):
    xyz=deform(pose,verts,weights)
    for side in ['L','R']:
        if records[j]['feet'][side]['stance']:
            fi,ti=[names.index(side+'_'+n) for n in ['foot','toe']]
            group=np.where(weights[:,fi]+weights[:,ti]>.8)[0]
            plant_gap=max(plant_gap,float(xyz[group,2].min()))
q=Rotation.from_rotvec(x[:,3:].reshape(-1,3)).as_quat().reshape(len(x),-1,4)
degrees=np.rad2deg(2*np.arccos(np.clip(abs((q[1:]*q[:-1]).sum(axis=2)),0,1)))
frame,bone=np.unravel_index(degrees.argmax(),degrees.shape)
report={'cycle_endpoint_max_error':float(abs(x[0]-x[-1]).max()),'max_bone_length_error_metres':bone_error,
        'min_surface_z':min(a['min_z'] for a in records),'max_planted_sole_z':plant_gap,
        'max_rotation_degrees_per_quarter_frame':float(degrees.max()),'max_rotation_bone':names[bone+1],
        'max_rotation_frame':float(frame/4),'stride_metres':2.4,'runtime_cycle_seconds':.8}
(out/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
assert report['cycle_endpoint_max_error']<1e-6
assert bone_error<1e-5
assert report['min_surface_z']>=-.005
assert plant_gap<.025, 'Planted foot is floating'
assert degrees.max()<5, 'Abrupt joint speed'
