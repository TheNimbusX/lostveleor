import bpy,json
from pathlib import Path
from mathutils import Matrix
BASE=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/production-2026-09-27/model');OUT=BASE/'revision-02'
bpy.ops.wm.open_mainfile(filepath=str(BASE/'Pelag_Model_Candidate.blend'))
r=bpy.data.objects['ARM_Pelag_v6_Original65'];body=bpy.data.objects['SM_Pelag_v6_Unchanged'];source=bpy.data.objects['SOURCE_saber']
u=json.loads((OUT/'unity_mount_samples.json').read_text())['rows'][0]
names=['mixamorig:Hips','mixamorig:Spine2','mixamorig:Head','mixamorig:RightHand','mixamorig:LeftHand','mixamorig:RightFoot']
def mat(m):return [list(row) for row in m]
report={'rig':mat(r.matrix_world),'body':mat(body.matrix_world),'bones':{},'saber':{'meshBounds':[[min(v.co[i] for v in source.data.vertices),max(v.co[i] for v in source.data.vertices)] for i in range(3)]},'unitybones':{k:u['bones'][k] for k in names},'unitybind':{x['name']:x['matrix'] for x in u['body']['bindBones'] if x['name'] in names}}
for k in names:report['bones'][k]={'restWorld':mat(r.matrix_world@r.data.bones[k].matrix_local),'poseWorld':mat(r.matrix_world@r.pose.bones[k].matrix)}
(OUT/'space_audit.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
