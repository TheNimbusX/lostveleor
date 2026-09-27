"""Shared helpers for the independent FBX check (runs inside Blender on a fresh, empty scene)."""
import math
import bpy
import numpy as np

V_WALK = 2.4 / 30.0     # m per frame the ground moves +Y under a walking mob (faces -Y)
REGION_BONES = {
    "slab_L": ("L_arm_lower",), "slab_R": ("R_arm_lower",),
    "hind_L": ("L_foot",), "hind_R": ("R_foot",),
    "body": ("pelvis", "spine_01", "spine_02"),
    "head": ("neck", "head", "jaw"),
}


def import_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, anim_offset=0.0)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    acts = {}
    for a in bpy.data.actions:
        acts[a.name.split("|")[-1]] = a
    return arm, mesh, acts


def weights(mesh):
    """-> (dominant bone name per vertex, dominant weight, influence count)"""
    names = {g.index: g.name for g in mesh.vertex_groups}
    n = len(mesh.data.vertices)
    dom, dw, cnt = [""] * n, np.zeros(n), np.zeros(n, int)
    for v in mesh.data.vertices:
        best, bw, c = "", -1.0, 0
        for g in v.groups:
            if g.weight > 0:
                c += 1
            if g.weight > bw:
                best, bw = names[g.group], g.weight
        dom[v.index], dw[v.index], cnt[v.index] = best, bw, c
    return np.array(dom), dw, cnt


def regions(dom, dw):
    out = {}
    for r, bones in REGION_BONES.items():
        m = np.isin(dom, bones)
        if r.startswith("slab"):
            m &= dw >= 0.99          # rigid slab shell only
        out[r] = np.nonzero(m)[0]
    out["all"] = np.arange(len(dom))
    return out


def set_action(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    if getattr(ad, "action_slot", None) is None and len(getattr(act, "slots", [])):
        ad.action_slot = act.slots[0]


def sample(arm, mesh, frames):
    """-> verts [F,N,3] world, bones {name: [F,4,4] world matrices}"""
    sc = bpy.context.scene
    n = len(mesh.data.vertices)
    V = np.zeros((len(frames), n, 3), np.float64)
    B = {pb.name: np.zeros((len(frames), 4, 4)) for pb in arm.pose.bones}
    buf = np.empty(n * 3, np.float32)
    for i, f in enumerate(frames):
        sc.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(dg)
        me = ev.to_mesh()
        me.vertices.foreach_get("co", buf)
        mw = np.array(mesh.matrix_world)
        co = buf.reshape(-1, 3).astype(np.float64)
        V[i] = co @ mw[:3, :3].T + mw[:3, 3]
        ev.to_mesh_clear()
        aw = np.array(arm.matrix_world)
        for pb in arm.pose.bones:
            B[pb.name][i] = aw @ np.array(pb.matrix)
    return V, B


def rot_angle_deg(Ma, Mb):
    """angle between the rotation parts of two 4x4 (scale removed)"""
    def R(M):
        r = M[:3, :3]
        return r / np.linalg.norm(r, axis=0)
    d = R(Ma).T @ R(Mb)
    c = max(-1.0, min(1.0, (np.trace(d) - 1) / 2))
    return math.degrees(math.acos(c))


def pose_diff(Va, Ba, Vb, Bb):
    pos = max(float(np.linalg.norm(Ba[k][:3, 3] - Bb[k][:3, 3])) for k in Ba)
    ang = max(rot_angle_deg(Ba[k], Bb[k]) for k in Ba)
    vert = float(np.linalg.norm(Va - Vb, axis=1).max())
    return {"bone_pos_mm": round(pos * 1000, 3), "bone_rot_deg": round(ang, 4), "vertex_mm": round(vert * 1000, 3)}


def edge_stretch(mesh, V, rest_co):
    e = np.array([ed.vertices[:] for ed in mesh.data.edges])
    L0 = np.linalg.norm(rest_co[e[:, 0]] - rest_co[e[:, 1]], axis=1)
    ok = L0 > 1e-5
    worst_hi, worst_lo = 1.0, 1.0
    arg_hi = arg_lo = (0, 0)
    for i in range(len(V)):
        L = np.linalg.norm(V[i][e[:, 0]] - V[i][e[:, 1]], axis=1)
        r = L[ok] / L0[ok]
        j, k = int(r.argmax()), int(r.argmin())
        if r[j] > worst_hi:
            worst_hi, arg_hi = float(r[j]), (i, int(np.nonzero(ok)[0][j]))
        if r[k] < worst_lo:
            worst_lo, arg_lo = float(r[k]), (i, int(np.nonzero(ok)[0][k]))
    return worst_hi, arg_hi, worst_lo, arg_lo, e
