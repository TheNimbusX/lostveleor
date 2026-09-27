"""Validation numbers for a skinned mesh + armature (used by export_rig.py)."""
import numpy as np


def weight_stats(mesh):
    names = [g.name for g in mesh.vertex_groups]
    hist = {}
    unweighted = 0
    worst_dev = 0.0
    per_bone = {n: 0 for n in names}
    rigid_hand = {}
    for v in mesh.data.vertices:
        gs = [g for g in v.groups if g.weight > 0.0]
        hist[len(gs)] = hist.get(len(gs), 0) + 1
        if not gs:
            unweighted += 1
            continue
        worst_dev = max(worst_dev, abs(sum(g.weight for g in gs) - 1.0))
        for g in gs:
            per_bone[names[g.group]] += 1
        if len(gs) == 1 and names[gs[0].group].endswith("Hand"):
            rigid_hand[names[gs[0].group]] = rigid_hand.get(names[gs[0].group], 0) + 1
    return {
        "vertices": len(mesh.data.vertices),
        "influence_histogram": {str(k): v for k, v in sorted(hist.items())},
        "max_influences": max(hist),
        "unweighted_vertices": unweighted,
        "worst_weight_sum_deviation": round(worst_dev, 6),
        "vertices_per_bone": per_bone,
        "vertices_rigid_to_hand": rigid_hand,
    }


def mesh_bounds(mesh):
    co = np.array([mesh.matrix_world @ v.co for v in mesh.data.vertices])
    return {"min": np.round(co.min(0), 4).tolist(), "max": np.round(co.max(0), 4).tolist(),
            "height_m": round(float(co[:, 2].max() - co[:, 2].min()), 4)}


def bone_table(arm):
    out = []
    for b in arm.data.bones:
        out.append({"name": b.name, "parent": b.parent.name if b.parent else None,
                    "head": [round(c, 4) for c in arm.matrix_world @ b.head_local],
                    "tail": [round(c, 4) for c in arm.matrix_world @ b.tail_local],
                    "deform": b.use_deform})
    return out
