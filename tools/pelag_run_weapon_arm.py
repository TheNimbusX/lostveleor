"""Calm Pelag's sabre arm across the whole locomotion set.

``Pelag_MX_Run`` is an unarmed Mixamo sprint.  Its right arm swings through the
full free-arm range, and the sabre is a rigid child of ``mixamorig:RightHand``,
so in game the blade windmills once per step - the loudest defect in the walk.
Nothing about the legs, hips or spine is wrong, so this script leaves every one
of those curves alone and rewrites only the four bones of the weapon arm.

The replacement is the idle carry pose, blended with the authored swing: the
arm still moves with the run, just around a held sabre instead of a free fist.

Every clip in the locomotion blend tree is treated with the SAME weights.  The
run, the two transitions and the three strafes cross-fade into each other
continuously; calming one and not the others would trade a windmill for a pop.

Frame counts and frame rates stay exactly what they were, so no downstream
timing constant moves.

Run inside Blender:

    blender -b -noaudio -P tools/pelag_run_weapon_arm.py -- [--weight 1.0]

Staged FBX files land in ``artifacts/pelag-animation-build/exports``.  Promoting
them into ``Assets`` is a separate, reviewed step, exactly as in
``pelag_animation_pipeline.py``.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import sys

import bpy
from mathutils import Vector


PROJECT = Path(r"C:\Users\d.grab\Desktop\the-game")
MIXAMO = PROJECT / "razlom/Assets/Resources/Characters/Pelag_v5/Mixamo"
BUILD = PROJECT / "artifacts/pelag-animation-build"
EXPORTS = BUILD / "exports"
ORIGINALS = BUILD / "source-originals"
REPORT = BUILD / "pelag-run-weapon-arm.json"

IDLE = MIXAMO / "Pelag_MX_Idle.fbx"

# Every clip the directional locomotion tree can play, plus the two transitions
# that touch it. Leaving any of them out puts an arm pop on a blend seam.
CLIPS = (
    "Pelag_MX_Run",
    "Pelag_MX_RunStart",
    "Pelag_MX_RunStop",
    "Pelag_MX_StrafeLeft",
    "Pelag_MX_StrafeRight",
    "Pelag_MX_StrafeBack",
)

# Guards the input the way the main pipeline guards the sabre combos: once a
# staged clip is promoted the live file is no longer the original, and a second
# run must not calm an already-calm arm again. The run's hash is pinned because
# it is the raw Mixamo delivery; the rest are pipeline output and are pinned by
# the backup taken on the first run.
RUN_ORIGINAL_SHA256 = "1ce29935bbd367018e861980b58cebc17626053817df001ad18c1b53969bd2da"

# Frame of the idle take used as the carry pose. The main pipeline samples the
# same frame for RunStart/RunStop, so start, loop and stop agree on where the
# sabre lives.
IDLE_CARRY_FRAME = 2.0

# Shoulder down to hand. The fingers are left alone: they grip the hilt, and
# the hilt is a child of the hand, so re-posing them moves nothing on screen.
ARM_BONES = (
    "mixamorig:RightShoulder",
    "mixamorig:RightArm",
    "mixamorig:RightForeArm",
    "mixamorig:RightHand",
)

# How much of the carry pose replaces the free swing, per bone. The shoulder
# keeps the most authored motion - that is the part that reads as running - and
# the wrist keeps the least, because the wrist is what turns a swing into a
# windmill: it sits at the base of a metre of blade.
DEFAULT_WEIGHTS = {
    "mixamorig:RightShoulder": 0.45,
    "mixamorig:RightArm": 0.62,
    "mixamorig:RightForeArm": 0.80,
    "mixamorig:RightHand": 0.92,
}

# Every clip takes the weights flat, transitions included, and that is not an
# oversight. RunStart opens on the idle pose and RunStop closes on it; the idle
# arm ALREADY holds the sabre where the carry pose puts it, so full weight
# leaves those two seams exactly where they were and pulls only the run-facing
# half in. A ramp was tried and measured worse: it left the middle frames
# half-swung, and the hand's arc across RunStart came out at 39 degrees against
# 20 for the flat weights.
RAMPS: dict[str, object] = {}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def pristine(name: str) -> Path:
    """The untouched input for one clip, whether or not a staged one was promoted.

    The first run copies the live file into ``source-originals``; every later
    run reads that copy.  Without it, promoting the output and re-running would
    blend the carry pose into an arm that already holds it.
    """
    live = MIXAMO / f"{name}.fbx"
    backup = ORIGINALS / live.name
    if backup.is_file():
        return backup
    if not live.is_file():
        raise RuntimeError(f"Missing clip: {live}")
    if name == "Pelag_MX_Run" and sha256(live) != RUN_ORIGINAL_SHA256:
        raise RuntimeError(
            f"{live.name} is not the pinned Mixamo original and no backup exists; "
            f"live={sha256(live)}, expected={RUN_ORIGINAL_SHA256}"
        )
    backup.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(live, backup)
    return backup


def clear_scene() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for blocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes,
                   bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for block in list(blocks):
            if block.users == 0:
                blocks.remove(block)


def import_fbx(path: Path, *, with_animation: bool):
    before = set(bpy.context.scene.objects)
    result = bpy.ops.import_scene.fbx(filepath=str(path), use_anim=with_animation)
    if "FINISHED" not in result:
        raise RuntimeError(f"FBX import failed: {path}")
    imported = [o for o in bpy.context.scene.objects if o not in before]
    armatures = [o for o in imported if o.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError(f"No armature in {path}")
    return max(armatures, key=lambda o: len(o.data.bones)), imported


def set_fractional_frame(frame: float) -> None:
    base = math.floor(frame)
    bpy.context.scene.frame_set(base, subframe=frame - base)
    bpy.context.view_layer.update()


def sample_arm(path: Path, frame: float):
    clear_scene()
    armature, _ = import_fbx(path, with_animation=True)
    set_fractional_frame(frame)
    out = {}
    for name in ARM_BONES:
        location, rotation, _scale = armature.pose.bones[name].matrix_basis.decompose()
        out[name] = (location.copy(), rotation.normalized())
    return out


def hand_axis_sweep(armature) -> float:
    """Angular range (degrees) swept by the hand's long axis over the clip.

    The sabre is a rigid child of the hand, so this number is what the blade
    does on screen. It is the one figure worth comparing before and after.
    """
    scene = bpy.context.scene
    axes = []
    for frame in range(scene.frame_start, scene.frame_end + 1):
        set_fractional_frame(float(frame))
        matrix = armature.matrix_world @ armature.pose.bones["mixamorig:RightHand"].matrix
        axes.append(Vector((matrix[0][1], matrix[1][1], matrix[2][1])).normalized())
    worst = 0.0
    for index, first in enumerate(axes):
        for second in axes[index + 1:]:
            worst = max(worst, math.degrees(math.acos(max(-1.0, min(1.0, first.dot(second))))))
    return worst


def iter_fcurves(action):
    legacy = getattr(action, "fcurves", None)
    if legacy is not None:
        yield from legacy
        return
    for layer in action.layers:
        for strip in layer.strips:
            for channelbag in strip.channelbags:
                yield from channelbag.fcurves


def shift_action(action, delta: float) -> None:
    """Move every key by ``delta`` frames.

    Blender's FBX round trip is not time-neutral: a take imported at frames
    1..34 exports again as 2..35.  Left alone that shift accumulates one frame
    per pass, and Unity's importer pins these clips to explicit frame ranges -
    RunStart to 1..6, the strafes to 1..17 - so a drifting take would quietly
    start cutting the wrong frames and break the loop seam.  Pre-shifting by
    -1 makes the file that comes out carry the same times as the file that
    went in.
    """
    for curve in iter_fcurves(action):
        for key in curve.keyframe_points:
            key.co.x += delta
            key.handle_left.x += delta
            key.handle_right.x += delta
        curve.update()


def export(armature, imported, destination: Path) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in imported:
        if obj.type in {"ARMATURE", "MESH"}:
            obj.select_set(True)
    bpy.context.view_layer.objects.active = armature
    destination.parent.mkdir(parents=True, exist_ok=True)
    result = bpy.ops.export_scene.fbx(
        filepath=str(destination),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=False,
        armature_nodetype="NULL",
        bake_anim=True,
        bake_anim_use_all_bones=False,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        use_space_transform=True,
        path_mode="AUTO",
    )
    if "FINISHED" not in result or not destination.is_file():
        raise RuntimeError(f"FBX export failed: {destination}")


def calm(name: str, carry, weights: dict[str, float]) -> dict[str, object]:
    source = pristine(name)
    clear_scene()
    armature, imported = import_fbx(source, with_animation=True)
    scene = bpy.context.scene
    action = armature.animation_data.action
    frame_start = int(round(action.frame_range[0]))
    frame_end = int(round(action.frame_range[1]))
    scene.frame_start, scene.frame_end = frame_start, frame_end
    source_fps = scene.render.fps / scene.render.fps_base

    before_sweep = hand_axis_sweep(armature)

    # Sample first, write second: the action is still driving these bones, so
    # keying as we walk the timeline would feed edited poses back into the
    # samples that follow.
    ramp = RAMPS.get(name)
    span = max(1, frame_end - frame_start)
    authored = {}
    for frame in range(frame_start, frame_end + 1):
        set_fractional_frame(float(frame))
        scale = 1.0 if ramp is None else ramp((frame - frame_start) / span)
        pose = {}
        for bone_name in ARM_BONES:
            location, rotation, _scale = \
                armature.pose.bones[bone_name].matrix_basis.decompose()
            target_location, target_rotation = carry[bone_name]
            weight = weights[bone_name] * scale
            pose[bone_name] = (
                location.lerp(target_location, weight),
                rotation.normalized().slerp(target_rotation, weight).normalized(),
            )
        authored[frame] = pose

    # These deliveries carry a key on every frame, so writing every frame in the
    # range replaces the arm curves outright and leaves no stale key behind.
    for frame, pose in authored.items():
        for bone_name, (location, rotation) in pose.items():
            bone = armature.pose.bones[bone_name]
            bone.rotation_mode = "QUATERNION"
            bone.location = location
            bone.rotation_quaternion = rotation
            bone.keyframe_insert("location", frame=frame, group=bone_name)
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone_name)

    after_sweep = hand_axis_sweep(armature)

    shift_action(action, -1.0)
    action.use_frame_range = True
    action.frame_start, action.frame_end = frame_start - 1, frame_end - 1
    scene.frame_start, scene.frame_end = frame_start - 1, frame_end - 1

    destination = EXPORTS / f"{name}.fbx"
    export(armature, imported, destination)
    return {
        "source": str(source),
        "source_sha256": sha256(source),
        "output": str(destination),
        "frames": [frame_start, frame_end],
        "source_fps": source_fps,
        "weight_ramp": "flat" if name not in RAMPS else name,
        "hand_axis_sweep_degrees": {
            "before": round(before_sweep, 2),
            "after": round(after_sweep, 2),
        },
    }


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--weight", type=float, default=1.0,
                        help="Scales every per-bone carry weight. 0 leaves the "
                             "authored swing untouched, 1 uses the tuned set.")
    parser.add_argument("--clips", nargs="*", default=list(CLIPS))
    args = parser.parse_args(argv)

    weights = {name: max(0.0, min(1.0, value * args.weight))
               for name, value in DEFAULT_WEIGHTS.items()}
    carry = sample_arm(IDLE, IDLE_CARRY_FRAME)

    payload = {"weights": weights, "carry_frame": IDLE_CARRY_FRAME, "clips": {}}
    for name in args.clips:
        payload["clips"][name] = calm(name, carry, weights)
        print(f"Calmed weapon arm: {name}")

    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print("RESULT " + json.dumps(payload))


if __name__ == "__main__":
    main()
