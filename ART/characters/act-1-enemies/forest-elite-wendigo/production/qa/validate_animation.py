"""Read-only, per-frame QA for Forest Wendigo Blender actions.

Run in Blender 5.2 with a rigged .blend open:
  blender -b Wendigo_Primary.blend --python validate_animation.py -- --out qa/primary_animation_audit.json

The script never saves or mutates the source file. Assigning actions and changing the
current frame only affects this temporary Blender process.
"""

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


PREFIX = "AN_ForestWendigo_"
GEOMETRY_LIMIT = 25000
CRITICAL_JOINT_STEP_M = 1.0
SUSPECT_JOINT_STEP_M = 0.55
GROUND_PENETRATION_M = -0.075
GROUND_FLOAT_M = 0.10
KEY_JOINTS = (
    "root", "pelvis", "spine_02", "head",
    "L_hand", "R_hand", "L_foot", "R_foot", "L_toe", "R_toe",
)


def argument(name, default=None):
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return args[args.index(name) + 1] if name in args and args.index(name) + 1 < len(args) else default


def r(value, digits=4):
    return round(float(value), digits)


def xyz(vector):
    return [r(value) for value in vector]


def distance(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def add_issue(issues, severity, code, action=None, frame=None, **detail):
    item = {"severity": severity, "code": code}
    if action is not None:
        item["action"] = action
    if frame is not None:
        item["frame"] = frame
    item.update(detail)
    issues.append(item)


def action_keyframes(action, slot):
    """Collect frame numbers from Blender 5 layered action channel bags."""
    frames = set()
    paths_by_frame = {}
    for layer in action.layers:
        for strip in layer.strips:
            bag = strip.channelbag(slot)
            if bag is None:
                continue
            for curve in bag.fcurves:
                for point in curve.keyframe_points:
                    frame = r(point.co.x, 5)
                    frames.add(frame)
                    paths_by_frame.setdefault(frame, set()).add(curve.data_path)
    return sorted(frames), paths_by_frame


def scale_channel_report(action, slot):
    keys = 0
    maximum_deviation = 0.0
    paths = set()
    for layer in action.layers:
        for strip in layer.strips:
            bag = strip.channelbag(slot)
            if bag is None:
                continue
            for curve in bag.fcurves:
                if not curve.data_path.endswith("scale"):
                    continue
                paths.add(curve.data_path)
                for point in curve.keyframe_points:
                    keys += 1
                    maximum_deviation = max(maximum_deviation, abs(point.co.y - 1.0))
    return {"key_count": keys, "paths": sorted(paths), "max_deviation_from_one": r(maximum_deviation, 6)}


def armature_meshes(scene, rig):
    meshes = []
    for obj in scene.objects:
        if obj.type != "MESH" or obj.hide_render:
            continue
        if obj.parent == rig or any(mod.type == "ARMATURE" and mod.object == rig for mod in obj.modifiers):
            meshes.append(obj)
    return meshes


def limb_indices(obj, side, kind):
    names = {f"{side}_foot", f"{side}_toe"} if kind == "sole" else {f"{side}_hand"}
    ids = {group.index for group in obj.vertex_groups if group.name in names}
    if not ids:
        return []
    selected = []
    for vert in obj.data.vertices:
        weight = sum(member.weight for member in vert.groups if member.group in ids)
        if weight >= 0.5:
            selected.append(vert.index)
    return selected


def mesh_sample(scene, rig, meshes, limb_lookup):
    graph = bpy.context.evaluated_depsgraph_get()
    triangle_total = 0
    min_z = math.inf
    limb_min = {kind: {"L": math.inf, "R": math.inf} for kind in ("sole", "hand")}
    mesh_counts = {}
    for obj in meshes:
        evaluated = obj.evaluated_get(graph)
        data = evaluated.to_mesh()
        try:
            data.calc_loop_triangles()
            count = len(data.loop_triangles)
            triangle_total += count
            mesh_counts[obj.name] = count
            matrix = evaluated.matrix_world
            limbs = limb_lookup[obj.name]
            for vert in data.vertices:
                z = (matrix @ vert.co).z
                if z < min_z:
                    min_z = z
            for kind in ("sole", "hand"):
                for side in ("L", "R"):
                    for index in limbs[kind][side]:
                        if index < len(data.vertices):
                            z = (matrix @ data.vertices[index].co).z
                            if z < limb_min[kind][side]:
                                limb_min[kind][side] = z
        finally:
            evaluated.to_mesh_clear()
    return triangle_total, r(min_z), {
        kind: {side: None if not math.isfinite(value) else r(value)
               for side, value in sides.items()}
        for kind, sides in limb_min.items()
    }, mesh_counts


def sample_frame(scene, rig, meshes, limb_lookup, frame):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    triangle_total, min_z, limbs, mesh_counts = mesh_sample(scene, rig, meshes, limb_lookup)
    world = rig.matrix_world
    joints = {}
    for name in KEY_JOINTS:
        bone = rig.pose.bones.get(name)
        if bone:
            joints[name] = xyz(world @ bone.head)
    root = rig.pose.bones.get("root")
    root_xy = xyz(world @ root.head)[:2] if root else xyz(world.translation)[:2]
    return {
        "frame": frame,
        "triangles": triangle_total,
        "mesh_triangles": mesh_counts,
        "min_surface_z_m": min_z,
        "sole_min_z_m": limbs["sole"],
        "hand_min_z_m": limbs["hand"],
        "root_xy_m": root_xy,
        "joints_m": joints,
    }


def required_ground_frames(name, start, end):
    if name.endswith("Leap"):
        return set(range(start, min(end, 23) + 1)) | set(range(max(start, 33), end + 1))
    if name.endswith("Death"):
        # The final death pose may rest on claws/knees, rather than on both feet.
        return set(range(start, end + 1))
    return set(range(start, end + 1))


def analyze_action(scene, rig, meshes, limb_lookup, action, contract, issues):
    slot = next((slot for slot in action.slots if slot.target_id_type == "OBJECT"), None)
    if slot is None:
        add_issue(issues, "fail", "missing_object_action_slot", action.name)
        return {"name": action.name, "sampled": False}
    rig.animation_data.action = action
    rig.animation_data.action_slot = slot
    effective_range = tuple(action.frame_range)
    start = int(round(effective_range[0]))
    end = int(round(effective_range[1]))
    if abs(effective_range[0] - start) > 1e-5 or abs(effective_range[1] - end) > 1e-5:
        add_issue(issues, "fail", "non_integer_action_limits", action.name,
                  frame_range=[r(x) for x in effective_range])
    expected = contract.get(action.name)
    expected_range = expected.get("frames") if expected else None
    if action.name.endswith("Walk"):
        expected_range = [0, 12]  # Approved 12-frame revision; contract may lag.
        if expected and expected.get("frames") != expected_range:
            add_issue(issues, "note", "walk_contract_pending_12_frame_update", action.name,
                      contract_frames=expected.get("frames"))
    if expected_range and [start, end] != expected_range:
        add_issue(issues, "fail", "action_range_mismatch", action.name,
                  actual=[start, end], expected=expected_range)
    keys, paths_by_frame = action_keyframes(action, slot)
    if not keys or keys[0] > start or keys[-1] < end:
        add_issue(issues, "fail", "missing_limit_keys", action.name,
                  actual_key_limits=[keys[0], keys[-1]] if keys else None,
                  action_limits=[start, end])
    scale_channels = scale_channel_report(action, slot)
    if scale_channels["max_deviation_from_one"] > 0.001:
        add_issue(issues, "fail", "non_unit_scale_animation", action.name,
                  scale_channels=scale_channels)
    elif scale_channels["key_count"]:
        add_issue(issues, "warn", "neutral_scale_channels_present", action.name,
                  scale_channels=scale_channels,
                  note="Scale values are all one, but unnecessary scale curves should be removed before export.")

    samples = [sample_frame(scene, rig, meshes, limb_lookup, frame)
               for frame in range(start, end + 1)]
    tri_max = max(item["triangles"] for item in samples)
    tri_min = min(item["triangles"] for item in samples)
    if tri_max > GEOMETRY_LIMIT:
        bad = next(item for item in samples if item["triangles"] > GEOMETRY_LIMIT)
        add_issue(issues, "fail", "triangle_budget_exceeded", action.name, bad["frame"],
                  triangles=bad["triangles"], limit=GEOMETRY_LIMIT)
    if tri_max != tri_min:
        add_issue(issues, "warn", "animated_triangle_count_varies", action.name,
                  triangle_min=tri_min, triangle_max=tri_max)

    root_start = samples[0]["root_xy_m"]
    root_error = max(math.hypot(*(x - y for x, y in zip(item["root_xy_m"], root_start)))
                     for item in samples)
    if root_error > 0.005:
        add_issue(issues, "fail", "horizontal_root_motion", action.name,
                  max_displacement_m=r(root_error), tolerance_m=0.005)

    joint_events = []
    for left, right in zip(samples, samples[1:]):
        for bone, previous in left["joints_m"].items():
            if bone not in right["joints_m"]:
                continue
            step = distance(previous, right["joints_m"][bone])
            if step >= SUSPECT_JOINT_STEP_M:
                joint_events.append({"from_frame": left["frame"], "to_frame": right["frame"],
                                     "bone": bone, "step_m": r(step)})
            if not math.isfinite(step):
                add_issue(issues, "fail", "non_finite_pose", action.name, right["frame"], bone=bone)
            elif step > CRITICAL_JOINT_STEP_M:
                add_issue(issues, "fail", "joint_jump", action.name, right["frame"], bone=bone,
                          step_m=r(step), limit_m=CRITICAL_JOINT_STEP_M)
    if joint_events:
        add_issue(issues, "warn", "fast_joint_motion_review", action.name,
                  events=sorted(joint_events, key=lambda item: -item["step_m"])[:8],
                  note="Fast attacks may be intentional; compare adjacent renders before changing curves.")

    penetrations = [item for item in samples if item["min_surface_z_m"] < GROUND_PENETRATION_M]
    if penetrations:
        add_issue(issues, "fail", "ground_penetration", action.name,
                  frames=[item["frame"] for item in penetrations],
                  minimum_z_m=min(item["min_surface_z_m"] for item in penetrations),
                  tolerance_m=GROUND_PENETRATION_M)
    minor_penetrations = [item for item in samples
                          if GROUND_PENETRATION_M <= item["min_surface_z_m"] < -0.03]
    if minor_penetrations:
        add_issue(issues, "warn", "minor_ground_penetration", action.name,
                  frames=[item["frame"] for item in minor_penetrations],
                  minimum_z_m=min(item["min_surface_z_m"] for item in minor_penetrations))
    ground_frames = required_ground_frames(action.name, start, end)
    floating = []
    for item in samples:
        if item["frame"] not in ground_frames:
            continue
        # Ground contact comes from a sole or supporting hand, not a dangling
        # skirt, antler, or another unrelated part of the mesh.
        candidate_z = list(item["sole_min_z_m"].values())
        if action.name.endswith(("Leap", "Death")):
            candidate_z += list(item["hand_min_z_m"].values())
        supports = [z for z in candidate_z if z is not None]
        support_z = min(supports) if supports else item["min_surface_z_m"]
        if support_z > GROUND_FLOAT_M:
            floating.append(item["frame"])
    if floating:
        add_issue(issues, "fail", "grounded_phase_floating", action.name,
                  frames=floating, surface_z_limit_m=GROUND_FLOAT_M)

    loop = bool(expected and expected.get("loop")) or action.name.endswith(("Idle", "Walk"))
    seam = None
    if loop:
        first, last = samples[0], samples[-1]
        per_joint = {bone: r(distance(first["joints_m"][bone], last["joints_m"][bone]))
                     for bone in first["joints_m"] if bone in last["joints_m"]}
        worst_bone = max(per_joint, key=per_joint.get) if per_joint else None
        worst = per_joint[worst_bone] if worst_bone else 0.0
        seam = {"max_joint_position_error_m": worst, "worst_bone": worst_bone}
        if worst > 0.025:
            add_issue(issues, "fail", "loop_position_seam", action.name,
                      max_error_m=worst, worst_bone=worst_bone, limit_m=0.025)
        if len(samples) >= 3:
            derivative_discontinuities = {}
            for bone in first["joints_m"]:
                if bone not in last["joints_m"]:
                    continue
                prev = Vector(samples[-2]["joints_m"][bone])
                end_p = Vector(last["joints_m"][bone])
                start_p = Vector(first["joints_m"][bone])
                nxt = Vector(samples[1]["joints_m"][bone])
                derivative_discontinuities[bone] = r(((end_p - prev) - (nxt - start_p)).length)
            seam["max_velocity_seam_m_per_frame"] = max(derivative_discontinuities.values(), default=0)
            seam["worst_velocity_bone"] = max(derivative_discontinuities,
                                              key=derivative_discontinuities.get,
                                              default=None)
            if seam["max_velocity_seam_m_per_frame"] > 0.18:
                add_issue(issues, "warn", "loop_velocity_seam", action.name,
                          max_velocity_difference_m_per_frame=seam["max_velocity_seam_m_per_frame"],
                          worst_bone=seam["worst_velocity_bone"])

    contact_frame = 18 if action.name.endswith("Claw") else 33 if action.name.endswith("Leap") else None
    contact = None
    if contact_frame is not None:
        relevant_keys = paths_by_frame.get(float(contact_frame), set())
        if not relevant_keys:
            add_issue(issues, "fail", "contact_frame_not_keyed", action.name, contact_frame)
        contact = samples[contact_frame - start] if start <= contact_frame <= end else None
        if contact is None:
            add_issue(issues, "fail", "contact_outside_action", action.name, contact_frame)
        elif action.name.endswith("Leap"):
            if contact["min_surface_z_m"] > GROUND_FLOAT_M:
                add_issue(issues, "fail", "leap_contact_above_ground", action.name, contact_frame,
                          min_surface_z_m=contact["min_surface_z_m"])
            # The visible landing must be approached from above; a zero-height
            # frame 32 would imply early visual contact relative to Sim frame 33.
            before = samples[contact_frame - start - 1]
            if before["min_surface_z_m"] < 0.06:
                add_issue(issues, "warn", "leap_may_touch_early", action.name, contact_frame - 1,
                          min_surface_z_m=before["min_surface_z_m"])
        elif action.name.endswith("Claw"):
            hand = contact["joints_m"].get("L_hand")
            if hand and hand[2] > 1.45:
                add_issue(issues, "warn", "claw_contact_hand_high", action.name, contact_frame,
                          hand_head_z_m=hand[2], note="A ground-level swipe may be hard to read.")

    return {
        "name": action.name,
        "sampled": True,
        "frame_range": [start, end],
        "key_frames": keys,
        "scale_channels": scale_channels,
        "triangle_range": [tri_min, tri_max],
        "max_root_xy_error_m": r(root_error),
        "min_surface_z_range_m": [min(item["min_surface_z_m"] for item in samples),
                                  max(item["min_surface_z_m"] for item in samples)],
        "max_joint_step_m": max((event["step_m"] for event in joint_events), default=0),
        "loop_seam": seam,
        "contact": contact,
        "samples": samples,
    }


def main():
    scene = bpy.context.scene
    rig = next((obj for obj in scene.objects if obj.type == "ARMATURE"), None)
    issues = []
    if rig is None:
        add_issue(issues, "fail", "no_armature")
    if scene.render.fps != 30:
        add_issue(issues, "fail", "wrong_fps", actual=scene.render.fps, expected=30)
    contract_path = Path(__file__).resolve().parents[1] / "animation_contract.json"
    contract_data = json.loads(contract_path.read_text(encoding="utf-8"))
    contract = {clip["name"]: clip for clip in contract_data["clips"]}
    actions = sorted((action for action in bpy.data.actions if action.name.startswith(PREFIX)),
                     key=lambda action: action.name)
    found_names = {action.name for action in actions}
    for name in contract:
        if name not in found_names:
            add_issue(issues, "note", "expected_clip_not_in_this_file", name)
    meshes = armature_meshes(scene, rig) if rig else []
    if not meshes:
        add_issue(issues, "fail", "no_renderable_skinned_mesh")
    limb_lookup = {obj.name: {kind: {side: limb_indices(obj, side, kind) for side in ("L", "R")}
                              for kind in ("sole", "hand")}
                   for obj in meshes}
    if any(not indices for kinds in limb_lookup.values() for sides in kinds.values()
           for indices in sides.values()):
        add_issue(issues, "note", "sole_vertex_subset_missing_for_some_meshes",
                  subsets={name: {kind: {side: len(ids) for side, ids in sides.items()}
                                  for kind, sides in kinds.items()}
                           for name, kinds in limb_lookup.items()},
                  note="Ground checks still use the full deformed surface.")
    reports = []
    if rig and meshes:
        rig.animation_data_create()
        for action in actions:
            reports.append(analyze_action(scene, rig, meshes, limb_lookup, action, contract, issues))
    counts = {level: sum(1 for issue in issues if issue["severity"] == level)
              for level in ("fail", "warn", "note")}
    result = {
        "source_blend": bpy.data.filepath,
        "fps": scene.render.fps,
        "triangle_limit": GEOMETRY_LIMIT,
        "mesh_names": [obj.name for obj in meshes],
        "actions": reports,
        "issues": issues,
        "issue_counts": counts,
        "automated_qa_pass": counts["fail"] == 0,
        "limits": [
            "This script cannot establish whether the pose is visually appealing.",
            "Ground height assumes the authored Blender ground plane is z=0; Unity terrain must be checked later.",
            "Action frame keys and visual pose cannot prove Unity Sim damage timing until stage 4 integration.",
        ],
    }
    target = Path(argument("--out", str(Path(__file__).with_name("animation_audit.json")))).resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print("ANIMATION_QA_JSON", target)
    print("ANIMATION_QA_SUMMARY", json.dumps({
        "file": bpy.data.filepath,
        "actions": [a["name"] for a in reports],
        "issue_counts": counts,
        "automated_qa_pass": result["automated_qa_pass"],
        "issues": issues,
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
