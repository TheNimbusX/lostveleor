"""Привязка серии сабли в том же мировом кадре, что и клипы.

Покой рига Mixamo из тейка SaberCombo лежит (поворот +90° по X у объекта
и стоя в пространстве арматуры по Z): кадры тейка стоят, а покой — лежит.
Перенос RazlomPelagAuthoredClips берёт позу покоя привязки как точку
отсчёта поворотов и таза, поэтому привязка выгружается стоя — без
поворота объекта арматуры (масштаб .01 тот же). Клипы не меняются.
"""
import bpy, sys, os
argv = sys.argv[sys.argv.index("--") + 1:]
out = argv[0]
bpy.ops.wm.open_mainfile(filepath=argv[1])
dst = bpy.data.objects["PelagSabre"]
for o in bpy.data.objects: o.select_set(False)
dst.select_set(True); bpy.context.view_layer.objects.active = dst
if dst.animation_data: dst.animation_data.action = None
for pb in dst.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
dst.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
hips = dst.matrix_world @ dst.pose.bones["mixamorig:Hips"].head
head = dst.matrix_world @ dst.pose.bones["mixamorig:Head"].head
print("bind hips", tuple(round(c, 3) for c in hips), "head", tuple(round(c, 3) for c in head))
bpy.ops.export_scene.fbx(filepath=os.path.join(out, "Pelag_AN_SabreBind.fbx"), use_selection=True,
                         object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=False, armature_nodetype='NULL')
print("rebind done")
