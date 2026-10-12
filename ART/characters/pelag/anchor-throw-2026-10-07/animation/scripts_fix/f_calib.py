"""animfix: калибровка переноса поз Unity → Blender. Кадр 0 прежнего мастера (clips/Pelag_AnchorThrow2_Work.blend) — та же
поза, что Throw0 в Unity: мировые повороты костей → calib_B.json (f_unitypose считает поправку осей кости по ним)."""
import bpy, sys, os, json
HERE = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "..", "clips", "Pelag_AnchorThrow2_Work.blend"))
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
rig.animation_data.action = bpy.data.actions["AnchorThrow2_Master"]
bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
out = {}
for pb in rig.pose.bones:
    q = (rig.matrix_world @ pb.matrix).to_3x3().normalized().to_quaternion()
    h = rig.matrix_world @ pb.head
    out[pb.name.replace("mixamorig:", "")] = dict(q=[q.w, q.x, q.y, q.z], head=[h.x, h.y, h.z])
json.dump(out, open(os.path.join(HERE, "calib_B.json"), "w"), indent=0)
print("CALIB", len(out))
