"""Repair equipment scale in the Rodin master and export a clean Unity FBX."""

import os
import sys

import bpy


PROP_LIMITS = {
    "PRP_Saber_0p92m": (0.75, 1.00),
    "PRP_SaberScabbard": (0.65, 1.00),
    "PRP_Hook_0p22m": (0.15, 0.28),
}


def arguments():
    if "--" not in sys.argv:
        raise RuntimeError("Expected output blend and FBX paths after --")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 2:
        raise RuntimeError("Expected output blend and FBX paths after --")
    return tuple(os.path.abspath(value) for value in values)


def world_size(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    low = Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners)))
    high = Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners)))
    return high - low


def object_span(obj):
    # The equipment is stored diagonally. The world AABB diagonal is stable
    # across that rotation, while a single axis extent can under-report length.
    return world_size(obj).length


def main():
    output_blend, output_fbx = arguments()
    os.makedirs(os.path.dirname(output_blend), exist_ok=True)
    os.makedirs(os.path.dirname(output_fbx), exist_ok=True)

    armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    if len(armatures) != 1:
        raise RuntimeError(f"Expected one armature, found {len(armatures)}")
    armature = armatures[0]

    repaired = []
    for name, limits in PROP_LIMITS.items():
        obj = bpy.data.objects.get(name)
        if obj is None:
            raise RuntimeError(f"Missing equipment object: {name}")

        length = object_span(obj)
        if length < limits[0] * 0.1:
            obj.scale = tuple(component * 100.0 for component in obj.scale)
            repaired.append(name)
            bpy.context.view_layer.update()
            length = object_span(obj)

        if not limits[0] <= length <= limits[1]:
            raise RuntimeError(
                f"{name} span {length:.4f}m is outside {limits[0]:.2f}-{limits[1]:.2f}m"
            )
        print(f"PROP_SIZE={name}:{length:.4f}")

    body = bpy.data.objects.get("model")
    if body is None or body.type != "MESH":
        raise RuntimeError("Body mesh 'model' is missing")
    body_height = world_size(body).z
    if not 1.70 <= body_height <= 1.90:
        raise RuntimeError(f"Body height {body_height:.4f}m is outside 1.70-1.90m")
    print(f"BODY_HEIGHT={body_height:.4f}")

    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = 30
    scene["gameplay_master"] = "Pelag_Rodin01_v2"
    scene["equipment_scale_repaired"] = True

    bpy.ops.wm.save_as_mainfile(filepath=output_blend, check_existing=False)

    export_objects = [armature]
    export_objects.extend(obj for obj in bpy.data.objects if obj.type == "MESH")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in export_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = armature

    bpy.ops.export_scene.fbx(
        filepath=output_fbx,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        bake_anim=False,
        use_armature_deform_only=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        use_space_transform=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        path_mode="COPY",
        embed_textures=True,
    )

    print(f"REPAIRED={','.join(repaired) if repaired else 'already-valid'}")
    print(f"OUTPUT_BLEND={output_blend}")
    print(f"OUTPUT_FBX={output_fbx}")


if __name__ == "__main__":
    from mathutils import Vector

    main()
