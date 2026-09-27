"""Where do beak / shells / tail land in the review cameras? (no save)

blender -b ForestSplitter_Baked_r01.blend -P probe_camera.py
"""
import sys
from pathlib import Path

import bpy
from bpy_extras.object_utils import world_to_camera_view

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import render_setup as rs  # noqa: E402

scene = rs.setup()
arm = bpy.data.objects["ARM_ForestSplitter"]
cams = {"game": rs.game_camera(), "side": rs.side_camera()}
scene.frame_set(0)
pts = {"beak": arm.matrix_world @ arm.pose.bones["head"].tail,
       "shell_L": arm.matrix_world @ arm.pose.bones["shell_L"].tail,
       "shell_R": arm.matrix_world @ arm.pose.bones["shell_R"].tail,
       "body_head": arm.matrix_world @ arm.pose.bones["body"].head}
for n, cam in cams.items():
    print("CAM", n, tuple(round(v, 2) for v in cam.location))
    for k, p in pts.items():
        v = world_to_camera_view(scene, cam, p)
        print("  ", k, tuple(round(c, 3) for c in p), "-> img x=%.2f y(from top)=%.2f depth=%.2f" % (v.x, 1 - v.y, v.z))
