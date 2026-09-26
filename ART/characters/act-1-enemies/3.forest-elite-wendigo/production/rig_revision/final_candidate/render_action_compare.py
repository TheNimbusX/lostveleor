"""Read-only action-pose skin comparison, on the actual revised animation keys."""
import json
from pathlib import Path
import bpy
from mathutils import Vector

HERE=Path(__file__).resolve().parent
PROD=HERE.parents[1]
OUT=HERE/'action_stress'/Path(bpy.data.filepath).stem
OUT.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene
M=bpy.data.objects['SM_ForestWendigo_LOD0']
R=bpy.data.objects['ARM_ForestWendigo']
prim=PROD/'animation/revision_primary/Wendigo_Primary_v2.blend'
sec=PROD/'animation/revision_secondary/output_final/ForestWendigo_WalkDeath_Revised.blend'
with bpy.data.libraries.load(str(prim),link=False) as (src,dst):
    dst.actions=[n for n in src.actions if n in ('AN_ForestWendigo_Claw','AN_ForestWendigo_Leap')]
with bpy.data.libraries.load(str(sec),link=False) as (src,dst):
    dst.actions=[n for n in src.actions if n=='AN_ForestWendigo_Death']

world=scene.world
world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.13,.14,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.6
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=650
scene.render.resolution_y=650
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='Standard'
scene.view_settings.look='Medium High Contrast'

floor=bpy.data.meshes.new('AuditFloor')
floor.from_pydata([(-50,-50,-.07),(50,-50,-.07),(50,50,-.07),(-50,50,-.07)],[],[(0,1,2,3)])
ground=bpy.data.objects.new('AuditFloor',floor)
scene.collection.objects.link(ground)
mat=bpy.data.materials.new('AuditFloor')
mat.diffuse_color=(.1,.11,.11,1)
ground.data.materials.append(mat)

def aim(ob,p):ob.rotation_euler=(p-ob.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power in (('Key',(2,4,6),850),('Fill',(-4,2,3),400),('Rim',(1,-3,5),750)):
    light=bpy.data.lights.new('Audit_'+name,'AREA')
    light.energy=power;light.shape='DISK';light.size=3
    ob=bpy.data.objects.new('Audit_'+name,light)
    scene.collection.objects.link(ob)
    ob.location=loc;aim(ob,Vector((0,0,1.5)))
camdata=bpy.data.cameras.new('AuditCamera')
cam=bpy.data.objects.new('AuditCamera',camdata)
scene.collection.objects.link(cam)
camdata.type='ORTHO';camdata.ortho_scale=4.6
scene.camera=cam

rest=[v.co.copy() for v in M.data.vertices]
edges=[(e.vertices[0],e.vertices[1]) for e in M.data.edges]
length=[(rest[a]-rest[b]).length for a,b in edges]
ids=[i for i,d in enumerate(length) if d>=.005]
deform=[b.name for b in R.data.bones if b.use_deform]
report={'rig':Path(bpy.data.filepath).name,'actions':{},'tris':len(M.data.loop_triangles),
        'deform_bones':len(deform)}
jobs={'AN_ForestWendigo_Claw':(15,18),'AN_ForestWendigo_Leap':(23,33),
      'AN_ForestWendigo_Death':(18,34,50)}
if not R.animation_data:R.animation_data_create()
for action_name,frames in jobs.items():
    action=bpy.data.actions.get(action_name)
    if not action:raise RuntimeError('Missing action '+action_name)
    R.animation_data.action=action
    for frame in frames:
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        ob=M.evaluated_get(bpy.context.evaluated_depsgraph_get())
        snapshot=ob.to_mesh()
        points=[v.co.copy() for v in snapshot.vertices]
        vals=sorted((points[edges[i][0]]-points[edges[i][1]]).length/length[i] for i in ids)
        info={'edge_p99':round(vals[int(.99*(len(vals)-1))],3),
              'edge_max':round(vals[-1],3),
              'min_z':round(min(p.z for p in points),3),
              'max_z':round(max(p.z for p in points),3)}
        ob.to_mesh_clear()
        label=action_name.rsplit('_',1)[-1]+'_'+str(frame).zfill(2)
        report['actions'][label]=info
        for angle,offset in (('game',Vector((1,1,.8))),('side',Vector((1,0,.2))),
                             ('front',Vector((0,1,.2)))):
            cam.location=Vector((0,0,1.4))+offset.normalized()*8
            aim(cam,Vector((0,0,1.4)))
            scene.render.filepath=str(OUT/(label+'_'+angle+'.png'))
            bpy.ops.render.render(write_still=True)

(OUT/'measurements.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('ACTION_COMPARE',json.dumps(report),flush=True)
