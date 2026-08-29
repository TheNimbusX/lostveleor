# Pelag_Boarder_01 production character task

Create the first production-quality playable character for Lost Veleor: `Pelag_Boarder_01`, a Pelag boarder with saber and chain hook.

## Source of truth

- Primary visual reference: `designed-images/deliverables/razlom-character-production-pack-v5/Pelag_Boarder_01/01_turnaround/turnaround_master.png`.
- A-pose: `designed-images/deliverables/razlom-character-production-pack-v5/Pelag_Boarder_01/02_a_pose/a_pose_front_back.png`.
- Equipment sheet: `designed-images/deliverables/razlom-character-production-pack-v5/Pelag_Boarder_01/04_equipment/saber_and_hook.png`.
- Dimensions, rig landmarks, construction, materials, motion notes, pose index, and scale-test documents beside those images are binding.
- Technical source to audit and reuse where sound: `designed-images/deliverables/razlom-character-production-pack-v5/Pelag_Boarder_01/10_3d_source/Pelag_Boarder_01_LOD1_Apose.blend` and its deterministic builder.

## Required outcome

Produce a materially improved stylized 3D hero asset that matches the approved 2D concept at the target isometric game scale. Replace the current primitive/blocky look with intentional anatomy, face, hair masses, layered costume, sandals, bracers, sash, saber, scabbard, folded chain, and hook. Preserve the fixed palette: coral is primary, turquoise is secondary, no gold and no blue/yellow pairing.

The character must keep Unity-compatible scale and orientation: 1 unit = 1 meter, soles at ground, root between feet, Unity forward +Z after export, in-place animation, root motion off. Preserve or rebuild a clean Humanoid-compatible rig, sockets, separate rigid props, chain controls, and the existing animation-retargeting contract.

Hell Clock is excluded from the reference stack completely. Do not borrow its palette, grimdark surface treatment, proportions, combat presentation, UI language, run structure, or material treatment. The approved bright Lost Veleor concept is the only character-art source of truth.

## Workflow constraints

- Do not overwrite anything inside the v5 deliverable pack.
- Do not delete or replace the existing Unity fallback yet.
- Work in `production/characters/Pelag_Boarder_01/` and keep the editable source, deterministic scripts, QA renders, reports, and exports separated.
- Use the installed Blender 5.2 executable at `C:/Program Files/Blender Foundation/Blender 5.2/blender.exe` for scripted inspection/build/render steps.
- Inspect in Solid shading first. Validate silhouette, front/back/left/right/top/bottom views, important attachment contacts, and target-size 80/120 px reads before judging materials.
- Build one verified stage at a time. Do not add detail until proportions and silhouette pass.
- Prefer deterministic, rerunnable scripts as durable memory. Existing character geometry may be reused only when it supports the concept accurately.
- No fake `.blend` or placeholder primitives presented as final. Report incomplete areas honestly.
- The deterministic baseline builder may be used for numerical tests and rig-contract transfer, but its primitive geometry must not be promoted into production art. Visible production geometry must be manually authored/sculpted and retopologized against the approved turnaround and the accepted lookdev target.

## Production stages

1. Audit the existing `.blend` numerically and visually against the reference; document anatomy, ratios, named contact interfaces, and gaps.
2. Correct primary silhouette and anatomy/proportions.
3. Rebuild face/head/hair/headband as clear stylized masses.
4. Rebuild costume layering and thickness, including safe deformation clearances.
5. Rebuild and verify saber, scabbard, folded chain and hook contacts/sockets.
6. Apply controlled toon-ready material IDs/colors without baked lighting.
7. Preserve/fix rig, skinning, sockets and prop separation; verify deformation poses.
8. Create QA renders and a machine-readable validation report.
9. Export a neutral animation-free FBX for Unity and document known issues.

The final review must explicitly distinguish `PASS`, `FAIL`, `BLOCKED`, and `TEMPORARY`. Do not call the asset production-ready unless the visible model is clearly beyond the supplied technical baseline.
