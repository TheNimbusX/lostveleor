"""Independent FBX verification helpers: import the exported package into an empty scene and sample it."""
import json
from pathlib import Path

import bpy
import numpy as np

VDIR = Path(__file__).resolve().parent
PKG = VDIR.parent
FBX = PKG / "ForestThorncaster.fbx"
EXPORT = json.loads((PKG / "export.json").read_text(encoding="utf-8"))
OUT = VDIR / "out"
OUT.mkdir(exist_ok=True)


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(FBX), anim_offset=0.0)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    return arm, mesh


def arm_actions(arm):
    """take name -> action for the armature (importer names them '<object>|<take>')."""
    out = {}
    for a in bpy.data.actions:
        obj, _, take = a.name.partition("|")
        if obj == arm.name:
            out[take] = a
    return out


def use(ob, act):
    ad = ob.animation_data or ob.animation_data_create()
    ad.action = act
    if len(act.slots):
        ad.action_slot = act.slots[0]


def fcurves(act):
    return [fc for lay in act.layers for st in lay.strips for bag in st.channelbags for fc in bag.fcurves]


def goto(f):
    bpy.context.scene.frame_set(int(f))
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


def rest_co(mesh):
    co = np.empty(len(mesh.data.vertices) * 3)
    mesh.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(mesh.matrix_world)
    return co @ M[:3, :3].T + M[:3, 3]


def group_names(mesh):
    return [g.name for g in mesh.vertex_groups]


def dominant(mesh):
    names = group_names(mesh)
    return np.array([names[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in mesh.data.vertices])


def rigid(mesh, bone, w=0.999):
    gi = mesh.vertex_groups[bone].index
    return np.array([any(g.group == gi and g.weight > w for g in v.groups) for v in mesh.data.vertices])


def spike_tip(mesh, arm, bone):
    """Vertex index of the arm-spike tip: rigid-to-bone vertex farthest from the bone head (bind pose)."""
    m = rigid(mesh, bone)
    rc = rest_co(mesh)
    head = np.array(arm.matrix_world @ arm.data.bones[bone].head_local)
    ids = np.nonzero(m)[0]
    return int(ids[np.argmax(np.linalg.norm(rc[ids] - head, axis=1))]), m


def dump(name, data):
    p = OUT / name
    p.write_text(json.dumps(data, indent=1, default=float))
    print("WROTE", p, flush=True)
