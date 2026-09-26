"""Render non-destructive Forest Wendigo action reviews from an animated .blend.

Example:
  blender -b Wendigo_Rig.blend --python render_clip_review.py -- \
      --output production/preview/output

The input .blend is never saved. Each action gets fixed right-side and game-camera
PNG sequences, key-pose stills, and a JSON record of evaluated geometry and
projected silhouette bounds. MP4s are also made when ffmpeg is on PATH.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import shutil
import subprocess
import sys
from pathlib import Path

import bpy
from mathutils import Vector


PREFIX = "AN_ForestWendigo_"
SCRIPT_DIR = Path(__file__).resolve().parent
CONTRACT_PATH = SCRIPT_DIR.parent / "animation_contract.json"


def arguments():
    raw = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=SCRIPT_DIR / "output")
    parser.add_argument("--width", type=int, default=512)
    parser.add_argument("--height", type=int, default=512)
    parser.add_argument("--stills-only", action="store_true",
                        help="Render key poses only; still measure every animation frame.")
    parser.add_argument("--measure-only", action="store_true")
    parser.add_argument("--no-mp4", action="store_true")
    parser.add_argument("--max-frames", type=int, default=0,
                        help="Development smoke test only; 0 means the whole action.")
    parser.add_argument("--require-actions", action="store_true")
    parser.add_argument("--no-floor", action="store_true")
    parser.add_argument("--front", action="store_true",
                        help="Also render a front view for reference comparison.")
    parser.add_argument("--extra-key-frames", type=str, default="",
                        help="Comma-separated additional still frames for pose review.")
    parser.add_argument("--action", type=str, default="",
                        help="Render only the named action; empty renders every Wendigo action.")
    return parser.parse_args(raw)


def identify_character_meshes(scene):
    meshes = [obj for obj in scene.objects if obj.type == "MESH" and not obj.hide_render]
    skinned = [obj for obj in meshes if obj.find_armature() is not None]
    if skinned:
        return skinned
    candidates = [obj for obj in meshes if not any(
        word in obj.name.lower() for word in ("floor", "ground", "grid", "plane")
    )]
    if not candidates:
        candidates = meshes
    if not candidates:
        raise RuntimeError("No visible character mesh found")
    largest = max(candidates, key=lambda obj: len(obj.data.vertices))
    # Unrigged static review usually has one mesh. Include other mesh children.
    return [obj for obj in candidates if obj == largest or obj.parent == largest]


def make_review_collection(scene):
    collection = bpy.data.collections.new("COL_ForestWendigo_ReviewOnly")
    scene.collection.children.link(collection)
    return collection


def look_at(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def make_camera(collection, name, target, direction, ortho_scale):
    data = bpy.data.cameras.new(name)
    data.type = "ORTHO"
    data.ortho_scale = ortho_scale
    camera = bpy.data.objects.new(name, data)
    collection.objects.link(camera)
    camera.location = target + direction.normalized() * 12.0
    look_at(camera, target)
    return camera


def add_review_lights(collection, target):
    lights = (
        ("Key", Vector((3.5, 4.0, 5.0)), 850, (1.0, 0.91, 0.79)),
        ("Fill", Vector((-4.0, 2.0, 2.8)), 420, (0.72, 0.84, 1.0)),
        ("Rim", Vector((0.0, -4.5, 4.5)), 980, (0.92, 0.98, 1.0)),
    )
    for name, offset, energy, color in lights:
        data = bpy.data.lights.new("Review_" + name, "AREA")
        data.energy = energy
        data.shape = "DISK"
        # A smaller key produces a defined contact shadow. The former 3.2 m
        # disk made planted paws appear to float in the review renders.
        data.size = 1.35 if name == "Key" else 3.2
        data.color = color
        obj = bpy.data.objects.new(data.name, data)
        collection.objects.link(obj)
        obj.location = target + offset
        look_at(obj, target)


def add_floor(collection, minimum_z):
    bpy.ops.mesh.primitive_plane_add(size=100, location=(0, 0, minimum_z - 0.012))
    floor = bpy.context.object
    floor.name = "Review_Floor_NotExported"
    for old in list(floor.users_collection):
        old.objects.unlink(floor)
    collection.objects.link(floor)
    material = bpy.data.materials.new("Review_Ground_Matte")
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (0.075, 0.095, 0.085, 1)
    shader.inputs["Roughness"].default_value = 0.95
    floor.data.materials.append(material)
    return floor


def setup_review_scene(scene, meshes, args):
    scene.frame_set(scene.frame_current)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    rest = evaluated_geometry(meshes, depsgraph, None)
    if rest["vertex_count"] == 0:
        raise RuntimeError("Character has no evaluated geometry")
    lo, hi = Vector(rest["world_min"]), Vector(rest["world_max"])
    target = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z + 1.55))
    # One scale across all clips and both camera angles. The tall antlers and
    # 2.7 m claw sweep need margin; vertical framing remains 3.1 m aware.
    scale = max(5.5, (hi - lo).z * 1.65, (hi - lo).x * 1.5,
                (hi - lo).y * 1.5)
    collection = make_review_collection(scene)
    cameras = {
        "side": make_camera(collection, "CAM_Wendigo_Side", target,
                            Vector((1, 0, 0.12)), scale),
        "game": make_camera(collection, "CAM_Wendigo_Game", target,
                            Vector((1, 1, 0.85)), scale),
    }
    if args.front:
        cameras["front"] = make_camera(collection, "CAM_Wendigo_Front", target,
                                       Vector((0, 1, 0.12)), scale)
    if not any(obj.type == "LIGHT" and not obj.hide_render for obj in scene.objects
               if obj.name not in {camera.name for camera in cameras.values()}):
        add_review_lights(collection, target)
    if not args.no_floor:
        add_floor(collection, lo.z)
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Review_World")
        scene.world.color = (0.16, 0.18, 0.16)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = args.width
    scene.render.resolution_y = args.height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.fps = 30
    return cameras, rest


def evaluated_geometry(meshes, depsgraph, projection_matrices):
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    silhouettes = {name: [math.inf, math.inf, -math.inf, -math.inf]
                   for name in (projection_matrices or {})}
    vertex_count = 0
    triangle_count = 0
    for obj in meshes:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
        if mesh is None:
            continue
        try:
            mesh.calc_loop_triangles()
            triangle_count += len(mesh.loop_triangles)
            vertex_count += len(mesh.vertices)
            world = evaluated.matrix_world
            for vertex in mesh.vertices:
                point = world @ vertex.co
                x, y, z = point.x, point.y, point.z
                lo[0] = min(lo[0], x)
                lo[1] = min(lo[1], y)
                lo[2] = min(lo[2], z)
                hi[0] = max(hi[0], x)
                hi[1] = max(hi[1], y)
                hi[2] = max(hi[2], z)
                for name, matrix in (projection_matrices or {}).items():
                    cx = matrix[0][0]*x + matrix[0][1]*y + matrix[0][2]*z + matrix[0][3]
                    cy = matrix[1][0]*x + matrix[1][1]*y + matrix[1][2]*z + matrix[1][3]
                    cw = matrix[3][0]*x + matrix[3][1]*y + matrix[3][2]*z + matrix[3][3]
                    if abs(cw) < 1e-9:
                        continue
                    u, v = 0.5*(cx/cw + 1), 0.5*(cy/cw + 1)
                    bounds = silhouettes[name]
                    bounds[0] = min(bounds[0], u)
                    bounds[1] = min(bounds[1], v)
                    bounds[2] = max(bounds[2], u)
                    bounds[3] = max(bounds[3], v)
        finally:
            evaluated.to_mesh_clear()
    if vertex_count == 0:
        return {"vertex_count": 0, "triangle_count": 0,
                "world_min": [0, 0, 0], "world_max": [0, 0, 0],
                "silhouette_ndc": {}}
    return {
        "vertex_count": vertex_count,
        "triangle_count": triangle_count,
        "world_min": [round(value, 5) for value in lo],
        "world_max": [round(value, 5) for value in hi],
        "silhouette_ndc": {name: [round(value, 5) for value in bounds]
                           for name, bounds in silhouettes.items()},
    }


def contract_for(action_name):
    if not CONTRACT_PATH.exists():
        return {}
    contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
    return next((clip for clip in contract.get("clips", [])
                 if clip.get("name") == action_name), {})


def action_frames(action):
    contract = contract_for(action.name)
    if contract.get("frames"):
        first, last = contract["frames"]
    else:
        first, last = action.frame_range
    first, last = int(round(first)), int(round(last))
    if last < first:
        raise RuntimeError("Invalid action range: " + action.name)
    return list(range(first, last + 1)), contract


def key_frames(frames, contract):
    start, end = frames[0], frames[-1]
    keys = {start, end, int(round(start + (end-start)*0.25)),
            int(round(start + (end-start)*0.5)),
            int(round(start + (end-start)*0.75))}
    for name, value in contract.items():
        if name.endswith("_frame") and isinstance(value, int) and start <= value <= end:
            keys.add(value)
    return sorted(keys)


def assign_action(action, scene):
    armatures = sorted((obj for obj in scene.objects if obj.type == "ARMATURE"),
                       key=lambda obj: len(obj.data.bones), reverse=True)
    other_objects = [obj for obj in scene.objects
                     if obj.type in {"MESH", "EMPTY"}
                     and not obj.name.startswith("Review_")]
    targets = armatures + other_objects
    for obj in targets:
        animation = obj.animation_data
        if animation is not None:
            animation.action = None
            for track in animation.nla_tracks:
                track.mute = True
    slots = list(action.slots) if hasattr(action, "slots") else []
    assignments = []
    if not slots:
        for target in targets:
            try:
                target.animation_data_create().action = action
                assignments.append({"target": target.name, "slot": None})
                break
            except (RuntimeError, TypeError, ValueError):
                continue
    else:
        for slot in slots:
            slot_type = getattr(slot, "target_id_type", getattr(slot, "id_type", "OBJECT"))
            if slot_type != "OBJECT":
                # Shape-key slots are uncommon for this asset; report them
                # rather than silently assigning to an incompatible object.
                assignments.append({"target": None, "slot": slot.identifier,
                                    "warning": "unsupported_slot_type:" + str(slot_type)})
                continue
            identifier = slot.identifier.removeprefix("OB").lower()
            sorted_targets = sorted(targets,
                                    key=lambda obj: (identifier not in obj.name.lower(),
                                                     obj.type != "ARMATURE"))
            success = False
            for target in sorted_targets:
                if any(item.get("target") == target.name for item in assignments):
                    continue
                try:
                    animation = target.animation_data_create()
                    animation.action = action
                    animation.action_slot = slot
                    assignments.append({"target": target.name, "slot": slot.identifier})
                    success = True
                    break
                except (RuntimeError, TypeError, ValueError):
                    continue
            if not success:
                assignments.append({"target": None, "slot": slot.identifier,
                                    "warning": "no_compatible_target"})
    if not any(item["target"] for item in assignments):
        raise RuntimeError("Could not assign action " + action.name)
    return assignments


def camera_projection(scene, depsgraph, camera):
    return (camera.calc_matrix_camera(depsgraph, x=scene.render.resolution_x,
                                      y=scene.render.resolution_y,
                                      scale_x=scene.render.pixel_aspect_x,
                                      scale_y=scene.render.pixel_aspect_y)
            @ camera.matrix_world.inverted())


def render_frame(scene, camera, path):
    scene.camera = camera
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def try_encode_video(frame_dir, start, output):
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        return None
    command = [ffmpeg, "-y", "-loglevel", "error", "-framerate", "30",
               "-start_number", str(start), "-i", str(frame_dir / "%04d.png"),
               "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "19",
               str(output)]
    result = subprocess.run(command, text=True, capture_output=True)
    return str(output) if result.returncode == 0 else None


def try_encode_animated(frame_dir, output):
    """Pillow is provided by the workspace Python, outside Blender's Python."""
    python = shutil.which("python") or shutil.which("python3")
    if not python:
        return None
    command = [python, str(SCRIPT_DIR / "encode_preview.py"),
               str(frame_dir), str(output), "--fps", "30"]
    result = subprocess.run(command, text=True, capture_output=True)
    if result.returncode != 0:
        print("ANIMATED_WEBP_FAILED", result.stderr.strip(), flush=True)
        return None
    print(result.stdout.strip(), flush=True)
    return str(output)


def main():
    args = arguments()
    if args.width <= 0 or args.height <= 0:
        raise ValueError("Resolution must be positive")
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    meshes = identify_character_meshes(scene)
    cameras, rest = setup_review_scene(scene, meshes, args)
    manifest = {
        "source_blend": bpy.data.filepath,
        "render_fps": 30,
        "forward_axis": "+Y",
        "expected_height_m": 3.1,
        "triangle_limit": 25000,
        "character_meshes": [obj.name for obj in meshes],
        "rest_sample": rest,
        "camera_positions": {name: list(camera.location) for name, camera in cameras.items()},
        "actions": {},
    }
    actions = sorted((action for action in bpy.data.actions if action.name.startswith(PREFIX)),
                     key=lambda action: action.name)
    if args.action:
        actions = [action for action in actions if action.name == args.action]
    if args.require_actions and not actions:
        raise RuntimeError("No AN_ForestWendigo_* actions in input .blend")
    if not actions:
        manifest["warning"] = "No actions found; static camera/lighting smoke test only"
        for name, camera in cameras.items():
            if not args.measure_only:
                render_frame(scene, camera, out / ("static_" + name + ".png"))
    for action in actions:
        frames, contract = action_frames(action)
        if args.max_frames:
            frames = frames[:args.max_frames]
        assignments = assign_action(action, scene)
        action_dir = out / re.sub(r"[^A-Za-z0-9_-]+", "_", action.name)
        action_dir.mkdir(exist_ok=True)
        keys = key_frames(frames, contract)
        if args.extra_key_frames:
            keys = sorted(set(keys) | {int(value) for value in args.extra_key_frames.split(",")
                                       if value.strip() and int(value) in frames})
        record = {
            "assigned": assignments,
            "frame_start": frames[0], "frame_end": frames[-1],
            "key_frames": keys, "contact_markers": {
                key: value for key, value in contract.items() if key.endswith("_frame")},
            "frames": [], "outputs": {},
        }
        manifest["actions"][action.name] = record
        for frame in frames:
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            projections = {name: camera_projection(scene, depsgraph, camera)
                           for name, camera in cameras.items()}
            measure = evaluated_geometry(meshes, depsgraph, projections)
            measure["frame"] = frame
            measure["over_triangle_budget"] = measure["triangle_count"] > 25000
            record["frames"].append(measure)
            if args.measure_only or (args.stills_only and frame not in keys):
                continue
            for name, camera in cameras.items():
                camera_dir = action_dir / name
                frame_dir = camera_dir / "frames"
                frame_dir.mkdir(parents=True, exist_ok=True)
                png = frame_dir / f"{frame:04d}.png"
                render_frame(scene, camera, png)
                if frame in keys:
                    key_dir = camera_dir / "keyposes"
                    key_dir.mkdir(exist_ok=True)
                    shutil.copyfile(png, key_dir / png.name)
        for name in cameras:
            camera_dir = action_dir / name
            sequence = camera_dir / "frames"
            if sequence.exists():
                record["outputs"][name] = {"png_sequence": str(sequence),
                                           "keyposes": str(camera_dir / "keyposes")}
                if not args.stills_only and not args.max_frames:
                    mp4 = (try_encode_video(sequence, frames[0], camera_dir / "review.mp4")
                           if not args.no_mp4 else None)
                    if mp4:
                        record["outputs"][name]["mp4"] = mp4
                    else:
                        webp = try_encode_animated(sequence, camera_dir / "review.webp")
                        if webp:
                            record["outputs"][name]["animated_webp"] = webp
        rows = record["frames"]
        union_min = [min(row["world_min"][axis] for row in rows) for axis in range(3)]
        union_max = [max(row["world_max"][axis] for row in rows) for axis in range(3)]
        record["summary"] = {
            "max_triangles": max(row["triangle_count"] for row in rows),
            "over_triangle_budget": any(row["over_triangle_budget"] for row in rows),
            "min_height_m": round(min(row["world_max"][2] - row["world_min"][2]
                                      for row in rows), 5),
            "max_height_m": round(max(row["world_max"][2] - row["world_min"][2]
                                      for row in rows), 5),
            "world_union_min": [round(value, 5) for value in union_min],
            "world_union_max": [round(value, 5) for value in union_max],
            "offscreen_frames": {
                name: [row["frame"] for row in rows
                       if (bounds := row["silhouette_ndc"][name])[0] < 0
                       or bounds[1] < 0 or bounds[2] > 1 or bounds[3] > 1]
                for name in cameras
            },
            "loop_end_bbox_delta_m": round(max(
                abs(rows[0][key][axis] - rows[-1][key][axis])
                for key in ("world_min", "world_max") for axis in range(3)), 5),
        }
        print("REVIEW_ACTION", action.name, "FRAMES", len(frames),
              "TRIS", record["summary"]["max_triangles"],
              "ASSIGN", assignments, flush=True)
        (out / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False,
                                                       indent=2), encoding="utf-8")
    (out / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False,
                                                   indent=2), encoding="utf-8")
    print("REVIEW_MANIFEST", out / "manifest.json", flush=True)


if __name__ == "__main__":
    main()
