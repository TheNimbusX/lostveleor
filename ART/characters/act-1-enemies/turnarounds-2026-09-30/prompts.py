# Prompts sent to Higgsfield gpt_image_2_5 (high, 2k, 16:9) on 2026-09-30.
COMMON = (
    "Character turnaround model sheet for 3D modelling (image-to-3D). Game: The War Remains, a bright, saturated, "
    "stylized hand-painted 3D action roguelike (Hades 2 / Shape of Dreams tier). {layout} "
    "Same character, same scale, feet on one shared ground line, evenly spaced, nothing overlapping or cropped. "
    "Neutral model-sheet pose: {pose} No action, no magic, no glow effects, no particles, no floating leaves or petals. "
    "Plain flat light warm-grey background (#ECEAE5), flat even studio light from the front, no dramatic shadows, "
    "no rim light, only a faint soft contact shadow under the feet. Clean readable silhouette, crisp edges, every part "
    "attached to the body and thick enough to model. Colours warm and saturated like our game: not washed out, not "
    "whitened, not grimdark. Small plain labels under the figures: FRONT, SIDE, BACK, 3/4. No other text, no logos, "
    "no frames, no UI. "
)
ROW = ("Show the character four times in one row, left to right: FRONT view, SIDE view (left profile), "
       "BACK view, 3/4 FRONT view.")
GRID = ("Layout 2x2 grid: top-left SIDE view (left profile, full length), top-right FRONT view, bottom-left BACK view, "
        "bottom-right 3/4 FRONT view.")
HUMAN_POSE = ("relaxed A-pose, arms about 35 degrees away from the body, hands open and relaxed, legs straight and "
              "shoulder-width apart, mouth closed.")
REFS_STYLE = ("Image {s} is an approved mob from the same game: match its rendering exactly (chunky hand-painted bark "
              "and moss texture, big clear shapes, warm saturated colours, soft painterly shading). Image {g} is our "
              "in-game screenshot: use it only for colour brightness and saturation; do not copy its scene, camera, UI "
              "or characters.")

P = {}
P["forest-mage"] = COMMON.format(layout=ROW, pose=HUMAN_POSE) + (
    "Images 1 and 2 are the character design of the Forest Mage: follow them exactly (face, proportions, costume, "
    "materials, colour placement). A lean elf-like forest caster, 2.25 m tall, half as wide as a guardian: spiky "
    "leaf hair, pointed ears, cream face markings, small goatee, green scarf, big cream feather-leaf fur collar, "
    "sleeveless green tunic with cream sash and a green gem pendant, rope belt with a wooden hexagonal leaf buckle, "
    "green tabard with cream diamond pattern, layered green-and-cream leaf skirt, rope-wrapped forearms and shins, "
    "his left leg overgrown by a mossy vine-root with pink and orange flowers (the one bright accent), root-foot on "
    "that leg, bare foot on the other. Hands empty, no spell light. " + REFS_STYLE.format(s=3, g=4))
P["forest-wolf"] = COMMON.format(layout=GRID, pose=(
    "standing calmly on four straight legs, head level looking forward, tail relaxed and slightly raised, mouth "
    "closed.")) + (
    "Images 1 and 2 are the character design of the Forest Wolf: follow them exactly. A long, low, lean predator "
    "wolf, 1.35 m tall, whose fur is made of layered grey bark plates, with moss patches on the back and haunches, "
    "5 to 7 chunky dry branches instead of a mane growing up and back from the neck and shoulders, a bushy bark "
    "tail ending in a branch, cream diamond mark on the forehead, pale green eyes, a rope and bead necklace with "
    "small wooden leaf charms and a cream diamond pendant, bark-wrapped forelegs, big claws. Keep him grey-bark but "
    "warm the greys slightly toward living wood so he sits in our bright forest; bright moss green. "
    + REFS_STYLE.format(s=3, g=4))
P["tree-brute"] = COMMON.format(layout=ROW, pose=(
    "standing upright, long arms hanging straight down beside the body with the huge fists near the ground and "
    "slightly away from the legs so the gaps are visible, legs apart.")) + (
    "Image 1 is the character design of the Tree Brute (elite): follow it exactly. A massive wooden brute, 2.90 m "
    "tall and wider than tall: tiny head with a pale bone-like beak mask and small eyes, huge plate shoulders of "
    "layered bark with thick moss on top, two antler-like branches rising from each shoulder, a cream diamond "
    "emblem plate on the chest, arms longer than the legs that reach the ground and end in huge bark-slab fists "
    "with claws, short thick stump legs with bark feet, jagged bark plates on forearms and shins. Warm brown bark "
    "and bright moss green. The only design reference is small: invent the back and side consistently (moss mantle "
    "over the back, bark plates along the spine). " + REFS_STYLE.format(s=2, g=3))
P["elder-guardian"] = COMMON.format(layout=ROW, pose=HUMAN_POSE) + (
    "Image 1 is the character design of the Elder Guardian (elite): follow it exactly. A tall horned warrior-tree, "
    "2.75 m: a large crown of branch-antlers with small green leaves, stern wooden face with a beard of bark and "
    "pale green eyes (painted, no glow), cream diamond mark on the forehead, broad shoulders with bark-and-leaf "
    "pauldrons, long bark arms with clawed wooden hands, rope bindings on wrists and ankles, cream sash over the "
    "chest, green-and-cream cloth tabard with diamond pattern and a wooden hexagonal leaf buckle with a hanging "
    "wooden tag, torn cream leaf skirt, tree-trunk legs with spreading root feet. No green magic wisps. "
    + REFS_STYLE.format(s=2, g=3))
P["flowerkeeper"] = COMMON.format(layout=ROW, pose=HUMAN_POSE.replace("hands open and relaxed", "long claw fingers relaxed")) + (
    "Images 1 and 2 are the character design of the Flowerkeeper (elite): follow them exactly. A tall thin humanoid "
    "of pale bark, 2.85 m with the flower: pointed cream leaf-mask face with yellow-green eyes, green leaf mane and "
    "collar, small pink blossoms on the shoulders and wrists, leaf skirt with a cream diamond pattern and a golden "
    "leaf clasp, long thin arms with long claw fingers, bent root legs with claw toes, two curling vine tendrils "
    "growing from the upper back, kept close to the body. Above and behind the head, attached to the upper back by "
    "a thick green stem: ONE giant CLOSED flower bud with layered crimson-pink petals and cream tips, exactly as in "
    "image 1. Image 2 shows how the bud opens in combat: use it only to understand the petal structure; draw the "
    "bud CLOSED, with each petal a clear separate layer. " + REFS_STYLE.format(s=3, g=4))
