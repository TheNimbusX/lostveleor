import bpy,json
from pathlib import Path
P=Path('C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/6.forest-stonepoof/production')
report=[]
for f in [P/'Stonehoof_Audit_Idle_r03.blend',P/'Stonehoof_Audit_Run_r03.blend',P/'animation/windup_start/Stonehoof_WindupStart_Baked_r01.blend',P/'animation/brake/Stonehoof_Brake_Baked_r01.blend',P/'animation/collision/Stonehoof_Collision_Baked_r01.blend']:
 bpy.ops.wm.open_mainfile(filepath=str(f));a=next(o for o in bpy.data.objects if o.type=='ARMATURE')
 report.append({'file':str(f),'arm':a.name,'scale':list(a.scale),'matrix':[list(row) for row in a.matrix_world],'bones':len(a.data.bones),'actions':[(x.name,list(x.frame_range)) for x in bpy.data.actions],'tracks':[(t.name,[(s.name,s.action.name) for s in t.strips]) for t in a.animation_data.nla_tracks] if a.animation_data else []})
print('INSPECT',json.dumps(report,default=lambda x:list(x)))
(P/'animation/unity_package/inspect.json').write_text(json.dumps(report,default=lambda x:list(x),indent=2))

