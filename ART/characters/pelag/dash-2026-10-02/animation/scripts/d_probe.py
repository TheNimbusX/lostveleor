"""Чтение тейков Пелага для дэша (только чтение): риг, диапазон, таз, наклон, стопы, кисти.

Запуск: blender -b --factory-startup -P d_probe.py -- <name> [<name> ...]
"""
import bpy, sys, math
from mathutils import Vector
D = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
argv = sys.argv[sys.argv.index("--") + 1:]
up = Vector((0, 0, 1))
for name in argv:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = D + ("\\Pelag_v6\\Runtime\\" if name.startswith("Pelag_v6") else "\\Pelag_v5\\Mixamo\\") + name + ".fbx"
    bpy.ops.import_scene.fbx(filepath=path)
    sc = bpy.context.scene
    print("=====", name, "fps", sc.render.fps)
    for o in bpy.data.objects:
        print("  obj", o.type, o.name, "loc", tuple(round(c, 3) for c in o.location),
              "rot", tuple(round(math.degrees(c), 1) for c in o.rotation_euler), "scale", tuple(round(c, 4) for c in o.scale),
              "parent", o.parent.name if o.parent else None)
        if o.type == 'MESH':
            print("    verts", len(o.data.vertices), "mats", [m.name for m in o.data.materials if m],
                  "images", [n.image.name for m in o.data.materials if m and m.node_tree for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image])
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    print("  bones", len(arm.data.bones), "acts", [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
    # покой: голова таза, головы, стоп в пространстве арматуры
    for b in ("mixamorig:Hips", "mixamorig:Head", "mixamorig:LeftFoot", "mixamorig:RightHand"):
        bb = arm.data.bones[b]
        print("  rest", b, tuple(round(c, 3) for c in bb.head_local), "tail", tuple(round(c, 3) for c in bb.tail_local))
    act = arm.animation_data.action if arm.animation_data else None
    if not act: continue
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    mw = arm.matrix_world
    def w(n): return mw @ arm.pose.bones[n].head
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        h = w("mixamorig:Hips"); nk = w("mixamorig:Neck"); hd = w("mixamorig:Head")
        v = (nk - h)
        lean = math.degrees(v.normalized().angle(up))
        lt = w("mixamorig:LeftToeBase"); rt = w("mixamorig:RightToeBase")
        la = w("mixamorig:LeftFoot"); ra = w("mixamorig:RightFoot")
        rh = w("mixamorig:RightHand"); lh = w("mixamorig:LeftHand")
        print("  f%03d hips %6.3f %6.3f %6.3f lean %4.0f dir %6.2f %6.2f head %5.3f | La %6.3f %6.3f %5.3f Lt %5.3f | Ra %6.3f %6.3f %5.3f Rt %5.3f | RH %6.3f %6.3f %5.3f LH %6.3f %6.3f %5.3f" % (
            f, h.x, h.y, h.z, lean, v.x, v.y, hd.z, la.x, la.y, la.z, lt.z, ra.x, ra.y, ra.z, rt.z, rh.x, rh.y, rh.z, lh.x, lh.y, lh.z))
