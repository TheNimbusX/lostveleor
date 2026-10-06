# Final Wreck look (06.10): owner chose "C + earth from A". Builds prompts.json (one-line ASCII prompts, higgsfield-prompt-gotchas).
# Frames = try 2: edit the guide screenshot, effect only inside the magenta zones (game numbers, make_guides.py).
# Try 1 (padded guide + thin outlines + whole C/A frames as refs) gave ~2x lanes again; its prompts are in try1/prompts.json.
import json, os

ROOT = os.path.dirname(os.path.abspath(__file__))

HEAD = (
    "Edit image 1. Image 1 is a real screenshot of our bright stylized top-down action roguelite The War Remains at the true in-game camera, with translucent magenta zones painted on the ground by us. "
    "Keep image 1 as it is: the same camera angle, zoom and framing, the same arena layout, and the same positions and sizes of the hero, rocks, mushrooms, rune circle and moss-covered guardian golems; "
    "do not zoom in, do not move the camera, the result must still match this screenshot. "
    "Change only these things: redraw Pelag on the same spot at the same small size in the skill pose below, and paint the skill effect exactly inside the magenta zones, replacing every magenta pixel, so no magenta remains anywhere. "
    "The zones are the true in-game size of the damage area: this is a BASIC skill, not an ultimate, the effect fills its zone and stays inside it, only a few small chips fly a little past its edge, and the rest of the arena stays clean. "
    "You may brighten the screenshot slightly to warm sunny daylight but keep its composition. "
    "Image 2 is a close-up swatch of the chosen iron and chain-link look: use it for material and drawing manner only, not for size, and drop its lightning-like crackle. "
    "Image 3 is a close-up swatch of the earth crater with torn turf and flying clods, the second layer at the impact. "
    "Hero Pelag as in image 4: brown hair, red headband, white wrap shirt with teal trim, red sash, light trousers, sandals; keep his own colours, do not whiten him, no rim light. "
    "His weapon is the big dark iron anchor on a heavy chain as in image 5, the anchor head about half as tall as Pelag. "
    "Paint the effect as hand-painted watercolor game VFX with thin dark ink outlines, crisp graphic shapes and real volume: standing and flying pieces that cast small shadows, not flat decals; the outer edge of the effect is the readable damage edge. "
    "Effect palette: dark iron, cold steel and brown earth. NO lightning, no electric crackle, no glowing zigzag bolts: every shard is a solid metal piece with flat facets, plus curled steel shavings. "
    "No foam, no water, no turquoise wave; no red, orange, gold or yellow in the effect. No text, no UI. "
)

SLAM = (
    "Pelag has just swung the anchor in a vertical arc over his head and slammed it crown-first into the ground in the middle of the round part of the magenta zone, the chain taut from his hands to the anchor, a short steel-white arc trail of the swing fading above him. "
    "The round part becomes a shallow crater with a cracked raised rim, torn turf flaps and brown earth clods thrown up and a quick burst of iron shards. "
)

BASE = (
    "SKILL Wreck, third press, the ground slam. " + SLAM +
    "The long narrow part of the zone becomes the iron wave: a straight row of heavy chain-link imprints pressed deep into the dirt as if a giant chain was stamped into the ground, "
    "steel shavings and angular iron shards flicking up along both long edges so the edges are crisp, and the front of the wave is a short standing crest of iron shards and earth that knocks a guardian golem standing in the zone up into the air. "
    "Family accent colour: cold steel blue-white, highlights around hex 9CC8F0 with white glints on dark iron; the steel never glows like energy. "
)

BREAKWATER = (
    "SKILL FORM Breakwater of Wreck. " + SLAM +
    "Then a rolling WALL travels along the long part of the zone: a knee-high to waist-high moving wall of churned earth, torn turf, dark iron plates and chain links, its front tumbling forward like a plough wall of ground and metal, "
    "exactly as wide as the zone, carrying two or three small light moss-covered forest critters lifted on its crest while heavy guardians stagger at its sides. "
    "Form colour sea green hex 1FB37E: sea-green glints on the iron shards and sea-green curled steel shavings over dark iron and brown earth; still no water and no foam. "
)

NINTH = (
    "SKILL FORM Ninth Wave of Wreck: a fully charged overhead slam. Pelag has brought the anchor down from high above with a heavy indigo steel arc trail; it hits crown-first in the middle of the large round part of the zone, "
    "which becomes a bigger, deeper crater with a thick broken rim, plates of earth tilted up and big clods flying, the chain taut from his hands. "
    "The wide long part of the zone (twice as wide as the base lane) holds one row of much larger, heavier chain-link imprints driven deep into the ground, cracked earth slabs heaved up along both edges, chunky iron shards and steel shavings, a guardian golem in the zone slammed and lifted. "
    "Form colour indigo hex 4B3FD0: deep indigo tint on the steel, indigo shadows in the imprints, pale lilac-white glints on the iron; heavier than the base but still compact, inside the zone. "
)

SHELL = (
    "SKILL FORM Water Shell of Wreck: a protective shell of loose chain links and small curved dark iron plates floats around Pelag, orbiting him close at chest and waist height like armour scales hanging in the air, "
    "pearl white hex E4EEF6 with a soft mother-of-pearl sheen on the plates and thin dark outlines; nothing is painted on the ground for the shell, no ground ring. "
)

FRAMES = {
    "Wreck-base-v1": ("guide-base-v1.jpg", BASE + "Moment: a split second after the impact, the wave front is two thirds along the zone, one guardian in the zone already lifted."),
    "Wreck-base-v2": ("guide-base-v2.jpg", BASE + "Moment: the wave front has just reached the far end of the zone, the last guardian in it lifted off the ground, the chain imprints behind the front settling into the dirt."),
    "Breakwater-v1": ("guide-breakwater-v1.jpg", BREAKWATER + "Moment: the wall is halfway along the zone carrying the small critters on its crest, the crater and a drag furrow behind it."),
    "Breakwater-v2": ("guide-breakwater-v2.jpg", BREAKWATER + "Moment: the wall has reached the far end of the zone and crashes, throwing the carried critters forward and scattering earth and iron chunks, a straight furrow with chain imprints left behind it."),
    "NinthWave-v1": ("guide-ninthwave-v1.jpg", NINTH + "Moment: the instant of impact, clods and slabs at their highest, the imprint row being punched in from the crater forward."),
    "NinthWave-v2": ("guide-ninthwave-v2.jpg", NINTH + "Moment: a beat later, the heavy imprint row has reached the far end of the zone, dust settling around the deep crater."),
    "WaterShell-v1": ("guide-watershell-v1.jpg", SHELL + "Moment: the swing before the slam: Pelag holds the chain with the anchor high over his head at the top of its arc, the closed shell orbits him tightly. The dashed magenta circle only shows the future burst radius: erase it completely and leave that ground clean, no crater, no lane, no impact yet."),
    "WaterShell-v2": ("guide-watershell-v2.jpg", SHELL + "Moment: the slam. " + SLAM + "The long narrow part becomes a short row of iron chain-link imprints. At the same instant the shell bursts outward: the pearl plates and chain links fly radially out from Pelag in all directions and stop at the dashed magenta circle, small pearl-white shavings trailing them, a nearby guardian knocked back by a plate. Erase the dashed line itself, paint no ring on the ground."),
    # extra retries after review: Breakwater accent too faint, Shell v1 read as a ring on the ground
    "Breakwater-v3": ("guide-breakwater-v1.jpg", BREAKWATER + "Moment: the wall is halfway along the zone carrying the small critters on its crest, the crater and a drag furrow behind it. The sea-green accent must be clearly visible: bright sea-green hex 1FB37E glints and rims on every iron plate and shard of the wall and a sea-green streak along the crest of the wall, so this form is recognised by its colour at a glance, while the mass stays dark iron and brown earth."),
    "WaterShell-v3": ("guide-watershell-v1.jpg", SHELL + "Moment: the swing before the slam: Pelag holds the chain with the anchor high over his head at the top of its arc. The shell clearly floats in the air around his upper body: a loose spherical cage of tilted pearl plates and chain links at different heights from his knees to above his head, each piece casting its own small shadow on the ground; it must not look like a ring lying on the ground. The dashed magenta circle only shows the future burst radius: erase it completely and leave that ground clean, no crater, no lane, no impact yet."),
}
# learned from the base frames (try 2): the rim came out as a ring of standing stakes, the lane ran ~2.5 m past the zone
FORM_TAIL = " The crater rim is low broken earth and turf, not a ring of standing slabs or stakes. The effect ends exactly at the far end of the zone and does not run past it."
FRAME_REFS = ["swatch_C-iron-lane.jpg", "swatch_A-earth-crater.jpg", "r2_model.jpg", "r3_anchor.jpg"]

ICON_HEAD = (
    "Game ability icon sheet: three separate square skill icons in one row on one wide image with generous gaps, each on its own dark navy square background exactly like reference images 2 and 3, "
    "the same hand-painted game icon style with thick readable shapes, thin dark outlines and crisp painterly highlights as those Whirlwind and Squall icons. "
    "Image 1 is our chosen silhouette draft for these three icons: keep its three compositions and apply the fixes below. "
    "The anchor and chain match image 4: dark iron anchor with a pointed crown and two hooked flukes, a ring, heavy chain links, a small red cloth tie on the chain grip. "
    "The anchor family gets its OWN accent colour, not turquoise: cold steel blue-white motion strokes, sparks and shards, highlights around hex 9CC8F0 with white glints, over dark iron and charcoal, "
    "so the three icons read as one family clearly different from the turquoise foam of Whirlwind and Squall. No foam, no water, no lightning bolts, no red, orange or gold effects (the small red cloth tie and the red sash of Pelag are fine). "
    "Each icon must read at 64 pixels by silhouette alone. "
    "Icon 1 on the left, WRECK: keep the draft as is: the anchor dropping vertically at the bottom of a big circular chain arc that fills the icon, crown smashing into a crater with an upward burst of rock shards and earth, the circle of the swing plus the strike; recolour its pale strokes to the cold steel blue-white family accent. "
    "Icon 2 in the middle, ANCHOR THROW: the same horizontal harpoon composition but the anchor and the chain are twice as thick and big: the anchor head large and filling the right half of the tile, the chain a heavy bold band crossing the whole tile from the left edge, strong cold steel blue-white speed streaks above and below it, the shape fills the tile edge to edge. "
    "Icon 3 on the right, ABORDAGE: the same rising diagonal yank composition but the figure must clearly be Pelag from image 5: light white shirt, bright red sash and red headband, brown hair, drawn larger and lighter and filling the lower left half of the tile, "
    "pulled up along a taut heavy chain toward the anchor hooked into a broken stone edge at the top right, cold steel blue-white motion streaks behind him; the figure is light-coloured against the dark background so it survives at 64 pixels, not a dark silhouette. "
    "No text, no letters, no frames."
)
ICONS = {
    "base-icons-v1": ICON_HEAD,
    "base-icons-v2": ICON_HEAD + " Variation: bolder and simpler shapes, fewer small details, stronger light-dark contrast.",
}
ICON_REFS = ["icons_silhouettes-v2.jpg", "icon_Whirlwind.jpg", "icon_Squall.jpg", "r3_anchor.jpg", "r2_model.jpg"]

FORMS_ICON = (
    "Game ability icon sheet: four square skill icons in a 2 by 2 grid on one square image with even gaps, each on its own dark navy square background, the same hand-painted game icon style as reference images 1 and 3. "
    "These are the four forms of the anchor skill Wreck. All four use EXACTLY the same silhouette and composition as image 1: the anchor dropping vertically at the bottom of a big circular chain arc that fills the tile, crown smashing into a crater with an upward burst of shards; "
    "only the accent colour and the material of the effect change, the dark iron anchor, the chain and the small red cloth tie stay identical, the way the four Whirlwind form icons in image 2 keep one silhouette and change only colour and material. "
    "Top left, BASE Wreck: as image 1, cold steel blue-white strokes and steel shards (hex 9CC8F0 with white glints). "
    "Top right, BREAKWATER, sea green hex 1FB37E: the circle of the swing is a churning band of earth clods, turf and iron plates rimmed with sea-green steel glints, the burst at the bottom is a low rolling wall of earth and iron chunks. "
    "Bottom left, NINTH WAVE, indigo hex 4B3FD0: a heavier, thicker swing stroke in deep indigo steel, a bigger darker crater burst with chunky earth slabs and indigo-lit iron shards, pale lilac-white highlights. "
    "Bottom right, WATER SHELL, pearl white hex E4EEF6: the circle of the swing is a ring of floating chain links and small curved iron plates with a mother-of-pearl sheen, bursting outward at the bottom, soft pearl glints. "
    "No foam, no water waves, no lightning, no red, orange or gold effects (the small red cloth tie is fine). Each must read at 64 pixels, and the four must be told apart by colour at a glance while clearly being the same skill. No text, no letters, no frames."
)
FORMS_ICONS = {
    "forms-icons-v1": ["new_Wreck_icon_v1.jpg", "whirlwind_forms_2x2.jpg", "new_base_icons_v1.jpg", "r3_anchor.jpg"],
    "forms-icons-v2": ["new_Wreck_icon_v2.jpg", "whirlwind_forms_2x2.jpg", "new_base_icons_v2.jpg", "r3_anchor.jpg"],
}

jobs = []
for name, (guide, text) in FRAMES.items():
    tail = "" if name.startswith("Wreck-base") or name in ("WaterShell-v1", "WaterShell-v3") else FORM_TAIL
    jobs.append({"name": name, "file": "frames/%s.png" % name, "prompt": HEAD + text + tail, "refs": [guide] + FRAME_REFS,
                 "quality": "medium", "resolution": "1k", "aspect_ratio": "16:9"})
for name, text in ICONS.items():
    jobs.append({"name": name, "file": "icons/%s.png" % name, "prompt": text, "refs": ICON_REFS,
                 "quality": "medium", "resolution": "2k", "aspect_ratio": "21:9"})
for name, refs in FORMS_ICONS.items():
    jobs.append({"name": name, "file": "icons/%s.png" % name, "prompt": FORMS_ICON, "refs": refs,
                 "quality": "medium", "resolution": "2k", "aspect_ratio": "1:1"})

for j in jobs:
    assert "\n" not in j["prompt"] and all(ord(c) < 128 for c in j["prompt"]), j["name"]
json.dump({"jobs": jobs}, open(os.path.join(ROOT, "prompts.json"), "w"), indent=1)
print({j["name"]: len(j["prompt"]) for j in jobs})
