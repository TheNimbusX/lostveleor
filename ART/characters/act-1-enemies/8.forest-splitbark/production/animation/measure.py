"""Per-frame measurements on the evaluated rig (used by probe_takes.py and bake_takes.py)."""
import bpy
import numpy as np

import splitter_pose as sp


def skin_min_z(mesh_obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh_obj.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    co = co.reshape(-1, 3)
    mw = np.array(mesh_obj.matrix_world)
    z = co @ mw[2, :3] + mw[2, 3]
    i = int(z.argmin())
    return float(z[i]), [round(float(v), 3) for v in co[i]]


def frame_metrics(arm, mesh, pose, pulled):
    pbs = arm.pose.bones
    out = {"pulled": {k: round(v, 4) for k, v in pulled.items()}}
    out["reach"] = {leg: round(sp.reach(arm, leg), 3) for leg in sp.LEGS}
    out["ik_err"] = {}
    for leg in sp.LEGS:
        ft = pbs["leg_%s_foot" % leg].head
        tg = pbs["CTRL_foot_" + leg].head
        out["ik_err"][leg] = round((ft - tg).length, 4)
    out["ankle"] = {leg: [round(v, 4) for v in pbs["leg_%s_foot" % leg].head] for leg in sp.LEGS}
    out["planted"] = [leg for leg in sp.LEGS if abs(pose.get("legs", {}).get(leg, {}).get("foot", (0, 0, 0))[2]) < 1e-6]
    out["beak"] = [round(v, 4) for v in pbs["head"].tail]
    out["shell"] = [round(pose.get("shell_L", {}).get("open", 0), 2), round(pose.get("shell_R", {}).get("open", 0), 2)]
    out["root_identity"] = pbs["root"].matrix_basis.is_identity if hasattr(pbs["root"].matrix_basis, "is_identity") else True
    z, where = skin_min_z(mesh)
    out["skin_min_z"] = round(z, 4)
    out["skin_min_at"] = where
    return out
