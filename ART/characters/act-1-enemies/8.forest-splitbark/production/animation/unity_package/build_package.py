"""Unity package: ForestSplitter.fbx (mesh + deform armature + all 10 takes) and export.json.

blender -b ../ForestSplitter_Baked_r02.blend -P build_package.py
r02 (27.09): the baked blend is ForestSplitter_Baked_r02.blend = the r01 baked blend + the four roll takes
(bake_roll.py, merge_r02.py); the six r01 actions are copied, not re-baked (validation_r02.json proves they
are key-for-key identical), so the old takes export exactly as before.
The baked blend is the read-only input (not saved back). Each baked action goes on its own NLA track as a
strip named exactly like the take, and the FBX is exported per NLA strip, so the FBX takes are named exactly
ForestSplitter_Idle ... ForestSplitter_Pop (the all-actions mode would prefix them with "ARM_ForestSplitter|").
Axes / scale / bake step follow the Stonehoof and Wendigo packages already in the game.
Writes: ForestSplitter.fbx, textures/*.png, ForestSplitter_Package.blend, export.json.
"""
import hashlib
import json
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

HERE = Path(__file__).resolve().parent
ANIM = HERE.parent
sys.path.insert(0, str(ANIM))
import takes_loops as tl  # noqa: E402
from takes import ORDER, TAKES  # noqa: E402

SRC = ANIM / "ForestSplitter_Baked_r02.blend"
FBX = HERE / "ForestSplitter.fbx"
TEX = HERE / "textures"
CHILD_SCALE, CHILD_SPEED = 0.6, 4.2
ROLL_REF = "vfx_target_frames_2026-09-27/2-roll-windup.png + 1-roll.png (target frames, no video reference)"
ROLL_TICK_M, ROLL_MAX_TICKS = 0.40, 16

assert Path(bpy.data.filepath).resolve() == SRC.resolve(), bpy.data.filepath
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.render.fps, scene.render.fps_base = 30, 1.0
arm = bpy.data.objects["ARM_ForestSplitter"]
mesh = bpy.data.objects["SM_ForestSplitter_LOD0"]

# ---- sanity of the input ----
for ob in list(bpy.data.objects):
    if ob not in (arm, mesh):
        bpy.data.objects.remove(ob, do_unlink=True)
assert arm.matrix_world == Matrix.Identity(4), arm.matrix_world
assert not any(pb.constraints for pb in arm.pose.bones), "baked armature must be constraint-free"
assert all(b.use_deform for b in arm.data.bones), "baked armature must be deform-only"
missing = [t for t in ORDER if t not in bpy.data.actions]
assert not missing, missing
for act in list(bpy.data.actions):
    if act.name not in ORDER:
        bpy.data.actions.remove(act)

# ---- mesh beak tip at the bite contact (the head bone tail sits ~9 cm above the real beak tip) ----
def mesh_beak_tip(frame):
    arm.animation_data_create()
    act = bpy.data.actions["ForestSplitter_Bite"]
    arm.animation_data.action = act
    if len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
    scene.frame_set(frame)
    names = [g.name for g in mesh.vertex_groups]
    head = [v.index for v in mesh.data.vertices
            if v.groups and names[max(v.groups, key=lambda g: g.weight).group] == "head"]
    ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    tip = min((mesh.matrix_world @ me.vertices[i].co for i in head), key=lambda p: p.y)
    ev.to_mesh_clear()
    arm.animation_data.action = None
    return tip


BEAK_TIP = mesh_beak_tip(TAKES["ForestSplitter_Bite"]["contact"])

# ---- one NLA track + strip per take, strip name == take name ----
arm.animation_data_create()
ad = arm.animation_data
ad.action = None
for tr in list(ad.nla_tracks):
    ad.nla_tracks.remove(tr)
for take in ORDER:
    act = bpy.data.actions[take]
    n = TAKES[take]["frames"]
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, n
    act.use_cyclic = TAKES[take]["loop"]
    track = ad.nla_tracks.new()
    track.name = take
    strip = track.strips.new(take, 0, act)
    strip.name = take
    strip.frame_start, strip.frame_end = 0, n
    strip.extrapolation = "NOTHING"
    strip.blend_type = "REPLACE"
    if len(act.slots):
        strip.action_slot = act.slots[0]

# ---- textures next to the FBX ----
TEX.mkdir(exist_ok=True)
textures = []
for img in bpy.data.images:
    key = img.name.split("_")[0]
    if img.source != "FILE" or img.size[0] <= 0 or key not in ("Color", "NormalGL", "ORM"):
        continue
    target = TEX / ("ForestSplitter_%s.png" % re.sub(r"[^A-Za-z0-9_-]+", "_", key))
    copy = img.copy()
    copy.filepath_raw = str(target)
    copy.file_format = "PNG"
    copy.save()
    bpy.data.images.remove(copy)
    img.filepath_raw = str(target)
    textures.append(target.name)

# ---- facing guide (1 m in front of the entity origin, like Stonehoof) ----
guide = bpy.data.objects.new("FacingGuide", None)
scene.collection.objects.link(guide)
guide.parent = arm
guide.location = (0.0, -1.0, 0.0)
arm["root_motion"] = "none: every take is in place, the sim moves the entity"
scene.frame_start, scene.frame_end = 0, TAKES[ORDER[0]]["frames"]
scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "ForestSplitter_Package.blend"), copy=True)

bpy.ops.object.select_all(action="DESELECT")
for ob in (arm, mesh, guide):
    ob.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=str(FBX), use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
    add_leaf_bones=False, use_armature_deform_only=True, axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
    bake_anim=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=0.25, bake_anim_simplify_factor=0.0,
    mesh_smooth_type="FACE", use_mesh_modifiers=True, path_mode="RELATIVE", embed_textures=False)

# ---- export.json ----
val = json.loads((ANIM / "validation.json").read_text(encoding="utf-8"))["takes"]
rows = json.loads((ANIM / "bake_report.json").read_text(encoding="utf-8"))["takes"]
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()  # noqa: E731
takes = {}
for take in ORDER:
    sp = TAKES[take]
    n = sp["frames"]
    e = {"frames": [0, n], "length_s": round(n / 30.0, 4), "loop": sp["loop"],
         "reference": ROLL_REF if sp["ref"] == "roll" else sp["ref"] + ".mp4", "note": sp["note"]}
    if "contact" in sp:
        e["contact_frame"] = sp["contact"]
        e["contact_phase"] = round(sp["contact"] / n, 4)
    if "release" in sp:
        e["release_frame"] = sp["release"]
    e["muzzle_offset"] = None
    takes[take] = e
bite = takes["ForestSplitter_Bite"]
bite.update({"windup_ticks": 18, "recovery_ticks": 12,
             "contact_rule": "beak furthest forward + shells clap + front-left paw stomp on frame 18 = sim windup tick 18"})
bx, by, bz = rows["ForestSplitter_Bite"]["rows"][18]["beak"]
bite["bite_point_at_contact_m"] = {"forward": round(-by, 3), "up": round(bz, 3), "right": round(-bx, 3),
                                   "unity_local": [round(-bx, 3), round(bz, 3), round(-by, 3)],
                                   "what": "head bone tip at frame 18, entity-local; no projectile"}
mx, my, mz = BEAK_TIP
bite["beak_mesh_tip_at_contact_m"] = {"forward": round(-my, 3), "up": round(mz, 3), "right": round(-mx, 3),
                                      "unity_local": [round(-mx, 3), round(mz, 3), round(-my, 3)],
                                      "what": "forwardmost skinned vertex of the head at frame 18 (the visible "
                                              "beak tip; use this for bite VFX)"}
bite["child_bite"] = {"windup_ticks": 12, "recovery_ticks": 8, "contact_phase": round(12 / 20, 4),
                      "playback_speed": 1.5, "scale": CHILD_SCALE,
                      "why": "Splitling bite 12+8 ticks has the same contact phase 0.6 as this clip, so the child "
                             "can reuse ForestSplitter_Bite at x1.5 (contact on its tick 12)"}
walk = takes["ForestSplitter_Walk"]
walk.update({"speed_mps": tl.SPEED, "stride_m_per_cycle": round(tl.STEP * tl.WALK_N, 4),
             "stance_foot_speed_mps": val["ForestSplitter_Walk"]["stance_foot_speed_mps"],
             "child": {"scale": CHILD_SCALE, "speed_mps": CHILD_SPEED,
                       "playback_speed": round(CHILD_SPEED / CHILD_SCALE / tl.SPEED, 4),
                       "why": "planted feet match ground speed when playback = speed / (scale * 3.1)"}})
takes["ForestSplitter_Death"].update(
    {"release_rule": "last frame: the view hides the parent and pops two children (SplitterSplit)"})
takes["ForestSplitter_Pop"].update(
    {"takeoff_frame": TAKES["ForestSplitter_Pop"]["takeoff"],
     "contact_rule": "landing on frame 8 = end of the sim's 8-tick SplitPop (1 m sideways, moved by the sim)",
     "scale": CHILD_SCALE})
# ---- r02: roll takes (27.09) ----
v2 = json.loads((ANIM / "validation_r02.json").read_text(encoding="utf-8"))
ball = v2["ball"]
takes["ForestSplitter_RollCurl"].update({
    "lock_frame": TAKES["ForestSplitter_RollCurl"]["lock"], "shake_frames": TAKES["ForestSplitter_RollCurl"]["shake"],
    "launch_frame": TAKES["ForestSplitter_RollCurl"]["launch"],
    "contract_rule": "tick 0 curl starts (body sinks, the view keeps turning the entity to the hero) = frame 0; "
                     "ball fully tucked by frame 12 = direction lock; frames 24-30 shake; frame 30 = launch tick "
                     "= ball pose, identical to RollLoop frame 0",
    "phases": {"0-2": "startled: head up, shells open a hair", "2-11": "body sinks, legs fold, head pulls in, "
               "shells clamp (snap past -7 deg at 10, squash bounce 10-14)", "13-24": "tension: shells tremble",
               "24-30": "shake: yaw rattle +-5 deg, rims lift <= 7 mm, shells rattle"},
    "start_pose": "Idle frame 0", "end_pose": "ball (= RollLoop frame 0)"})
takes["ForestSplitter_RollLoop"].update({
    "start_pose": "ball", "roll": {
        "sim": "launch on RollCurl frame 30, %.2f m/tick for up to %d ticks" % (ROLL_TICK_M, ROLL_MAX_TICKS),
        "view_spin": "spin the whole model about its lateral axis through spin_pivot; this clip only keeps the "
                     "tucked pose with a small squash wobble (2 beats per 12-frame loop, rims dip <= 4.5 mm)",
        "spin_pivot_unity_local": ball["spin_pivot_unity_local"],
        "spin_radius_m": ball["spin_radius_m"],
        "lift_while_spinning_m": ball["lift_to_clear_ground_m"],
        "spin_direction": "rolling forward = positive rotation about the entity's local +X (the top moves "
                          "forward, +Z)",
        "angle_per_tick_deg": round(math.degrees(ROLL_TICK_M / ball["spin_radius_m"]), 1),
        "stop": "on the last roll tick finish the turn to the next upright (~4 frames, ease out) while "
                "RollUncurl / RollDizzy frames 0-4 play (they start in the ball pose)",
        "ball_size_m": ball["size_m"]}})
takes["ForestSplitter_RollUncurl"].update({
    "punish_ticks": 30, "start_pose": "ball (= RollLoop frame 0)", "end_pose": "Idle frame 0",
    "phases": {"0-5": "stop jolt: shells rattle, small yaw", "5-8": "stunned beat (still a ball)",
               "8-11": "head peeks out", "7-13": "legs swing out in the air, paws plant on 13",
               "12-21": "body pushes up off the rims (paws stay planted)",
               "19-26": "shells exhale open (~7 deg) and settle", "16-25": "head shake"}})
takes["ForestSplitter_RollDizzy"].update({
    "wall_hit_frame": 0, "bump_peak_frame": 2, "start_pose": "ball (= RollLoop frame 0)", "end_pose": "Idle frame 0",
    "use": "instead of RollUncurl when the roll ends on a wall",
    "phases": {"0-5": "bounces back off the wall (+3.5 cm back, +1.8 cm up at 2), shells clap",
               "5-10": "graceless unfold: legs swing out, paws plant splayed on 10",
               "10-15": "body pushes up onto the splayed feet", "12-36": "dizzy: body sways in a lopsided circle, "
               "head circles the other way, shells flap unevenly", "35-43": "head shake-off",
               "36-44": "feet step home one by one"}})
report = {
    "fbx": FBX.name, "fbx_bytes": FBX.stat().st_size, "textures": ["textures/" + t for t in textures],
    "fps": 30, "root_motion": False, "in_place": "root bone static in every take; no XZ travel",
    "axes": {"blender": "front -Y, up +Z", "fbx_export": "axis_forward -Z, axis_up Y (Unity front +Z)",
             "scale": "apply_unit_scale, FBX_SCALE_NONE (as Stonehoof/Wendigo)"},
    "facing_guide": "empty 'FacingGuide' 1 m in front of the origin",
    "fbx_take_names": ORDER, "bake_step_frames": 0.25,
    "triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
    "deform_bones": len(arm.data.bones), "max_influences": 4,
    "takes": takes,
    "sources": {"baked": SRC.name, "baked_sha256": sha(SRC),
                "editable": "ForestSplitter_Anim_r02.blend", "editable_sha256": sha(ANIM / "ForestSplitter_Anim_r02.blend"),
                "authoring": "takes_loops.py, takes_bite.py, takes_react.py -> bake_takes.py (r01); "
                              "takes_roll.py -> bake_roll.py -> merge_r02.py (r02, r01 actions copied as they are)",
                "r01_baked": "ForestSplitter_Baked_r01.blend", "r01_baked_sha256": sha(ANIM / "ForestSplitter_Baked_r01.blend"),
                "validation_r02": "validation_r02.json"},
    "revision": {"r01": "26.09: Idle, Walk, Bite, Hit, Death, Pop (owner-approved)",
                 "r02": "27.09: + RollCurl, RollLoop, RollUncurl, RollDizzy; r01 takes key-for-key identical "
                        "(validation_r02.json r01_identical)"},
}
(HERE / "export.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
print("PACKAGE_EXPORTED", FBX, flush=True)
