"""Ракурсы, масштаб и проверка исходных движений на исправленной модели."""
import bpy,json,math,sys
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'production';REVIEW=OUT/'review'/'model_stage'
PROJECT=ROOT.parents[3]
mode=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'turntable'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Stonehoof_ModelCandidate_r03.blend'))
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;cam=scene.camera
arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0']
audit=json.loads((OUT/'model_audit.json').read_text())
scene.render.resolution_x=768;scene.render.resolution_y=768
def aim(point,offset,scale):
    target=Vector(point);cam.location=target+Vector(offset).normalized()*10
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
def render(path):
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
def bounds(objects):
    bpy.context.view_layer.update()
    pts=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
    return Vector(tuple(min(p[i] for p in pts) for i in range(3))),Vector(tuple(max(p[i] for p in pts) for i in range(3)))

if mode=='turntable':
    folder=REVIEW/'turntable_frames';folder.mkdir(exist_ok=True)
    for i in range(96):
        angle=2*math.pi*i/96
        aim((0,0,0.72),(math.sin(angle),-math.cos(angle),0.42),2.65)
        render(folder/f'{i:04d}.png')
elif mode=='comparison':
    before=set(bpy.data.objects)
    base=PROJECT/'razlom/Assets/Resources/Characters/Pelag_v6'
    bpy.ops.import_scene.fbx(filepath=str(base/'Runtime/Pelag_v6_MixamoRig.fbx'),global_scale=100)
    objects=[o for o in bpy.data.objects if o not in before];meshes=[o for o in objects if o.type=='MESH']
    roots=[o for o in objects if o.parent not in objects]
    for obj in roots:obj.scale*=0.5433*1.82
    # Материал из текущего игрового ресурса героя.
    mat=bpy.data.materials.new('MAT_Pelag_Review');mat.use_nodes=True
    bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=0.82
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(base/'Pelag_v6_BaseColor.jpg'));tex.image.pack()
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    for obj in meshes:obj.data.materials.clear();obj.data.materials.append(mat)
    lo,hi=bounds(meshes);height=hi.z-lo.z
    for obj in roots:obj.location+=Vector((1.1-(lo.x+hi.x)/2,-(lo.y+hi.y)/2,-lo.z))
    arm.location.x=-1.0
    # Уменьшаем раскинутые T-pose руки только для сравнения силуэтов.
    hero_arm=next((o for o in objects if o.type=='ARMATURE'),None)
    if hero_arm:
        before_idle=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(PROJECT/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_CombatIdle.fbx'),global_scale=100)
        idle_objects=[o for o in bpy.data.objects if o not in before_idle]
        idle_arm=next(o for o in idle_objects if o.type=='ARMATURE')
        if idle_arm.animation_data and idle_arm.animation_data.action:
            action=idle_arm.animation_data.action
            hero_arm.animation_data_create();hero_arm.animation_data.action=action
            if action.slots:hero_arm.animation_data.action_slot=action.slots[0]
            scene.frame_set(1)
        for obj in idle_objects:obj.hide_render=True
    bpy.context.view_layer.update()
    scene.render.resolution_x=1400;scene.render.resolution_y=900
    aim((0,0,0.88),(0,-1,0.18),4.65);render(REVIEW/'comparison_front.png')
    aim((0,0,0.83),(0.32,-1,0.8),4.65);render(REVIEW/'comparison_game.png')
    (OUT/'comparison_audit.json').write_text(json.dumps({'pelag_source':str(base/'Runtime/Pelag_v6_MixamoRig.fbx'),'pelag_height_m':height,'unity_import_scale':0.5433,'game_scale':1.82,'stonehoof_height_m':audit['dimensions'][2]},indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Stonehoof_ScaleReview_r03.blend'))
elif mode in ('idle','run'):
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(OUT/'sources'/f'{mode}.fbx'))
    imported=[o for o in bpy.data.objects if o not in before]
    source=next(o for o in imported if o.type=='ARMATURE')
    source_mesh=next(o for o in imported if o.type=='MESH')
    for obj in imported:obj.hide_render=True
    clip=source.animation_data.action
    raw=json.loads((OUT/'source_audit.json').read_text())['sources']['rig']['meshes'][0]
    low=Vector(raw['bounds_min']);high=Vector(raw['bounds_max'])
    T=Matrix.Scale(audit['scale_factor_from_rig'],4)@Matrix.Translation(Vector((-(low.x+high.x)/2,-(low.y+high.y)/2,-low.z)))
    folder=REVIEW/(mode+'_frames');folder.mkdir(exist_ok=True)
    aim((0,0,0.75),(1,-1,0.48),2.85)
    action=bpy.data.actions.new('AUDIT_Source_'+mode.title());arm.animation_data_create();arm.animation_data.action=action
    samples=[]
    end=120 if mode=='idle' else 14
    for f in range(1,end+1):
        scene.frame_set(f)
        for bone in source.pose.bones:
            target_matrix=T@source.matrix_world@bone.matrix
            for i in range(3):
                for j in range(3):target_matrix[i][j]/=audit['scale_factor_from_rig']
            arm.pose.bones[bone.name].matrix=target_matrix
            bpy.context.view_layer.update()
        for bone in arm.pose.bones:
            bone.keyframe_insert('location',frame=f,group=bone.name)
            bone.keyframe_insert('rotation_quaternion',frame=f,group=bone.name)
            bone.keyframe_insert('scale',frame=f,group=bone.name)
        bpy.context.view_layer.update()
        dg=bpy.context.evaluated_depsgraph_get()
        evaluated=mesh.evaluated_get(dg).to_mesh();src_evaluated=source_mesh.evaluated_get(dg).to_mesh()
        errors=[((mesh.matrix_world@evaluated.vertices[i].co)-(T@source_mesh.matrix_world@src_evaluated.vertices[i].co)).length for i in range(10074)]
        feet={b.name:list(arm.matrix_world@b.head) for b in arm.pose.bones if b.name.endswith('bot2')}
        samples.append({'frame':f,'max_vertex_difference_m':max(errors),'mean_vertex_difference_m':sum(errors)/len(errors),'feet':feet})
        mesh.evaluated_get(dg).to_mesh_clear();source_mesh.evaluated_get(dg).to_mesh_clear()
        render(folder/f'{f-1:04d}.png')
    for obj in imported:bpy.data.objects.remove(obj,do_unlink=True)
    scene.frame_start=1;scene.frame_end=end;scene.render.fps=30;scene.frame_set(1)
    (OUT/(mode+'_motion_audit.json')).write_text(json.dumps({'label':'existing_source_motion_not_new_authored_clip','frames':end,'fps':30,'max_difference_m':max(x['max_vertex_difference_m'] for x in samples),'samples':samples},indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/f'Stonehoof_Audit_{mode.title()}_r03.blend'))
print('STONEHOOF_REVIEW_COMPLETE '+mode)
