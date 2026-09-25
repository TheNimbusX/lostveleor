"""Probe Blender's QuadriFlow remesher on the approved 24k Wendigo candidate."""

import json
import sys
import time
from pathlib import Path

import bpy


source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
target = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
target.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
if len(meshes) != 1:
    raise RuntimeError(f"Expected one mesh, found {len(meshes)}")
obj = meshes[0]
copy = obj.copy()
copy.data = obj.data.copy()
bpy.context.scene.collection.objects.link(copy)
obj.hide_set(True)
obj.hide_render = True
bpy.ops.object.select_all(action="DESELECT")
copy.select_set(True)
bpy.context.view_layer.objects.active = copy
copy.name = "SM_ForestWendigo_QuadriFlowProbe"
started = time.monotonic()
result = {"source": str(source)}
try:
    print("QUADRIFLOW_START", flush=True)
    print("QUADRIFLOW_PROPERTIES", list(bpy.ops.object.quadriflow_remesh.get_rna_type().properties.keys()), flush=True)
    bpy.ops.object.quadriflow_remesh(mode="FACES", target_faces=10500,
                                    use_preserve_sharp=True, use_preserve_boundary=True,
                                    preserve_attributes=True, use_mesh_symmetry=False,
                                    seed=1)
    copy.data.calc_loop_triangles()
    result.update({
        "status": "success",
        "vertices": len(copy.data.vertices),
        "faces": len(copy.data.polygons),
        "triangles": len(copy.data.loop_triangles),
        "uv_layers": len(copy.data.uv_layers),
    })
    bpy.ops.wm.save_as_mainfile(filepath=str(target / "quadriflow_probe.blend"))
except Exception as exc:
    result.update({"status": "error", "error": repr(exc)})
result["seconds"] = round(time.monotonic() - started, 2)
(target / "quadriflow_probe.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result), flush=True)
