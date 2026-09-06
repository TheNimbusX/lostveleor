"""Измерение кадра: тон, насыщенность, цветовые массы.

Световой проход нельзя принимать на глаз: приглушённый оттенок в отрыве
смотрится благороднее, следующий берут темнее для сочетания, и через три
итерации получается Darkest Dungeon. Защита одна — сверять каждую партию с
эталоном, а не с предыдущей, и сверять числом.

Эталон здесь — принятые концепты Акта I из levels/. Они и есть утверждённый
стиль, поэтому целевые значения берутся из них, а не назначаются.

  тон       доля площади в тёмном / среднем / светлом и медиана яркости.
            Кадр, у которого медиана 0.64 и 6% тёмного, выглядит выцветшим
            независимо от того, что нарисовано на текстурах.
  насыщ.    медиана S. Ловит сползание в серость раньше, чем глаз.
  массы     6 крупнейших цветов и доли. Правило «3–5 крупных цветовых масс».

Запуск:
    python tools/gate0_frame_audit.py --frames artifacts/capture/look-pass-1
"""

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass, asdict
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent

# Светлая полоса начинается с 0.65: именно её требует закон «не меньше
# четверти площади в светлом значении».
DARK_MAX = 0.25
LIGHT_MIN = 0.65

CONCEPTS = [
    ("концепт: дерево", "levels/tree-act1-v1.png"),
    ("концепт: корни", "levels/roots-1.png"),
    ("концепт: камень", "levels/stone-1.png"),
    ("концепт: пень", "levels/pen-1.png"),
]


@dataclass
class FrameStats:
    name: str
    dark_share: float
    mid_share: float
    light_share: float
    luma_median: float
    luma_p05: float
    luma_p95: float
    sat_median: float
    sat_p90: float
    masses: list


def _luma(rgb: np.ndarray) -> np.ndarray:
    """Rec.709: тон, каким его видит глаз, а не среднее по каналам."""
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def _saturation(rgb: np.ndarray) -> np.ndarray:
    top = rgb.max(axis=-1)
    bottom = rgb.min(axis=-1)
    return np.where(top > 1e-6, (top - bottom) / np.maximum(top, 1e-6), 0.0)


def _masses(image: Image.Image, mask: np.ndarray, count: int = 6) -> list:
    """Крупнейшие цветовые массы и их доли от видимой площади."""
    quant = image.quantize(colors=12, method=Image.Quantize.MEDIANCUT,
                           dither=Image.Dither.NONE)
    idx = np.asarray(quant)
    palette = np.asarray(quant.getpalette()[: 12 * 3], dtype=np.uint8).reshape(-1, 3)

    total = int(mask.sum())
    if total == 0:
        return []
    out = []
    for i in range(len(palette)):
        share = float(((idx == i) & mask).sum()) / total
        if share <= 0:
            continue
        r, g, b = (int(v) for v in palette[i])
        out.append({"hex": f"#{r:02X}{g:02X}{b:02X}", "share": round(share, 4)})
    out.sort(key=lambda m: -m["share"])
    return out[:count]


def measure(path: Path, name: str) -> FrameStats:
    image = Image.open(path)
    if image.mode in ("RGBA", "LA", "P"):
        image = image.convert("RGBA")
        mask = np.asarray(image)[..., 3] > 16
        image = image.convert("RGB")
    else:
        image = image.convert("RGB")
        mask = np.ones(image.size[::-1], dtype=bool)

    rgb = np.asarray(image, dtype=np.float32) / 255.0
    luma = _luma(rgb)
    sat = _saturation(rgb)

    # Концепт-лист персонажа сдан на белой подложке без альфы. Если её не
    # убрать, тональный отчёт станет отчётом о белом листе.
    if mask.all():
        studio = (luma > 0.88) & (sat < 0.10)
        border = np.zeros_like(studio)
        border[0, :] = border[-1, :] = border[:, 0] = border[:, -1] = True
        if studio[border].mean() > 0.6:
            mask = ~studio

    visible = luma[mask]
    vsat = sat[mask]
    return FrameStats(
        name=name,
        dark_share=float((visible < DARK_MAX).mean()),
        mid_share=float(((visible >= DARK_MAX) & (visible < LIGHT_MIN)).mean()),
        light_share=float((visible >= LIGHT_MIN).mean()),
        luma_median=float(np.median(visible)),
        luma_p05=float(np.percentile(visible, 5)),
        luma_p95=float(np.percentile(visible, 95)),
        sat_median=float(np.median(vsat)),
        sat_p90=float(np.percentile(vsat, 90)),
        masses=_masses(image, mask),
    )


def table(rows: list[FrameStats]) -> str:
    head = ("| источник | тёмное | среднее | светлое | медиана тона | медиана насыщ. "
            "| крупнейшая масса |\n|---|---:|---:|---:|---:|---:|---|\n")
    body = ""
    for s in rows:
        top = s.masses[0] if s.masses else {"hex": "—", "share": 0.0}
        body += (f"| {s.name} | {s.dark_share:.0%} | {s.mid_share:.0%} | {s.light_share:.0%} "
                 f"| {s.luma_median:.2f} | {s.sat_median:.2f} "
                 f"| {top['hex']} {top['share']:.0%} |\n")
    return head + body


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", nargs="*", default=[],
                    help="файлы или каталоги со снимками игры")
    ap.add_argument("--out", default="artifacts/gate0")
    args = ap.parse_args()

    out = ROOT / args.out
    out.mkdir(parents=True, exist_ok=True)

    concepts = [measure(ROOT / rel, label) for label, rel in CONCEPTS
                if (ROOT / rel).exists()]

    shots: list[Path] = []
    for item in args.frames:
        path = ROOT / item if not Path(item).is_absolute() else Path(item)
        if path.is_dir():
            shots.extend(sorted(path.glob("*.png")))
        elif path.exists():
            shots.append(path)

    frames = [measure(p, f"{p.parent.name}/{p.name}") for p in shots]

    md = ["# Тональный отчёт", "", "## Эталон: принятые концепты Акта I", "",
          table(concepts)]
    if frames:
        md += ["", "## Кадр игры", "", table(frames)]
    text = "\n".join(md) + "\n"

    (out / "tone.md").write_text(text, encoding="utf-8")
    (out / "tone.json").write_text(
        json.dumps({"concepts": [asdict(c) for c in concepts],
                    "frames": [asdict(f) for f in frames]},
                   ensure_ascii=False, indent=2), encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
