"""Close-up renders of chosen poses: args are pose:view:tx,ty,tz:ortho specs.

usage: blender -b -P pose_closeups.py -- <rig.blend> <out_dir> <mode> spec [spec ...]
mode: texture | weights:<Bone>
"""
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_common as rc  # noqa: E402
import rig_poses as rp  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
src, out, mode = Path(args[0]).resolve(), Path(args[1]).resolve(), args[2]
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(src))
arm, mesh = rc.find_rig()
scene, cam = rc.setup_scene(760)
if mode == "texture":
    scene.display.shading.color_type = "TEXTURE"
elif mode == "stretch":
    import numpy as np
    me = mesh.data
    ev = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)
    rest = np.array([v.co[:] for v in me.vertices])
    rest_len = np.linalg.norm(rest[ev[:, 0]] - rest[ev[:, 1]], axis=1)
    attr = me.color_attributes.new("stretch", "BYTE_COLOR", "POINT")
    me.color_attributes.active_color = attr
    scene.display.shading.color_type = "VERTEX"

    def paint_stretch():
        dg = bpy.context.evaluated_depsgraph_get()
        em = mesh.evaluated_get(dg).to_mesh()
        pco = np.array([v.co[:] for v in em.vertices])
        mesh.evaluated_get(dg).to_mesh_clear()
        r = np.abs(np.log(np.maximum(np.linalg.norm(pco[ev[:, 0]] - pco[ev[:, 1]], axis=1), 1e-6)
                          / np.maximum(rest_len, 1e-6)))
        vmax = np.zeros(len(rest))
        np.maximum.at(vmax, ev[:, 0], r)
        np.maximum.at(vmax, ev[:, 1], r)
        t = np.clip(vmax / 0.7, 0, 1)  # log(2) ~ 0.7 -> full red
        for i, tt in enumerate(t):
            attr.data[i].color = (0.25 + 0.75 * tt, 0.25 + 0.4 * (1 - tt), 0.3 * (1 - tt), 1)
else:
    bone = mode.split(":")[1]
    me = mesh.data
    attr = me.color_attributes.new("heat", "BYTE_COLOR", "POINT")
    gi = mesh.vertex_groups[bone].index
    for v in me.vertices:
        w = next((g.weight for g in v.groups if g.group == gi), 0.0)
        attr.data[v.index].color = (w, 0.15 + 0.5 * w * (1 - w), 1 - w, 1)
    me.color_attributes.active_color = attr
    scene.display.shading.color_type = "VERTEX"
for spec in args[3:]:
    pose, view, tgt, ortho = spec.split(":")
    rp.apply(arm, pose)
    if mode == "stretch":
        paint_stretch()
    rc.place_camera(cam, view, target=tuple(float(c) for c in tgt.split(",")), ortho=float(ortho))
    tag = mode.replace(":", "_")
    scene.render.filepath = str(out / f"cu_{pose}_{view}_{tag}.png")
    bpy.ops.render.render(write_still=True)
