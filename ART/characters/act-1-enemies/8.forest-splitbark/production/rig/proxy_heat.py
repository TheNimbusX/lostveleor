"""Bone-heat weights through a watertight voxel proxy.

Blender's heat solver fails on the raw Tripo mesh (424 boundary edges, slivers) and on a plain
voxel remesh of it (68 loose leaf crumbs make the system singular). The solve therefore runs on
the largest component of a voxel-remeshed copy and is transferred back (nearest face, interpolated).
Returns a (verts, bones) numpy matrix in `names` order for the original mesh.
"""
import bmesh
import bpy
import numpy as np


def keep_largest_component(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen, comps = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, comp = [v], []
        seen.add(v.index)
        while stack:
            c = stack.pop()
            comp.append(c)
            for e in c.link_edges:
                w = e.other_vert(c)
                if w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        comps.append(comp)
    comps.sort(key=len, reverse=True)
    dropped = [v for c in comps[1:] for v in c]
    bmesh.ops.delete(bm, geom=dropped, context="VERTS")
    bm.to_mesh(me)
    bm.free()
    return len(comps), len(dropped)


def heat_matrix(mesh_obj, arm_obj, names, voxel=0.012):
    scene = bpy.context.scene
    proxy = mesh_obj.copy()
    proxy.data = mesh_obj.data.copy()
    proxy.name = "TMP_HeatProxy"
    scene.collection.objects.link(proxy)
    proxy.modifiers.clear()
    proxy.vertex_groups.clear()
    proxy.parent = None
    rem = proxy.modifiers.new("Voxel", "REMESH")
    rem.mode = "VOXEL"
    rem.voxel_size = voxel
    bpy.ops.object.select_all(action="DESELECT")
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = proxy
    bpy.ops.object.modifier_apply(modifier=rem.name)
    components, dropped = keep_largest_component(proxy.data)

    bpy.ops.object.select_all(action="DESELECT")
    proxy.select_set(True)
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    proxy.parent = None
    proxy.modifiers.clear()
    pgi = {g.index for g in proxy.vertex_groups}
    proxy_unweighted = sum(1 for v in proxy.data.vertices
                           if not any(g.group in pgi and g.weight > 1e-6 for g in v.groups))

    for g in proxy.vertex_groups:
        if g.name not in mesh_obj.vertex_groups:
            mesh_obj.vertex_groups.new(name=g.name)
    dt = mesh_obj.modifiers.new("HeatTransfer", "DATA_TRANSFER")
    dt.object = proxy
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    bpy.ops.object.select_all(action="DESELECT")
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh_obj
    while mesh_obj.modifiers.find(dt.name) > 0:
        bpy.ops.object.modifier_move_up(modifier=dt.name)
    bpy.ops.object.modifier_apply(modifier=dt.name)

    col = {n: i for i, n in enumerate(names)}
    gi = {g.index: g.name for g in mesh_obj.vertex_groups}
    H = np.zeros((len(mesh_obj.data.vertices), len(names)))
    for v in mesh_obj.data.vertices:
        for g in v.groups:
            n = gi.get(g.group)
            if n in col:
                H[v.index, col[n]] = g.weight
    info = {"proxy_faces": len(proxy.data.polygons), "proxy_components": components,
            "proxy_dropped_vertices": dropped, "proxy_unweighted_vertices": proxy_unweighted,
            "voxel_m": voxel}
    bpy.data.meshes.remove(proxy.data)
    return H, info
