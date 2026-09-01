"""Build Pelag's stylized 4x4 alpha flipbooks and editable Blender source.

The effect language follows the reviewed combat references: compact cream
cores, warm orange directional masses, broken coral accents, and a hard
five-tick cutoff for every bright layer.  Only the ground crack persists.
"""

from __future__ import annotations

from array import array
import hashlib
import json
import math
from pathlib import Path
import random

import bpy
from mathutils import Vector


PROJECT = Path(r"C:\Users\d.grab\Desktop\the-game")
VFX_ROOT = PROJECT / "razlom/Assets/Resources/VFX/Pelag"
TEXTURES = VFX_ROOT / "Textures"
SOURCE_DIR = VFX_ROOT / "Source~"
SOURCE_BLEND = SOURCE_DIR / "Pelag_VFX_Flipbooks.blend"
MANIFEST = VFX_ROOT / "generated-assets.json"
BUILD = PROJECT / "artifacts/pelag-vfx-build"
FRAME_DIR = BUILD / "frames"
REPORT = BUILD / "pelag-vfx-build-report.json"

ANCHOR_ROOT = PROJECT / "razlom/Assets/Resources/Weapons/Pelag/AnchorChain"
REFERENCE_FILES = (
    ANCHOR_ROOT / "Pelag_AnchorHead.fbx",
    ANCHOR_ROOT / "Pelag_AnchorGrip.fbx",
    ANCHOR_ROOT / "Pelag_ChainLink.fbx",
)
REFERENCE_TEXTURE = ANCHOR_ROOT / "Pelag_AnchorChain_BaseColor.jpg"

GRID = 4
CELL = 512
ATLAS_SIZE = GRID * CELL
FPS = 30
BRIGHT_FRAMES = 5

CREAM = (0.984, 0.922, 0.788)
WARM = (0.957, 0.502, 0.259)
ORANGE = (0.953, 0.565, 0.259)
DEEP = (0.937, 0.286, 0.165)
DUST = (0.941, 0.863, 0.749)
CORAL = (0.941, 0.361, 0.278)
DARK = (0.235, 0.105, 0.075)

SCENE: bpy.types.Scene
CAMERA: bpy.types.Object
ACTIVE_COLLECTION: bpy.types.Collection
MATERIALS: dict[tuple, bpy.types.Material] = {}


def clear_file() -> None:
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def setup_scene() -> None:
    global SCENE, CAMERA, ACTIVE_COLLECTION
    clear_file()
    SCENE = bpy.context.scene
    SCENE.name = "Pelag_VFX_Flipbook_Source"
    SCENE.render.engine = "BLENDER_EEVEE"
    SCENE.render.resolution_x = CELL
    SCENE.render.resolution_y = CELL
    SCENE.render.resolution_percentage = 100
    SCENE.render.image_settings.file_format = "PNG"
    SCENE.render.image_settings.color_mode = "RGBA"
    SCENE.render.image_settings.color_depth = "8"
    SCENE.render.film_transparent = True
    SCENE.render.image_settings.color_mode = "RGBA"
    SCENE.view_settings.view_transform = "Standard"
    SCENE.view_settings.look = "Medium High Contrast"
    SCENE.view_settings.exposure = 0.0
    SCENE.view_settings.gamma = 1.0
    SCENE.render.fps = FPS

    camera_data = bpy.data.cameras.new("Pelag_FX_Ortho_Camera")
    CAMERA = bpy.data.objects.new("Pelag_FX_Ortho_Camera", camera_data)
    SCENE.collection.objects.link(CAMERA)
    CAMERA.location = (0.0, 0.0, 10.0)
    CAMERA.rotation_euler = (0.0, 0.0, 0.0)
    CAMERA.rotation_euler[0] = 0.0
    CAMERA.rotation_euler = (0.0, 0.0, 0.0)
    CAMERA.rotation_mode = "QUATERNION"
    CAMERA.rotation_quaternion = Vector((0.0, 0.0, -1.0)).to_track_quat("-Z", "Y")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 6.0
    SCENE.camera = CAMERA

    ACTIVE_COLLECTION = bpy.data.collections.new("_AtlasRender")
    SCENE.collection.children.link(ACTIVE_COLLECTION)


def material(color: tuple[float, float, float], alpha: float, intensity: float = 1.0) -> bpy.types.Material:
    key = (*[round(value, 4) for value in color], round(alpha, 4), round(intensity, 3))
    if key in MATERIALS:
        return MATERIALS[key]
    mat = bpy.data.materials.new("FX_{:02x}{:02x}{:02x}_A{:03d}_I{:02d}".format(
        *(round(channel * 255) for channel in color), round(alpha * 100), round(intensity * 10)
    ))
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    transparent = nodes.new("ShaderNodeBsdfTransparent")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (*color, 1.0)
    emission.inputs["Strength"].default_value = intensity
    mix = nodes.new("ShaderNodeMixShader")
    mix.inputs[0].default_value = alpha
    links.new(transparent.outputs[0], mix.inputs[1])
    links.new(emission.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], output.inputs[0])
    mat.surface_render_method = "DITHERED"
    MATERIALS[key] = mat
    return mat


def link_object(obj: bpy.types.Object) -> bpy.types.Object:
    ACTIVE_COLLECTION.objects.link(obj)
    return obj


def polygon(
    name: str,
    points: list[tuple[float, float]],
    color: tuple[float, float, float],
    alpha: float,
    *,
    z: float = 0.0,
    intensity: float = 1.0,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata([(x, y, z) for x, y in points], [], [list(range(len(points)))])
    mesh.materials.append(material(color, alpha, intensity))
    obj = bpy.data.objects.new(name, mesh)
    return link_object(obj)


def ellipse(
    name: str,
    center: tuple[float, float],
    radii: tuple[float, float],
    color: tuple[float, float, float],
    alpha: float,
    *,
    rotation: float = 0.0,
    z: float = 0.0,
    intensity: float = 1.0,
    segments: int = 48,
) -> bpy.types.Object:
    cosine = math.cos(rotation)
    sine = math.sin(rotation)
    points = []
    for index in range(segments):
        angle = math.tau * index / segments
        x = radii[0] * math.cos(angle)
        y = radii[1] * math.sin(angle)
        points.append((center[0] + x * cosine - y * sine, center[1] + x * sine + y * cosine))
    return polygon(name, points, color, alpha, z=z, intensity=intensity)


def star(
    name: str,
    center: tuple[float, float],
    outer: float,
    inner: float,
    points_count: int,
    color: tuple[float, float, float],
    alpha: float,
    *,
    rotation: float = 0.0,
    z: float = 0.0,
    intensity: float = 1.0,
) -> bpy.types.Object:
    points = []
    for index in range(points_count * 2):
        angle = rotation + math.pi * index / points_count
        radius = outer if index % 2 == 0 else inner
        points.append((center[0] + math.cos(angle) * radius, center[1] + math.sin(angle) * radius))
    return polygon(name, points, color, alpha, z=z, intensity=intensity)


def stroke(
    name: str,
    points: list[tuple[float, float]],
    width: float,
    color: tuple[float, float, float],
    alpha: float,
    *,
    z: float = 0.0,
    intensity: float = 1.0,
    cyclic: bool = False,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(name + "_Curve", "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 2
    curve.bevel_resolution = 3
    curve.bevel_depth = width
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for target, (x, y) in zip(spline.points, points):
        target.co = (x, y, z, 1.0)
    spline.use_cyclic_u = cyclic
    curve.materials.append(material(color, alpha, intensity))
    return link_object(bpy.data.objects.new(name, curve))


def arc(
    name: str,
    radius: float,
    angle_start: float,
    angle_end: float,
    width: float,
    color: tuple[float, float, float],
    alpha: float,
    *,
    squash: float = 1.0,
    z: float = 0.0,
    intensity: float = 1.0,
    segments: int = 32,
) -> bpy.types.Object:
    points = []
    for index in range(segments):
        angle = angle_start + (angle_end - angle_start) * index / (segments - 1)
        points.append((math.cos(angle) * radius, math.sin(angle) * radius * squash))
    return stroke(name, points, width, color, alpha, z=z, intensity=intensity)


def tapered_streak(
    name: str,
    start: tuple[float, float],
    end: tuple[float, float],
    half_width: float,
    color: tuple[float, float, float],
    alpha: float,
    *,
    z: float = 0.0,
    intensity: float = 1.0,
) -> bpy.types.Object:
    direction = Vector((end[0] - start[0], end[1] - start[1]))
    direction.normalize()
    normal = Vector((-direction.y, direction.x)) * half_width
    points = [
        (start[0], start[1]),
        (end[0] + normal.x, end[1] + normal.y),
        (end[0] - normal.x, end[1] - normal.y),
    ]
    return polygon(name, points, color, alpha, z=z, intensity=intensity)


def clear_render_shapes() -> None:
    for obj in list(ACTIVE_COLLECTION.objects):
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if data and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Curve):
                bpy.data.curves.remove(data)


def build_anchor_spin(frame: int) -> None:
    alpha = (0.62, 0.95, 0.68, 0.38, 0.14)[frame]
    phase = frame * 0.78
    arc("AnchorSpin_Wide", 1.65, phase - 1.25, phase + 0.55, 0.16, WARM, alpha, squash=0.64, intensity=1.2)
    arc("AnchorSpin_Core", 1.28, phase - 0.82, phase + 0.32, 0.075, CREAM, min(1.0, alpha * 1.15), squash=0.68, z=0.02, intensity=1.5)
    if frame < 3:
        arc("AnchorSpin_Ghost", 1.92, phase + 1.6, phase + 2.3, 0.055, DEEP, alpha * 0.62, squash=0.58)
    # Small dark fluke marker keeps the spin centered without replacing the
    # separately rendered physical anchor head.
    star("AnchorSpin_Hub", (0.0, 0.0), 0.27, 0.10, 4, DARK, alpha * 0.80, rotation=phase, z=0.03)


def radial_shards(count: int, radius: float, length: float, width: float, alpha: float, seed: int) -> None:
    rng = random.Random(seed)
    for index in range(count):
        angle = math.tau * index / count + rng.uniform(-0.12, 0.12)
        start_radius = radius + rng.uniform(-0.08, 0.10)
        end_radius = start_radius + length * rng.uniform(0.72, 1.25)
        start = (math.cos(angle) * start_radius, math.sin(angle) * start_radius)
        end = (math.cos(angle) * end_radius, math.sin(angle) * end_radius)
        tapered_streak(f"Shard_{index:02d}", end, start, width, ORANGE, alpha * rng.uniform(0.65, 1.0), z=0.01)


def build_impact(frame: int) -> None:
    if frame == 0:
        star("Impact_Pin", (0.0, 0.0), 0.46, 0.10, 6, CREAM, 0.92, intensity=1.7)
        ellipse("Impact_Marker", (0.0, -0.05), (0.58, 0.18), WARM, 0.50)
    elif frame == 1:
        ellipse("Impact_Core", (0.0, 0.0), (0.72, 0.58), CREAM, 1.0, intensity=1.9)
        arc("Impact_Halo", 1.12, 0.08, math.tau - 0.15, 0.14, WARM, 0.90, squash=0.56)
        radial_shards(10, 0.62, 1.25, 0.13, 0.95, 101)
    elif frame == 2:
        arc("Impact_OpenRing", 1.38, 0.20, math.tau - 0.55, 0.11, ORANGE, 0.74, squash=0.52)
        radial_shards(9, 0.88, 1.15, 0.09, 0.72, 102)
        for index, x in enumerate((-1.25, -0.55, 0.48, 1.15)):
            ellipse(f"Dust_{index}", (x, -0.68 + 0.09 * (index % 2)), (0.38, 0.19), DUST, 0.44, rotation=0.18 * index)
    elif frame == 3:
        radial_shards(7, 1.25, 0.72, 0.065, 0.46, 103)
        for index, x in enumerate((-1.45, -0.72, 0.42, 1.35)):
            ellipse(f"Dust_{index}", (x, -0.72), (0.31, 0.14), DUST, 0.30)
    else:
        radial_shards(4, 1.52, 0.34, 0.04, 0.23, 104)
        ellipse("LastDust", (0.7, -0.74), (0.26, 0.10), DUST, 0.18)


CRACK_BRANCHES = (
    ((0.0, 0.0), (0.42, 0.18), (0.98, 0.08), (1.54, 0.36), (2.18, 0.22)),
    ((0.0, 0.0), (-0.38, 0.12), (-0.92, 0.42), (-1.48, 0.31), (-2.02, 0.64)),
    ((0.0, 0.0), (0.16, -0.28), (0.55, -0.69), (0.48, -1.18), (0.88, -1.62)),
    ((0.0, 0.0), (-0.22, -0.22), (-0.63, -0.55), (-1.04, -0.82), (-1.46, -1.38)),
    ((0.0, 0.0), (0.06, 0.34), (-0.12, 0.76), (0.18, 1.18), (0.02, 1.72)),
)


def build_ground_crack(frame: int) -> None:
    growth = min(1.0, (frame + 1) / 4.0)
    if frame < BRIGHT_FRAMES:
        core_alpha = (0.78, 1.0, 0.66, 0.34, 0.10)[frame]
        star("Crack_Core", (0.0, 0.0), 0.42 + frame * 0.06, 0.09, 7, CREAM, core_alpha, intensity=1.5)
    trace_alpha = 0.82 if frame < 5 else max(0.16, 0.68 * (1.0 - (frame - 5) / 14.0))
    points_to_show = max(2, min(5, 1 + math.ceil(growth * 4)))
    for index, branch in enumerate(CRACK_BRANCHES):
        visible = list(branch[:points_to_show])
        stroke(f"Crack_{index}", visible, 0.045 if frame < 5 else 0.035, DEEP if frame < 5 else DARK, trace_alpha, z=0.01)
        if frame >= 3 and index < 3:
            midpoint = visible[-2]
            side_angle = math.atan2(visible[-1][1] - midpoint[1], visible[-1][0] - midpoint[0]) + (1.0 if index % 2 else -1.0)
            tip = (midpoint[0] + math.cos(side_angle) * 0.42, midpoint[1] + math.sin(side_angle) * 0.42)
            stroke(f"CrackSide_{index}", [midpoint, tip], 0.027, DARK, trace_alpha * 0.75, z=0.01)


def build_dash_smear(frame: int) -> None:
    alpha = (0.56, 0.86, 0.62, 0.34, 0.14)[frame]
    # Keep the fading tail inside the atlas cell-safe gutter.  At the final
    # frame the previous 0.22 multiplier put three antialias pixels on the
    # 18-pixel boundary; 0.20 preserves the read while leaving margin.
    shift = 0.20 * frame
    tapered_streak("Dash_Main", (-2.55 + shift, 0.18), (1.55 + shift, 0.38), 0.46, CORAL, alpha, intensity=1.15)
    tapered_streak("Dash_Core", (-2.10 + shift, -0.10), (1.90 + shift, 0.02), 0.19, CREAM, min(0.92, alpha * 1.18), z=0.02, intensity=1.45)
    if frame < 3:
        tapered_streak("Dash_ClothGhost", (-1.95 + shift, -0.72), (0.48 + shift, -0.42), 0.30, WARM, alpha * 0.58)
        polygon(
            "Dash_TorsoFragment",
            [(-0.70 + shift, 0.55), (0.62 + shift, 0.84), (0.28 + shift, 0.20), (-0.98 + shift, 0.05)],
            ORANGE,
            alpha * 0.46,
        )
    if frame >= 2:
        for index in range(3):
            x = -1.1 + index * 0.72 + shift
            tapered_streak(f"Dash_Fragment_{index}", (x - 0.38, -0.48 + index * 0.33), (x, -0.40 + index * 0.33), 0.07, WARM, alpha * 0.62)


def build_chain_glint(frame: int) -> None:
    centers = [(-1.55 + index * 0.78, 0.0) for index in range(5)]
    for index, center in enumerate(centers):
        points = []
        for segment in range(32):
            angle = math.tau * segment / 32
            points.append((center[0] + math.cos(angle) * 0.31, center[1] + math.sin(angle) * 0.16))
        stroke(f"Link_{index}", points, 0.035, DARK, 0.34, cyclic=True)
    active = min(4, frame)
    strength = (0.72, 1.0, 0.78, 0.48, 0.20)[frame]
    star("Chain_LocalGlint", centers[active], 0.46, 0.075, 6, CREAM, strength, rotation=frame * 0.43, z=0.03, intensity=1.8)
    if frame < 4:
        tapered_streak(
            "Chain_GlintTail",
            (centers[active][0] - 0.62, -0.05),
            (centers[active][0] - 0.08, 0.0),
            0.065,
            WARM,
            strength * 0.55,
            z=0.02,
        )


EFFECTS = {
    "Pelag_FX_AnchorSpin_4x4": (build_anchor_spin, BRIGHT_FRAMES, "spinning anchor blur"),
    "Pelag_FX_ImpactBurst_4x4": (build_impact, BRIGHT_FRAMES, "ground impact dust and sparks"),
    "Pelag_FX_GroundCrack_4x4": (build_ground_crack, 16, "persistent non-emissive ground trace"),
    "Pelag_FX_DashSmear_4x4": (build_dash_smear, BRIGHT_FRAMES, "body dash smear"),
    "Pelag_FX_ChainGlint_4x4": (build_chain_glint, BRIGHT_FRAMES, "localized chain-tension glint"),
}


def render_frame(path: Path) -> None:
    SCENE.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def build_atlas(name: str, builder, authored_frames: int) -> dict[str, object]:
    FRAME_DIR.mkdir(parents=True, exist_ok=True)
    atlas_pixels = array("f", [0.0]) * (ATLAS_SIZE * ATLAS_SIZE * 4)
    cell_stats = []
    for frame in range(authored_frames):
        clear_render_shapes()
        builder(frame)
        frame_path = FRAME_DIR / f"{name}_{frame:02d}.png"
        render_frame(frame_path)
        frame_image = bpy.data.images.load(str(frame_path), check_existing=False)
        frame_pixels = array("f", [0.0]) * (CELL * CELL * 4)
        frame_image.pixels.foreach_get(frame_pixels)
        max_alpha = max(frame_pixels[3::4])
        nonzero_alpha = sum(1 for value in frame_pixels[3::4] if value > 1.0 / 255.0)
        cell_stats.append({"frame": frame, "max_alpha": max_alpha, "nonzero_alpha_pixels": nonzero_alpha})
        column = frame % GRID
        row_from_top = frame // GRID
        destination_y = (GRID - 1 - row_from_top) * CELL
        for source_y in range(CELL):
            source_start = source_y * CELL * 4
            destination_start = ((destination_y + source_y) * ATLAS_SIZE + column * CELL) * 4
            atlas_pixels[destination_start : destination_start + CELL * 4] = frame_pixels[
                source_start : source_start + CELL * 4
            ]
        bpy.data.images.remove(frame_image)

    atlas = bpy.data.images.new(name, width=ATLAS_SIZE, height=ATLAS_SIZE, alpha=True, float_buffer=False)
    atlas.alpha_mode = "STRAIGHT"
    atlas.pixels.foreach_set(atlas_pixels)
    atlas.file_format = "PNG"
    destination = TEXTURES / f"{name}.png"
    destination.parent.mkdir(parents=True, exist_ok=True)
    atlas.filepath_raw = str(destination)
    atlas.save()
    bpy.data.images.remove(atlas)

    if not cell_stats or cell_stats[0]["nonzero_alpha_pixels"] == 0:
        raise RuntimeError(f"Atlas {name} has no visible first frame")
    return {
        "path": str(destination.relative_to(PROJECT)).replace("\\", "/"),
        "absolute_path": str(destination),
        "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
        "width": ATLAS_SIZE,
        "height": ATLAS_SIZE,
        "grid": [GRID, GRID],
        "cell": [CELL, CELL],
        "layout": "row-major, frame 0 at top-left",
        "authored_frames": authored_frames,
        "cell_stats": cell_stats,
    }


def import_references() -> None:
    reference_collection = bpy.data.collections.new("REF_AnchorKit")
    SCENE.collection.children.link(reference_collection)
    reference_image = bpy.data.images.load(str(REFERENCE_TEXTURE), check_existing=True)
    reference_image.filepath = "//../../../Weapons/Pelag/AnchorChain/Pelag_AnchorChain_BaseColor.jpg"
    for path in REFERENCE_FILES:
        before = set(bpy.context.scene.objects)
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
        imported = [obj for obj in bpy.context.scene.objects if obj not in before]
        for obj in imported:
            for collection in list(obj.users_collection):
                collection.objects.unlink(obj)
            reference_collection.objects.link(obj)
            obj.hide_render = True
            obj.hide_viewport = False
            obj["source_fbx"] = str(path)
            if obj.type == "MESH":
                for slot in obj.material_slots:
                    material_data = slot.material
                    if material_data is None or material_data.node_tree is None:
                        continue
                    for node in material_data.node_tree.nodes:
                        if node.type == "TEX_IMAGE":
                            node.image = reference_image
    for image in list(bpy.data.images):
        if image != reference_image and image.users == 0:
            bpy.data.images.remove(image)
    reference_collection["purpose"] = "Canonical anchor head, grip, and 96-triangle link references; do not rename."


def build_source_preview_collections() -> None:
    global ACTIVE_COLLECTION
    if ACTIVE_COLLECTION and ACTIVE_COLLECTION.name in bpy.data.collections:
        clear_render_shapes()
        bpy.data.collections.remove(ACTIVE_COLLECTION)
    representatives = {
        "FX_AnchorSpin": (build_anchor_spin, 1, -12.0),
        "FX_ImpactBurst": (build_impact, 1, -6.0),
        "FX_GroundCrack": (build_ground_crack, 6, 0.0),
        "FX_DashSmear": (build_dash_smear, 1, 6.0),
        "FX_ChainGlint": (build_chain_glint, 2, 12.0),
    }
    for name, (builder, frame, offset) in representatives.items():
        collection = bpy.data.collections.new(name)
        SCENE.collection.children.link(collection)
        ACTIVE_COLLECTION = collection
        builder(frame)
        for obj in collection.objects:
            obj.location.x += offset
        collection["preview_frame"] = frame
        collection["atlas_fps"] = FPS
        collection["bright_cutoff_frames"] = BRIGHT_FRAMES
    SCENE["pelag_vfx_palette"] = "#FBEBC9 #F48042 #EF894E #F0DCBF"
    SCENE["bright_lifetime"] = "5 frames / 0.1667 seconds at 30 fps"
    SCENE["ground_trace_exception"] = "GroundCrack frames 5-15 are dark, low-emissive trace only"


def main() -> None:
    TEXTURES.mkdir(parents=True, exist_ok=True)
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    BUILD.mkdir(parents=True, exist_ok=True)
    setup_scene()
    import_references()
    # FBX import may restore the source file's scene rate.  The source blend is
    # part of the deliverable, so assert the simulation rate again afterwards.
    SCENE.render.fps = FPS
    SCENE.render.fps_base = 1.0

    assets = []
    report_effects = {}
    for name, (builder, authored_frames, role) in EFFECTS.items():
        result = build_atlas(name, builder, authored_frames)
        result["role"] = role
        result["background"] = "transparent"
        result["pivot"] = [0.5, 0.5]
        result["runtime_display_m"] = [3.0, 3.0]
        result["collision_envelope"] = None
        result["bright_frames"] = BRIGHT_FRAMES
        result["bright_lifetime_seconds"] = BRIGHT_FRAMES / FPS
        result["persistent_exception"] = name == "Pelag_FX_GroundCrack_4x4"
        assets.append(result)
        report_effects[name] = result
        print(f"Built {name}.png")

    manifest = {
        "schemaVersion": 1,
        "generator": "Blender 5.2 Pelag VFX deterministic pipeline",
        "source": str(SOURCE_BLEND.relative_to(PROJECT)).replace("\\", "/"),
        "fps": FPS,
        "acceptance": {
            "transparent_background": True,
            "no_text_or_watermark": True,
            "atlas_gutter_policy": "geometry remains inside an 18-pixel cell-safe margin",
            "bright_alpha_zero_from_frame": BRIGHT_FRAMES,
            "ground_trace_exception": "GroundCrack may persist through frame 15; cream core is absent from frame 5 onward",
        },
        "assets": [
            {key: value for key, value in asset.items() if key not in {"absolute_path", "cell_stats"}}
            for asset in assets
        ],
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    build_source_preview_collections()
    SCENE.render.fps = FPS
    SCENE.render.fps_base = 1.0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND), check_existing=False)
    report = {
        "blender_version": bpy.app.version_string,
        "render_engine": SCENE.render.engine,
        "fps": SCENE.render.fps / SCENE.render.fps_base,
        "source_blend": str(SOURCE_BLEND),
        "manifest": str(MANIFEST),
        "references": [str(path) for path in REFERENCE_FILES],
        "effects": report_effects,
    }
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Saved editable source {SOURCE_BLEND}")
    print(f"VFX report {REPORT}")


if __name__ == "__main__":
    main()
