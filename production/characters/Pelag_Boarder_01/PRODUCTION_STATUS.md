# Pelag_Boarder_01 — production status

Updated: 2026-08-29

## Direction

- Approved 2D turnaround is the binding design source.
- `references/Pelag_Boarder_01_3D_Lookdev_Target_v1.png` is the owner-approved 3D look-development target. The v5 turnaround remains the binding construction source.
- Bright cel/toon presentation is mandatory: large soft forms, controlled black outline, two-step shadows, coral primary, turquoise secondary, no blue/yellow or blue/gold pairing.
- Hell Clock is not a visual, gameplay, run-structure, UI, or production reference.

## Reuse policy

The v5 `.blend` and `.fbx` are technical donors only. The following may be retained after validation:

- meter scale, ground root and Humanoid bone contract;
- sockets and prop switching contract;
- control-rig naming and animation-retargeting contract;
- chain bone count and gameplay dimensions;
- Unity import/validation scripts.

The visible body, face, hair, costume, hands, footwear, equipment surfaces, UVs and materials must be rebuilt to the approved art.

## Gate status

| Gate | Status | Evidence / blocker |
|---|---|---|
| Approved 2D source | PASS | v5 turnaround and A-pose |
| 3D lookdev target | PASS | owner fixed the bright 3D direction on 2026-08-29 |
| Technical baseline audit | PASS | `qa/baseline_audit.json` |
| Generated sculpt donor | PASS WITH LIMITS | `source/triposr_pass_v1/review/Pelag_Boarder_01_GeneratedSculpt_v1.blend`; useful organic silhouette donor, not an importable game mesh |
| Production silhouette/anatomy | IN PROGRESS | retopology and separated costume construction start from the generated donor; primitive scripted geometry is rejected |
| Production face/hair | IN PROGRESS | generated donor needs a clean authored face and simplified chunky hair masses |
| Costume construction | FAIL | layer plan is correct, surface quality and deformation topology are not final |
| Production UV/materials | BLOCKED | starts after visible geometry passes |
| Rig/skinning transfer | BLOCKED | starts after visible geometry passes |
| Unity production import | BLOCKED | no accepted production FBX yet |

## Files that must not be imported

`source/geometry_pass_v1/`, `source/geometry_pass_v2/` and `source/geometry_pass_v3/` are rejected QA experiments. They are retained only as evidence of the failed primitive-building approach. They are not production assets and must not replace the current Unity fallback.

`source/triposr_pass_v1/0/mesh.obj` is raw generated reconstruction data. It is licensed for use through the MIT-licensed TripoSR pipeline, but it remains a sculpt donor only: fused clothing/body, dense topology and rough face/back reconstruction make direct Unity import forbidden.

## Next accepted deliverable

A manually authored/sculpted Pelag LOD0 with the approved silhouette and costume, shown in:

1. front/back/left/right/top/bottom solid views;
2. neutral three-quarter toon lookdev;
3. dark and light gameplay-floor tests at 80 px and 120 px;
4. comparison beside Orvill shield infantry;
5. squat, arms-up, lunge and wide-step deformation poses.

Only after this visible gate passes should the asset receive final UVs, atlas materials, transferred Humanoid rig/weights and Unity export.
