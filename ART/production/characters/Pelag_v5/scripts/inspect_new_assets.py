import bpy
import os
import sys


def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def world_bounds(obj):
    corners = [obj.matrix_world @ __import__("mathutils").Vector(corner) for corner in obj.bound_box]
    mins = [min(p[i] for p in corners) for i in range(3)]
    maxs = [max(p[i] for p in corners) for i in range(3)]
    return mins, maxs, [maxs[i] - mins[i] for i in range(3)]


def inspect(path):
    clean()
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True)
    print(f"ASSET_BEGIN={path}")
    for obj in sorted(bpy.data.objects, key=lambda item: item.name):
        parent = obj.parent.name if obj.parent else "-"
        print(f"OBJECT={obj.name}|type={obj.type}|parent={parent}|scale={tuple(round(v, 5) for v in obj.scale)}")
        if obj.type == "MESH":
            mins, maxs, size = world_bounds(obj)
            tris = sum(len(poly.vertices) - 2 for poly in obj.data.polygons)
            materials = [slot.material.name if slot.material else "-" for slot in obj.material_slots]
            print(
                f"MESH={obj.name}|verts={len(obj.data.vertices)}|tris={tris}|"
                f"min={tuple(round(v, 5) for v in mins)}|max={tuple(round(v, 5) for v in maxs)}|"
                f"size={tuple(round(v, 5) for v in size)}|materials={materials}"
            )
        elif obj.type == "ARMATURE":
            names = [bone.name for bone in obj.data.bones]
            print(f"ARMATURE={obj.name}|bones={len(names)}|first={names[:20]}|last={names[-10:]}")
    for action in sorted(bpy.data.actions, key=lambda item: item.name):
        print(
            f"ACTION={action.name}|frames={tuple(round(v, 3) for v in action.frame_range)}|"
            f"slots={len(action.slots)}|fcurves={sum(len(channelbag.fcurves) for layer in action.layers for strip in layer.strips for channelbag in getattr(strip, 'channelbags', []))}"
        )
    print("ASSET_END")


for asset in sys.argv[sys.argv.index("--") + 1:]:
    inspect(os.path.abspath(asset))
