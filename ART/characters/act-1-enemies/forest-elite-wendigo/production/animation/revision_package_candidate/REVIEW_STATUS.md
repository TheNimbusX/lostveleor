# Wendigo six-action candidate: visual review rejected

The owner rejected the candidate gallery on 2026-09-25. **Do not assemble, export, promote, or import these actions into Unity.** No six-action candidate `.blend` or `.fbx` was created here.

The isolated `assembly_report.json` records an earlier technical validation run using Leap v6. It checked source rig and mesh identity, 24,636 triangles, action frame ranges, four bone influences per vertex, and zero horizontal root drift. Leap v7b later superseded v6 and passed a separate read-only rig/mesh/action audit. These checks do not establish acceptable motion.

The builders produce motion from a small set of numeric poses or periodic functions, interpolate scalar channels independently, and bake every frame. Claw and Leap enable hand and foot IK, then adjust unreachable wrist targets and ground contacts numerically. This can satisfy contact measurements while making limbs look locked, changing velocity abruptly, and losing the weight transfer and expressive arcs of the reference. The existing rig audit also documents asymmetric shoulder reach and local skin stretch under extreme poses. Packaging would preserve these problems.

The scripts and read-only audit files remain here as experimental material. The Walk/Death `output_polished` revision was not finalized or selected for packaging.
