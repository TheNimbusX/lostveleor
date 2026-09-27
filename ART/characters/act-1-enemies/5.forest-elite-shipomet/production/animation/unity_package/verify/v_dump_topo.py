"""Dump topology + weights of the re-imported mesh for offline numpy analysis (edges, faces, weights, bones).

blender -b -P v_dump_topo.py
"""
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vcommon as vc  # noqa: E402

arm, mesh = vc.load()
E = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
mesh.data.edges.foreach_get("vertices", E)
np.save(vc.OUT / "edges.npy", E.reshape(-1, 2))
names = vc.group_names(mesh)
W = np.zeros((len(mesh.data.vertices), len(names)), dtype=np.float32)
for v in mesh.data.vertices:
    for g in v.groups:
        W[v.index, g.group] = g.weight
np.save(vc.OUT / "weights.npy", W)
bones = {b.name: {"head": list(arm.matrix_world @ b.head_local), "tail": list(arm.matrix_world @ b.tail_local),
                  "parent": b.parent.name if b.parent else None} for b in arm.data.bones}
(vc.OUT / "groups.json").write_text(json.dumps({"groups": names, "bones": bones}, indent=1))
print("TOPO_OK")
