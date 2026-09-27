"""Game skeleton for ForestThorncaster: Meshy/Mixamo names, metres, origin at feet, faces -Y.

Heads come from the Meshy auto-rig except where the Meshy joints did not match
the mesh (legs: knee/ankle heights were 30 cm apart between sides; wrists sat
mid-forearm).  Those are re-measured from limb centre lines (limb_slices.py) and
the red spike geometry (probe_spikes.py).
"""
import bpy
from mathutils import Vector

# name: (parent, head, tail)  -- tails only matter inside Blender
JOINTS = {
    "Hips": (None, (0.013, -0.004, 1.648), (0.012, -0.016, 1.759)),
    "Spine02": ("Hips", (0.010, -0.028, 1.759), None),
    "Spine01": ("Spine02", (0.008, -0.052, 1.870), None),
    "Spine": ("Spine01", (0.006, -0.076, 1.981), None),
    "neck": ("Spine", (0.005, -0.084, 2.015), None),
    "Head": ("neck", (0.000, -0.107, 2.123), (0.000, -0.090, 2.400)),
    "head_end": ("Head", (0.000, -0.090, 2.400), (0.000, -0.090, 2.520)),
    "headfront": ("Head", (0.000, -0.276, 2.123), (0.000, -0.396, 2.123)),
    # arms (Meshy shoulder/elbow; wrist + hand tail filled from spike detection)
    "LeftShoulder": ("Spine", (0.028, -0.075, 2.077), None),
    "LeftArm": ("LeftShoulder", (0.114, -0.074, 2.077), None),
    "LeftForeArm": ("LeftArm", (0.405, 0.003, 1.839), None),
    "LeftHand": ("LeftForeArm", None, None),
    "RightShoulder": ("Spine", (-0.022, -0.077, 2.052), None),
    "RightArm": ("RightShoulder", (-0.133, -0.077, 2.052), None),
    "RightForeArm": ("RightArm", (-0.414, -0.005, 1.791), None),
    "RightHand": ("RightForeArm", None, None),
    # legs (Meshy hips; knee/ankle/toe re-measured, same heights both sides)
    "LeftUpLeg": ("Hips", (0.173, -0.026, 1.539), None),
    "LeftLeg": ("LeftUpLeg", (0.250, -0.020, 0.940), None),
    "LeftFoot": ("LeftLeg", (0.335, 0.095, 0.380), None),
    "LeftToeBase": ("LeftFoot", (0.385, -0.030, 0.100), (0.390, -0.215, 0.030)),
    "RightUpLeg": ("Hips", (-0.089, -0.012, 1.546), None),
    "RightLeg": ("RightUpLeg", (-0.200, -0.015, 0.940), None),
    "RightFoot": ("RightLeg", (-0.320, 0.075, 0.380), None),
    "RightToeBase": ("RightFoot", (-0.400, -0.030, 0.100), (-0.410, -0.255, 0.030)),
}

ORDER = list(JOINTS.keys())
CHAINS = {
    "LArm": ["LeftArm", "LeftForeArm", "LeftHand"],
    "RArm": ["RightArm", "RightForeArm", "RightHand"],
    "LLeg": ["LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase"],
    "RLeg": ["RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"],
}
SPINE = ["Hips", "Spine02", "Spine01", "Spine", "neck", "Head"]


def resolve(joints):
    """Fill missing tails with the head of the (first) child."""
    heads = {n: Vector(j[1]) for n, j in joints.items()}
    tails = {}
    for n, (parent, head, tail) in joints.items():
        if tail is not None:
            tails[n] = Vector(tail)
            continue
        kids = [k for k, j in joints.items() if j[0] == n and k not in ("head_end", "headfront")]
        # prefer the chain child (not the shoulders/legs branching off the spine)
        pref = {"Spine": "neck", "Hips": "Spine02"}
        child = pref.get(n) or (kids[0] if kids else None)
        tails[n] = heads[child].copy() if child else heads[n] + Vector((0, 0, 0.1))
    return heads, tails


def build_armature(joints, name, roll_ref=Vector((0, -1, 0))):
    heads, tails = resolve(joints)
    data = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    data.display_type = "STICK"
    obj.show_in_front = True
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for n in ORDER:
        b = data.edit_bones.new(n)
        b.head = heads[n]
        b.tail = tails[n]
        # head_end/headfront carry no weights but stay in the FBX as sockets
        b.use_deform = True
    for n in ORDER:
        parent = joints[n][0]
        if parent:
            b = data.edit_bones[n]
            b.parent = data.edit_bones[parent]
            b.use_connect = False
    # consistent rolls: local Z of every bone points to the character's front (-Y)
    for b in data.edit_bones:
        b.align_roll(roll_ref if abs(b.vector.normalized().dot(roll_ref)) < 0.9 else Vector((0, 0, 1)))
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj
