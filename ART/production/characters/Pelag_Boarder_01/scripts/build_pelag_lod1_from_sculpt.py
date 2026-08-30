import math
import os
import sys

import bpy
from mathutils import Vector


TARGET_HEIGHT = 1.91
TARGET_BODY_TRIS = 18500


def args():
    if "--" not in sys.argv:
        raise RuntimeError("Expected <source.obj> <output_dir> after --")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 2:
        raise RuntimeError("Expected <source.obj> <output_dir>")
    return [os.path.abspath(value) for value in values]


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return lo, hi


def normalize(obj):
    obj.rotation_euler = (0.0, math.radians(-90.0), math.radians(90.0))
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    lo, hi = bounds(obj)
    scale = TARGET_HEIGHT / (hi.z - lo.z)
    obj.scale = (scale, scale, scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    lo, hi = bounds(obj)
    obj.location = (-(lo.x + hi.x) / 2.0, -(lo.y + hi.y) / 2.0, -lo.z)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)


def material(name, color, roughness=0.78, metallic=0.0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat


def assign_region_materials(obj, mats):
    for mat in mats:
        obj.data.materials.append(mat)

    # The generated source is deliberately recolored with large spatial masks.
    # At gameplay scale these broad masses are more readable than noisy baked color.
    for poly in obj.data.polygons:
        c = poly.center
        ax = abs(c.x)
        idx = 0  # cream cloth
        if c.z > 1.69:
            idx = 4  # hair/headband region; face is restored by a skin overlay below
        elif c.z > 1.52 and ax < 0.24 and c.y < -0.13:
            idx = 1  # face
        elif 0.30 < ax < 0.47 and 1.25 < c.z < 1.42:
            idx = 5  # turquoise sleeve trim
        elif ax > 0.50 and 0.76 < c.z < 1.35:
            idx = 1  # hands and exposed forearms
        elif ax > 0.40 and 0.96 < c.z < 1.31:
            idx = 3  # bracers
        elif 0.77 < c.z < 1.08 and ax < 0.43:
            idx = 2  # coral sash
        elif 0.13 < c.z < 0.62 and ax < 0.34:
            idx = 1  # lower legs
        elif c.z < 0.18:
            idx = 3  # sandals
        poly.material_index = idx


def add_uv_sphere(name, location, scale, mat, segments=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def curve(name, points, radius, mat, cyclic=False, resolution=2):
    data = bpy.data.curves.new(name + "_Curve", type="CURVE")
    data.dimensions = "3D"
    data.resolution_u = resolution
    data.bevel_depth = radius
    data.bevel_resolution = 2
    spline = data.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for point, co in zip(spline.points, points):
        point.co = (*co, 1.0)
    spline.use_cyclic_u = cyclic
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def cylinder_between(name, start, end, radius, mat, vertices=16):
    start = Vector(start)
    end = Vector(end)
    mid = (start + end) * 0.5
    delta = end - start
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=delta.length, location=mid)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = delta.to_track_quat("Z", "Y").to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    return obj


def panel(name, z_top, z_bottom, x_top, x_bottom, y_front, depth, mat, x_offset=0.0):
    verts = [
        (x_offset - x_top, y_front, z_top),
        (x_offset + x_top, y_front, z_top),
        (x_offset + x_bottom, y_front, z_bottom),
        (x_offset - x_bottom, y_front, z_bottom),
        (x_offset - x_top, y_front + depth, z_top),
        (x_offset + x_top, y_front + depth, z_top),
        (x_offset + x_bottom, y_front + depth, z_bottom),
        (x_offset - x_bottom, y_front + depth, z_bottom),
    ]
    faces = [
        (0, 1, 2, 3), (4, 7, 6, 5),
        (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (4, 0, 3, 7),
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(mat)
    bevel = obj.modifiers.new("SoftEdges", "BEVEL")
    bevel.width = 0.012
    bevel.segments = 2
    return obj


def create_accents(mats):
    cream, skin, coral, leather, hair, turquoise, rope_mat = mats

    # Clean face volume masks the reconstruction's noisy facial surface while
    # retaining the generated ears, neck and silhouette.
    add_uv_sphere("Face_CleanVolume", (0.0, -0.215, 1.635), (0.132, 0.095, 0.165), skin)
    add_uv_sphere("Eye_L", (-0.048, -0.313, 1.675), (0.013, 0.007, 0.025), hair, 16, 10)
    add_uv_sphere("Eye_R", (0.048, -0.313, 1.675), (0.013, 0.007, 0.025), hair, 16, 10)
    curve("Brow_L", [(-0.078, -0.318, 1.715), (-0.045, -0.322, 1.722), (-0.020, -0.318, 1.714)], 0.006, hair)
    curve("Brow_R", [(0.020, -0.318, 1.714), (0.045, -0.322, 1.722), (0.078, -0.318, 1.715)], 0.006, hair)
    curve("Mouth", [(-0.032, -0.319, 1.590), (0.0, -0.322, 1.582), (0.032, -0.319, 1.590)], 0.0045, hair)

    # Permanent high-readability accents.
    curve("Collar_Trim", [(-0.19, -0.245, 1.48), (-0.09, -0.258, 1.38), (0.02, -0.266, 1.27), (0.15, -0.254, 1.13)], 0.014, turquoise)
    curve("Headband", [
        (-0.135, -0.18, 1.73), (-0.09, -0.275, 1.74), (0.0, -0.31, 1.745),
        (0.09, -0.275, 1.74), (0.135, -0.18, 1.73), (0.09, -0.08, 1.73),
        (0.0, -0.045, 1.73), (-0.09, -0.08, 1.73),
    ], 0.022, coral, cyclic=True)

    panel("CoralPanel_L", 0.99, 0.51, 0.105, 0.145, -0.235, 0.018, coral, -0.105)
    panel("CoralPanel_R", 0.97, 0.48, 0.090, 0.135, -0.246, 0.018, coral, 0.120)
    panel("CreamPanel_C", 0.98, 0.54, 0.070, 0.095, -0.258, 0.016, cream, -0.005)

    # A permanent rope loop is a class/nation read at 80-120 px.
    rope_points = []
    for i in range(24):
        angle = math.tau * i / 24.0
        rope_points.append((0.225 + 0.072 * math.cos(angle), -0.305, 0.800 + 0.180 * math.sin(angle)))
    curve("RopeLoop", rope_points, 0.025, rope_mat, cyclic=True, resolution=1)
    add_uv_sphere("RopeKnot", (0.20, -0.325, 1.015), (0.055, 0.035, 0.055), rope_mat, 16, 10)


def setup_render(output_dir):
    qa = os.path.join(output_dir, "qa")
    os.makedirs(qa, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 768
    scene.render.resolution_y = 768
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.fps = 30
    scene.render.use_freestyle = True
    scene.render.line_thickness = 1.25
    lineset = scene.view_layers[0].freestyle_settings.linesets[0]
    lineset.select_silhouette = True
    lineset.select_contour = True
    lineset.select_external_contour = True
    lineset.select_crease = False
    lineset.select_border = False
    lineset.select_material_boundary = False
    lineset.select_edge_mark = False
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.72, 0.68, 0.62, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.65

    def area(name, loc, energy, color):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = energy
        data.size = 4.0
        data.color = color
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_euler = (Vector((0, 0, 1.0)) - obj.location).to_track_quat("-Z", "Y").to_euler()
        return obj

    lights = [
        area("Key", (3.5, -4.0, 4.8), 1050, (1.0, 0.82, 0.68)),
        area("Fill", (-3.0, -2.5, 3.0), 700, (0.55, 0.76, 1.0)),
        area("Rim", (2.0, 3.2, 4.0), 850, (1.0, 0.38, 0.28)),
    ]
    camera_data = bpy.data.cameras.new("QA_Camera")
    camera = bpy.data.objects.new("QA_Camera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 2.28
    target = Vector((0.0, 0.0, 0.95))
    views = {
        "front": (0.0, -4.2, 1.05),
        "back": (0.0, 4.2, 1.05),
        "left": (-4.2, 0.0, 1.05),
        "right": (4.2, 0.0, 1.05),
        "iso_front": (3.2, -3.2, 2.65),
        "iso_back": (-3.2, 3.2, 2.65),
    }
    for name, loc in views.items():
        camera.location = loc
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(qa, name + ".png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera, do_unlink=True)
    for light in lights:
        bpy.data.objects.remove(light, do_unlink=True)


def main():
    source_obj, output_dir = args()
    os.makedirs(output_dir, exist_ok=True)
    clear_scene()
    bpy.ops.wm.obj_import(filepath=source_obj)
    body = next(obj for obj in bpy.context.selected_objects if obj.type == "MESH")
    body.name = "Pelag_Boarder_01_BodyCostume_LOD1"
    normalize(body)

    current_tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    decimate = body.modifiers.new("ProductionTriangleBudget", "DECIMATE")
    decimate.ratio = min(1.0, TARGET_BODY_TRIS / current_tris)
    decimate.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    for poly in body.data.polygons:
        poly.use_smooth = True

    mats = [
        material("MAT_Pelag_Cream", (0.92, 0.79, 0.62)),
        material("MAT_Pelag_Skin", (0.76, 0.34, 0.075)),
        material("MAT_Pelag_Coral", (0.94, 0.22, 0.18)),
        material("MAT_Pelag_Leather", (0.19, 0.07, 0.035)),
        material("MAT_Pelag_Hair", (0.045, 0.022, 0.016)),
        material("MAT_Pelag_Turquoise", (0.04, 0.68, 0.67)),
        material("MAT_Pelag_Rope", (0.72, 0.49, 0.20)),
    ]
    assign_region_materials(body, mats)
    create_accents(mats)

    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = 30
    scene["production_status"] = "VISUAL_QA_ONLY"
    scene["source_sculpt"] = source_obj
    scene["animations_authorized"] = False

    setup_render(output_dir)
    blend_path = os.path.join(output_dir, "Pelag_Boarder_01_LOD1_VisualPass_v4.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    total_tris = sum(
        sum(len(poly.vertices) - 2 for poly in obj.data.polygons)
        for obj in scene.objects if obj.type == "MESH"
    )
    print(f"BLEND={blend_path}")
    print(f"BODY_TRIS={sum(len(p.vertices)-2 for p in body.data.polygons)}")
    print(f"TOTAL_TRIS={total_tris}")


if __name__ == "__main__":
    main()
