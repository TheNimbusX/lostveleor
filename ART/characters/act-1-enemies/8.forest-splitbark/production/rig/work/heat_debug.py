"""Debug why bone heat fails: sphere sanity check, then the voxel proxy bone by bone."""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import rig_spec as spec  # noqa: E402

scene = bpy.context.scene


def arm_with(bones, name):
    ad = bpy.data.armatures.new(name)
    ao = bpy.data.objects.new(name, ad)
    scene.collection.objects.link(ao)
    bpy.ops.object.select_all(action="DESELECT")
    ao.select_set(True)
    bpy.context.view_layer.objects.active = ao
    bpy.ops.object.mode_set(mode="EDIT")
    for n, h, t in bones:
        b = ad.edit_bones.new(n)
        b.head, b.tail = Vector(h), Vector(t)
    bpy.ops.object.mode_set(mode="OBJECT")
    return ao


def heat(mesh, arm):
    mesh.vertex_groups.clear()
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    cnt = sum(1 for v in mesh.data.vertices if any(g.weight > 1e-6 for g in v.groups))
    mesh.parent = None
    mesh.modifiers.clear()
    return cnt, len(mesh.data.vertices)


bpy.ops.mesh.primitive_uv_sphere_add(radius=0.5, location=(3, 0, 0.5))
sph = bpy.context.active_object
a = arm_with([("b", (3, 0, 0.3), (3, 0, 0.7))], "A_sph")
print("SPHERE", heat(sph, a), flush=True)

orig = bpy.data.objects[spec.MESH_NAME]
proxy = orig.copy()
proxy.data = orig.data.copy()
scene.collection.objects.link(proxy)
rem = proxy.modifiers.new("V", "REMESH")
rem.mode = "VOXEL"
rem.voxel_size = 0.012
bpy.ops.object.select_all(action="DESELECT")
proxy.select_set(True)
bpy.context.view_layer.objects.active = proxy
bpy.ops.object.modifier_apply(modifier="V")
print("PROXY faces", len(proxy.data.polygons), flush=True)
import bmesh
bm = bmesh.new(); bm.from_mesh(proxy.data); bm.verts.ensure_lookup_table()
seen = set(); comps = []
for v in bm.verts:
    if v.index in seen: continue
    st = [v]; seen.add(v.index); comp = []
    while st:
        c = st.pop(); comp.append(c)
        for e in c.link_edges:
            w = e.other_vert(c)
            if w.index not in seen: seen.add(w.index); st.append(w)
    comps.append(comp)
comps.sort(key=len, reverse=True)
print("COMPONENTS", len(comps), [len(c) for c in comps[:8]], flush=True)
bad = [v for c in comps[1:] for v in c]
bmesh.ops.delete(bm, geom=bad, context="VERTS")
bm.to_mesh(proxy.data); bm.free()
for n, h, t, *_ in spec.all_bones():
    if n not in ("spine", "shell_L", "leg_front_L_lower"):
        continue
    a = arm_with([(n, h, t)], "A_" + n)
    print("BONE", n, heat(proxy, a), flush=True)
    a.select_set(False)
print("ORIG spine", heat(orig, arm_with([("spine", (0, 0.05, 0.64), (0, -0.22, 0.64))], "A_o")), flush=True)
