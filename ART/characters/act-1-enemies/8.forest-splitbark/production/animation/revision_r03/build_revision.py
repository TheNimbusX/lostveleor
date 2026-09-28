"""Isolated Blender revision: split real shell surfaces, preserve skin/UV, rebuild Bite and Death.

Run with the original production/rig/ForestSplitter_Rig.blend in background Blender.
Never saves over the original. Outputs editable rig, deform-only package, FBX, review and report here.
"""
import copy
import json
import math
import sys
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ANIM = HERE.parent
PROD = ANIM.parent
sys.path.insert(0, str(ANIM))
import bake_lib as bl
import splitter_pose as sp
import takes_bite as old_bite
import takes_react as old_react
from curves import track
from takes import ORDER, TAKES

assert Path(bpy.data.filepath).resolve() == (PROD/'rig/ForestSplitter_Rig.blend').resolve()
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.render.fps = 30
arm = bpy.data.objects[sp.ARM]
original = bpy.data.objects[sp.MESH]
labels = np.load(PROD/'rig/work/tri_labels.npy').astype(int)
assert len(labels) == len(original.data.polygons)
cap_uv = tuple(json.loads((PROD/'debris/shells_report.json').read_text(encoding='utf-8'))['cap_uv'])
collection = bpy.data.collections.new('COL_ForestSplitter_R03')
scene.collection.children.link(collection)
report = {'revision': 'r03', 'fps': 30, 'release_frame': 12, 'release_ticks': 12,
          'bite_contact_frame': 18, 'bite_recovery_frames': 12,
          'source': str(PROD/'rig/ForestSplitter_Rig.blend'), 'parts': {},
          'reference': ['production/references/death.mp4','production/references/bite.mp4',
                        'production/vfx_target_frames_2026-09-26/1-burst-leap.png'],
          'approach': 'Existing 24k shell segmentation becomes three independent closed skinned meshes. Original outer UVs and rig retained.'}

def cap_boundaries(bm):
    caps=bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0)['faces']
    uv=bm.loops.layers.uv.active
    for face in caps:
        face.smooth=True
        for e in face.edges:e.smooth=True
        for l in face.loops:l[uv].uv=(cap_uv[0]+l.vert.co.y*.035,cap_uv[1]+l.vert.co.z*.035)
    # Constrained triangulation avoids a central fan over the concave shell cross-section.
    result=bmesh.ops.triangulate(bm,faces=list(caps),quad_method='BEAUTY',ngon_method='BEAUTY')
    return len(result['faces'])

parts=[]
for label,name in [(0,'SM_ForestSplitter_LOD0'),(1,'SM_ForestSplitter_ShellL'),(2,'SM_ForestSplitter_ShellR')]:
    obj=original.copy(); obj.data=original.data.copy(); collection.objects.link(obj)
    bm=bmesh.new(); bm.from_mesh(obj.data); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if labels[f.index]!=label],context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm,geom=loose,context='VERTS')
    # Source rig contains hundreds of sub-3 mm duplicate seams/T junctions.
    # Weld that numerical seam noise before making independent watertight parts.
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.003)
    bmesh.ops.dissolve_degenerate(bm,dist=0.00001,edges=list(bm.edges))
    for _ in range(12):
        overlaps={min(e.link_faces,key=lambda f:f.calc_area()) for e in bm.edges if len(e.link_faces)>2}
        if not overlaps:break
        bmesh.ops.delete(bm,geom=list(overlaps),context='FACES')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    # Segmentation boundaries contain single-face teeth and tiny disconnected islands.
    # Remove those before closing, as in the existing approved debris extraction.
    remaining=set(bm.faces)
    while remaining:
        seed=remaining.pop();group=[seed];stack=[seed]
        while stack:
            face=stack.pop()
            for e in face.edges:
                for other in e.link_faces:
                    if other in remaining:remaining.remove(other);group.append(other);stack.append(other)
        if len(group)<60:bmesh.ops.delete(bm,geom=group,context='FACES')
    for _ in range(5):
        ears=[f for f in bm.faces if sum(e.is_boundary for e in f.edges)>=2]
        if not ears:break
        bmesh.ops.delete(bm,geom=ears,context='FACES')
    cap_count=cap_boundaries(bm)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.normal_update()
    boundary=sum(e.is_boundary for e in bm.edges)
    bm.to_mesh(obj.data); bm.free()
    if obj.data.has_custom_normals:
        obj.data.normals_split_custom_set([(0.0,0.0,0.0)]*len(obj.data.loops))
    if label:
        obj.vertex_groups.clear()
        obj.vertex_groups.new(name='shell_L' if label==1 else 'shell_R').add(list(range(len(obj.data.vertices))),1.0,'REPLACE')
    else:
        # The body's seam must stop following the detached shell bones.
        shell_groups={g.index for g in obj.vertex_groups if g.name in ('shell_L','shell_R')}
        spine=obj.vertex_groups['spine'].index
        for v in obj.data.vertices:
            weights={g.group:g.weight for g in v.groups}
            moved=sum(weights.pop(g,0) for g in shell_groups)
            if moved: weights[spine]=weights.get(spine,0)+moved
            for g in list(v.groups): obj.vertex_groups[g.group].remove([v.index])
            total=sum(weights.values())
            for g,w in weights.items():
                if w>0: obj.vertex_groups[g].add([v.index],w/total,'REPLACE')
    obj['production_part'] = 'body' if not label else 'detachable_shell'
    obj['source_label'] = label
    obj.name=name+'_r03'
    parts.append(obj)
    report['parts'][name]={'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),
                           'cap_faces':cap_count,'open_edges':boundary,'material':obj.data.materials[0].name}
bpy.data.objects.remove(original,do_unlink=True)
for obj in parts: obj.name=obj.name.removesuffix('_r03')
report['triangles']=sum(p['triangles'] for p in report['parts'].values())
report['triangles_before_cap_budget_pass']=report['triangles']
if report['triangles']>25000:
    # Reserve the closing faces inside the existing 25k budget. Only 2.2% reduction,
    # retaining original UV and deform layers rather than replacing the outer surface.
    for obj in parts:
        bpy.context.view_layer.objects.active=obj
        mod=obj.modifiers.new('CapBudget','DECIMATE');mod.ratio=min(0.978,24950/report['triangles']);mod.use_collapse_triangulate=True
        bpy.ops.object.modifier_move_to_index(modifier=mod.name,index=0)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        report['parts'][obj.name]['triangles']=sum(len(p.vertices)-2 for p in obj.data.polygons)
        report['parts'][obj.name]['vertices']=len(obj.data.vertices)
    report['triangles']=sum(p['triangles'] for p in report['parts'].values())
assert report['triangles'] <= 25000, report['triangles']

death_l=track([(0,2.5),(1,-3.0,'snap'),(3,16.0,'out'),(4,13.0),(6,24.0),(9,40.0,'in'),(12,62.0,'out')])
death_r=track([(0,2.5),(2,-3.0,'snap'),(4,14.0,'out'),(5,11.5),(7,25.0),(10,42.0,'in'),(12,58.0,'out')])
bite_shell=track([(0,2.5),(4,9.0),(10,20.0,'out'),(14,24.0),(16,24.0),(18,-4.0,'snap'),(20,-2.0),(23,4.0),(30,2.5)])
def death(t):
    p=copy.deepcopy(old_react.death(t));p['shell_L']['open']=death_l(t);p['shell_R']['open']=death_r(t)
    return p
def bite(t):
    p=copy.deepcopy(old_bite.bite(t))
    load=max(0,min(t/10,1))*max(0,min((18-t)/3,1))
    x,y,z=p['body']['loc'];p['body']['loc']=(x,y+0.025*load,z-0.025*load)
    pitch,roll,yaw=p['body']['rot'];p['body']['rot']=(pitch-2.0*load,roll,yaw)
    for side in ('L','R'):p['shell_'+side]['open']=bite_shell(t)+(0.6*math.sin(t*2.6)*(1 if side=='L' else -1) if 10<=t<=16 else 0)
    return p

names=bl.deform_names(arm)
if arm.animation_data:
    arm.animation_data.action=None
    for tr in list(arm.animation_data.nla_tracks):arm.animation_data.nla_tracks.remove(tr)
samples={};controls={}
for take,fn,n in [('ForestSplitter_Bite',bite,30),('ForestSplitter_Death',death,12)]:
    snaps=[];frames=[];bases=[]
    for f in range(n+1):sp.apply(arm,fn(f));snaps.append(bl.control_values(arm))
    for i in range(n*4+1):
        t=i/4;sp.apply(arm,fn(t));frames.append(t);bases.append(bl.basis_from_pose(arm,bl.sample(arm,names)))
    samples[take]=(frames,bases);controls[take]=snaps
    print('REBUILT',take,flush=True)

for act in list(bpy.data.actions):bpy.data.actions.remove(act)
with bpy.data.libraries.load(str(ANIM/'ForestSplitter_Anim_r02.blend'),link=False) as (src,dst):
    dst.actions=[t+'_CTRL' for t in ORDER if t not in samples and t+'_CTRL' in src.actions]
for t,ss in controls.items():bl.write_control_action(arm,t+'_CTRL',ss,False)
for act in bpy.data.actions:act.use_fake_user=True
arm.animation_data.action=bpy.data.actions['ForestSplitter_Death_CTRL']
scene.frame_set(0)
for img in bpy.data.images:
    if img.source=='FILE' and not img.packed_file:
        img.filepath=bpy.path.abspath(img.filepath)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'ForestSplitter_Editable_r03.blend'))

arm.animation_data.action=None
for act in list(bpy.data.actions):bpy.data.actions.remove(act)
sp.reset(arm);bl.strip_controls(arm)
with bpy.data.libraries.load(str(ANIM/'ForestSplitter_Baked_r02.blend'),link=False) as (src,dst):
    dst.actions=[t for t in ORDER if t not in samples]
for take,(frames,bases) in samples.items():bl.write_action(arm,take,frames,bases,False)
for act in bpy.data.actions:act.use_fake_user=True
guide=bpy.data.objects.new('FacingGuide',None);collection.objects.link(guide);guide.parent=arm;guide.location=(0,-1,0)
arm.animation_data.action=None
for take in ORDER:
    action=bpy.data.actions[take]
    tr=arm.animation_data.nla_tracks.new();tr.name=take
    strip=tr.strips.new(take,0,action);strip.name=take;strip.frame_start=0;strip.frame_end=TAKES[take]['frames']
    strip.extrapolation='NOTHING';strip.blend_type='REPLACE'
    if len(action.slots):strip.action_slot=action.slots[0]
scene.frame_start=0;scene.frame_end=60;scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'ForestSplitter_Package_r03.blend'))
bpy.ops.object.select_all(action='DESELECT')
for ob in [arm,guide]+parts:ob.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(HERE/'ForestSplitter.fbx'),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},
    add_leaf_bones=False,use_armature_deform_only=True,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
    bake_anim=True,bake_anim_use_nla_strips=True,bake_anim_use_all_actions=False,bake_anim_force_startend_keying=True,
    bake_anim_step=0.25,bake_anim_simplify_factor=0.0,mesh_smooth_type='FACE',use_mesh_modifiers=True,path_mode='ABSOLUTE')

# Debris is cut from the actual separated shell at the release pose; no substitute primitives.
for tr in arm.animation_data.nla_tracks:tr.mute=True
act=bpy.data.actions['ForestSplitter_Death'];arm.animation_data.action=act
if len(act.slots):arm.animation_data.action_slot=act.slots[0]
scene.frame_set(12)
deps=bpy.context.evaluated_depsgraph_get()
for obj,filename in zip(parts[1:],['ShellHalfL','ShellHalfR']):
    mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(deps),preserve_all_data_layers=True,depsgraph=deps)
    debris=bpy.data.objects.new(filename,mesh);collection.objects.link(debris)
    bpy.ops.object.select_all(action='DESELECT');debris.select_set(True);bpy.context.view_layer.objects.active=debris
    bpy.ops.export_scene.fbx(filepath=str(HERE/(filename+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,mesh_smooth_type='FACE',use_mesh_modifiers=True,bake_anim=False,path_mode='ABSOLUTE',add_leaf_bones=False)
    xyz=np.array([v.co[:] for v in mesh.vertices]);lo,hi=xyz.min(0),xyz.max(0)
    report['parts'][obj.name]['death_bounds_unity']={'min':[-float(hi[0]),float(lo[2]),-float(hi[1])],
        'max':[-float(lo[0]),float(hi[2]),-float(lo[1])]}
    bpy.data.objects.remove(debris,do_unlink=True)
report['bones']=len(arm.data.bones)
report['takes']=ORDER
report['artistic_review']='pending owner; inspect game/side release and windup renders'
(HERE/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('R03_DONE',report['triangles'],flush=True)
