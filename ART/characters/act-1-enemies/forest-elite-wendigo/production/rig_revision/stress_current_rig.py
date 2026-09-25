"""Non-destructive current-rig stress study against the approved motion references.

Run: blender -b ForestWendigo_Rig.blend --python stress_current_rig.py
Outputs are intentionally restricted to rig_revision/.
"""

import json
import math
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


OUT = Path(__file__).resolve().parent / ("stress_" + Path(bpy.data.filepath).stem)
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
rig = bpy.data.objects["ARM_ForestWendigo"]
mesh = bpy.data.objects["SM_ForestWendigo_LOD0"]
for pose_bone in rig.pose.bones:
    pose_bone.rotation_mode = "QUATERNION"


def reset():
    for b in rig.pose.bones:
        b.location = (0, 0, 0)
        b.rotation_quaternion = (1, 0, 0, 0)
        b.scale = (1, 1, 1)
        for c in b.constraints:
            if c.type == "IK":
                c.influence = 0


def move(name, x=0, y=0, z=0):
    b = rig.pose.bones[name]
    b.location = b.bone.matrix_local.to_3x3().inverted() @ Vector((x, y, z))


def rotate(name, axis, degrees):
    b = rig.pose.bones[name]
    basis = b.bone.matrix_local.to_3x3()
    rot = Quaternion(Vector(axis), math.radians(degrees))
    b.rotation_quaternion = b.rotation_quaternion @ (basis.inverted() @ rot.to_matrix() @ basis).to_quaternion()


def ik(side, kind):
    lower = "leg_lower" if kind == "foot" else "arm_lower"
    rig.pose.bones[f"{side}_{lower}"].constraints[f"IK_{side}_{kind}_Plant"].influence = 1


def pose_neutral():
    reset()


def pose_quad_crouch():
    reset()
    for side in ("L", "R"):
        ik(side, "foot")
        ik(side, "hand")
    move("pelvis", y=0.16, z=-0.54)
    rotate("pelvis", (1, 0, 0), -9)
    rotate("spine_01", (1, 0, 0), -19)
    rotate("spine_02", (1, 0, 0), -23)
    rotate("neck", (1, 0, 0), 17)
    rotate("head", (1, 0, 0), 9)
    # The sourced right shoulder rests well behind the left. Lead it around
    # with the clavicle before asking IK to plant the right claw in front.
    rotate("R_clavicle", (0, 0, 1), 60)
    rotate("R_clavicle", (1, 0, 0), 30)
    move("CTRL_L_hand", x=-0.11, y=0.42, z=-0.55)
    move("CTRL_R_hand", x=0.04, y=1.20, z=-0.64)
    move("CTRL_L_foot", x=-0.07, y=-0.11, z=0.09)
    move("CTRL_R_foot", x=0.07, y=-0.03, z=0.02)


def pose_claw_arc():
    reset()
    for side in ("L", "R"):
        ik(side, "foot")
    move("pelvis", x=-0.12, y=0.12, z=-0.15)
    rotate("pelvis", (0, 0, 1), 17)
    rotate("spine_01", (0, 0, 1), 19)
    rotate("spine_02", (0, 0, 1), 27)
    rotate("spine_01", (1, 0, 0), -12)
    rotate("spine_02", (1, 0, 0), -13)
    rotate("neck", (0, 0, 1), -12)
    rotate("R_clavicle", (0, 0, 1), -18)
    rotate("R_arm_upper", (1, 0, 0), 85)
    rotate("R_arm_upper", (0, 0, 1), 80)
    rotate("R_arm_lower", (1, 0, 0), 20)
    rotate("R_hand", (1, 0, 0), 35)
    rotate("L_arm_upper", (1, 0, 0), -20)


def pose_fold_dead():
    reset()
    for side in ("L", "R"):
        ik(side, "foot")
        ik(side, "hand")
    move("pelvis", y=0.22, z=-0.67)
    rotate("pelvis", (1, 0, 0), -13)
    rotate("spine_01", (1, 0, 0), -25)
    rotate("spine_02", (1, 0, 0), -25)
    rotate("neck", (1, 0, 0), -20)
    rotate("head", (1, 0, 0), -21)
    rotate("R_clavicle", (0, 0, 1), 60)
    rotate("R_clavicle", (1, 0, 0), 30)
    move("CTRL_L_hand", x=-0.18, y=0.58, z=-0.58)
    move("CTRL_R_hand", x=0.10, y=1.35, z=-0.65)
    move("CTRL_L_foot", x=-0.04, y=-0.20, z=0.11)
    move("CTRL_R_foot", x=0.04, y=-0.10, z=0.01)


def quantile(values, q):
    sorted_values = sorted(values)
    return sorted_values[int((len(sorted_values) - 1) * q)]


groups = {}
for side in ("L", "R"):
    for kind in ("hand", "foot"):
        if kind == "hand":
            names = {f"{side}_hand"}
        else:
            names = {f"{side}_foot", f"{side}_toe"}
        group_ids = {mesh.vertex_groups[name].index for name in names}
        groups[f"{side}_{kind}"] = [
            v.index for v in mesh.data.vertices
            if sum(g.weight for g in v.groups if g.group in group_ids) > .7
        ]

rest = [v.co.copy() for v in mesh.data.vertices]
edges = [(e.vertices[0], e.vertices[1]) for e in mesh.data.edges]
rest_lengths = [max((rest[a] - rest[b]).length, 1e-5) for a, b in edges]

world = scene.world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (.12, .13, .14, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = .6
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 900
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "Medium High Contrast"

ground_mesh = bpy.data.meshes.new("AuditGround")
ground_mesh.from_pydata([(-5,-5,0),(5,-5,0),(5,5,0),(-5,5,0)], [], [(0,1,2,3)])
ground = bpy.data.objects.new("AuditGround", ground_mesh)
scene.collection.objects.link(ground)
ground_mat = bpy.data.materials.new("AuditGround")
ground_mat.diffuse_color = (.10, .11, .11, 1)
ground.data.materials.append(ground_mat)

def aim(obj, point):
    obj.rotation_euler = (point - obj.location).to_track_quat("-Z", "Y").to_euler()

for name, loc, power in (("Key", (2,4,6), 850), ("Fill", (-4,2,3), 400), ("Rim", (1,-3,5), 750)):
    ld = bpy.data.lights.new(name, "AREA")
    ld.energy = power
    ld.shape = "DISK"
    ld.size = 3
    ob = bpy.data.objects.new(name, ld)
    scene.collection.objects.link(ob)
    ob.location = loc
    aim(ob, Vector((0,0,1.5)))

camera_data = bpy.data.cameras.new("AuditCamera")
camera = bpy.data.objects.new("AuditCamera", camera_data)
scene.collection.objects.link(camera)
camera_data.type = "ORTHO"
camera_data.ortho_scale = 4.4
scene.camera = camera

report = {"source": bpy.data.filepath, "tris": len(mesh.data.loop_triangles), "poses": {}}
for name, pose in (("neutral", pose_neutral), ("quad_crouch", pose_quad_crouch),
                   ("claw_arc", pose_claw_arc), ("fold_dead", pose_fold_dead)):
    pose()
    bpy.context.view_layer.update()
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    deformed = evaluated.to_mesh()
    pos = [evaluated.matrix_world @ v.co for v in deformed.vertices]
    stretching = [(pos[a] - pos[b]).length / length for (a, b), length in zip(edges, rest_lengths)]
    meaningful = [(i, ratio) for i, ratio in enumerate(stretching) if rest_lengths[i] >= .005]
    meaningful.sort(key=lambda row: row[1], reverse=True)
    worst = []
    for index, ratio in meaningful[:12]:
        a, b = edges[index]
        midpoint = (rest[a] + rest[b]) / 2
        def weights(vertex_index):
            vertex = mesh.data.vertices[vertex_index]
            return sorted(((mesh.vertex_groups[g.group].name,round(g.weight,2)) for g in vertex.groups),
                          key=lambda item: item[1], reverse=True)[:3]
        worst.append({"ratio":round(ratio,2),"rest_m":round(rest_lengths[index],4),
                      "midpoint":[round(v,3) for v in midpoint],"groups_a":weights(a),"groups_b":weights(b)})
    info = {
        "edge_stretch_p95": round(quantile(stretching, .95), 3),
        "edge_stretch_p99": round(quantile(stretching, .99), 3),
        "edge_stretch_max": round(max(stretching), 3),
        "edge_stretch_p99_over_5mm": round(quantile([x[1] for x in meaningful], .99), 3),
        "edge_stretch_worst_over_5mm": worst,
        "min_mesh_z": round(min(v.z for v in pos), 3),
        "min_by_limb": {key: round(min(pos[i].z for i in ids), 3) for key, ids in groups.items()},
        "bone_heads": {key: [round(v, 3) for v in (rig.matrix_world @ rig.pose.bones[key].head)]
                       for key in ("pelvis", "head", "L_hand", "R_hand", "L_foot", "R_foot")},
    }
    evaluated.to_mesh_clear()
    report["poses"][name] = info
    for direction, offset in (("game", Vector((1,1,.8))), ("side", Vector((1,0,.2)))):
        camera.location = Vector((0,0,1.4)) + offset.normalized() * 8
        aim(camera, Vector((0,0,1.4)))
        scene.render.filepath = str(OUT / f"{name}_{direction}.png")
        bpy.ops.render.render(write_still=True)

(OUT / "measurements.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("STRESS_RIG_REPORT", json.dumps(report), flush=True)
