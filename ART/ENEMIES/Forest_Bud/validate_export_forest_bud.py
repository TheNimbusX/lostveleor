import bpy,bmesh,json,math,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(r'C:/Users/d.grab/Desktop/the-game');PROD=ROOT/'ART/ENEMIES/Forest_Bud';OUT=ROOT/'artifacts/forest-bud-production';LIVE=ROOT/'razlom/Assets/Resources/Characters/Forest_Bud'
if not globals().get('FOREST_PRODUCTION_LOADED',False):bpy.ops.wm.open_mainfile(filepath=str(PROD/'ForestBudRanged_Production.blend'))
rig=bpy.data.objects['ARM_ForestBudRanged'];scene=bpy.context.scene;report={'meshes':[],'clips':[]}
meshes=[o for o in bpy.data.objects if o.type=='MESH' and o.parent==rig]
for ob in meshes+[bpy.data.objects['SM_ProjectileFruit']]:
    bm=bmesh.new();bm.from_mesh(ob.data);ob.data.calc_loop_triangles()
    rec={'name':ob.name,'triangles':len(ob.data.loop_triangles),'vertices':len(ob.data.vertices),'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'winding_errors':sum(e.is_manifold and not e.is_contiguous for e in bm.edges),'unweighted':sum(not v.groups for v in ob.data.vertices) if ob.parent else 0,'max_influences':max((len(v.groups) for v in ob.data.vertices),default=0),'weight_error':max((abs(sum(g.weight for g in v.groups)-1) for v in ob.data.vertices),default=0) if ob.parent else 0,'material_slots':[m.name for m in ob.data.materials]}
    report['meshes'].append(rec);bm.free()
report['triangles_character']=sum(x['triangles'] for x in report['meshes'] if x['name']!='SM_ProjectileFruit')
report['bones']=len(rig.data.bones);report['deform_bones']=sum(b.use_deform for b in rig.data.bones)
for track in rig.animation_data.nla_tracks:track.mute=True
body=bpy.data.objects['SM_Body'];feet=['L_Front_Foot','R_Front_Foot','L_Hind_Foot','R_Hind_Foot']
for action in bpy.data.actions:
    if action.name not in ['Idle','Walk','Ranged_Attack','Death','Hit']:continue
    rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
    start,end=[int(x) for x in action.frame_range];rootpositions=[];mins=[];maxs=[];feet_z=[];stance_error=[]
    endpoints=[]
    for frame in range(start,end+1):
        scene.frame_set(frame);bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
        rootpositions.append(list(rig.pose.bones['root'].matrix.translation));feet_z.append([rig.pose.bones[b].matrix.translation.z for b in feet])
        evaluated=body.evaluated_get(dg);me=evaluated.to_mesh();mins.append(min(v.co.z for v in me.vertices));maxs.append(max(v.co.z for v in me.vertices));evaluated.to_mesh_clear()
        if frame in [start,end]:endpoints.append({b.name:b.matrix.copy() for b in rig.pose.bones})
        for f in feet:
            control='CTRL_'+f.replace('_Foot','')
            stance_error.append((rig.pose.bones[f].matrix.translation-rig.pose.bones[control].matrix.translation).length)
    loop=max(abs(endpoints[0][name][i][j]-endpoints[-1][name][i][j]) for name in endpoints[0] for i in range(4) for j in range(4))
    report['clips'].append({'name':action.name,'frames':[start,end],'duration':(end-start)/30,'root_max_drift':max(Vector(p).length for p in rootpositions),'body_min_z':min(mins),'body_max_z':max(maxs),'foot_height_range':[min(min(z) for z in feet_z),max(max(z) for z in feet_z)],'max_foot_ik_error':max(stance_error),'loop_matrix_error':loop,'body_min_z_frames':mins})
report['walk_root_equivalent_speed']=rig['walk_root_equivalent_speed'];report['attack_shots_seconds']=list(rig['attack_shots_seconds']);report['hit_seconds']=rig['hit_seconds']
(OUT/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('FOREST_VALIDATION',json.dumps({k:v for k,v in report.items() if k not in ['meshes','clips']}))
for rec in report['meshes']:
    if rec['boundary_edges'] or rec['nonmanifold_edges'] or rec['winding_errors'] or rec['unweighted'] or rec['max_influences']>4 or rec['weight_error']>.0001:raise RuntimeError('Mesh validation failed: '+str(rec))
if report['triangles_character']>28000:raise RuntimeError('Triangle budget exceeded')
if '--export' not in sys.argv and not globals().get('FOREST_EXPORT',False):print('VALIDATION_ONLY');quit()
for rec in report['clips']:
    if rec['root_max_drift']>.00001:raise RuntimeError('Root drift: '+rec['name'])
    if rec['body_min_z']<-.001:raise RuntimeError('Ground penetration: '+rec['name'])
    # Hit не зациклен, но обязан вернуться в позу покоя: View накладывает его поверх любой фазы.
    if rec['name'] in ['Idle','Walk','Ranged_Attack','Hit'] and rec['loop_matrix_error']>.0001:raise RuntimeError('Loop seam: '+rec['name'])
scene.frame_set(1);rig.animation_data.action=bpy.data.actions['Idle'];rig.animation_data.action_slot=bpy.data.actions['Idle'].slots[0]
rig.data.pose_position='POSE'
bpy.ops.object.select_all(action='DESELECT');rig.hide_set(False);rig.select_set(True);bpy.context.view_layer.objects.active=rig
for ob in meshes:ob.hide_set(False);ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(LIVE/'ForestBudRanged.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_armature_deform_only=True,armature_nodetype='NULL',bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,path_mode='ABSOLUTE',embed_textures=False)
bpy.ops.object.select_all(action='DESELECT');fruit=bpy.data.objects['SM_ProjectileFruit'];fruit.hide_set(False);fruit.hide_render=False;fruit.select_set(True);bpy.context.view_layer.objects.active=fruit
bpy.ops.export_scene.fbx(filepath=str(LIVE/'ProjectileFruit.fbx'),use_selection=True,object_types={'MESH'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=False,path_mode='ABSOLUTE',embed_textures=False)
fruit.hide_render=True;fruit.hide_set(True)
print('FOREST_EXPORT_SUCCESS')
