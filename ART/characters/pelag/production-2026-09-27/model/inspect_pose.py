import bpy,json
from mathutils import Matrix
from pathlib import Path
OUT=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/production-2026-09-27/model')
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
def info(r):return {n:{'rest_head':list(r.matrix_world@r.data.bones[n].head_local),'pose_head':list(r.matrix_world@r.pose.bones[n].head),'basis':[list(row) for row in r.pose.bones[n].matrix_basis]} for n in ['mixamorig:RightArm','mixamorig:RightForeArm','mixamorig:RightHand','mixamorig:Hips']}
report={'target':info(rig)}
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=r'C:/Users/d.grab/Desktop/the-game/razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_CombatIdle.fbx',use_anim=True)
src=next(o for o in set(bpy.data.objects)-before if o.type=='ARMATURE')
bpy.context.scene.frame_set(1);bpy.context.view_layer.update()
report['source']=info(src);report['action']=src.animation_data.action.name;report['frames']=list(src.animation_data.action.frame_range)
(OUT/'pose_audit.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
