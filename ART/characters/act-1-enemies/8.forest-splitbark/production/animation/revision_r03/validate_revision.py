"""Independent background FBX roundtrip: action set, rig, UVs, skin, mesh budgets and release poses."""
import json
from pathlib import Path
import bpy
import bmesh
HERE=Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(HERE/'ForestSplitter.fbx'),use_anim=True)
meshes=[o for o in bpy.data.objects if o.type=='MESH']
arms=[o for o in bpy.data.objects if o.type=='ARMATURE']
assert len(meshes)==3 and len(arms)==1,(len(meshes),len(arms))
arm=arms[0]
actions={a.name.split('|')[-1]:a for a in bpy.data.actions}
expected={'ForestSplitter_'+n for n in ['Idle','Walk','Bite','Hit','Death','Pop','RollCurl','RollLoop','RollUncurl','RollDizzy']}
assert set(actions)==expected,set(actions)
report={'parts':{},'takes':{},'bones':len(arm.data.bones),'root_motion':False}
for o in meshes:
    bm=bmesh.new();bm.from_mesh(o.data)
    report['parts'][o.name]={'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
        'open_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
        'uv_layers':len(o.data.uv_layers),'unweighted':sum(not v.groups for v in o.data.vertices),
        'material_names':[m.name for m in o.data.materials]}
    bm.free()
    assert report['parts'][o.name]['uv_layers']>0
    assert report['parts'][o.name]['unweighted']==0
    assert any(m.type=='ARMATURE' and m.object==arm for m in o.modifiers)
report['triangles']=sum(v['triangles'] for v in report['parts'].values())
assert report['triangles']<=25000
for name,a in actions.items():
    lo,hi=a.frame_range
    report['takes'][name]={'frames':[float(lo),float(hi)],'duration_seconds':round((hi-lo)/30,4)}
assert abs(report['takes']['ForestSplitter_Death']['duration_seconds']-.4)<.0001
assert abs(report['takes']['ForestSplitter_Bite']['duration_seconds']-1.0)<.0001
ad=arm.animation_data or arm.animation_data_create()
for tr in ad.nla_tracks:tr.mute=True
for name in ['ForestSplitter_Death','ForestSplitter_Bite']:
    a=actions[name];ad.action=a
    if a.slots:ad.action_slot=a.slots[0]
    for frame in [a.frame_range[0],a.frame_range[1]]:
        bpy.context.scene.frame_set(int(frame));bpy.context.view_layer.update()
        for obj in meshes:
            ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh()
            assert all(all(abs(c)<100 for c in v.co) for v in me.vertices),obj.name
            ev.to_mesh_clear()
report['pass']=True
(HERE/'roundtrip.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('R03_ROUNDTRIP_PASS',report['triangles'],len(arm.data.bones),len(actions),flush=True)
