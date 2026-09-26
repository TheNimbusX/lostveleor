# Forest Wendigo Claw — isolated art revision

Source rig: `production/rig_revision/final_candidate/ForestWendigo_Rig_SkinCandidate.blend`. The production package, gallery, and Unity files were not changed. The editable source is `ForestWendigo_Claw_ArtRevision.blend`; its single assigned action is `AN_ForestWendigo_Claw`.

The character loads weight onto the right support paw, takes a small left lead step, opens the strike claw behind the shoulder at frames 14–15, then accelerates through frames 16–18. Contact is exactly frame 18 at 30 fps. The free claw counters high to keep the mask readable. Chest and head continue through frames 19–22 before the asymmetric recovery ends at frame 30. Sim owns horizontal travel, so the rig root remains at XY `(0, 0)`.

Measured on every evaluated frame in `audit_v1.json`:

- Wrist sweep from f15 to f18: **140.2°** in world XY.
- Lowest strike-claw mesh at f18: **0.070 m** above z=0. It stays about **0.05–0.08 m** through f19–22.
- Right support control XY drift: **0 m**. At f18–21, **59–61** weighted right-paw vertices lie within 5 cm of the ground, up from seven before the support-paw roll correction. The planted foot's lowest vertices remain approximately at z=0 through the attack.
- Maximum IK target mismatch for either hand: **0.0001 m**. Longest right arm reach: **98.3%** of its 0.885 m bone span.
- Frame range: **0–30**. Triangle count: **24,636**. The three-view review reports no offscreen frames and an exact f0/f30 geometry seam.

Review media: `ForestWendigo_Claw_game.mp4`, `ForestWendigo_Claw_front.mp4`, `ForestWendigo_Claw_side.mp4`, and `ForestWendigo_Claw_keyposes.png`. The full PNG sequences and review manifest are in `review_delivery_final/`.

Art assessment: the attack now has a readable high tell and a visible claw path into a grounded, low contact. Its f18 silhouette remains very compressed by design, and the crossing antlers and free claw add some visual density from the front. At small gameplay scale, the quick contact will benefit from the game's existing impact feedback or trail if available. The geometry of the mask remains visible in all three review cameras. The source mesh at neutral/rest has minor toe penetration (about 1 cm), and the recovery transition reaches about 1.7 cm; those values come from the skin candidate's paw geometry, not horizontal root movement.
