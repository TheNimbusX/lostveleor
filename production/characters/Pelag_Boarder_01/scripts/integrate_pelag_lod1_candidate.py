import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def cli_args():
    if "--" not in sys.argv:
        raise RuntimeError("Expected baseline.blend visual.blend output.blend output.fbx after --")
    values = [os.path.abspath(v) for v in sys.argv[sys.argv.index("--") + 1 :]]
    if len(values) != 4:
        raise RuntimeError("Expected four paths after --")
    return values


def ensure_collection(name):
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(collection)
    return collection


def unlink_everywhere(obj):
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)


def move_to_collection(obj, collection):
    unlink_everywhere(obj)
    collection.objects.link(obj)


def duplicate_join(objects, name):
    duplicates = []
    for source in objects:
        dup = source.copy()
        dup.data = source.data.copy()
        bpy.context.scene.collection.objects.link(dup)
        dup.matrix_world = source.matrix_world.copy()
        for modifier in list(dup.modifiers):
            dup.modifiers.remove(modifier)
        duplicates.append(dup)
    bpy.ops.object.select_all(action="DESELECT")
    for dup in duplicates:
        dup.select_set(True)
    bpy.context.view_layer.objects.active = duplicates[0]
    bpy.ops.object.join()
    donor = bpy.context.object
    donor.name = name
    return donor


def append_objects(blend_path):
    with bpy.data.libraries.load(blend_path, link=False) as (source, target):
        target.objects = [name for name in source.objects]
    result = []
    for obj in target.objects:
        if obj is None or obj.type not in {"MESH", "CURVE"}:
            continue
        bpy.context.scene.collection.objects.link(obj)
        result.append(obj)
    return result


def convert_to_mesh(obj):
    if obj.type == "MESH":
        return obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    return bpy.context.object


def join_objects(objects, name):
    if not objects:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    return result


def ensure_uv(obj):
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name="UV0")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    try:
        bpy.ops.uv.smart_project(angle_limit=1.15192, island_margin=0.02)
    except TypeError:
        bpy.ops.uv.smart_project()
    bpy.ops.object.mode_set(mode="OBJECT")


def transfer_weights(target, donor, rig):
    for group in list(target.vertex_groups):
        target.vertex_groups.remove(group)
    for group in donor.vertex_groups:
        target.vertex_groups.new(name=group.name)
    modifier = target.modifiers.new("TransferLegacyWeights", "DATA_TRANSFER")
    modifier.object = donor
    modifier.use_vert_data = True
    modifier.data_types_verts = {"VGROUP_WEIGHTS"}
    modifier.vert_mapping = "POLYINTERP_NEAREST"
    modifier.mix_mode = "REPLACE"
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    armature = target.modifiers.new("Armature", "ARMATURE")
    armature.object = rig
    target.parent = rig


def lerped_pair(value, anchors):
    if value <= anchors[0][0]:
        return [(anchors[0][1], 1.0)]
    if value >= anchors[-1][0]:
        return [(anchors[-1][1], 1.0)]
    for (left_value, left_name), (right_value, right_name) in zip(anchors, anchors[1:]):
        if left_value <= value <= right_value:
            t = (value - left_value) / max(0.000001, right_value - left_value)
            return [(left_name, 1.0 - t), (right_name, t)]
    return [(anchors[-1][1], 1.0)]


def assign_analytic_weights(target, rig):
    for group in list(target.vertex_groups):
        target.vertex_groups.remove(group)
    groups = {
        bone.name: target.vertex_groups.new(name=bone.name)
        for bone in rig.data.bones
        if bone.use_deform
    }

    torso_anchors = [
        (0.98, "Hips"), (1.12, "Spine"), (1.30, "Chest"),
        (1.43, "UpperChest"), (1.53, "Neck"), (1.66, "Head"),
    ]
    leg_anchors_left = [
        (0.05, "LeftFoot"), (0.30, "LeftLowerLeg"),
        (0.66, "LeftUpperLeg"), (0.98, "Hips"),
    ]
    leg_anchors_right = [
        (0.05, "RightFoot"), (0.30, "RightLowerLeg"),
        (0.66, "RightUpperLeg"), (0.98, "Hips"),
    ]
    arm_anchors_left = [
        (0.17, "LeftShoulder"), (0.27, "LeftUpperArm"),
        (0.39, "LeftLowerArm"), (0.51, "LeftHand"),
    ]
    arm_anchors_right = [
        (0.17, "RightShoulder"), (0.27, "RightUpperArm"),
        (0.39, "RightLowerArm"), (0.51, "RightHand"),
    ]

    for vertex in target.data.vertices:
        world = target.matrix_world @ vertex.co
        ax = abs(world.x)
        if world.z > 1.52:
            weights = lerped_pair(world.z, torso_anchors)
        elif world.z > 0.80 and ax > 0.235:
            weights = lerped_pair(ax, arm_anchors_left if world.x > 0.0 else arm_anchors_right)
        elif world.z < 1.00:
            if ax < 0.035 and world.z > 0.48:
                weights = [("Hips", 1.0)]
            else:
                weights = lerped_pair(world.z, leg_anchors_left if world.x >= 0.0 else leg_anchors_right)
        else:
            weights = lerped_pair(world.z, torso_anchors)
        total = sum(weight for _, weight in weights)
        for name, weight in weights:
            groups[name].add([vertex.index], weight / total, "REPLACE")

    armature = target.modifiers.new("Armature", "ARMATURE")
    armature.object = rig
    target.parent = rig


def assign_single_bone(obj, rig, bone_name):
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig
    obj.parent = rig


def transfer_hand_shapes(target, legacy_body):
    # Preserve the existing hand-pose contract by projecting only hand vertices
    # to the legacy Open/Grip/Fist evaluated surfaces. The rest of the new mesh
    # stays fixed, so these keys cannot disturb the approved costume silhouette.
    if legacy_body.data.shape_keys is None:
        return
    basis = target.shape_key_add(name="Basis")
    target.shape_key_add(name="Open")
    target.shape_key_add(name="Grip")
    target.shape_key_add(name="Fist")

    depsgraph = bpy.context.evaluated_depsgraph_get()
    old_keys = legacy_body.data.shape_keys.key_blocks
    for key in old_keys:
        key.value = 0.0

    inverse_old = legacy_body.matrix_world.inverted()
    inverse_target = target.matrix_world.inverted()
    for shape_name in ("Open", "Grip", "Fist"):
        if shape_name not in old_keys:
            continue
        old_keys[shape_name].value = 1.0
        bpy.context.view_layer.update()
        evaluated = legacy_body.evaluated_get(depsgraph)
        evaluated_mesh = evaluated.to_mesh()
        bvh = BVHTree.FromPolygons(
            [vertex.co.copy() for vertex in evaluated_mesh.vertices],
            [polygon.vertices[:] for polygon in evaluated_mesh.polygons],
            all_triangles=False,
        )
        key = target.data.shape_keys.key_blocks[shape_name]
        for index, vertex in enumerate(target.data.vertices):
            world = target.matrix_world @ vertex.co
            if abs(world.x) < 0.60 or not (0.76 < world.z < 1.20):
                continue
            nearest = bvh.find_nearest(inverse_old @ world)
            if nearest is None:
                continue
            old_local = nearest[0]
            key.data[index].co = inverse_target @ (legacy_body.matrix_world @ old_local)
        evaluated.to_mesh_clear()
        old_keys[shape_name].value = 0.0
    basis.value = 0.0

    # Some legacy packs carried named but zero-displacement keys. Guarantee a
    # usable gameplay-scale grip/fist on the new organic hands in that case.
    basis_key = target.data.shape_keys.key_blocks["Basis"]
    for shape_name, strength in (("Grip", 0.46), ("Fist", 0.78)):
        key = target.data.shape_keys.key_blocks[shape_name]
        existing = max(
            (key.data[i].co - basis_key.data[i].co).length
            for i in range(len(target.data.vertices))
        )
        if existing > 0.002:
            continue
        for index, vertex in enumerate(target.data.vertices):
            world = target.matrix_world @ vertex.co
            if abs(world.x) < 0.42 or not (0.78 < world.z < 1.18):
                continue
            side = 1.0 if world.x > 0.0 else -1.0
            palm = Vector((0.49 * side, -0.005, 1.025))
            finger_weight = min(1.0, max(0.0, (abs(world.x) - 0.43) / 0.11))
            vertical_weight = min(1.0, max(0.0, 1.0 - abs(world.z - 1.02) / 0.22))
            weight = strength * max(0.18, finger_weight) * vertical_weight
            shaped_world = world.lerp(palm, weight)
            key.data[index].co = inverse_target @ shaped_world


def fit_armature_to_candidate(rig):
    # The approved compact silhouette has a 1.084 m hand-to-hand span, while the
    # technical donor used 1.72 m. Keep the Humanoid hierarchy, but fit the arm
    # segment lengths and hand sockets to the actual production geometry.
    coordinates = {
        "LeftShoulder": ((0.02, 0.0, 1.46), (0.16, 0.0, 1.46)),
        "LeftUpperArm": ((0.16, 0.0, 1.46), (0.34, 0.0, 1.28)),
        "LeftLowerArm": ((0.34, 0.0, 1.28), (0.47, 0.0, 1.10)),
        "LeftHand": ((0.47, 0.0, 1.10), (0.54, 0.0, 1.01)),
        "Hook_L": ((0.48, -0.015, 1.09), (0.54, -0.02, 1.02)),
        "RightShoulder": ((-0.02, 0.0, 1.46), (-0.16, 0.0, 1.46)),
        "RightUpperArm": ((-0.16, 0.0, 1.46), (-0.34, 0.0, 1.28)),
        "RightLowerArm": ((-0.34, 0.0, 1.28), (-0.47, 0.0, 1.10)),
        "RightHand": ((-0.47, 0.0, 1.10), (-0.54, 0.0, 1.01)),
        "Weapon_R": ((-0.48, -0.015, 1.09), (-0.54, -0.02, 1.02)),
        "IK_Hand_L": ((0.47, -0.02, 1.10), (0.54, -0.02, 1.01)),
        "Pole_Elbow_L": ((0.34, -0.40, 1.28), (0.34, -0.40, 1.40)),
        "IK_Hand_R": ((-0.47, -0.02, 1.10), (-0.54, -0.02, 1.01)),
        "Pole_Elbow_R": ((-0.34, -0.40, 1.28), (-0.34, -0.40, 1.40)),
    }
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail) in coordinates.items():
        bone = rig.data.edit_bones.get(name)
        if bone is None:
            raise RuntimeError(f"Missing required armature bone: {name}")
        bone.head = head
        bone.tail = tail
    bpy.ops.object.mode_set(mode="OBJECT")


def clean_preview_objects():
    for obj in list(bpy.data.objects):
        if obj.name.startswith("PREVIEW_"):
            bpy.data.objects.remove(obj, do_unlink=True)


def main():
    baseline_path, visual_path, output_blend, output_fbx = cli_args()
    os.makedirs(os.path.dirname(output_blend), exist_ok=True)
    os.makedirs(os.path.dirname(output_fbx), exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=baseline_path)

    rig = bpy.data.objects.get("Pelag_Boarder_01_Rig")
    legacy_body = bpy.data.objects.get("SKM_Pelag_Body")
    legacy_clothing = bpy.data.objects.get("SKM_Pelag_Clothing")
    if rig is None or legacy_body is None or legacy_clothing is None:
        raise RuntimeError("Technical donor is missing required rig/body/clothing")

    donor = duplicate_join([legacy_body, legacy_clothing], "TMP_LegacyWeightDonor")
    donor.hide_render = True

    imported = [convert_to_mesh(obj) for obj in append_objects(visual_path)]
    by_name = {obj.name: obj for obj in imported}
    production_collection = ensure_collection("PRODUCTION_MODEL")

    body_names = {"Pelag_Boarder_01_BodyCostume_LOD1", "Face_CleanVolume"}
    panel_names = {"CoralPanel_L", "CoralPanel_R", "CreamPanel_C"}
    rope_names = {"RopeLoop", "RopeKnot"}
    body_parts = [obj for obj in imported if obj.name in body_names]
    panels = [obj for obj in imported if obj.name in panel_names]
    ropes = [obj for obj in imported if obj.name in rope_names]
    accent_parts = [obj for obj in imported if obj not in body_parts + panels + ropes]

    body = join_objects(body_parts, "SKM_Pelag_ProductionBody")
    accents = join_objects(accent_parts, "SKM_Pelag_ProductionAccents")
    panels_mesh = join_objects(panels, "SKM_Pelag_ProductionPanels")
    rope = join_objects(ropes, "PRP_Pelag_RopeLoop")

    for obj in (body, accents, panels_mesh, rope):
        if obj:
            move_to_collection(obj, production_collection)
            ensure_uv(obj)

    assign_analytic_weights(body, rig)
    assign_analytic_weights(accents, rig)
    assign_analytic_weights(panels_mesh, rig)
    assign_single_bone(rope, rig, "Hips")
    transfer_hand_shapes(body, legacy_body)
    fit_armature_to_candidate(rig)

    # Retain the validated equipment, chain and sockets; discard the rejected
    # visible legacy shell after it has served as a weight/shape donor.
    for obj in (legacy_body, legacy_clothing, donor):
        bpy.data.objects.remove(obj, do_unlink=True)
    clean_preview_objects()

    for obj in (body, accents, panels_mesh, rope, rig):
        obj.scale = (1.0, 1.0, 1.0)
    bpy.context.scene.render.fps = 30
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene["animations_authorized"] = False
    bpy.context.scene["visual_candidate"] = "Pelag LOD1 v4"

    bpy.ops.wm.save_as_mainfile(filepath=output_blend)

    export_objects = [rig, body, accents, panels_mesh, rope]
    export_objects.extend(
        obj for obj in bpy.data.objects
        if obj.name in {"SKM_Pelag_Chain", "PRP_Hook_0p22m", "PRP_Saber_0p92m", "PRP_SaberScabbard"}
    )
    bpy.ops.object.select_all(action="DESELECT")
    for obj in export_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=output_fbx,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False,
        bake_anim=False,
        use_armature_deform_only=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
    )
    print(f"OUTPUT_BLEND={output_blend}")
    print(f"OUTPUT_FBX={output_fbx}")
    print(f"EXPORT_OBJECTS={','.join(obj.name for obj in export_objects)}")


if __name__ == "__main__":
    main()
