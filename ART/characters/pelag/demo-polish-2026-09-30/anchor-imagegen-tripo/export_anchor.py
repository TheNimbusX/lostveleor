"""Re-export the paid Tripo anchor from its editable Blender source.

Open Pelag_Anchor_Tripo_Work.blend and run this script. If used from an
empty isolated scene it imports the original GLB; it creates no geometry.
The empty Anchor_Attachment is a chain socket, not a visible model.
"""
import bpy
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "export"
OUT.mkdir(exist_ok=True)
anchor = bpy.data.objects.get("Pelag_AnchorHead_Tripo")
if anchor is None:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(ROOT / "tripo-out/pelag-anchor-demo-313b5f0a/model.glb"))
    imported = set(bpy.data.objects) - before
    meshes = [o for o in imported if o.type == "MESH"]
    assert len(meshes) == 1, "Inspect a changed Tripo export before continuing."
    anchor = meshes[0]
    anchor.name = "Pelag_AnchorHead_Tripo"
    bpy.ops.object.select_all(action="DESELECT")
    anchor.select_set(True)
    bpy.context.view_layer.objects.active = anchor
    anchor.rotation_mode = "XYZ"
    anchor.rotation_euler.z = -math.pi / 2
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    collection = bpy.data.collections.new("COL_PelagAnchor_Tripo")
    bpy.context.scene.collection.children.link(collection)
    for c in list(anchor.users_collection):
        c.objects.unlink(anchor)
    collection.objects.link(anchor)

socket = next((o for o in anchor.children if o.type == "EMPTY"
               and o.name.startswith("Anchor_Attachment")), None)
if socket is None:
    socket = bpy.data.objects.new("Anchor_Attachment", None)
    anchor.users_collection[0].objects.link(socket)
    socket.parent = anchor
socket.name = "Anchor_Attachment"
socket.location = (0, -.01, .405)
socket.rotation_euler = (0, 0, 0)
socket.scale = (1, 1, 1)
assert socket.name == "Anchor_Attachment", "Remove only your obsolete duplicate socket."
bpy.context.view_layer.update()
anchor.data.calc_loop_triangles()
assert len(anchor.data.loop_triangles) <= 6000
bpy.ops.object.select_all(action="DESELECT")
anchor.select_set(True)
socket.select_set(True)
bpy.context.view_layer.objects.active = anchor
bpy.ops.export_scene.fbx(
    filepath=str(OUT / "Pelag_AnchorHead_Tripo.fbx"),
    use_selection=True, object_types={"MESH", "EMPTY"},
    axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
    bake_space_transform=True, bake_anim=False, use_mesh_modifiers=True,
    path_mode="AUTO", embed_textures=False,
)
(ROOT / "model-audit.json").write_text(json.dumps({
    "source": "Tripo task 313b5f0a-278d-4bcb-bacd-6c7ef17454d6",
    "triangles": len(anchor.data.loop_triangles),
    "dimensions_blender_m": list(anchor.dimensions),
    "chain_socket_blender_local_m": list(socket.location),
    "fbx_axes": "-Z forward, Y up",
    "exported_objects": [anchor.name, socket.name],
    "textures": ["Anchor_BaseColor.png", "Anchor_NormalGL.png", "Anchor_ORM.png"],
}, ensure_ascii=False, indent=2), encoding="utf-8")
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "Pelag_Anchor_Tripo_Work.blend"))
