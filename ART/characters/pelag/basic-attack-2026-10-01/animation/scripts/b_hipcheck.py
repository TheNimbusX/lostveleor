import bpy, sys
from mathutils import Vector
d = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters\Pelag_v5\Mixamo"
def load(name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=d + "\\" + name + ".fbx")
    return [o for o in bpy.data.objects if o not in before and o.type == 'ARMATURE'][0]
bpy.ops.wm.read_factory_settings(use_empty=True)
bind = load("Pelag_AN_SabreBind")
bh = bind.matrix_world @ bind.pose.bones["mixamorig:Hips"].head
bhead = bind.matrix_world @ bind.pose.bones["mixamorig:Head"].head
L = (bh - bhead).length
print("bind hips", tuple(round(c, 4) for c in bh), "len", round(L, 4), "scale", tuple(bind.scale), "rot", tuple(bind.rotation_euler))
for name in ("Pelag_AN_Sabre1", "Pelag_AN_Sabre2", "Pelag_AN_Sabre3", "Pelag_AN_Bind", "Pelag_AN_SquallRecoverB"):
    try:
        arm = load(name)
    except Exception as ex:
        print(name, "fail", ex); continue
    act = arm.animation_data.action if arm.animation_data else None
    sc = bpy.context.scene
    rng = act.frame_range if act else (0, 0)
    worst = 0
    for f in range(int(rng[0]), int(rng[1]) + 1):
        sc.frame_set(f)
        h = arm.matrix_world @ arm.pose.bones["mixamorig:Hips"].head
        worst = max(worst, (h - bh).length)
        if f in (int(rng[0]), int(rng[1])):
            print("  ", name, f, tuple(round(c, 4) for c in h))
    print(name, "scale", tuple(round(s, 4) for s in arm.scale), "worst offset", round(worst, 4), "ratio", round(worst / L, 3))
