"""Save the master rig .blend (textures unpacked to rig/textures) and the Unity FBX (Stonehoof settings).
blender -b -P export_rig.py -- <built_rig.blend> <rig_dir> <out_export_json>
"""
import sys, json, re, hashlib
from pathlib import Path
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, rig_dir, rep = Path(argv[0]).resolve(), Path(argv[1]).resolve(), Path(argv[2]).resolve()
tex_dir = rig_dir / "textures"
tex_dir.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(src))
scene = bpy.context.scene
scene.render.fps = 30
mesh = next(o for o in scene.objects if o.type == "MESH")
arm = next(o for o in scene.objects if o.type == "ARMATURE")

# keep only images the mob's material uses (the candidate also carries a Rodin-addon placeholder)
used = {n.image for m in mesh.data.materials if m and m.node_tree
        for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
for m in list(bpy.data.materials):
    if m.users == 0 or m not in list(mesh.data.materials):
        if m.users == 0 or m.name == "Material":
            bpy.data.materials.remove(m)
for img in list(bpy.data.images):
    if img.source == "FILE" and img not in used:
        bpy.data.images.remove(img)
textures = []
for img in bpy.data.images:
    if img.source != "FILE" or img.size[0] <= 0:
        continue
    clean = re.sub(r"[^A-Za-z0-9_-]+", "_", img.name.split("_64347b8c")[0]).strip("_")
    target = tex_dir / f"T_ForestRootSnarer_{clean}.png"
    img.filepath_raw = str(target)
    img.file_format = "PNG"
    img.save()
    if img.packed_file:
        img.unpack(method="REMOVE")
    img.filepath = str(target)
    img.reload()
    textures.append(str(target))
for m in mesh.data.materials:
    if m and m.name.startswith("tripo_material"):
        m.name = "M_ForestRootSnarer"

master = rig_dir / "ForestRootSnarer_Rig.blend"
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.wm.save_as_mainfile(filepath=str(master), relative_remap=True)

bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = rig_dir / "ForestRootSnarer_Rig.fbx"
bpy.ops.export_scene.fbx(
    filepath=str(fbx), use_selection=True, object_types={"ARMATURE", "MESH"},
    add_leaf_bones=False, use_armature_deform_only=True, axis_forward="-Z", axis_up="Y",
    apply_unit_scale=True, global_scale=1.0, apply_scale_options="FBX_SCALE_NONE",
    bake_anim=False, mesh_smooth_type="FACE", use_mesh_modifiers=True,
    use_tspace=False, path_mode="RELATIVE", embed_textures=False, armature_nodetype="NULL")

me = mesh.data
me.calc_loop_triangles()
deform = [b.name for b in arm.data.bones if b.use_deform]
names = {g.index: g.name for g in mesh.vertex_groups}
counts = {n: 0 for n in deform}
zero = over4 = unnorm = 0
for v in me.vertices:
    ws = [(names[g.group], g.weight) for g in v.groups if g.weight > 1e-6 and names[g.group] in deform]
    zero += not ws
    over4 += len(ws) > 4
    unnorm += bool(ws) and abs(sum(w for _, w in ws) - 1) > 0.005
    for n, w in ws:
        counts[n] += 1
out = {
    "master_blend": str(master), "unity_fbx": str(fbx),
    "fbx_sha256": hashlib.sha256(fbx.read_bytes()).hexdigest(), "textures": textures,
    "fbx_settings": {"axis_forward": "-Z", "axis_up": "Y", "deform_only": True, "leaf_bones": False,
                     "bake_anim": False, "note": "same axis/deform settings as Stonehoof unity_package"},
    "triangles": len(me.loop_triangles), "vertices": len(me.vertices),
    "materials": [m.name for m in me.materials if m],
    "bones": [{"name": b.name, "parent": b.parent.name if b.parent else None, "deform": b.use_deform,
               "head": [round(c, 4) for c in b.head_local], "tail": [round(c, 4) for c in b.tail_local]}
              for b in arm.data.bones],
    "weights": {"unweighted": zero, "over_4": over4, "unnormalized": unnorm, "verts_per_bone": counts},
    "constraints": {pb.name: [c.name for c in pb.constraints] for pb in arm.pose.bones if pb.constraints},
}
rep.write_text(json.dumps(out, indent=2), encoding="utf-8")
print("EXPORT_DONE", json.dumps({k: out[k] for k in ("triangles", "weights")}), flush=True)
