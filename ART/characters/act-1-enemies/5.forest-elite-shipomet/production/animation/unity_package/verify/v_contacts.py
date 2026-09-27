"""Check 4 on the re-imported FBX: LineCast ground contact, Burst star extreme, Shot release, Hit peak, Death rest.

blender -b -P v_contacts.py   -> out/contacts.json
"""
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vcommon as vc  # noqa: E402

arm, mesh = vc.load()
muz = bpy.data.objects.get("Muzzle_RightSpike")
acts = vc.arm_actions(arm)
tipL, mL = vc.spike_tip(mesh, arm, "LeftHand")
tipR, mR = vc.spike_tip(mesh, arm, "RightHand")
rootR = vc.rest_co(mesh)[mR]
R = {"tip_vertices": {"L": tipL, "R": tipR}}


def sample(take):
    vc.use(arm, acts["ForestThorncaster_" + take])
    f0, f1 = vc.EXPORT["takes"]["ForestThorncaster_" + take]["frames"]
    out = []
    for f in range(f0, f1 + 1):
        vc.goto(f)
        co = vc.mesh_co(mesh)
        m = np.array(muz.matrix_world) if muz else None
        out.append((f, co, m))
    return out


# LineCast: lowest point of each arm spike per frame
lc = sample("LineCast")
zL = {f: float(co[mL, 2].min()) for f, co, _ in lc}
zR = {f: float(co[mR, 2].min()) for f, co, _ in lc}
first = min(f for f in zL if zL[f] <= 0.005 and zR[f] <= 0.005)
tips = {f: (co[tipL].copy(), co[tipR].copy()) for f, co, _ in lc}
hold = [f for f in range(26, 61)]
drift = max(float(np.linalg.norm(tips[f][i] - tips[26][i])) for f in hold for i in range(2))
spd = {f: max(float(np.linalg.norm(tips[f][i] - tips[f - 1][i])) * 30 for i in range(2)) for f in range(1, 76)}
R["LineCast"] = {"contract_contact": vc.EXPORT["takes"]["ForestThorncaster_LineCast"]["contact_frame"],
                 "first_frame_both_spikes_at_ground": first,
                 "spike_min_z_L_R_f20_27": {f: [round(zL[f], 3), round(zR[f], 3)] for f in range(20, 28)},
                 "tip_xy_at_contact": [[round(float(x), 3) for x in tips[first][i][:2]] for i in range(2)],
                 "tip_depth_hold_26_60": [round(min(min(zL[f], zR[f]) for f in hold), 3), round(max(max(zL[f], zR[f]) for f in hold), 3)],
                 "tip_drift_hold_26_60_m": round(drift, 4),
                 "tip_speed_mps_f18_26": {f: round(spd[f], 1) for f in range(18, 27)},
                 "tip_speed_peak": [max(spd, key=spd.get), round(max(spd.values()), 1)]}

# Burst: distance between spike tips (star spread) and tip speed
bu = sample("Burst")
spread = {f: float(np.linalg.norm(co[tipL] - co[tipR])) for f, co, _ in bu}
mx = max(spread.values())
R["Burst"] = {"contract_release": vc.EXPORT["takes"]["ForestThorncaster_Burst"]["release_frame"],
              "spread_max_frame": max(spread, key=spread.get), "spread_max_m": round(mx, 3),
              "first_frame_at_99pct_spread": min(f for f in spread if spread[f] >= 0.99 * mx),
              "spread_m": {f: round(spread[f], 2) for f in range(16, 30)}}

# Shot: muzzle empty and right spike tip; release = forward whip peak
sh = sample("Shot")
tipp = {f: co[tipR] for f, co, _ in sh}
mz = {f: m[:3, 3] for f, _, m in sh}
fwd = {f: -float(tipp[f][1]) for f in tipp}        # Blender -Y forward
spd = {f: float(np.linalg.norm(tipp[f] - tipp[f - 1])) * 30 for f in range(1, len(sh))}
rel = vc.EXPORT["takes"]["ForestThorncaster_Shot"]["release_frame"]
d = mz[rel]
R["Shot"] = {"contract_release": rel,
             "tip_speed_peak_frame": max(spd, key=spd.get), "tip_speed_peak_mps": round(max(spd.values()), 1),
             "tip_speed_mps_f16_26": {f: round(spd[f], 1) for f in range(16, 27)},
             "tip_forward_m_f16_26": {f: round(fwd[f], 3) for f in range(16, 27)},
             "tip_most_forward_frame": max(fwd, key=fwd.get),
             "cocked_back_most_frame": min(fwd, key=fwd.get),
             "muzzle_world_blender_at_release": [round(float(x), 3) for x in d],
             "muzzle_world_unity_at_release": [round(-float(d[0]), 3), round(float(d[2]), 3), round(-float(d[1]), 3)],
             "muzzle_to_spike_tip_vertex_m": round(float(np.linalg.norm(d - tipp[rel])), 3),
             "export_muzzle_unity": vc.EXPORT["takes"]["ForestThorncaster_Shot"]["muzzle"]["world_unity_at_release"]}
if muz:
    y = np.array(sh[rel][2])[:3, 1]
    y = y / np.linalg.norm(y)
    R["Shot"]["muzzle_axis_unity_at_release"] = [round(-float(y[0]), 3), round(float(y[2]), 3), round(-float(y[1]), 3)]

# Hit: displacement from frame 0
hi = sample("Hit")
disp = {f: float(np.linalg.norm(co - hi[0][1], axis=1).mean()) for f, co, _ in hi}
R["Hit"] = {"contract_peak": vc.EXPORT["takes"]["ForestThorncaster_Hit"]["events"]["peak"],
            "mean_vertex_displacement_peak_frame": max(disp, key=disp.get),
            "mean_disp_m": {f: round(v, 3) for f, v in disp.items()}}

# Death: final pose on the ground
de = sample("Death")
last = de[-1][1]
body = ~(mL | mR)
R["Death"] = {"final_min_z_all": round(float(last[:, 2].min()), 4), "final_min_z_body": round(float(last[body, 2].min()), 4),
              "final_max_z": round(float(last[:, 2].max()), 3),
              "final_verts_within_2cm_of_ground": int((last[:, 2] < 0.02).sum()),
              "final_verts_below_minus1cm": int((last[:, 2] < -0.01).sum()),
              "hold_44_48_max_move_m": round(float(np.linalg.norm(de[-1][1] - de[-5][1], axis=1).max()), 4),
              "min_z_per_frame": {f: round(float(co[:, 2].min()), 3) for f, co, _ in de[28:]}}
vc.dump("contacts.json", R)
