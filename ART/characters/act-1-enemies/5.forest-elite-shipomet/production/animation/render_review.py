"""Render review frames of every take (game camera + side) from the animation master.

blender -b ForestThorncaster_Anim_r01.blend -P render_review.py -- [Take,...] [res=480]
-> ../review/animation_r01/frames/<Take>/<view>/0000.png
Walk: the floor scrolls backward at 2.2 m/s so planted claws should stick to the checker.
Shot: review-only overlay = red flight line during the aim (15-21) and the flying thorn after release.
"""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_eval as ae  # noqa: E402
import review_scene as rs  # noqa: E402
from anim_apply import ORDER, PREFIX  # noqa: E402
from takes_attack import SHOT_DIR, SHOT_MUZZLE, SHOT_RELEASE  # noqa: E402
from takes_loco import WALK_SPEED  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
only = args[0].split(",") if args and args[0] else ORDER
res = int(args[1]) if len(args) > 1 else 480
OUT = HERE.parent / "review" / "animation_r01" / "frames"
FRAMING = {  # take: (target, game ortho, side ortho)
    "LineCast": ((0, -0.3, 1.55), 4.9, 4.6), "Burst": ((0, -0.1, 1.45), 4.8, 4.4),
    "Shot": ((0, -0.6, 1.3), 5.0, 4.8), "Death": ((0, -0.4, 0.9), 5.4, 5.0),
}
arm = bpy.data.objects["ARM_ForestThorncaster"]
scene, cam, floor = rs.setup(res)
floor.data.materials[0].node_tree.nodes["Image Texture"].image.pixels = [
    c * 1.45 if i % 4 != 3 else c for i, c in enumerate(floor.data.materials[0].node_tree.nodes["Image Texture"].image.pixels)]
scene.display.shading.background_color = (0.42, 0.46, 0.47)


def red_mat():
    m = bpy.data.materials.new("REVIEW_Red")
    m.diffuse_color = (0.85, 0.08, 0.05, 1)
    return m


def shot_props():
    red = red_mat()
    bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.05, depth=0.45)
    thorn = bpy.context.active_object
    thorn.name = "REVIEW_Thorn"
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.012, depth=10.0)
    line = bpy.context.active_object
    line.name = "REVIEW_AimLine"
    for o in (thorn, line):
        o.data.materials.append(red)
        o.color = (0.85, 0.08, 0.05, 1)
    return thorn, line


scene.display.shading.color_type = "TEXTURE"
props = None
for name in only:
    act = bpy.data.actions[PREFIX + name]
    ae.use_action(arm, act)
    n = int(act.frame_end)
    target, gs, ss = FRAMING.get(name, (None, None, None))
    if name == "Shot" and props is None:
        props = shot_props()
    for view in ("game", "side"):
        rs.place(cam, view, gs if view == "game" else ss, target)
        if view == "side":
            d = (cam.location - Vector(target or rs.TARGET)).normalized()
            d = Vector((d.x, d.y, 0.2)).normalized()
            t = Vector(target or rs.TARGET)
            cam.location = t + d * 30
            cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        folder = OUT / name / view
        folder.mkdir(parents=True, exist_ok=True)
        for f in range(n + 1):
            ae.goto(f)
            floor.location.y = (WALK_SPEED * f / 30.0) % 1.0 if name == "Walk" else 0.0
            if props:
                thorn, line = props
                vis_t = name == "Shot" and f >= SHOT_RELEASE
                vis_l = name == "Shot" and 15 <= f <= SHOT_RELEASE + 2
                thorn.hide_render, line.hide_render = not vis_t, not vis_l
                dirv = Vector(SHOT_DIR)
                if vis_l:
                    line.location = Vector(SHOT_MUZZLE) + dirv * 5.0   # line of fire from the release point
                    line.rotation_euler = dirv.to_track_quat("Z", "Y").to_euler()
                if vis_t:
                    start = Vector(SHOT_MUZZLE)
                    thorn.location = start + dirv * (0.2 + 16.0 * (f - SHOT_RELEASE) / 30.0)
                    thorn.rotation_euler = dirv.to_track_quat("Z", "Y").to_euler()
            scene.render.filepath = str(folder / f"{f:04d}.png")
            bpy.ops.render.render(write_still=True)
        print("RENDERED", name, view, n + 1, flush=True)
print("RENDER_DONE")
