"""Render review frames of the baked takes (the .blend is not saved back).

blender -b ForestSplitter_Baked_r02.blend -P render_review.py -- [take-substrings...] [--seq] [--only-seq]
                                                                   [--roll-seq] [--only-roll-seq]
Frames -> ../review/animation_r01/frames/<Take>_<view>/NNNN.png (game + side), and the in-game style
sequence (parent Death, two 0.6-scale children Pop sideways 1 m in 8 frames, then Idle) -> frames/Sequence_game/.
Walk: the ground scrolls backward at 3.1 m/s so planted feet must stick to the checker.
r02 (27.09): --roll-seq renders the roll as the game will show it (frames/RollSeq_uncurl, frames/RollSeq_dizzy):
RollCurl, launch on tick 30, 0.40 m/tick with the view's spin about the ball pivot (validation_r02.json), then
RollUncurl after 16 ticks, or RollDizzy after a wall hit on tick 9. The spin settles upright in the first 4 frames
of Uncurl/Dizzy.
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


if "--only-seq" not in args and "--only-roll-seq" not in args:
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
if "--roll-seq" in args or "--only-roll-seq" in args:
    import json
    import math

    from mathutils import Matrix, Vector

    ball = json.loads((HERE / "validation_r02.json").read_text(encoding="utf-8"))["ball"]
    PIV = Vector(ball["spin_pivot_blender"])
    RAD, LIFT = ball["spin_radius_m"], ball["lift_to_clear_ground_m"]
    TICK_M = 0.40
    arm.hide_render = mesh.hide_render = False
    for a in (arm, mesh):
        a.location = (0, 0, 0)
    pivot = bpy.data.objects.new("RollPivot", None)
    scene.collection.objects.link(pivot)
    arm.parent = pivot
    wall = None
    scene.render.resolution_x, scene.render.resolution_y = 640, 360
    # the entity is turned 30 deg so the 6.4 m roll crosses the game-angle frame right -> left
    HEAD = Matrix.Rotation(math.radians(-30.0), 4, "Z")
    cam = rs.game_camera(dist=9.6, target=HEAD @ Vector((0.0, -3.3, 0.3)))
    cam.data.lens = 36
    scene.camera = cam

    def place(travel, angle, lift):
        pivot.matrix_world = HEAD @ (Matrix.Translation(PIV + Vector((0.0, -travel, lift)))
                                     @ Matrix.Rotation(angle, 4, "X") @ Matrix.Translation(-PIV))

    def settle(a0, k, n=4):
        """Finish the turn to the next upright over n frames (ease out)."""
        target = math.ceil(a0 / (2 * math.pi) - 1e-6) * 2 * math.pi
        s = min(1.0, k / n)
        e = 1 - (1 - s) ** 3
        return a0 + (target - a0) * e, LIFT * (1 - e)

    for name, ticks, after, after_n in (("uncurl", 16, "ForestSplitter_RollUncurl", 30),
                                        ("dizzy", 9, "ForestSplitter_RollDizzy", 45)):
        if wall is not None:
            bpy.data.objects.remove(wall, do_unlink=True)
            wall = None
        if name == "dizzy":
            bpy.ops.mesh.primitive_cube_add(size=1.0)
            wall = bpy.context.active_object
            wall.name = "ReviewWall"
            wall.scale = (2.4, 0.35, 0.6)       # low stone wall: does not hide the ball from the game angle
            wall.location = HEAD @ Vector((0.0, -(TICK_M * ticks + 0.53 + 0.175), 0.3))
            wall.rotation_euler = (0.0, 0.0, math.radians(-30.0))
            mat = bpy.data.materials.new("ReviewWall")
            mat.use_nodes = True
            mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.35, 0.3, 0.26, 1)
            wall.data.materials.append(mat)
        seq = [("ForestSplitter_RollCurl", f, 0.0, 0.0, 0.0) for f in range(31)]
        for k in range(1, ticks + 1):
            d = TICK_M * k
            seq.append(("ForestSplitter_RollLoop", k % 12, d, d / RAD, LIFT * min(1.0, k / 2)))
        d_end = TICK_M * ticks
        a_end = d_end / RAD
        for f in range(after_n + 1):
            a, lift = settle(a_end, f)
            seq.append((after, f, d_end, a, lift))
        seq += [("ForestSplitter_Idle", f, d_end, settle(a_end, 99)[0], 0.0) for f in range(1, 13)]
        for i, (take, f, travel, angle, lift) in enumerate(seq):
            use(arm, take)
            place(travel, angle, lift)
            shot(OUT / f"RollSeq_{name}" / f"{i:04d}.png", f)
        print("RENDERED RollSeq", name, len(seq), flush=True)
    arm.parent = None
print("RENDER_DONE", flush=True)
