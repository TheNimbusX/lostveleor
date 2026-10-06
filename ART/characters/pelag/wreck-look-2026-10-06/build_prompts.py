import json

BASE = (
    "Gameplay concept frame for the bright stylized top-down action roguelite The War Remains, keep exactly the same high top-down camera angle, "
    "forest arena, brown dirt clearing with flagstones, grass edges, orange flowers, warm bright daylight and painted stylized 3D look as the forest arena screenshot reference. "
    "Hero Pelag exactly as in the character turnaround reference: young sailor-warrior with brown hair, red headband, white wrap shirt with teal trim, red sash with a sabre tucked in it, sandals. "
    "His weapon is the big dark iron anchor on a heavy chain exactly as in the anchor sheet reference, he holds the chain grip with both hands. "
    "Key moment of the anchor skill Wreck, third press: Pelag stands in the left third of the frame and has just swung the anchor overhead on its chain and slammed it into the ground about two meters in front of him, "
    "and from the impact point a damaging line runs six meters forward along a straight lane toward the upper right, hitting three moss-covered forest guardian golems from the arena screenshot that stagger and get knocked up. "
    "Paint the effect in the same hand-painted watercolor game VFX manner with thin dark ink outlines and crisp shapes as the approved splash effect reference, but use that reference only for brush manner and outline, not for its colors or foam shapes. "
    "The effect must have real volume, standing and rising shapes, not flat stickers on the ground, and it must clearly show the exact damage area of the lane. "
    "Strict effect palette rule: no red, no orange, no gold, no yellow fire anywhere in the effect, any sparks are cold blue-white. "
    "The game stays bright and readable, do not darken the scene, do not whiten or add rim light to the characters. No text, no UI. "
)

DIRS = {
    "A-earth": (
        "Direction EARTH AND WEIGHT: the effect is the ground itself breaking, not water. "
        "A deep crater with a cracked raised rim where the anchor bit into the earth, the anchor half buried in it, "
        "and along the whole lane a ridge of torn turf and brown soil heaving up as if something huge plows fast under the ground, "
        "broken roots whipping up, chunks of rock and clods of earth flying and rolling forward, heavy grey-brown dust clouds billowing in volume around the guardians, "
        "a few tiny cold blue-white sparks only at the iron contact, water only as a handful of small secondary droplets. Absolutely no turquoise foam wave."
    ),
    "B-deep": (
        "Direction THE DEEP: dark deep-sea water instead of bright foam. "
        "At the impact a heavy splash column of ink-navy and teal-black water bursts up around the anchor, "
        "and along the lane a dark heavy wave rolls forward, its body deep navy and black-teal with indigo shadows inside, only its edge glows with a luminous turquoise rim and a thin white crest on top, "
        "dark heavy droplets with bright cyan highlights, the water feels as heavy as the pressure of the ocean depth, "
        "clearly different from light turquoise foam: the mass of the effect is dark and only the edges shine."
    ),
    "C-iron": (
        "Direction IRON AND CHAIN: cold metal, no water at all. "
        "A steel-white streak trail follows the arc of the anchor from high above the hero down to the ground, "
        "at the impact a burst of blue-white sparks and a sharp metallic shock ring flat on the ground with a bright white center, "
        "along the lane a fast iron-grey and pale steel-blue shockwave rips forward with ghostly translucent chain-link imprints stamped into the ground in a straight row, "
        "cracked earth under it, small steel shards, cold blue-white glow only on the edges of the shapes."
    ),
}

VARIANT = {
    1: "Composition: the lane runs diagonally toward the upper right, the moment right after impact, the line is two thirds of the way along the lane.",
    2: "Composition: the lane runs almost horizontally to the right, a split second later, the line has reached the far end of the lane and the last guardian is lifted off the ground.",
}

ICON_BASE = (
    "Game ability icon sheet: three separate square skill icons in one row on one wide image, generous gap between them, "
    "each on its own dark navy square background exactly like the reference skill icons, same hand-painted game icon style with thick readable shapes, thin dark outlines and crisp painterly highlights as the Whirlwind, Squall and anchor geyser reference icons. "
    "The anchor and chain must match the anchor sheet reference: dark iron anchor with a pointed crown and two hooked flukes, a ring, heavy chain links, a small red cloth tie on the chain grip. "
    "Neutral palette for now: dark iron, warm grey stone, pale bone-white motion strokes, muted slate blue, no turquoise foam, no red, orange or gold effects. "
    "Each icon must read at 64 pixels by its silhouette alone and the three silhouettes and compositions must be clearly different from each other and from the references, which all share one diagonal anchor pose that we must avoid repeating. "
)

ICONS = {
    1: (
        "Icon 1 on the left, WRECK: the anchor at the end of a big swinging arc, a broad pale motion swoosh curving over the top from the upper left down to the lower right, "
        "the heavy anchor head smashing crown-first into cracked ground at the bottom, ground split into radiating cracks, stone chunks bursting up, the shape is a heavy downward hook curve ending in an impact. "
        "Icon 2 in the middle, ANCHOR THROW: the anchor flying far away from the viewer into depth along a perfectly straight taut chain, the chain starts large at the bottom left corner and narrows in strong perspective to a small anchor near the upper right, "
        "parallel speed lines along the chain, a strong sense of distance, the shape is one long straight receding diagonal. "
        "Icon 3 on the right, ABORDAGE: a close-up of the anchor flukes hooked and biting deep into a broken stone edge at the top right, the chain pulled rigid and straight to the bottom left, links stretched, "
        "short motion chevrons along the chain pointing toward the anchor, a sense of a violent yank pulling toward the bite, the shape is a taut line ending in a big hooked bite. No text, no letters, no ornamental frames."
    ),
    2: (
        "Icon 1 on the left, WRECK: seen from the side, the anchor falling vertically like a hammer at the bottom of a tall circular chain arc that fills the icon, a crater and an upward burst of rock shards and dust under it, "
        "the composition is a big circle arc with a heavy weight at the bottom center. "
        "Icon 2 in the middle, ANCHOR THROW: the anchor shown small and sharp flying horizontally to the right with its crown leading like a harpoon, trailing a long straight chain line across the whole icon from the left edge, "
        "the chain pulled perfectly straight, horizontal speed streaks, empty space ahead of it, the composition is a long horizontal arrow. "
        "Icon 3 on the right, ABORDAGE: a small dark silhouette of the sailor hero at the bottom left being yanked off his feet toward the top right along a taut chain, "
        "the anchor hooked into the ground or an enemy at the top right, chain links rigid, motion lines behind the hero, the composition is a rising diagonal with a figure pulled toward a hook. No text, no letters, no ornamental frames."
    ),
}

out = {"frames": {}, "icons": {}}
for key, d in DIRS.items():
    for v in (1, 2):
        out["frames"]["%s-v%d" % (key, v)] = BASE + d + " " + VARIANT[v]
for v, t in ICONS.items():
    out["icons"]["silhouettes-v%d" % v] = ICON_BASE + t

for grp in out.values():
    for k, p in grp.items():
        assert "\n" not in p and all(ord(c) < 128 for c in p), k
json.dump(out, open("prompts.json", "w"), indent=1)
print({k: len(p) for g in out.values() for k, p in g.items()})
