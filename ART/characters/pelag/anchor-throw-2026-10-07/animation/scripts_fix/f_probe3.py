import bpy, sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, "..", "scripts"))
import t_lib as T
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "..", "clips", "Pelag_AnchorThrow2_Work.blend"))
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
rig.animation_data.action = bpy.data.actions["AnchorThrow2_Master"]
bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
P = lambda n: rig.matrix_world @ rig.pose.bones["mixamorig:" + n].head
for n in ("Hips", "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand", "LeftFoot", "RightFoot", "Head"):
    print("ORIG", n, tuple(round(x, 3) for x in P(n)))
