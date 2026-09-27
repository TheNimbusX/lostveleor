"""Render frames of the re-imported FBX (game camera 52 deg down, 3/4; optional side / close-up on a bone).

blender -b -P v_render.py -- <fbx|-> <outdir> Take:f,f,f[:view[:bone[:scale]]] ...
  view = game | side | front ; bone = close-up target (bone head at that frame)
"""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

VD = Path(__file__).resolve().parent
sys.path.insert(0, str(VD))
sys.path.insert(0, str(VD.parents[1]))
import vcommon as vc  # noqa: E402
import review_scene as rs  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
if argv[0] != "-":
    vc.FBX = Path(argv[0])
outdir = Path(argv[1]).resolve()
outdir.mkdir(parents=True, exist_ok=True)
arm, mesh = vc.load()
for img in bpy.data.images:          # FBX path_mode STRIP -> textures sit in unity_package/Textures
    p = vc.PKG / "Textures" / Path(img.filepath).name
    if p.exists():
        img.filepath = str(p)
        img.reload()
scene, cam, _ = rs.setup(640)
acts = vc.arm_actions(arm)
specs = argv[2:]
if specs and specs[0] == "GREY":   # flat clay shading: shows stretched / torn geometry better than the dark texture
    specs = specs[1:]
    sh = scene.display.shading
    sh.color_type = "SINGLE"
    sh.single_color = (0.72, 0.68, 0.6)
    sh.show_object_outline = False
    sh.cavity_ridge_factor = sh.cavity_valley_factor = 1.0
for spec in specs:
    parts = spec.split(":")
    take, frames = parts[0], [int(x) for x in parts[1].split(",")]
    view = parts[2] if len(parts) > 2 else "game"
    bone = parts[3] if len(parts) > 3 and parts[3] else None
    scale = float(parts[4]) if len(parts) > 4 else None
    vc.use(arm, acts["ForestThorncaster_" + take])
    for f in frames:
        vc.goto(f)
        tgt = None
        if bone and bone.startswith("v") and bone[1:].isdigit():      # close-up on a deformed vertex
            tgt = Vector(vc.mesh_co(mesh)[int(bone[1:])])
        elif bone:
            tgt = arm.matrix_world @ arm.pose.bones[bone].head
        if view in ("front", "back"):
            t = Vector(tgt) if tgt is not None else rs.TARGET
            cam.location = t + Vector((0, -30 if view == "front" else 30, 2)).normalized() * 30
            cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
            cam.data.ortho_scale = scale or 4.6
        else:
            rs.place(cam, view, scale, tgt)
        tag = f"{take}_{view}{'_' + bone if bone else ''}_{f:03d}"
        scene.render.filepath = str(outdir / (tag + ".png"))
        bpy.ops.render.render(write_still=True)
        print("RENDERED", tag, flush=True)
