"""Quick look at the re-imported FBX: bones, rest heads, actions, fps, FBX header timing."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
from check_common import FBX, import_fbx, scene  # noqa: E402
from io_scene_fbx import parse_fbx  # noqa: E402

root, ver = parse_fbx.parse(str(FBX))
info = {"fbx_version": ver, "stacks": [], "global": {}}
for el in root.elems:
    if el.id == b"GlobalSettings":
        for p in el.elems:
            if p.id == b"Properties70":
                for q in p.elems:
                    k = q.props[0].decode(errors="replace")
                    if k in ("TimeMode", "CustomFrameRate", "UnitScaleFactor", "UpAxis", "FrontAxis",
                             "UpAxisSign", "FrontAxisSign", "CoordAxis", "CoordAxisSign", "TimeSpanStart",
                             "TimeSpanStop"):
                        info["global"][k] = q.props[-1]
    if el.id == b"Objects":
        for ob in el.elems:
            if ob.id == b"AnimationStack":
                d = {"name": ob.props[1].split(b"\x00")[0].decode()}
                for p in ob.elems:
                    if p.id == b"Properties70":
                        for q in p.elems:
                            d[q.props[0].decode()] = q.props[-1]
                for k in ("LocalStart", "LocalStop", "ReferenceStart", "ReferenceStop"):
                    if k in d:
                        d[k + "_frames"] = round(d[k] / 46186158000 * 30, 4)
                info["stacks"].append(d)

arm, mesh = import_fbx()
info["scene_fps"] = [scene.render.fps, scene.render.fps_base]
info["objects"] = [(o.name, o.type, o.parent.name if o.parent else None, [round(x, 4) for x in o.matrix_world.translation],
                    [round(x, 4) for x in o.matrix_world.to_scale()]) for o in scene.objects]
info["bones"] = {b.name: {"parent": b.parent.name if b.parent else None,
                          "head": [round(x, 4) for x in arm.matrix_world @ b.head_local]} for b in arm.data.bones}
info["actions"] = {a.name: [round(x, 3) for x in a.frame_range] for a in bpy.data.actions}
info["mesh"] = {"verts": len(mesh.data.vertices), "polys": len(mesh.data.polygons),
                "tris": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
                "groups": len(mesh.vertex_groups), "materials": [m.name for m in mesh.data.materials if m],
                "modifiers": [(m.type, getattr(m, "object", None) and m.object.name) for m in mesh.modifiers]}
print("PEEK", json.dumps(info, default=str), flush=True)
