"""Build the ForestRootSnarer game rig: armature, two heat passes, rule-based weight fix, IK helpers.
blender -b -P build_rig.py -- <candidate.blend> <out_rig.blend> <out_build_json>
"""
import sys, json
from pathlib import Path
import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from rig_layout import BONES, MANUAL_BONES  # noqa: E402
import rig_build_lib as lib  # noqa: E402
import rig_weights as rw  # noqa: E402
from rig_controls import add_ik_controls  # noqa: E402

CFG = {"clav_r0": 0.14, "clav_r1": 0.30, "thigh_z0": 0.44, "thigh_z1": 0.56,
       "slab_x0": 0.42, "slab_x1": 0.50, "jaw_z0": 0.40, "jaw_z1": 0.47,
       "smooth_iterations": 6}

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst, rep = (Path(a).resolve() for a in argv[:3])
bpy.ops.wm.open_mainfile(filepath=str(src))
scene = bpy.context.scene
scene.render.fps = 30
mesh_obj = next(o for o in scene.objects if o.type == "MESH")
mesh_obj.name = mesh_obj.data.name = "SM_ForestRootSnarer_LOD0"
for g in list(mesh_obj.vertex_groups):
    mesh_obj.vertex_groups.remove(g)

arm_obj = lib.create_armature(scene)
deform = [b[0] for b in BONES if b[0] != "root"]
heat_bones = [n for n in deform if n not in MANUAL_BONES]
A, resA = lib.heat_pass(scene, mesh_obj, arm_obj, set(heat_bones))
B, resB = lib.heat_pass(scene, mesh_obj, arm_obj, set(rw.TORSO))

me = mesh_obj.data
co = [tuple(v.co) for v in me.vertices]
bones = {b.name: (tuple(b.head_local), tuple(b.tail_local)) for b in arm_obj.data.bones}
W, rigid, stats = rw.compose(co, A, B, bones, CFG)
adj = [[] for _ in co]
for e in me.edges:
    a, b = e.vertices
    adj[a].append(b); adj[b].append(a)
W = rw.smooth(W, adj, rigid, CFG["smooth_iterations"])
W = rw.limit_normalize(W, 4)
lib.write_weights(mesh_obj, W, deform)
lib.bind(mesh_obj, arm_obj)
ik = add_ik_controls(arm_obj)

build = {
    "source": str(src), "heat_A": resA, "heat_B": resB, "cfg": CFG, "rule_stats": stats,
    "heat_A_unweighted": sum(1 for a in A if not a), "heat_B_unweighted": sum(1 for b in B if not b),
    "ik": ik,
}
lib.activate(arm_obj)
bpy.ops.wm.save_as_mainfile(filepath=str(dst))
rep.write_text(json.dumps(build, indent=2), encoding="utf-8")
print("BUILD_DONE", json.dumps(build), flush=True)
