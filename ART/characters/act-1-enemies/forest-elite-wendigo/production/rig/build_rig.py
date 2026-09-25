"""Build a game-coordinate armature for the approved Forest Wendigo candidate."""

import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
out_dir = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
out_dir.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
mesh_obj = next(obj for obj in bpy.data.objects if obj.type == "MESH")
mesh_obj.name = "SM_ForestWendigo_LOD0"
mesh_obj.data.name = "SM_ForestWendigo_LOD0"
scene = bpy.context.scene
scene.render.fps = 30

arm_data = bpy.data.armatures.new("ARM_ForestWendigo")
arm_obj = bpy.data.objects.new("ARM_ForestWendigo", arm_data)
scene.collection.objects.link(arm_obj)
arm_obj.show_in_front = True
arm_data.display_type = "STICK"
bpy.ops.object.select_all(action="DESELECT")
arm_obj.select_set(True)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")


def add_bone(name, head, tail, parent=None, connect=False):
    bone = arm_data.edit_bones.new(name)
    bone.head = head
    bone.tail = tail
    bone.use_deform = True
    if parent:
        bone.parent = arm_data.edit_bones[parent]
        bone.use_connect = connect
    return bone


add_bone("root", (0, 0, 0), (0, 0, 0.12))
add_bone("pelvis", (0, 0, 1.19), (0, 0, 1.42), "root")
add_bone("spine_01", (0, 0, 1.42), (0, -0.015, 1.70), "pelvis", True)
add_bone("spine_02", (0, -0.015, 1.70), (0, 0.05, 2.00), "spine_01", True)
add_bone("neck", (0, 0.05, 2.00), (0, 0.26, 2.20), "spine_02", True)
add_bone("head", (0, 0.26, 2.20), (0, 0.47, 2.52), "neck", True)

limbs = {
    "L": {
        "clavicle": ((-.18, .05, 1.96), (-.37, .29, 1.91)),
        "arm_upper": ((-.37, .29, 1.91), (-.49, .51, 1.48)),
        "arm_lower": ((-.49, .51, 1.48), (-.53, .76, 1.13)),
        "hand": ((-.53, .76, 1.13), (-.54, .87, .85)),
        "leg_upper": ((-.20, .13, 1.27), (-.43, .27, .72)),
        "leg_lower": ((-.43, .27, .72), (-.60, .39, .24)),
        "foot": ((-.60, .39, .24), (-.60, .55, .09)),
        "toe": ((-.60, .55, .09), (-.61, .64, .06)),
    },
    "R": {
        "clavicle": ((.18, -.12, 1.96), (.37, -.42, 1.91)),
        "arm_upper": ((.37, -.42, 1.91), (.42, -.62, 1.48)),
        "arm_lower": ((.42, -.62, 1.48), (.51, -.81, 1.13)),
        "hand": ((.51, -.81, 1.13), (.52, -.90, .85)),
        "leg_upper": ((.19, -.21, 1.27), (.24, -.43, .72)),
        "leg_lower": ((.24, -.43, .72), (.48, -.51, .24)),
        "foot": ((.48, -.51, .24), (.55, -.52, .09)),
        "toe": ((.55, -.52, .09), (.63, -.53, .06)),
    },
}
for side, points in limbs.items():
    add_bone(f"{side}_clavicle", *points["clavicle"], "spine_02")
    add_bone(f"{side}_arm_upper", *points["arm_upper"], f"{side}_clavicle", True)
    add_bone(f"{side}_arm_lower", *points["arm_lower"], f"{side}_arm_upper", True)
    add_bone(f"{side}_hand", *points["hand"], f"{side}_arm_lower", True)
    add_bone(f"{side}_leg_upper", *points["leg_upper"], "pelvis")
    add_bone(f"{side}_leg_lower", *points["leg_lower"], f"{side}_leg_upper", True)
    add_bone(f"{side}_foot", *points["foot"], f"{side}_leg_lower", True)
    add_bone(f"{side}_toe", *points["toe"], f"{side}_foot", True)

bpy.ops.object.mode_set(mode="OBJECT")

# Weight painting starts with Blender's heat solver; max four influence cleanup
# and pose probes are checked below before treating this as an exportable rig.
bpy.ops.object.select_all(action="DESELECT")
mesh_obj.select_set(True)
arm_obj.select_set(True)
bpy.context.view_layer.objects.active = arm_obj
result = bpy.ops.object.parent_set(type="ARMATURE_AUTO")
print("AUTO_WEIGHT_RESULT", list(result), flush=True)
bpy.ops.object.select_all(action="DESELECT")
mesh_obj.select_set(True)
bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.object.vertex_group_limit_total(limit=4)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)

# Long rigid pieces must not rubber-bend with nearby foliage and torso bones.
# Keep the hand/palm transition blended; lock only distal claws and the high
# antler/skull area to their anatomical rigid bone.
rigid_counts = {"head": 0, "L_hand": 0, "R_hand": 0}
for vertex in mesh_obj.data.vertices:
    co = vertex.co
    target_bone = None
    if co.z > 2.45 or (co.z > 2.19 and co.y > .22 and abs(co.x) < .31):
        target_bone = "head"
    elif co.x < -.2 and co.y > .79 and co.z < 1.08:
        target_bone = "L_hand"
    elif co.x > .2 and co.y < -.79 and co.z < 1.08:
        target_bone = "R_hand"
    if target_bone is None:
        continue
    for assignment in list(vertex.groups):
        mesh_obj.vertex_groups[assignment.group].remove([vertex.index])
    mesh_obj.vertex_groups[target_bone].add([vertex.index], 1.0, "REPLACE")
    rigid_counts[target_bone] += 1

# Animator-only targets. FK remains the default so existing action scripts can
# key the deform bones directly; IK influences can be keyed to 1 for a planted
# hand or foot. The helpers are omitted from the deform-only FBX export.
bpy.ops.object.select_all(action="DESELECT")
arm_obj.select_set(True)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")
controls = []
for side in ("L", "R"):
    for kind, upper, lower, end in (
        ("hand", f"{side}_arm_upper", f"{side}_arm_lower", f"{side}_hand"),
        ("foot", f"{side}_leg_upper", f"{side}_leg_lower", f"{side}_foot"),
    ):
        upper_bone = arm_data.edit_bones[upper]
        lower_bone = arm_data.edit_bones[lower]
        start = upper_bone.head.copy()
        joint = lower_bone.head.copy()
        finish = lower_bone.tail.copy()
        axis = finish - start
        projection = start + axis * ((joint - start).dot(axis) / axis.length_squared)
        normal = joint - projection
        if normal.length < 0.01:
            normal = Vector((1 if side == "R" else -1, 0, 0))
        normal.normalize()
        pole_location = joint + normal * 0.75
        target = arm_data.edit_bones.new(f"CTRL_{side}_{kind}")
        target.head = finish
        target.tail = finish + Vector((0, 0, 0.14))
        target.parent = arm_data.edit_bones["root"]
        target.use_deform = False
        pole = arm_data.edit_bones.new(f"CTRL_{side}_{'elbow' if kind == 'hand' else 'knee'}")
        pole.head = pole_location
        pole.tail = pole_location + Vector((0, 0, 0.14))
        pole.parent = arm_data.edit_bones["root"]
        pole.use_deform = False
        controls.append((side, kind, upper, lower, target.name, pole.name))
bpy.ops.object.mode_set(mode="OBJECT")

ik_report = {}
for side, kind, upper_name, lower_name, target_name, pole_name in controls:
    upper_pose = arm_obj.pose.bones[upper_name]
    lower_pose = arm_obj.pose.bones[lower_name]
    rest_joint = lower_pose.head.copy()
    constraint = lower_pose.constraints.new("IK")
    constraint.name = f"IK_{side}_{kind}_Plant"
    constraint.target = arm_obj
    constraint.subtarget = target_name
    constraint.pole_target = arm_obj
    constraint.pole_subtarget = pole_name
    constraint.chain_count = 2
    best_angle = 0
    best_drift = float("inf")
    for step in range(24):
        angle = math.radians(-180 + step * 15)
        constraint.pole_angle = angle
        bpy.context.view_layer.update()
        drift = (lower_pose.head - rest_joint).length
        if drift < best_drift:
            best_drift = drift
            best_angle = angle
    constraint.pole_angle = best_angle
    bpy.context.view_layer.update()
    ik_report[constraint.name] = {
        "rest_joint_drift_m_if_enabled": round((lower_pose.head - rest_joint).length, 6),
        "pole_angle_degrees": round(math.degrees(best_angle), 1),
    }
    constraint.influence = 0
bpy.context.view_layer.update()

mesh = mesh_obj.data
mesh.calc_loop_triangles()
bm = bmesh.new()
bm.from_mesh(mesh)
geometry_report = {
    "triangles": len(mesh.loop_triangles),
    "boundary_edges": sum(edge.is_boundary for edge in bm.edges),
    "overfull_edges": sum(len(edge.link_faces) > 2 for edge in bm.edges),
}
bm.free()
deform_names = {bone.name for bone in arm_data.bones if bone.use_deform}
zero = 0
over_four = 0
unnormalized = 0
for vertex in mesh.vertices:
    weights = [entry.weight for entry in vertex.groups
               if mesh_obj.vertex_groups[entry.group].name in deform_names
               and entry.weight > 1e-6]
    zero += not weights
    over_four += len(weights) > 4
    unnormalized += bool(weights) and abs(sum(weights) - 1.0) > 0.005
report = {
    "source": str(source),
    "bone_names": [bone.name for bone in arm_data.bones],
    "front_axis": "+Y",
    "up_axis": "+Z",
    "scene_fps": scene.render.fps,
    "geometry": geometry_report,
    "unweighted_vertices": zero,
    "over_four_influences": over_four,
    "unnormalized_vertices": unnormalized,
    "material_count": len(mesh.materials),
    "rigid_vertex_counts": rigid_counts,
    "ik_controls": [name for bone in arm_data.bones if bone.name.startswith("CTRL_") for name in [bone.name]],
    "ik_rest_pose": ik_report,
}
blend_path = out_dir / "Wendigo_Rig_v3.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
report["blend"] = str(blend_path)
(out_dir / "rig_v3_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report), flush=True)
