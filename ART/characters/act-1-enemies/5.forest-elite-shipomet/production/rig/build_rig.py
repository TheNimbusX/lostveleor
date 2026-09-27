"""Build the ForestThorncaster game rig from the Meshy auto-rig + approved 23k candidate.

usage: blender -b -P build_rig.py -- <meshy.glb> <candidate.glb> <out.blend> <stats.json>
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_common as rc  # noqa: E402
import rig_geo as rg  # noqa: E402
import rig_skeleton as sk  # noqa: E402
import rig_weights as rw  # noqa: E402
import rig_wrist as rwr  # noqa: E402

meshy_path, cand_path, out_blend, out_stats = [Path(a).resolve() for a in sys.argv[sys.argv.index("--") + 1:]]
stats = {"issues_fixed": []}

# 1. Meshy source: positions + weights in metres
m_arm, m_mesh = rc.load_meshy(meshy_path)
stats["meshy"] = {"armature_object_scale": list(m_arm.scale), "mesh_tris": rc.tri_count(m_mesh),
                  "bones": [b.name for b in m_arm.data.bones]}
stats["issues_fixed"].append("Deleted stray 'Icosphere' helper mesh from the Meshy GLB.")
m_names = [g.name for g in m_mesh.vertex_groups]
m_co = rg.world_coords(m_mesh)
m_w = np.zeros((len(m_co), len(sk.ORDER)))
for v in m_mesh.data.vertices:
    for g in v.groups:
        m_w[v.index, sk.ORDER.index(m_names[g.group])] += g.weight

# 2. Approved candidate mesh (same vertex positions, full PBR material)
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(cand_path))
cand = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
for o in list(bpy.data.objects):
    if o in before or o == cand:
        continue
    bpy.data.objects.remove(o, do_unlink=True)
mw = cand.matrix_world.copy()
cand.parent = None
cand.data.transform(mw)
cand.matrix_world = Matrix.Identity(4)
cand.name = cand.data.name = rc.MESH_NAME
co = rg.world_coords(cand)
stats["height_m"] = float(co[:, 2].max() - co[:, 2].min())
stats["ground_z"] = float(co[:, 2].min())
stats["footprint_m"] = [float(np.ptp(co[:, 0])), float(np.ptp(co[:, 1]))]

W, exact = rw.transfer_by_position(co, m_co, m_w)
stats["weight_transfer"] = {"candidate_verts": len(co), "exact_position_matches": exact,
                            "interpolated_from_neighbours": len(co) - exact}
for o in (m_mesh, m_arm):
    bpy.data.objects.remove(o, do_unlink=True)
col = {n: i for i, n in enumerate(sk.ORDER)}

# 3. Long arm spikes (largest red component in each forearm/hand region) -> wrist joints
img = next(i for i in bpy.data.images if i.name.startswith("Color_"))
red = rg.is_red(rg.vertex_colors_from_image(cand, img))
weld = rg.weld_ids(co)
joints = dict(sk.JOINTS)
spikes = {}
for side in ("Left", "Right"):
    memb = W[:, col[side + "ForeArm"]] + W[:, col[side + "Hand"]]
    mask = red & (memb > 0.5)
    roots = rg.welded_components(cand, weld, mask)
    ids, counts = np.unique(roots[mask], return_counts=True)
    spike = mask & (roots == ids[np.argmax(counts)])
    p = co[spike]
    top = p[:, 2].max()
    base = p[p[:, 2] > top - 0.05].mean(0)
    tip = p[np.argmin(p[:, 2])]
    axis = (tip - base) / np.linalg.norm(tip - base)
    wrist = base - axis * 0.08
    elbow = Vector(joints[side + "ForeArm"][1])
    joints[side + "Hand"] = (side + "ForeArm", tuple(wrist), tuple(tip))
    spikes[side] = {"mask": spike, "base": base, "tip": tip, "wrist": wrist}
    stats.setdefault("arm_spikes", {})[side] = {
        "vertices": int(spike.sum()), "base": np.round(base, 3).tolist(), "tip": np.round(tip, 3).tolist(),
        "length_m": round(float(np.linalg.norm(tip - base)), 3), "wrist_joint": np.round(wrist, 3).tolist(),
        "forearm_length_m": round((Vector(wrist) - elbow).length, 3)}
stats["issues_fixed"].append("Wrists moved from mid-forearm (Meshy) to 8 cm above the spike root on the spike axis.")

arm = sk.build_armature(joints, rc.ARM_NAME)
heads = {b.name: np.array(b.head_local) for b in arm.data.bones}
tails = {b.name: np.array(b.tail_local) for b in arm.data.bones}

# 4. Re-split every limb's Meshy membership along the new joints
limb_cfg = {
    "LArm": [0.07, 0.05], "RArm": [0.07, 0.05],
    "LLeg": [0.09, 0.07, 0.05], "RLeg": [0.09, 0.07, 0.05],
}
for chain, hw in limb_cfg.items():
    bones = sk.CHAINS[chain]
    pts = [heads[b] for b in bones] + [tails[bones[-1]]]
    rw.redistribute(W, [col[b] for b in bones], pts, hw, co)
# toes: only claws in front of the toe joint follow ToeBase, the rest stays on Foot
for side in ("Left", "Right"):
    tb, ft = col[side + "ToeBase"], col[side + "Foot"]
    gate = rw.smoothstep(heads[side + "ToeBase"][1] + 0.02, heads[side + "ToeBase"][1] - 0.05, co[:, 1])
    moved = W[:, tb] * (1 - gate)
    W[:, tb] -= moved
    W[:, ft] += moved
stats["issues_fixed"].append("Leg joints re-measured (Meshy ankles were at 0.46 m left vs 0.77 m right; "
                             "left toe bone had no weights) and leg weights re-split along knee 0.94 m / ankle 0.38 m.")

# 5. Crown branches were shared between Head, neck and both shoulders -> rigid Head
arm_memb = sum(W[:, col[b]] for b in sk.CHAINS["LArm"] + sk.CHAINS["RArm"])
crown = rw.smoothstep(2.11, 2.20, co[:, 2]) * (np.abs(co[:, 0]) < 0.45) * (1 - np.clip(arm_memb * 2, 0, 1))
rw.blend_to_bone(W, crown, col["Head"])
stats["issues_fixed"].append("Head crown branches were weighted to LeftShoulder/RightShoulder/neck; "
                             "everything above the skull base is now rigid to Head.")

# 6. Long spikes rigid to Hand (no stretch when the wrist or elbow bends)
for side, sp in spikes.items():
    W[sp["mask"]] = 0.0
    W[sp["mask"], col[side + "Hand"]] = 1.0
stats["issues_fixed"].append("Long arm spikes rigidly weighted to LeftHand/RightHand (weight 1.0).")

# 6b. r03 (animation verification): the red spike component reached into the wrist band, so a Hand-1.0 spike vertex
# sat next to a ForeArm-1.0 bark vertex and one edge took the whole wrist bend (up to 16 cm stretch in Burst/Death).
# Harmonic Hand/ForeArm blend over the wrist band; the spike beyond its root (8 cm past the wrist) stays rigid.
ev = np.empty(len(cand.data.edges) * 2, dtype=np.int64)
cand.data.edges.foreach_get("vertices", ev)
for side in ("Left", "Right"):
    W, info = rwr.blend_wrist(co, ev.reshape(-1, 2), W, col[side + "Hand"], col[side + "ForeArm"],
                              heads[side + "Hand"], tails[side + "Hand"], fore_s=-0.10, root_s=0.08)
    stats.setdefault("wrist_blend", {})[side] = info
stats["issues_fixed"].append("Wrist: Hand/ForeArm weights blended harmonically from 10 cm above to 8 cm below the wrist "
                             "joint (spike beyond its root stays rigid); no single-edge seam at the spike root.")

# 7. Seam-split duplicates share weights, prune, limit 4, normalise
W = rw.weld_average(W, weld)
W, empty = rw.cleanup(W, limit=4, min_w=0.01)
assert empty == 0, f"{empty} vertices lost all weights"
for i, n in enumerate(sk.ORDER):
    vg = cand.vertex_groups.new(name=n)
    nz = np.nonzero(W[:, i])[0]
    for vi in nz:
        vg.add([int(vi)], float(W[vi, i]), "REPLACE")

mod = cand.modifiers.new("Armature", "ARMATURE")
mod.object = arm
cand.parent = arm
cand.matrix_parent_inverse = Matrix.Identity(4)
for m in list(bpy.data.meshes):
    if m.users == 0:
        bpy.data.meshes.remove(m)
for m in list(bpy.data.materials):
    if m.users == 0:
        bpy.data.materials.remove(m)
for im in list(bpy.data.images):
    if im.users == 0:
        bpy.data.images.remove(im)
scene = bpy.context.scene
scene.render.fps = 30
scene.unit_settings.system = "METRIC"
scene.unit_settings.scale_length = 1.0
arm["source"] = "Meshy auto-rig r01 skeleton/weights on the approved 23k candidate mesh"
arm["front_axis"] = "-Y"
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(out_blend))
stats["joints"] = {n: np.round(heads[n], 3).tolist() for n in sk.ORDER}
out_stats.write_text(json.dumps(stats, indent=1), encoding="utf-8")
print("BUILD_OK", json.dumps(stats["arm_spikes"]))
