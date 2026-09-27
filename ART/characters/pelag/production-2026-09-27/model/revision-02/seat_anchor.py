import bpy,json,math,sys
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
BASE=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/production-2026-09-27/model');OUT=BASE/'revision-02'
# Reuse the verified exact Unity pose/mount mapping, without running renders.
exec((OUT/'build_mount_revision.py').read_text().split('# Quick render exact grip first.')[0])
for o in [anchor,eye,grip]:o.hide_render=False
def points(o):return [o.matrix_world@v.co for v in o.data.vertices]
def body_points():
 ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();p=[ev.matrix_world@v.co for v in me.vertices];poly=[tuple(f.vertices) for f in me.polygons];ev.to_mesh_clear();return p,poly
def frame():
 def pos(n):return rig.matrix_world@rig.pose.bones['mixamorig:'+n].head
 low=pos('Spine');high=pos('Spine2');up=(high-low).normalized();right=pos('LeftShoulder')-pos('RightShoulder');right=(right-up*right.dot(up)).normalized();back=up.cross(right).normalized();right=back.cross(up).normalized()
 f=Matrix((right,back,up)).transposed().to_4x4();f.translation=low.lerp(high,.55);return f
F=frame();fi=F.inverted();pts,polys=body_points()
spinegroups={g.index for g in body.vertex_groups if g.name in ['mixamorig:Spine','mixamorig:Spine1','mixamorig:Spine2']}
torsoids=[v.index for v in body.data.vertices if sum(g.weight for g in v.groups if g.group in spinegroups)>.45]
tp=[fi@pts[i] for i in torsoids];central=[p for p in tp if abs(p.x)<.13 and -.18<p.z<.19]
depth=max(p.y for p in central)
anchor.parent=None;anchor.matrix_parent_inverse=Matrix.Identity(4)
R=Matrix.Rotation(math.atan2(.55,.835),4,'X')@Matrix.Scale(.94/.49038118124,4)
shape=[R@v.co for v in anchor.data.vertices];zmid=(max(p.z for p in shape)+min(p.z for p in shape))*.5
# Seat central shaft 25mm behind torso; deepest part determines collision.
base=Vector((0,depth+.025-min(p.y for p in shape),.015-zmid))
local=Matrix.Translation(base)@R
carrier=bpy.data.objects.new('CTRL_Anchor_BackCarry',None);hero.objects.link(carrier);carrier.empty_display_type='CUBE';carrier.empty_display_size=.08;carrier.matrix_world=F
anchor.parent=carrier;anchor.matrix_parent_inverse=Matrix.Identity(4);anchor.matrix_basis=local
bpy.context.view_layer.update()
top=anchor.matrix_world@Vector((-.0019656515,.096264005,-.006767678));axis=(F.to_3x3()@Vector((0,0,1))).normalized()
eye.matrix_world=Matrix.Translation(top+axis*.048)@axis.to_track_quat('Z','Y').to_matrix().to_4x4()@Matrix.Scale(.8,4)
for o in [eye]:
 w=o.matrix_world.copy();o.parent=carrier;o.matrix_parent_inverse=Matrix.Identity(4);o.matrix_basis=F.inverted()@w
# Grip on the same carry frame, alongside shaft. It no longer drifts with hips.
grip.parent=carrier;grip.matrix_parent_inverse=Matrix.Identity(4);grip.matrix_basis=Matrix.Translation(Vector((.25,depth+.05,-.04)))@Matrix.Rotation(math.radians(-12),4,'Z')@Matrix.Scale(.4,4)
bpy.context.view_layer.update()
report={'torso_vertex_count':len(torsoids),'back_frame':serial(F),'anchor_local':serial(local),'torso_back_local_depth':depth,'anchor_shape_local_bounds':[[min(p[i] for p in shape),max(p[i] for p in shape)] for i in range(3)],'pose_metrics':[]}
def render(name,loc,target=(0,0,.9),ortho=2.65):
 cam=scene.camera;cam.location=loc;cam.data.ortho_scale=ortho;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
def metrics(label):
 points_body,polygons=body_points();torsoTree=BVHTree.FromPolygons(points_body,[p for p in polygons if all(i in set(torsoids) for i in p)])
 aps=points(anchor);d=[torsoTree.find_nearest(v)[3] for v in aps];return {'label':label,'min_anchor_torso_distance':min(d),'anchor_min_z':min(v.z for v in aps),'body_min_z':min(v.z for v in points_body)}
scene.render.resolution_x=900;scene.render.resolution_y=950;scene.render.resolution_percentage=100;scene.cycles.samples=12
for label in ['idle','cleave_windup','cleave_contact','roll_mid']:
 pose(label);carrier.matrix_world=frame();bpy.context.view_layer.update();report['pose_metrics'].append(metrics(label))
 render('seating_'+label+'_side',(5,0,1.5))
 if label=='idle':render('seating_idle_back',(0,5,1.5));render('seating_idle_game',(3,-4,4.2))
pose('idle');carrier.matrix_world=frame();bpy.context.view_layer.update()
(OUT/'anchor_seating_preflight.json').write_text(json.dumps(report,indent=2));print('SEATING',json.dumps(report))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Seating_Intermediate.blend'))
