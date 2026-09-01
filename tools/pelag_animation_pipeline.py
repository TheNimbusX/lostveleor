"""Author, export, and validate Pelag's 30 fps in-place animation set.

Run this inside Blender 5.2 through ``tools/blender_mcp_exec.py``.  The script
only writes staged FBX files and QA renders under ``artifacts``.  Promotion to
Unity's Assets folder is deliberately a separate reviewed step.
"""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
from pathlib import Path
import shutil
from typing import Callable

import bpy
from mathutils import Euler, Quaternion, Vector


PROJECT = Path(r"C:\Users\d.grab\Desktop\the-game")
MIXAMO = PROJECT / "razlom/Assets/Resources/Characters/Pelag_v5/Mixamo"
BUILD = PROJECT / "artifacts/pelag-animation-build"
EXPORTS = BUILD / "exports"
PREVIEWS = BUILD / "previews"
REPORT = BUILD / "pelag-animation-build-report.json"
ORIGINALS = BUILD / "source-originals"

FPS = 30
# Keep the evaluated skinned shell just above the gameplay ground plane.  The
# authored foot targets below describe the intended contact, but the imported
# Pelag shell extends a few centimetres below the foot-bone heads on crouched
# poses.  A small positive margin absorbs FBX interpolation/round-trip error
# without changing pose rotations or the simulation's in-place root policy.
MESH_GROUND_MARGIN_M = 0.0015
HIPS = "mixamorig:Hips"
SPINE = "mixamorig:Spine"
SPINE1 = "mixamorig:Spine1"
SPINE2 = "mixamorig:Spine2"
LOWER_BONES = {
    "mixamorig:LeftUpLeg",
    "mixamorig:LeftLeg",
    "mixamorig:LeftFoot",
    "mixamorig:LeftToeBase",
    "mixamorig:LeftToe_End",
    "mixamorig:RightUpLeg",
    "mixamorig:RightLeg",
    "mixamorig:RightFoot",
    "mixamorig:RightToeBase",
    "mixamorig:RightToe_End",
}

SOURCE = {
    "anchor": MIXAMO / "Pelag_MX_AnchorAttack.fbx",
    "idle": MIXAMO / "Pelag_MX_Idle.fbx",
    "run": MIXAMO / "Pelag_MX_Run.fbx",
    "saber": MIXAMO / "Pelag_MX_SaberCombo.fbx",
    "dual": MIXAMO / "Pelag_MX_DualCombo.fbx",
}
EXPECTED_ORIGINAL_HASHES = {
    "saber": "f250e904a0f3632598c80698d3e6ac8e904e79ccd45957293f3a39af7ef7221d",
    "dual": "0b9ac7a071b1475af356c7ec0e1162d403445296f5df2122e710d3dccdc59eb8",
}


@dataclass
class BoneTransform:
    location: Vector
    rotation: Quaternion

    def copy(self) -> "BoneTransform":
        return BoneTransform(self.location.copy(), self.rotation.copy())


Pose = dict[str, BoneTransform]


@dataclass
class ClipBuild:
    output_name: str
    base_source: str
    poses: list[Pose]
    contact_frames: list[int]
    loop: bool
    timing: dict[str, object]
    foot_clearance_m: list[float] | None = None

    @property
    def frame_start(self) -> int:
        return 1

    @property
    def frame_end(self) -> int:
        return len(self.poses)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def pristine_source(source_key: str) -> Path:
    """Return a hash-verified original, even after staged retimes are promoted."""
    live = SOURCE[source_key]
    expected_hash = EXPECTED_ORIGINAL_HASHES.get(source_key)
    if expected_hash is None:
        return live
    backup = ORIGINALS / live.name
    if live.is_file() and sha256(live) == expected_hash:
        if not backup.is_file():
            backup.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(live, backup)
        return live
    if backup.is_file() and sha256(backup) == expected_hash:
        return backup
    actual = sha256(live) if live.is_file() else "missing"
    raise RuntimeError(
        f"No hash-verified original for {live.name}; live={actual}, expected={expected_hash}"
    )


def clear_scene_data() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for datablocks in (
        bpy.data.actions,
        bpy.data.armatures,
        bpy.data.meshes,
        bpy.data.cameras,
        bpy.data.lights,
        bpy.data.materials,
    ):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def import_fbx(path: Path, *, with_animation: bool) -> tuple[bpy.types.Object, list[bpy.types.Object]]:
    before = set(bpy.context.scene.objects)
    result = bpy.ops.import_scene.fbx(filepath=str(path), use_anim=with_animation)
    if "FINISHED" not in result:
        raise RuntimeError(f"FBX import failed: {path}")
    imported = [obj for obj in bpy.context.scene.objects if obj not in before]
    armatures = [obj for obj in imported if obj.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError(f"No armature in {path}")
    armature = max(armatures, key=lambda obj: len(obj.data.bones))
    return armature, imported


def source_action(armature: bpy.types.Object) -> bpy.types.Action:
    if armature.animation_data and armature.animation_data.action:
        return armature.animation_data.action
    actions = list(bpy.data.actions)
    if len(actions) == 1:
        if armature.animation_data is None:
            armature.animation_data_create()
        armature.animation_data.action = actions[0]
        return actions[0]
    raise RuntimeError(f"No unambiguous source action for {armature.name}")


def set_fractional_frame(frame: float) -> None:
    base = math.floor(frame)
    bpy.context.scene.frame_set(base, subframe=frame - base)
    bpy.context.view_layer.update()


def capture_pose(armature: bpy.types.Object) -> Pose:
    pose: Pose = {}
    for bone in armature.pose.bones:
        location, rotation, _scale = bone.matrix_basis.decompose()
        pose[bone.name] = BoneTransform(location.copy(), rotation.normalized())
    return pose


def sample_file(source_key: str, frames: list[float]) -> tuple[list[Pose], set[str]]:
    clear_scene_data()
    armature, _ = import_fbx(pristine_source(source_key), with_animation=True)
    action = source_action(armature)
    armature.animation_data.action = action
    samples = []
    for frame in frames:
        set_fractional_frame(frame)
        samples.append(capture_pose(armature))
    return samples, {bone.name for bone in armature.data.bones}


def clone_pose(pose: Pose) -> Pose:
    return {name: transform.copy() for name, transform in pose.items()}


def blend_pose(first: Pose, second: Pose, weight: float, bones: set[str] | None = None) -> Pose:
    weight = max(0.0, min(1.0, weight))
    result = clone_pose(first)
    selected = bones if bones is not None else set(first).intersection(second)
    for name in selected:
        if name not in first or name not in second:
            continue
        location = first[name].location.lerp(second[name].location, weight)
        rotation = first[name].rotation.slerp(second[name].rotation, weight).normalized()
        result[name] = BoneTransform(location, rotation)
    return result


def rotate_local(pose: Pose, bone_name: str, axis: tuple[float, float, float], degrees: float) -> None:
    if bone_name not in pose or abs(degrees) < 1e-6:
        return
    delta = Quaternion(Vector(axis), math.radians(degrees))
    pose[bone_name].rotation = (pose[bone_name].rotation @ delta).normalized()


def piecewise(frame: float, knots: list[tuple[float, float]]) -> float:
    if frame <= knots[0][0]:
        return knots[0][1]
    for (left_frame, left_value), (right_frame, right_value) in zip(knots, knots[1:]):
        if frame <= right_frame:
            alpha = (frame - left_frame) / (right_frame - left_frame)
            return left_value + (right_value - left_value) * alpha
    return knots[-1][1]


def root_lock(poses: list[Pose]) -> None:
    """Keep Mixamo horizontal root fixed while retaining authored vertical motion.

    On the canonical imported Mixamo rest basis, local X/Y span Blender's
    world-ground X/Y and local Z is vertical.  Unity also bakes root Y/XZ into
    pose on import.
    """
    root = poses[0][HIPS].location.copy()
    for pose in poses:
        pose[HIPS].location.x = root.x
        pose[HIPS].location.y = root.y


def add_vertical_offset(pose: Pose, source_units: float) -> None:
    pose[HIPS].location.z += source_units


def make_anchor_leap() -> ClipBuild:
    # Tick 0 is the cast/release.  Game.Sim starts its exact eight forced-motion
    # ticks on the following tick, so flight occupies animation frames 2..9.
    frames = list(range(1, 17))
    anchor_map = [
        piecewise(frame, [(1, 47), (2, 60), (5, 70), (9, 77), (10, 152), (16, 167)])
        for frame in frames
    ]
    run_map = [piecewise(frame, [(1, 1), (9, 30), (16, 34)]) for frame in frames]
    anchor, _ = sample_file("anchor", anchor_map)
    run, _ = sample_file("run", run_map)
    poses: list[Pose] = []
    lower_weights = [0.0, 0.55, 0.9, 1.0, 1.0, 1.0, 1.0, 0.85, 0.45, 0.0] + [0.0] * 6
    vertical = [0, -25, -55, -85, -110, -125, -110, -85, -50, 12, 15, 10, 6, 3, 0, 0]
    lean = [0, 5, 10, 15, 18, 20, 20, 18, 15, -5, -4, -3, -2, -1, 0, 0]
    for index, base_pose in enumerate(anchor):
        pose = blend_pose(base_pose, run[index], lower_weights[index], LOWER_BONES)
        add_vertical_offset(pose, vertical[index])
        rotate_local(pose, HIPS, (1, 0, 0), lean[index])
        rotate_local(pose, SPINE2, (1, 0, 0), -0.35 * lean[index])
        poses.append(pose)
    root_lock(poses)
    return ClipBuild(
        "Pelag_MX_AnchorLeap",
        "anchor",
        poses,
        [10],
        False,
        {
            "cast_release_frame": 1,
            "pull_flight_frames": [2, 9],
            "pull_flight_ticks": 8,
            "landing_frame": 10,
            "landing_crouch_frames": [10, 12],
        },
        foot_clearance_m=[0.0, 0.18, 0.38, 0.58, 0.76, 0.88, 0.82, 0.65, 0.35, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
    )


def make_anchor_sweep() -> ClipBuild:
    # The wide throw is already at its release silhouette on tick 0.  The
    # two-handed reverse pull follows Game.Sim exactly for frames 2..11.
    frames = list(range(1, 17))
    source_map = [
        piecewise(
            frame,
            [(1, 47), (2, 62), (5, 107), (11, 47), (12, 82), (14, 137), (16, 167)],
        )
        for frame in frames
    ]
    poses, _ = sample_file("anchor", source_map)
    yaw = [-35, -48, -58, -68, -72, -56, -38, -18, 2, 18, 30, 18, 10, 5, 2, 0]
    crouch = [0, 0, 0, 0, 0, -1, -2, -3, -5, -7, -9, -7, -5, -3, -1, 0]
    pull_lean = [0, 2, 4, 6, 8, 10, 13, 16, 19, 22, 24, 18, 12, 7, 3, 0]
    for index, pose in enumerate(poses):
        rotate_local(pose, HIPS, (0, 1, 0), yaw[index])
        rotate_local(pose, SPINE, (0, 1, 0), -0.24 * yaw[index])
        rotate_local(pose, SPINE1, (0, 1, 0), -0.18 * yaw[index])
        rotate_local(pose, SPINE2, (0, 1, 0), -0.12 * yaw[index])
        rotate_local(pose, SPINE, (1, 0, 0), 0.28 * pull_lean[index])
        rotate_local(pose, SPINE1, (1, 0, 0), 0.34 * pull_lean[index])
        rotate_local(pose, SPINE2, (1, 0, 0), 0.38 * pull_lean[index])
        add_vertical_offset(pose, crouch[index])
    root_lock(poses)
    return ClipBuild(
        "Pelag_MX_AnchorSweep",
        "anchor",
        poses,
        [5, 11],
        False,
        {
            "wide_throw_frame": 1,
            "far_extension_frame": 5,
            "two_hand_yank_frames": [2, 11],
            "two_hand_yank_ticks": 10,
            "recovery_frames": [12, 16],
        },
    )


def make_chain_step() -> ClipBuild:
    run_frames = [1, 8, 15, 22, 29, 1]
    anchor_frames = [62, 66, 70, 73, 77, 62]
    run, _ = sample_file("run", run_frames)
    anchor, _ = sample_file("anchor", anchor_frames)
    poses = []
    vertical = [0, -35, -75, -45, 35, 0]
    lean = [0, 12, 24, 32, 16, 0]
    upper = set(anchor[0]) - LOWER_BONES - {HIPS}
    for index in range(6):
        pose = blend_pose(run[index], anchor[index], 1.0, upper)
        add_vertical_offset(pose, vertical[index])
        rotate_local(pose, SPINE, (1, 0, 0), 0.25 * lean[index])
        rotate_local(pose, SPINE1, (1, 0, 0), 0.35 * lean[index])
        rotate_local(pose, SPINE2, (1, 0, 0), 0.40 * lean[index])
        poses.append(pose)
    root_lock(poses)
    poses[-1] = clone_pose(poses[0])
    return ClipBuild(
        "Pelag_MX_ChainStep",
        "run",
        poses,
        [5],
        True,
        {
            "loop_ticks": 5,
            "contact_frame": 5,
            "contact_flash_frames": [5],
            "repeat_limit": 4,
            "loop_seam": "frame 6 equals frame 1; Unity excludes the duplicate endpoint",
        },
        foot_clearance_m=[0.0, 0.22, 0.40, 0.28, 0.0, 0.0],
    )


def make_run_start() -> ClipBuild:
    idle, _ = sample_file("idle", [2] * 6)
    run, _ = sample_file("run", [1, 3, 5, 7, 9, 11])
    weights = [0.0, 0.12, 0.35, 0.62, 0.85, 1.0]
    lean = [0, -5, -9, -11, -7, 0]
    poses = []
    for index, weight in enumerate(weights):
        pose = blend_pose(idle[index], run[index], weight)
        rotate_local(pose, HIPS, (1, 0, 0), lean[index])
        poses.append(pose)
    root_lock(poses)
    return ClipBuild(
        "Pelag_MX_RunStart",
        "run",
        poses,
        [],
        False,
        {"duration_ticks": 5, "transition": "idle to run"},
    )


def make_run_stop() -> ClipBuild:
    run, _ = sample_file("run", [18, 20, 22, 24, 26, 28])
    idle, _ = sample_file("idle", [2] * 6)
    weights = [0.0, 0.18, 0.42, 0.68, 0.88, 1.0]
    lean = [0, 5, 10, 11, 6, 0]
    poses = []
    for index, weight in enumerate(weights):
        pose = blend_pose(run[index], idle[index], weight)
        rotate_local(pose, HIPS, (1, 0, 0), lean[index])
        poses.append(pose)
    root_lock(poses)
    return ClipBuild(
        "Pelag_MX_RunStop",
        "run",
        poses,
        [],
        False,
        {"duration_ticks": 5, "transition": "run to idle"},
    )


def make_strafe(name: str, yaw_degrees: float, backwards: bool = False) -> ClipBuild:
    if backwards:
        source_frames = [piecewise(frame, [(1, 33), (16, 1)]) for frame in range(1, 17)] + [33]
    else:
        source_frames = [piecewise(frame, [(1, 1), (16, 33)]) for frame in range(1, 17)] + [1]
    poses, _ = sample_file("run", source_frames)
    for pose in poses:
        rotate_local(pose, HIPS, (0, 1, 0), yaw_degrees)
        rotate_local(pose, SPINE, (0, 1, 0), -0.28 * yaw_degrees)
        rotate_local(pose, SPINE1, (0, 1, 0), -0.2 * yaw_degrees)
        rotate_local(pose, SPINE2, (0, 1, 0), -0.18 * yaw_degrees)
        if backwards:
            rotate_local(pose, HIPS, (1, 0, 0), 11)
            rotate_local(pose, SPINE2, (1, 0, 0), -7)
    root_lock(poses)
    poses[-1] = clone_pose(poses[0])
    return ClipBuild(
        name,
        "run",
        poses,
        [],
        True,
        {
            "loop_ticks": 16,
            "direction": "back" if backwards else ("left" if yaw_degrees < 0 else "right"),
            "loop_seam": "frame 17 equals frame 1",
        },
    )


def make_retimed_saber() -> ClipBuild:
    # Preserve the two source segments in seconds while converting 60 -> 30
    # fps.  Both authored contacts were 12 simulation ticks after their
    # segment start; the new contacts are exactly six ticks after the start.
    frames = list(range(1, 75))
    source_frames = []
    for frame in frames:
        if frame <= 25:
            source_frames.append(piecewise(frame, [(1, 2), (7, 35), (25, 50)]))
        else:
            source_frames.append(piecewise(frame, [(25, 50), (31, 74), (74, 147)]))
    poses, _ = sample_file("saber", source_frames)
    root_lock(poses)
    return ClipBuild(
        "Pelag_MX_SaberCombo",
        "saber",
        poses,
        [7, 31],
        False,
        {
            "attack_a": {
                "clip_frames": [1, 25],
                "contact_frame": 7,
                "contact_tick": 6,
                "contact_normalized": 6 / 24,
            },
            "attack_b": {
                "clip_frames": [25, 74],
                "contact_frame": 31,
                "contact_tick": 6,
                "contact_normalized": 6 / 49,
            },
            "source_fps": 60,
            "output_fps": 30,
            "duration_preserved_seconds": 73 / 30,
        },
    )


def make_retimed_dual() -> ClipBuild:
    frames = list(range(1, 77))
    source_frames = [piecewise(frame, [(1, 2), (7, 40), (76, 152)]) for frame in frames]
    poses, _ = sample_file("dual", source_frames)
    root_lock(poses)
    return ClipBuild(
        "Pelag_MX_DualCombo",
        "dual",
        poses,
        [7],
        False,
        {
            "clip_frames": [1, 76],
            "first_contact_frame": 7,
            "first_contact_tick": 6,
            "first_contact_normalized": 6 / 75,
            "source_fps": 60,
            "output_fps": 30,
            "duration_preserved_seconds": 75 / 30,
        },
    )


def iter_fcurves(action: bpy.types.Action):
    legacy = getattr(action, "fcurves", None)
    if legacy is not None:
        yield from legacy
        return
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                channelbag = strip.channelbag(slot, ensure=False)
                if channelbag:
                    yield from channelbag.fcurves


def author_action(armature: bpy.types.Object, clip: ClipBuild) -> bpy.types.Action:
    if armature.animation_data is None:
        armature.animation_data_create()
    armature.animation_data_clear()
    armature.animation_data_create()
    action = bpy.data.actions.new(clip.output_name)
    armature.animation_data.action = action
    for frame, pose in enumerate(clip.poses, start=1):
        for bone_name, transform in pose.items():
            bone = armature.pose.bones.get(bone_name)
            if bone is None:
                raise RuntimeError(f"Missing output bone {bone_name} in {clip.output_name}")
            bone.rotation_mode = "QUATERNION"
            bone.location = transform.location
            bone.rotation_quaternion = transform.rotation
            bone.keyframe_insert("location", frame=frame, group=bone_name)
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone_name)
    for curve in iter_fcurves(action):
        for keyframe in curve.keyframe_points:
            keyframe.interpolation = "LINEAR"
    action.use_frame_range = True
    action.frame_start = clip.frame_start
    action.frame_end = clip.frame_end
    bpy.context.scene.frame_start = clip.frame_start
    bpy.context.scene.frame_end = clip.frame_end
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.render.fps_base = 1.0
    return action


def foot_height(armature: bpy.types.Object) -> float:
    candidates = (
        "mixamorig:LeftFoot",
        "mixamorig:LeftToeBase",
        "mixamorig:LeftToe_End",
        "mixamorig:RightFoot",
        "mixamorig:RightToeBase",
        "mixamorig:RightToe_End",
    )
    heights = [
        (armature.matrix_world @ armature.pose.bones[name].matrix.translation).z
        for name in candidates
        if name in armature.pose.bones
    ]
    if not heights:
        raise RuntimeError("Pelag foot bones are unavailable for ground alignment")
    return min(heights)


def align_foot_clearance(armature: bpy.types.Object, clip: ClipBuild) -> None:
    if clip.foot_clearance_m is None:
        return
    if len(clip.foot_clearance_m) != len(clip.poses):
        raise RuntimeError(f"Foot-clearance map length mismatch for {clip.output_name}")
    scene = bpy.context.scene
    for frame, target_height in enumerate(clip.foot_clearance_m, start=clip.frame_start):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        root = armature.pose.bones[HIPS]
        current_height = foot_height(armature)
        original_vertical = root.location.z
        rest_basis = armature.matrix_world.to_3x3() @ root.bone.matrix_local.to_3x3()
        # The imported Mixamo rig uses local Z for vertical motion.  Solving
        # local Y here silently fights the horizontal in-place lock and, on
        # this rig, has effectively zero influence on world-space height.
        derivative = (rest_basis @ Vector((0.0, 0.0, 1.0))).z
        if abs(derivative) < 1e-6:
            raise RuntimeError(f"Cannot solve vertical root channel for {clip.output_name} frame {frame}")
        root.location.z = original_vertical + (target_height - current_height) / derivative
        root.keyframe_insert("location", frame=frame, group=HIPS)
    scene.frame_set(clip.frame_start)
    bpy.context.view_layer.update()


def mesh_min_height(armature: bpy.types.Object, imported: list[bpy.types.Object]) -> float:
    """Return the lowest evaluated skinned vertex in world metres."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    minimum = float("inf")
    for obj in imported:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            matrix = evaluated.matrix_world
            for vertex in mesh.vertices:
                minimum = min(minimum, float((matrix @ vertex.co).z))
        finally:
            evaluated.to_mesh_clear()
    if minimum == float("inf"):
        raise RuntimeError("No evaluated Pelag mesh available for ground alignment")
    return minimum


def align_mesh_clearance(
    armature: bpy.types.Object,
    imported: list[bpy.types.Object],
    clip: ClipBuild,
) -> None:
    """Lift only Hips when the evaluated shell would cross the ground plane.

    The correction is translational: rotations, authored frame count, root X/Y
    lock, and all contact frame indices remain untouched.  We intentionally
    evaluate the skinned mesh instead of relying on foot-bone heads, because
    the shell has a measurable ankle/sole thickness below those heads.
    """
    scene = bpy.context.scene
    root = armature.pose.bones[HIPS]
    rest_basis = armature.matrix_world.to_3x3() @ root.bone.matrix_local.to_3x3()
    derivative = (rest_basis @ Vector((0.0, 0.0, 1.0))).z
    if abs(derivative) < 1e-6:
        raise RuntimeError("Cannot solve vertical root channel for mesh ground alignment")

    for frame in range(clip.frame_start, clip.frame_end + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        current_height = mesh_min_height(armature, imported)
        if current_height >= MESH_GROUND_MARGIN_M:
            continue
        root.location.z += (MESH_GROUND_MARGIN_M - current_height) / derivative
        root.keyframe_insert("location", frame=frame, group=HIPS)

    # Newly inserted keys default to Bezier in Blender; keep the deterministic
    # linear interpolation used by all authored pose channels.
    action = source_action(armature)
    for curve in iter_fcurves(action):
        for keyframe in curve.keyframe_points:
            keyframe.interpolation = "LINEAR"
    scene.frame_set(clip.frame_start)
    bpy.context.view_layer.update()


def export_animation(
    armature: bpy.types.Object,
    imported: list[bpy.types.Object],
    destination: Path,
) -> None:
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


def look_at(obj: bpy.types.Object, target: Vector) -> None:
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def prepare_preview(imported: list[bpy.types.Object]) -> bpy.types.Object:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 384
    scene.render.resolution_y = 384
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.world.color = (0.018, 0.022, 0.03)
    # Blender 5.2 exposes the AgX-prefixed look when the default view
    # transform is active; older project scenes may expose the unprefixed
    # identifier.  Resolve the available enum instead of hard-coding one.
    for look in ("AgX - Medium High Contrast", "Medium High Contrast"):
        try:
            scene.view_settings.look = look
            break
        except TypeError:
            continue

    clay = bpy.data.materials.new("Pelag_QA_Clay")
    clay.use_nodes = True
    principled = clay.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (0.20, 0.43, 0.56, 1.0)
    principled.inputs["Roughness"].default_value = 0.72
    for obj in imported:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            slot.material = clay

    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    ground = bpy.context.object
    ground.name = "QA_Ground"
    ground_mat = bpy.data.materials.new("Pelag_QA_Ground")
    ground_mat.diffuse_color = (0.035, 0.045, 0.055, 1)
    ground.data.materials.append(ground_mat)

    bpy.ops.object.light_add(type="AREA", location=(2.3, -2.8, 3.2))
    key = bpy.context.object
    key.data.energy = 950
    key.data.shape = "DISK"
    key.data.size = 2.2
    look_at(key, Vector((0, 0, 0.65)))
    bpy.ops.object.light_add(type="AREA", location=(-2.2, 1.8, 2.0))
    fill = bpy.context.object
    fill.data.energy = 520
    fill.data.color = (1.0, 0.34, 0.12)
    fill.data.size = 2.0
    look_at(fill, Vector((0, 0, 0.7)))

    bpy.ops.object.camera_add(location=(2.35, -3.65, 1.35))
    camera = bpy.context.object
    camera.data.lens = 63
    look_at(camera, Vector((0, 0, 0.62)))
    scene.camera = camera
    return camera


def attach_preview_mesh(
    output_armature: bpy.types.Object,
    source_key: str,
) -> list[bpy.types.Object]:
    source_armature, source_objects = import_fbx(pristine_source(source_key), with_animation=False)
    meshes = [obj for obj in source_objects if obj.type == "MESH"]
    for mesh in meshes:
        world_matrix = mesh.matrix_world.copy()
        for modifier in mesh.modifiers:
            if modifier.type == "ARMATURE":
                modifier.object = output_armature
        mesh.parent = output_armature
        mesh.matrix_world = world_matrix
    bpy.data.objects.remove(source_armature, do_unlink=True)
    return [output_armature, *meshes]


def render_preview(clip: ClipBuild, imported: list[bpy.types.Object], armature: bpy.types.Object) -> list[str]:
    prepare_preview(imported)
    action = source_action(armature)
    roundtrip_start = round(action.frame_range[0])
    frame_offset = roundtrip_start - clip.frame_start
    key_frames = sorted(
        {
            1,
            clip.frame_end,
            *clip.contact_frames,
            max(1, round((clip.frame_start + clip.frame_end) * 0.5)),
        }
    )
    output_dir = PREVIEWS / clip.output_name
    output_dir.mkdir(parents=True, exist_ok=True)
    paths = []
    for frame in key_frames:
        bpy.context.scene.frame_set(frame + frame_offset)
        bpy.context.scene.render.filepath = str(output_dir / f"frame_{frame:03d}.png")
        bpy.ops.render.render(write_still=True)
        paths.append(str(Path(bpy.context.scene.render.filepath)))
    return paths


def validate_export(path: Path, clip: ClipBuild, expected_bones: set[str]) -> dict[str, object]:
    clear_scene_data()
    armature, imported = import_fbx(path, with_animation=True)
    action = source_action(armature)
    found_bones = {bone.name for bone in armature.data.bones}
    if found_bones != expected_bones:
        missing = sorted(expected_bones - found_bones)
        extra = sorted(found_bones - expected_bones)
        raise RuntimeError(f"Bone mismatch in {path.name}; missing={missing}, extra={extra}")
    start, end = (round(value) for value in action.frame_range)
    # Blender's FBX importer reports exported animation one frame later than
    # its authored range (the existing Mixamo files behave the same way: Unity
    # frame 1 imports to Blender frame 2).  Validate duration, not this importer
    # display offset.
    expected_intervals = clip.frame_end - clip.frame_start
    if end - start != expected_intervals:
        raise RuntimeError(
            f"Frame duration mismatch in {path.name}: {(start, end)} has {end - start} intervals; "
            f"expected {expected_intervals}"
        )

    mesh_triangles = sum(
        sum(len(polygon.vertices) - 2 for polygon in obj.data.polygons)
        for obj in imported
        if obj.type == "MESH"
    )
    source_armature, _source_objects = import_fbx(pristine_source(clip.base_source), with_animation=False)
    max_bind_head_error = 0.0
    max_bind_tail_error = 0.0
    for source_bone in source_armature.data.bones:
        output_bone = armature.data.bones[source_bone.name]
        source_head = source_armature.matrix_world @ source_bone.head_local
        source_tail = source_armature.matrix_world @ source_bone.tail_local
        output_head = armature.matrix_world @ output_bone.head_local
        output_tail = armature.matrix_world @ output_bone.tail_local
        max_bind_head_error = max(max_bind_head_error, (source_head - output_head).length)
        max_bind_tail_error = max(max_bind_tail_error, (source_tail - output_tail).length)
    if max(max_bind_head_error, max_bind_tail_error) > 1e-4:
        raise RuntimeError(
            f"Bind pose drift in {path.name}: head={max_bind_head_error}, tail={max_bind_tail_error}"
        )

    scale_curves = [curve for curve in iter_fcurves(action) if curve.data_path.endswith(".scale")]
    max_scale_error = 0.0
    root_positions = []
    first_pose = None
    last_pose = None
    min_mesh_height = float("inf")
    min_mesh_frame = start
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for bone in armature.pose.bones:
            _location, _rotation, scale = bone.matrix_basis.decompose()
            max_scale_error = max(max_scale_error, *(abs(component - 1.0) for component in scale))
        frame_mesh_height = mesh_min_height(armature, imported)
        if frame_mesh_height < min_mesh_height:
            min_mesh_height = frame_mesh_height
            min_mesh_frame = frame
        root = armature.pose.bones[HIPS]
        # Validate in Blender world coordinates after the FBX round-trip;
        # armature-space axes are rotated by the importer's FBX conversion.
        world_root = armature.matrix_world @ root.matrix.translation
        root_positions.append(world_root.xy.copy())
        if frame == start:
            first_pose = capture_pose(armature)
        if frame == end:
            last_pose = capture_pose(armature)
    if max_scale_error > 1e-4:
        raise RuntimeError(f"Non-unit animated bone scale in {path.name}: {max_scale_error}")
    horizontal_drift = max(
        (position - root_positions[0]).length
        for position in root_positions
    )
    if horizontal_drift > 0.001:
        raise RuntimeError(f"Horizontal root drift in {path.name}: {horizontal_drift} m")
    if min_mesh_height < -1e-4:
        raise RuntimeError(
            f"Skinned mesh penetrates ground in {path.name}: {min_mesh_height} m at frame {min_mesh_frame}"
        )

    seam_location = 0.0
    seam_rotation_degrees = 0.0
    if clip.loop and first_pose is not None and last_pose is not None:
        for name in expected_bones:
            seam_location = max(
                seam_location,
                (first_pose[name].location - last_pose[name].location).length,
            )
            seam_rotation_degrees = max(
                seam_rotation_degrees,
                math.degrees(first_pose[name].rotation.rotation_difference(last_pose[name].rotation).angle),
            )
        if seam_location > 1e-3 or seam_rotation_degrees > 0.05:
            raise RuntimeError(
                f"Loop seam mismatch in {path.name}: {seam_location}, {seam_rotation_degrees} degrees"
            )
    return {
        "bone_count": len(found_bones),
        "bone_names_preserved": True,
        "fbx_roundtrip_frame_range": [start, end],
        "authored_frame_range": [clip.frame_start, clip.frame_end],
        "fbx_importer_frame_offset": start - clip.frame_start,
        "fps": FPS,
        "duration_seconds": (end - start) / FPS,
        "scale_curve_count_after_fbx_roundtrip": len(scale_curves),
        "authored_scale_curve_count": 0,
        "max_scale_error": max_scale_error,
        "horizontal_root_drift_m": horizontal_drift,
        "min_mesh_ground_height_m": min_mesh_height,
        "min_mesh_ground_frame": min_mesh_frame,
        "mesh_ground_margin_m": MESH_GROUND_MARGIN_M,
        "bind_pose_max_head_error_m": max_bind_head_error,
        "bind_pose_max_tail_error_m": max_bind_tail_error,
        "embedded_preview_mesh_triangles": mesh_triangles,
        "loop_seam_max_location": seam_location,
        "loop_seam_max_rotation_degrees": seam_rotation_degrees,
    }


def build_and_export(clip: ClipBuild) -> tuple[Path, set[str], list[str]]:
    clear_scene_data()
    armature, imported = import_fbx(pristine_source(clip.base_source), with_animation=False)
    expected_bones = {bone.name for bone in armature.data.bones}
    if set(clip.poses[0]) != expected_bones:
        raise RuntimeError(f"Pose/rig mismatch before authoring {clip.output_name}")
    author_action(armature, clip)
    align_foot_clearance(armature, clip)
    align_mesh_clearance(armature, imported, clip)
    destination = EXPORTS / f"{clip.output_name}.fbx"
    export_animation(armature, imported, destination)
    clear_scene_data()
    preview_armature, preview_imported = import_fbx(destination, with_animation=True)
    preview_paths = render_preview(clip, preview_imported, preview_armature)
    return destination, expected_bones, preview_paths


def main() -> None:
    for source_key in EXPECTED_ORIGINAL_HASHES:
        pristine_source(source_key)

    EXPORTS.mkdir(parents=True, exist_ok=True)
    PREVIEWS.mkdir(parents=True, exist_ok=True)
    builders: list[Callable[[], ClipBuild]] = [
        make_anchor_leap,
        make_anchor_sweep,
        make_chain_step,
        make_run_start,
        make_run_stop,
        lambda: make_strafe("Pelag_MX_StrafeLeft", -38),
        lambda: make_strafe("Pelag_MX_StrafeRight", 38),
        lambda: make_strafe("Pelag_MX_StrafeBack", 0, backwards=True),
        make_retimed_saber,
        make_retimed_dual,
    ]

    report: dict[str, object] = {
        "blender_version": bpy.app.version_string,
        "render_engine": "BLENDER_EEVEE",
        "fps": FPS,
        "root_motion": "in-place; local Mixamo X/Z fixed; Unity Bake Into Pose remains enabled",
        "clips": {},
    }
    staged = []
    for builder in builders:
        clip = builder()
        destination, expected_bones, previews = build_and_export(clip)
        validation = validate_export(destination, clip, expected_bones)
        report["clips"][clip.output_name] = {
            "fbx": str(destination),
            "frames": [clip.frame_start, clip.frame_end],
            "frame_count_inclusive": clip.frame_end - clip.frame_start + 1,
            "duration_ticks": clip.frame_end - clip.frame_start,
            "duration_seconds": (clip.frame_end - clip.frame_start) / FPS,
            "contact_frames": clip.contact_frames,
            "loop": clip.loop,
            "timing": clip.timing,
            "previews": previews,
            "validation": validation,
        }
        staged.append(destination.name)
        print(f"Built and validated {destination.name}")

    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Pelag animation build complete: {', '.join(staged)}")
    print(f"Report: {REPORT}")


if __name__ == "__main__":
    main()
