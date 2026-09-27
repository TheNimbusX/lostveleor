"""Armature creation + heat-weight passes (bpy)."""
import bpy
from rig_layout import BONES


def create_armature(scene, name="ARM_ForestRootSnarer"):
    arm_data = bpy.data.armatures.new(name)
    arm_obj = bpy.data.objects.new(name, arm_data)
    scene.collection.objects.link(arm_obj)
    arm_obj.show_in_front = True
    arm_data.display_type = "STICK"
    activate(arm_obj)
    bpy.ops.object.mode_set(mode="EDIT")
    for bname, head, tail, parent, connect in BONES:
        b = arm_data.edit_bones.new(bname)
        b.head, b.tail = head, tail
        b.roll = 0.0
        b.use_deform = bname != "root"
        if parent:
            b.parent = arm_data.edit_bones[parent]
            b.use_connect = connect
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


def activate(obj, others=()):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for o in others:
        o.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def heat_pass(scene, mesh_obj, arm_obj, deform_names):
    """Heat-weight a throw-away copy of the mesh against only `deform_names`.
    Returns list (per vertex) of {bone: weight}."""
    saved = {b.name: b.use_deform for b in arm_obj.data.bones}
    for b in arm_obj.data.bones:
        b.use_deform = b.name in deform_names
    tmp = bpy.data.objects.new("TMP_HEAT", mesh_obj.data.copy())
    scene.collection.objects.link(tmp)
    for g in list(tmp.vertex_groups):
        tmp.vertex_groups.remove(g)
    activate(arm_obj, others=(tmp,))
    res = bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    names = {g.index: g.name for g in tmp.vertex_groups}
    out = []
    for v in tmp.data.vertices:
        out.append({names[g.group]: g.weight for g in v.groups if g.weight > 1e-5 and names[g.group] in deform_names})
    me = tmp.data
    bpy.data.objects.remove(tmp)
    bpy.data.meshes.remove(me)
    for b in arm_obj.data.bones:
        b.use_deform = saved[b.name]
    return out, list(res)


def write_weights(mesh_obj, weights, bone_names):
    for g in list(mesh_obj.vertex_groups):
        mesh_obj.vertex_groups.remove(g)
    groups = {n: mesh_obj.vertex_groups.new(name=n) for n in bone_names}
    for i, wd in enumerate(weights):
        for n, w in wd.items():
            if w > 1e-5:
                groups[n].add([i], w, "REPLACE")


def bind(mesh_obj, arm_obj):
    mesh_obj.parent = arm_obj
    mesh_obj.matrix_parent_inverse.identity()
    mod = next((m for m in mesh_obj.modifiers if m.type == "ARMATURE"), None)
    if mod is None:
        mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj
    mod.use_vertex_groups = True
    mod.use_bone_envelopes = False
    return mod
