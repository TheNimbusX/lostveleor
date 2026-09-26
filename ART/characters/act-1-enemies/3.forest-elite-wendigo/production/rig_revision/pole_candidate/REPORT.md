# Forest Wendigo — right IK pole audit

Source: `../final_candidate/ForestWendigo_Rig_SkinCandidate.blend`. The original
Claw/Leap actions were loaded from `animation/revision_primary`, and Walk/Death
from `animation/secondary/output_v9`. No production rig, action or Unity asset
was edited. This is an isolated test of the existing reviewed clips, not an
accepted animation revision.

## Result

**Do not promote `ForestWendigo_Rig_PoleCandidate.blend`.** Both right pole
controls start on the left side of the creature; moving them to the right is
geometrically intuitive, but it alters the IK solution and inherited palm/foot
rotation for all previously authored actions. The neutral joint drift is only
1.2 mm at the elbow and 2.9 mm at the knee, yet the paw no longer supports the
Leap crouch or landing. The comparison is in `source_vs_pole_candidate.png`.

| Measure | Original | Pole candidate |
|---|---:|---:|
| Triangle count | 24,636 | 24,636 |
| Deform-bone rest change | — | 0 m |
| Mesh bind-position change | — | 0 m |
| Leap f23: right-hand lowest claw above ground | 0.014 m | 0.219 m |
| Leap f33: right-hand lowest claw above ground | 0.012 m | 0.229 m |
| Leap f23: right-knee X | +0.035 m | −0.029 m |
| Walk f3: right-knee X | +0.093 m | −0.005 m |
| Death f45: right-knee X | +0.042 m | −0.010 m |

The global pole move also leaves unreachable hand-target errors unchanged (for
example 0.269 m on Leap f13 and 0.176 m on Leap f24), because those are caused
by shoulder position and arm length rather than IK-plane orientation.

## Safer action-space correction

Keep the original pole-rest geometry and key *pose* offsets only where needed.
The read-only offset sweep `action_pole_sweep.json` measured the following on
the original rig:

- Leap f23/f33: key `CTRL_R_knee.location.x` about +0.40 m in crouch and
  landing, fading to zero during flight and recovery. Right-knee X changes
  +0.035/+0.052 → +0.142/+0.140 m; the hand stays at 0.014/0.012 m above the
  floor and the right foot remains within about 0.03 m of its original floor
  contact. This keeps the four-point landing without letting the knee fold
  under the midline.
- Walk f3: the same control at +0.40 m moves right-knee X +0.093 → +0.200 m.
  Reduce to zero by f6, where the right foot is planted; retaining +0.40 m
  there raises the toe by about 0.024 m.
- Death f34/f45: `CTRL_R_knee.location.x` +0.40 m moves knee X roughly
  +0.04 → +0.14 m and moves the right foot vertically less than 0.012 m.
- Claw f18: `CTRL_R_elbow.location.x` +0.40 m moves elbow X −0.025 → +0.147 m
  while retaining the wrist target. It raises the claw tip by about 0.09 m;
  prefer +0.25–0.30 m and preview the entire swept arc before final keys.

Those offsets are only numerical options, **not finished action keys**. Each
must be eased into/out of its phase and checked for wrist orientation, skin
collapse, ground penetration and smoothness frame by frame. Palm orientation
needs its own hand control counter-rotation if an IK plane is shifted broadly.

The right arm's reach is a separate limitation. Forcing the right claw to an
extreme target across the head can miss by 0.3–0.7 m; rotating the clavicle
forward/outward can recover reach, but on the current skin it stretches the
shoulder. Prefer a reachable hand arc plus torso turn. The companion skin
stress audit is `../diagnosis.json` and the unpromoted weight experiment is
`../ForestWendigo_Rig_WeightRevision_v2.blend`.

## Files

- `build_pole_candidate.py`, `build_report.json`: isolated global pole attempt.
- `compare_poles.py`, `compare_source/measurements.json`,
  `compare_candidate/measurements.json`: same-action measurements and renders.
- `sweep_action_poles.py`, `action_pole_sweep.json`: read-only action offset
  sweep on the approved source rig.
- `source_vs_pole_candidate.png`: side-by-side source/candidate pose sheet.
