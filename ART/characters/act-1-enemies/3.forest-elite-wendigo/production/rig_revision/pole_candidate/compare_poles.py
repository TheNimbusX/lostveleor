"""Evaluate the same reviewed actions on source SkinCandidate and pole candidate.

Called separately with each .blend using Blender -b. Actions are appended only
in memory; no source scenes or actions are saved or edited.
"""

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


HERE = Path(__file__).resolve().parent
PROD = HERE.parent.parent
label = sys.argv[sys.argv.index("--") + 1]
OUT = HERE / f"compare_{label}"
OUT.mkdir(exist_ok=True)
rig = bpy.data.objects["ARM_ForestWendigo"]
mesh = bpy.data.objects["SM_ForestWendigo_LOD0"]
sources = (
    PROD / "animation" / "revision_primary" / "Wendigo_Primary_v3_Skin.blend",
    PROD / "animation" / "secondary" / "output_v9" / "Wendigo_Secondary_Actions.blend",
)
for path in sources:
    with bpy.data.libraries.load(str(path), link=False) as (src, dst):
        dst.actions = [name for name in src.actions if name in {
            "AN_ForestWendigo_Claw", "AN_ForestWendigo_Leap",
            "AN_ForestWendigo_Walk", "AN_ForestWendigo_Death"}]

mesh.data.calc_loop_triangles()
groups = {}
for side in ("L","R"):
    for kind in ("hand","foot"):
        names = ({f"{side}_hand"} if kind == "hand" else {f"{side}_foot",f"{side}_toe"})
        ids = {mesh.vertex_groups[name].index for name in names}
        groups[f"{side}_{kind}"] = [v.index for v in mesh.data.vertices
                                     if sum(g.weight for g in v.groups if g.group in ids) > .7]

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = 820
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.12,.13,.15,1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = .6

def aim(obj, point):
    obj.rotation_euler = (point-obj.location).to_track_quat("-Z","Y").to_euler()

for name,position,power in (("PoleKey",(3,3,5),700),("PoleFill",(-3,2,3),380),("PoleRim",(0,-3,4),700)):
    ld = bpy.data.lights.new(name,"AREA")
    ld.energy = power
    ld.shape = "DISK"
    ld.size = 3
    ob = bpy.data.objects.new(name,ld)
    scene.collection.objects.link(ob)
    ob.location = position
    aim(ob,Vector((0,0,1.5)))
camera_data = bpy.data.cameras.new("PoleCamera")
camera = bpy.data.objects.new("PoleCamera",camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"
camera_data.ortho_scale = 4.2

samples = {
    "Claw": [0,12,15,18,22,30],
    "Leap": [0,13,23,24,26,33,38,51],
    "Walk": [0,3,6,9,12],
    "Death": [0,11,22,34,45],
}
renders = {"Claw":[15,18],"Leap":[23,33],"Walk":[6],"Death":[45]}
report = {"label":label,"source_file":bpy.data.filepath,"tris":len(mesh.data.loop_triangles),
          "deform_bones":sum(b.use_deform for b in rig.data.bones),"actions":{}}
rig.animation_data_create()
for action_name, frames in samples.items():
    action = bpy.data.actions[f"AN_ForestWendigo_{action_name}"]
    rig.animation_data.action = action
    data = {}
    for frame in frames:
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        pb = rig.pose.bones
        def xyz(bone, endpoint="head"):
            return [round(v,4) for v in (rig.matrix_world @ getattr(pb[bone],endpoint))]
        evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        deformed = evaluated.to_mesh()
        minimum = {name:round(min((evaluated.matrix_world @ deformed.vertices[i].co).z
                                  for i in ids),4) for name,ids in groups.items()}
        evaluated.to_mesh_clear()
        data[str(frame)] = {
            "R_elbow":xyz("R_arm_lower"),"R_knee":xyz("R_leg_lower"),
            "R_wrist":xyz("R_hand"),"R_ankle":xyz("R_foot"),
            "R_elbow_pole":xyz("CTRL_R_elbow"),"R_knee_pole":xyz("CTRL_R_knee"),
            "R_wrist_target":xyz("CTRL_R_hand"),"R_ankle_target":xyz("CTRL_R_foot"),
            "R_elbow_ik":round(pb["R_arm_lower"].constraints["IK_R_hand_Plant"].influence,3),
            "R_knee_ik":round(pb["R_leg_lower"].constraints["IK_R_foot_Plant"].influence,3),
            "paw_floor_m":minimum,
        }
        data[str(frame)]["R_wrist_target_error_m"] = round(
            math.dist(data[str(frame)]["R_wrist"],data[str(frame)]["R_wrist_target"]),4)
        data[str(frame)]["R_ankle_target_error_m"] = round(
            math.dist(data[str(frame)]["R_ankle"],data[str(frame)]["R_ankle_target"]),4)
        if frame in renders[action_name]:
            for camera_name, offset in (("front",Vector((0,1,.18))),
                                        ("game",Vector((1,1,.8)))):
                target = Vector((0,0,1.45))
                camera.location = target+offset.normalized()*8
                aim(camera,target)
                scene.render.filepath = str(OUT/f"{action_name}_{frame:02d}_{camera_name}.png")
                bpy.ops.render.render(write_still=True)
    report["actions"][action_name] = data

(OUT/"measurements.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print("POLE_COMPARE",label,OUT,flush=True)
