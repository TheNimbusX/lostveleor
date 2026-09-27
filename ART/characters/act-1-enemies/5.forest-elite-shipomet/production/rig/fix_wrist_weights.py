"""Apply the wrist weight blend (rig_wrist.py) to the rig blend in place.

blender -b -P fix_wrist_weights.py -- <rig.blend> <report.json> [fore_s root_s]
The caller keeps a backup; the report lists the blend band and the edge weight jumps before/after.
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_weights as rw  # noqa: E402
import rig_wrist as rwr  # noqa: E402


def apply(arm, mesh, fore_s=-0.10, root_s=0.08):
    names = [g.name for g in mesh.vertex_groups]
    n = len(mesh.data.vertices)
    co = np.empty(n * 3)
    mesh.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(mesh.matrix_world)
    co = co @ M[:3, :3].T + M[:3, 3]
    E = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
    mesh.data.edges.foreach_get("vertices", E)
    E = E.reshape(-1, 2)
    W = np.zeros((n, len(names)))
    for v in mesh.data.vertices:
        for g in v.groups:
            W[v.index, g.group] = g.weight
    info = {}
    for side in ("Left", "Right"):
        b = arm.data.bones[side + "Hand"]
        wrist = np.array(arm.matrix_world @ b.head_local)
        tip = np.array(arm.matrix_world @ b.tail_local)
        W, info[side] = rwr.blend_wrist(co, E, W, names.index(side + "Hand"), names.index(side + "ForeArm"),
                                        wrist, tip, fore_s=fore_s, root_s=root_s)
    W, empty = rw.cleanup(W, limit=4, min_w=0.01)
    assert empty == 0
    for gi, name in enumerate(names):
        vg = mesh.vertex_groups[name]
        col = W[:, gi]
        on = np.nonzero(col > 0)[0]
        off = np.nonzero(col <= 0)[0]
        if len(off):
            vg.remove([int(i) for i in off])
        for i in on:
            vg.add([int(i)], float(col[i]), "REPLACE")
    info["max_influences"] = int((W > 0).sum(1).max())
    return info


if __name__ == "__main__" and "--" in sys.argv:
    args = sys.argv[sys.argv.index("--") + 1:]
    blend, report = Path(args[0]).resolve(), Path(args[1]).resolve()
    fs, rs_ = (float(args[2]), float(args[3])) if len(args) > 3 else (-0.10, 0.08)
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    bpy.context.preferences.filepaths.save_version = 0
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH" and o.parent == arm)
    info = apply(arm, mesh, fs, rs_)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    report.write_text(json.dumps(info, indent=1))
    print("WRIST_FIX_OK", json.dumps(info))
