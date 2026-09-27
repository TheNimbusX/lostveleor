"""Helpers for bake_takes.py: control keys, deform sampling, basis maths, action writing."""
import bpy
from mathutils import Matrix

import splitter_pose as sp

FK_KEYED = ("body", "spine", "neck", "head", "shell_L", "shell_R")


def deform_names(arm):
    return [b.name for b in arm.data.bones if b.use_deform]


def control_values(arm):
    """Snapshot of the editable controls after splitter_pose.apply(): {data_path: [values]}."""
    pbs = arm.pose.bones
    out = {}
    for n in FK_KEYED + tuple("CTRL_foot_" + leg for leg in sp.LEGS):
        out['pose.bones["%s"].location' % n] = list(pbs[n].location)
        out['pose.bones["%s"].rotation_quaternion' % n] = list(pbs[n].rotation_quaternion)
    for leg in sp.LEGS:
        for part, c in zip(("lower", "foot"), sp.constraints(arm, leg)):
            owner = "leg_%s_%s" % (leg, part)
            out['pose.bones["%s"].constraints["%s"].influence' % (owner, c.name)] = [c.influence]
    return out


def write_control_action(arm, name, snapshots, cyclic):
    """snapshots: list of control_values() per integer frame. Bezier keys, not evaluated here."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    for path in snapshots[0]:
        group = path.split('"')[1]
        for i in range(len(snapshots[0][path])):
            fc = act.fcurve_ensure_for_datablock(arm, path, index=i, group_name=group)
            fc.keyframe_points.clear()
            fc.keyframe_points.add(len(snapshots))
            co = []
            for f, snap in enumerate(snapshots):
                co += [float(f), snap[path][i]]
            fc.keyframe_points.foreach_set("co", co)
            fc.update()
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, len(snapshots) - 1
    act.use_cyclic = cyclic
    return act


def sample(arm, names):
    return {n: arm.pose.bones[n].matrix.copy() for n in names}


def basis_from_pose(arm, pose_mats):
    """Local (basis) matrices that reproduce the given armature-space pose matrices without constraints."""
    out = {}
    for n, m in pose_mats.items():
        b = arm.data.bones[n]
        if b.parent is None:
            out[n] = b.matrix_local.inverted() @ m
        else:
            rel = b.parent.matrix_local.inverted() @ b.matrix_local
            out[n] = rel.inverted() @ pose_mats[b.parent.name].inverted() @ m
    return out


def write_action(arm, name, frames, bases, cyclic):
    """frames: list of float frames; bases: list of {bone: Matrix} (same length). LINEAR keys."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    names = list(bases[0].keys())
    prev = {}
    series = {n: ([], []) for n in names}
    for f, basis in zip(frames, bases):
        for n in names:
            loc, rot, _ = basis[n].decompose()
            if n in prev and rot.dot(prev[n]) < 0:
                rot.negate()
            prev[n] = rot
            series[n][0].append(loc)
            series[n][1].append(rot)
    for n in names:
        for prop, vals, size in (("location", series[n][0], 3), ("rotation_quaternion", series[n][1], 4)):
            for i in range(size):
                fc = act.fcurve_ensure_for_datablock(arm, 'pose.bones["%s"].%s' % (n, prop), index=i, group_name=n)
                fc.keyframe_points.clear()
                fc.keyframe_points.add(len(frames))
                co = []
                for f, v in zip(frames, vals):
                    co += [f, v[i]]
                fc.keyframe_points.foreach_set("co", co)
                fc.keyframe_points.foreach_set("interpolation", [1] * len(frames))  # LINEAR
                fc.update()
    act.use_frame_range = True
    act.frame_start = frames[0]
    act.frame_end = frames[-1]
    act.use_cyclic = cyclic
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return act


def strip_controls(arm):
    """Remove constraints and CTRL bones: the baked armature is deform-only FK."""
    for pb in arm.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for eb in list(arm.data.edit_bones):
        if eb.name.startswith("CTRL_"):
            arm.data.edit_bones.remove(eb)
    bpy.ops.object.mode_set(mode="OBJECT")
    for coll in list(arm.data.collections):
        if coll.name == "CTRL":
            arm.data.collections.remove(coll)


def identity_rest(arm):
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
