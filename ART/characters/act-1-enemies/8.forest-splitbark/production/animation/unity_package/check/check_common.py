"""Independent re-import helpers for the ForestSplitter.fbx check (step 3).

Starts from an empty scene, imports only the FBX, and gives numpy access to the
evaluated skin, bone world matrices and per-vertex dominant bone.
All positions are Blender world space (front -Y, up +Z) of the re-imported FBX.
"""
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
PKG = HERE.parent
FBX = PKG / "ForestSplitter.fbx"
EXPORT = PKG / "export.json"
scene = bpy.context.scene


def empty_scene():
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.armatures, bpy.data.actions,
                 bpy.data.materials, bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for block in list(coll):
            coll.remove(block)


def import_fbx():
    empty_scene()
    bpy.ops.import_scene.fbx(filepath=str(FBX), anim_offset=0.0)
    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in scene.objects if o.type == "MESH")
    return arm, mesh


def take_action(name):
    return next((a for a in bpy.data.actions if not a.name.startswith("FacingGuide")
                 and (a.name == name or a.name.endswith("|" + name))), None)


def use(ob, act):
    ob.animation_data_create()
    ob.animation_data.action = act
    if act is not None and len(act.slots):
        ob.animation_data.action_slot = act.slots[0]


def goto(f):
    scene.frame_set(int(np.floor(f)), subframe=float(f - np.floor(f)))


def skin(mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    mw = np.array(mesh.matrix_world)
    return co @ mw[:3, :3].T + mw[:3, 3]


def bones(arm):
    mw = arm.matrix_world
    return {pb.name: mw @ pb.matrix for pb in arm.pose.bones}


def dominant_bone(mesh):
    """Per vertex: name of the heaviest group, and number of non-zero influences."""
    names = [g.name for g in mesh.vertex_groups]
    dom, count, wmax = [], [], []
    for v in mesh.data.vertices:
        ws = [(g.weight, names[g.group]) for g in v.groups if g.weight > 1e-6]
        count.append(len(ws))
        if ws:
            w, n = max(ws)
            dom.append(n)
            wmax.append(w)
        else:
            dom.append(None)
            wmax.append(0.0)
    return np.array(dom, dtype=object), np.array(count), np.array(wmax)


def edges(mesh):
    e = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
    mesh.data.edges.foreach_get("vertices", e)
    return e.reshape(-1, 2)
