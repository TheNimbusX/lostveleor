"""Evaluate baked actions in the animation master (Blender side helpers for validation and review)."""
import bpy
import numpy as np


def use_action(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.use_nla = False
    ad.action = act
    if len(act.slots):
        ad.action_slot = act.slots[0]


def goto(f):
    s = bpy.context.scene
    s.frame_set(int(f), subframe=float(f) - int(f))
    bpy.context.view_layer.update()


def mesh_co(mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    M = np.array(mesh.matrix_world)
    return co @ M[:3, :3].T + M[:3, 3]


def dominant(mesh):
    names = [g.name for g in mesh.vertex_groups]
    return np.array([names[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in mesh.data.vertices])


def rigid(mesh, bone, w=0.999):
    gi = mesh.vertex_groups[bone].index
    return np.array([any(g.group == gi and g.weight > w for g in v.groups) for v in mesh.data.vertices])


def tail(arm, bone):
    pb = arm.pose.bones[bone]
    return np.array(arm.matrix_world @ pb.tail)
