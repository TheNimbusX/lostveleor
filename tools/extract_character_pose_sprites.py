from __future__ import annotations

from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


WORKSPACE = Path(r"C:\Users\d.grab\Desktop\the-game")
PACK = WORKSPACE / "ART/designed-images" / "deliverables" / "razlom-character-production-pack-v5"
OUTPUT = WORKSPACE / "razlom" / "Assets" / "Resources" / "CharacterSprites"

CANVAS = 1536
GROUND_Y = 1400  # top-left image coordinates; leaves transparent space below the feet
DOWNSAMPLE = 4


SHEETS = (
    (
        "Pelag",
        PACK / "Pelag_Boarder_01" / "08_animation_poses" / "poses locomotion_attack.png",
        ("idle_0", "idle_1", "run_0", "run_1", "attack_0", "hook_0", "attack_1"),
        1.45,
    ),
    (
        "Pelag",
        PACK / "Pelag_Boarder_01" / "08_animation_poses" / "poses reactions_ability.png",
        ("hit_0", "hit_1", "stun_0", "knockback_0", "death_0", "hook_1"),
        1.45,
    ),
    (
        "Orvill",
        PACK / "Orvill_ShieldInfantry_01" / "08_animation_poses" / "poses guard_attack.png",
        ("idle_0", "walk_0", "attack_0", "attack_1", "idle_1"),
        1.00,
    ),
    (
        "Orvill",
        PACK / "Orvill_ShieldInfantry_01" / "08_animation_poses" / "poses block_reactions_death.png",
        ("block_0", "hit_0", "hit_1", "death_0", "death_1"),
        1.00,
    ),
)


def label_components(alpha: Image.Image) -> tuple[np.ndarray, list[dict[str, float]]]:
    small = alpha.resize(
        (alpha.width // DOWNSAMPLE, alpha.height // DOWNSAMPLE), Image.Resampling.NEAREST
    ).filter(ImageFilter.MaxFilter(3))
    mask = np.asarray(small, dtype=np.uint8) >= 20
    height, width = mask.shape
    labels = np.zeros((height, width), dtype=np.int32)
    components: list[dict[str, float]] = []
    label = 0

    for y in range(height):
        for x in range(width):
            if not mask[y, x] or labels[y, x] != 0:
                continue
            label += 1
            queue = deque(((x, y),))
            labels[y, x] = label
            count = 0
            sum_x = 0
            sum_y = 0
            while queue:
                px, py = queue.popleft()
                count += 1
                sum_x += px
                sum_y += py
                for nx, ny in ((px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1)):
                    if 0 <= nx < width and 0 <= ny < height and mask[ny, nx] and labels[ny, nx] == 0:
                        labels[ny, nx] = label
                        queue.append((nx, ny))
            components.append(
                {
                    "label": label,
                    "area": count,
                    "cx": sum_x / count,
                    "cy": sum_y / count,
                }
            )
    return labels, components


def extract_sheet(character: str, source: Path, names: tuple[str, ...], scale: float) -> None:
    image = Image.open(source).convert("RGBA")
    rgba = np.asarray(image)
    alpha = image.getchannel("A")
    labels, components = label_components(alpha)

    majors = sorted(components, key=lambda c: c["area"], reverse=True)[: len(names)]
    majors.sort(key=lambda c: c["cx"])
    major_label_to_pose = {int(c["label"]): index + 1 for index, c in enumerate(majors)}

    label_to_pose = np.zeros(len(components) + 1, dtype=np.int16)
    for component in components:
        component_label = int(component["label"])
        if component_label in major_label_to_pose:
            label_to_pose[component_label] = major_label_to_pose[component_label]
            continue
        nearest = min(
            range(len(majors)),
            key=lambda i: (component["cx"] - majors[i]["cx"]) ** 2
            + (component["cy"] - majors[i]["cy"]) ** 2,
        )
        label_to_pose[component_label] = nearest + 1

    assigned_small = label_to_pose[labels]
    assigned = np.asarray(
        Image.fromarray(assigned_small.astype(np.uint8), mode="L").resize(image.size, Image.Resampling.NEAREST),
        dtype=np.uint8,
    )
    alpha_array = rgba[:, :, 3]

    out_dir = OUTPUT / character
    out_dir.mkdir(parents=True, exist_ok=True)

    for pose_index, name in enumerate(names, start=1):
        pose_mask = (assigned == pose_index) & (alpha_array > 0)
        ys, xs = np.nonzero(pose_mask)
        if len(xs) == 0:
            raise RuntimeError(f"No pixels extracted for {character}/{name} from {source}")

        left, right = int(xs.min()), int(xs.max()) + 1
        top, bottom = int(ys.min()), int(ys.max()) + 1
        isolated = np.zeros_like(rgba)
        isolated[pose_mask] = rgba[pose_mask]
        crop = Image.fromarray(isolated[top:bottom, left:right], mode="RGBA")

        scaled_width = max(1, round(crop.width * scale))
        scaled_height = max(1, round(crop.height * scale))
        if scaled_width > CANVAS - 32 or scaled_height > CANVAS - 32:
            fit = min((CANVAS - 32) / scaled_width, (CANVAS - 32) / scaled_height)
            scaled_width = max(1, round(scaled_width * fit))
            scaled_height = max(1, round(scaled_height * fit))
        actual_scale_x = scaled_width / crop.width
        actual_scale_y = scaled_height / crop.height
        crop = crop.resize((scaled_width, scaled_height), Image.Resampling.LANCZOS)

        # Median alpha x follows the painted body better than the bounding-box
        # center when a sword, shield, or chain extends far to one side.
        anchor_x = float(np.median(xs))
        anchor_y = float(ys.max())
        anchor_in_crop_x = (anchor_x - left) * actual_scale_x
        anchor_in_crop_y = (anchor_y - top) * actual_scale_y

        paste_x = round(CANVAS * 0.5 - anchor_in_crop_x)
        paste_y = round(GROUND_Y - anchor_in_crop_y)
        canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
        canvas.alpha_composite(crop, (paste_x, paste_y))
        output_path = out_dir / f"{name}.png"
        canvas.save(output_path, optimize=True)
        print(f"{character}/{name}: {output_path} source_bbox={(left, top, right, bottom)}")


def main() -> None:
    for character, source, names, scale in SHEETS:
        extract_sheet(character, source, names, scale)


if __name__ == "__main__":
    main()
