"""Render review frames of the baked takes (the .blend is not saved back).

blender -b ForestSplitter_Baked_r01.blend -P render_review.py -- [take-substrings...] [--seq] [--only-seq]
Frames -> ../review/animation_r01/frames/<Take>_<view>/NNNN.png (game + side), and the in-game style
sequence (parent Death, two 0.6-scale children Pop sideways 1 m in 8 frames, then Idle) -> frames/Sequence_game/.
Walk: the ground scrolls backward at 3.1 m/s so planted feet must stick to the checker.
"""
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import render_setup as rs  # noqa: E402
import takes_loops as tl  # noqa: E402
from takes import ORDER, TAKES  # noqa: E402

OUT = HERE.parent / "review" / "animation_r01" / "frames"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
names = [a for a in args if not a.startswith("--")]
scene = rs.setup()
ground = rs.ground()
cams = {"game": rs.game_camera(), "side": rs.side_camera()}
arm = bpy.data.objects["ARM_ForestSplitter"]
mesh = bpy.data.objects["SM_ForestSplitter_LOD0"]


def use(obj, take):
    obj.animation_data.action = bpy.data.actions[take]
    if len(obj.animation_data.action.slots):
        obj.animation_data.action_slot = obj.animation_data.action.slots[0]


def shot(path, frame):
    scene.frame_set(int(frame), subframe=frame - int(frame))
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


if "--only-seq" not in args:
    for take in ORDER:
        if names and not any(n in take for n in names):
            continue
        spec = TAKES[take]
        use(arm, take)
        count = spec["frames"] if spec["loop"] else spec["frames"] + 1
        for view, cam in cams.items():
            scene.camera = cam
            for f in range(count):
                ground.location.y = (f * tl.STEP) % 1.0 if take.endswith("Walk") else 0.0
                shot(OUT / f"{take}_{view}" / f"{f:04d}.png", f)
            print("RENDERED", take, view, count, flush=True)
    ground.location.y = 0.0

if "--seq" in args or "--only-seq" in args:
    # two children share mesh/armature data; the parent is hidden the frame they appear
    kids = []
    for side in (1, -1):
        a = arm.copy()
        a.animation_data_create()
        m = mesh.copy()
        scene.collection.objects.link(a)
        scene.collection.objects.link(m)
        m.parent = a
        m.modifiers["Armature"].object = a
        a.scale = (0.6, 0.6, 0.6)
        kids.append((a, m, side))
    scene.camera = rs.game_camera(dist=6.0, target=rs.TARGET)
    seq = [("parent", "ForestSplitter_Death", f) for f in range(12)]
    seq += [("kids", "ForestSplitter_Pop", f) for f in range(11)]
    seq += [("kids", "ForestSplitter_Idle", f) for f in range(16)]
    use(arm, "ForestSplitter_Death")
    for i, (who, take, f) in enumerate(seq):
        parent_on = who == "parent"
        arm.hide_render = mesh.hide_render = not parent_on
        for a, m, side in kids:
            a.hide_render = m.hide_render = parent_on
            if not parent_on:
                use(a, take)
                k = f if take.endswith("Pop") else 10
                a.location.x = side * (0.1 + 1.0 * min(k, 8) / 8.0)
        shot(OUT / "Sequence_game" / f"{i:04d}.png", f)
    print("RENDERED Sequence", len(seq), flush=True)
print("RENDER_DONE", flush=True)
