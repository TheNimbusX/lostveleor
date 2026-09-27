"""Render every take from the game camera (52 deg down, 3/4 front) and the left side.
blender -b ForestRootSnarer_Anim_r01.blend -P render_review.py -- <out_dir> [take ...]
Frames land in <out_dir>/<take>/<view>/NNNN.png; the walk scrolls the checker at 2.4 m/s.
Framing is fixed per take and view, fitted to the projected mesh over the whole take (plus the
ground under it), so the mob fills the frame without the camera moving."""
import sys, json
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, render_util, clip_walk  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
out = Path(argv[0]).resolve()
only = set(argv[1:])
RES = (640, 440)
MARGIN = 1.10
sc = bpy.context.scene
arm = bpy.data.objects[anim_core.ARM_NAME]
mesh = bpy.data.objects[anim_core.MESH_NAME]
rig = anim_core.Rig(arm, mesh)
for pb in arm.pose.bones:
    for c in pb.constraints:
        c.influence = 0.0
ground, cam = render_util.setup(RES)
meta = {}
for act in list(bpy.data.actions):
    if only and act.name not in only:
        continue
    arm.animation_data.action = act
    n = int(act.frame_range[1])
    clouds = []
    for f in range(0, n + 1, 2):
        sc.frame_set(f)
        co = rig.mesh_co()
        clouds.append(co[::7])
    pts = np.concatenate(clouds)
    foot = pts.copy()
    foot[:, 2] = 0.0                      # keep the ground contact line in frame
    pts = np.concatenate([pts, foot])
    meta[act.name] = {"frames": n, "views": {}}
    for view in ("game", "side"):
        d = render_util.view_dir(view)
        right = d.cross(Vector((0, 0, 1))).normalized()
        up = right.cross(d).normalized()
        pr = pts @ np.array(right)
        pu = pts @ np.array(up)
        cr, cu = (pr.min() + pr.max()) / 2, (pu.min() + pu.max()) / 2
        center = right * cr + up * cu
        w, h = pr.max() - pr.min(), pu.max() - pu.min()
        ortho = max(w, h * RES[0] / RES[1]) * MARGIN
        render_util.place_camera(cam, view, center, ortho)
        folder = out / act.name / view
        folder.mkdir(parents=True, exist_ok=True)
        for old in folder.glob("*.png"):
            old.unlink()
        for f in range(n + 1):
            sc.frame_set(f)
            render_util.scroll_ground(ground, f, clip_walk.V if act.name.endswith("Walk") else 0.0)
            sc.render.filepath = str(folder / f"{f:04d}.png")
            bpy.ops.render.render(write_still=True)
        meta[act.name]["views"][view] = {"ortho_m": round(ortho, 3), "center": [round(c, 3) for c in center]}
        print("RENDERED", act.name, view, n + 1, flush=True)
prev = out / "render_meta.json"
if only and prev.exists():      # partial render (e.g. a take added later): keep the other takes' entries
    meta = {**json.loads(prev.read_text()), **meta}
prev.write_text(json.dumps(dict(sorted(meta.items())), indent=1))
print("RENDER_DONE", flush=True)
