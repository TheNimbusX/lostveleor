"""Solve named test poses, report reach / ground numbers, render game+side+front stills.
blender -b <rig.blend> -P probe_pose.py -- <out_dir> <module> [ortho]
<module>.poses(rig) -> {name: pose dict}
"""
import sys, json, importlib
from pathlib import Path
import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, render_util  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
out = Path(argv[0]).resolve()
out.mkdir(parents=True, exist_ok=True)
mod = importlib.import_module(argv[1])
ortho = float(argv[2]) if len(argv) > 2 else 2.6
arm = bpy.data.objects[anim_core.ARM_NAME]
mesh = bpy.data.objects[anim_core.MESH_NAME]
rig = anim_core.Rig(arm, mesh)
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
    for c in pb.constraints:
        c.influence = 0.0
ground, cam = render_util.setup((480, 400))
report = {}
for name, P in mod.poses(rig).items():
    M, info = rig.solve(P)
    B = rig.basis(M)
    for pb in arm.pose.bones:
        pb.matrix_basis = B.get(pb.name, pb.matrix_basis.copy()) if pb.name in B else pb.matrix_basis
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    co = np.empty(len(ev.data.vertices) * 3)
    ev.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    report[name] = {"reach_err": {k: round(v, 4) for k, v in info["reach_err"].items()},
                    "slab_min_z": {k: round(v, 4) for k, v in info["slab_min_z"].items()},
                    "mesh_min_z": round(float(co[:, 2].min()), 4), "mesh_max_z": round(float(co[:, 2].max()), 4),
                    "hand_socket": {s: [round(c, 3) for c in M[f"{s}_hand"].translation] for s in "LR"},
                    "ankle": {s: [round(c, 3) for c in M[f"{s}_foot"].translation] for s in "LR"}}
    ctr = ((co.min(0) + co.max(0)) / 2).tolist()
    for view in ("game", "side", "front"):
        render_util.place_camera(cam, view, ctr, ortho)
        bpy.context.scene.render.filepath = str(out / f"{name}_{view}.png")
        bpy.ops.render.render(write_still=True)
(out / "probe.json").write_text(json.dumps(report, indent=1))
print("PROBE", json.dumps(report))
