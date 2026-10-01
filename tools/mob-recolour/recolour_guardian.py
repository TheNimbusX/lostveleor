"""Перекраска атласа Лесного хранителя под палитру семьи мобов леса (эталон — Корнехват).

    python tools/mob-recolour/recolour_guardian.py --install   # замер, атлас, превью и установка в проект
    python tools/mob-recolour/recolour_guardian.py --size 1024 --no-preview   # быстрая проба (только в --work)

Что делает и почему так:

1. Блендером (blender_uv_dump.py) читает UV каждого моба и считает цвет ТОЛЬКО по пикселям атласа,
   которые лежат на теле, — поля между островами не врут в статистику.
2. Переводит цвет в OKLab/OKLCh (перцептивное пространство: L — светлота, C — насыщенность, h — тон)
   и делит пиксели на мягкие семьи: «кора/дерево» (h < ~97°), «листва/мох» (h > ~97°)
   и «нейтральные» (C < ~0.03: кремовые руны, серое, чёрное).
3. Светлота — одной кривой квантилей на всё тело (порядок пикселей и контраст сохраняются, щели
   остаются тёмными) плюс небольшой добор медианы семьи в светах (у эталона листва светлее коры).
   Насыщенность и тон — по «растяжке» семьи: k-я полоса светлоты коры Хранителя получает
   насыщенность (отношением) и тон (сдвигом) k-й полосы коры эталона. Рисунок атласа не меняется.
4. Пишет новый атлас в --work, отчёт palette-report.json и, если найден Blender, рендер
   «было / стало / Корнехват» одним светом. С --install кладёт атлас рядом со старым
   (Forest_Guardian_BaseColor_v2.png, старый jpg не трогается) с .meta фиксированного GUID и
   переводит на него Forest_Guardian_Material.mat. Игра берёт атлас по пути ArenaView.OrvillTexture.

Никакого подкрашивания в шейдере: меняется только картинка, материал просто смотрит на новый файл.
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


REPO = Path(__file__).resolve().parents[2]
CHARACTERS = REPO / "razlom" / "Assets" / "Resources" / "Characters"
BLENDER = Path(r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL = Path(__file__).resolve().parent

# Модель, по которой берутся UV, и атлас цвета. Хранитель — цель, Корнехват — эталон, остальные —
# семья для отчёта (их числа показывают, что эталон не выбивается из леса).
MOBS = {
    "guardian": ("Forest_Guardian/Forest_Guardian.fbx", "Forest_Guardian/Forest_Guardian_BaseColor.jpg"),
    "snarer": ("Forest_RootSnarer/ForestRootSnarer.fbx", "Forest_RootSnarer/T_ForestRootSnarer_Color.png"),
    "wendigo": ("Forest_Wendigo/ForestWendigo.fbx", "Forest_Wendigo/ForestWendigo_BaseColor.png"),
    "stonehoof": ("Forest_Stonehoof/ForestStonehoof.fbx", "Forest_Stonehoof/Stonehoof_BaseColor.png"),
    "splitter": ("Forest_Splitter/ForestSplitter.fbx", "Forest_Splitter/ForestSplitter_Color.png"),
    "thorncaster": ("Forest_Thorncaster/ForestThorncaster.fbx", "Forest_Thorncaster/ForestThorncaster_Color.png"),
    "rootswarm": ("Forest_RootSwarm/Forest_RootSwarm.fbx", "Forest_RootSwarm/Forest_RootSwarm_BaseColor.JPEG"),
    "bud": ("Forest_Bud/ForestBudRanged.fbx", "Forest_Bud/Textures/ForestBud_BaseColor.png"),
}
TARGET, REFERENCE = "guardian", "snarer"
OUTPUT_TEXTURE = CHARACTERS / "Forest_Guardian" / "Forest_Guardian_BaseColor_v2.png"
# GUID нового атласа зашит: повторный прогон перезаписывает картинку, а ссылки материала не рвутся.
OUTPUT_GUID = "a6d244a9d64c413492478c27ab435e0a"
SOURCE_GUID = "d5ab94232440d004d9673ccf9f6ad940"
MATERIAL = CHARACTERS / "Forest_Guardian" / "Forest_Guardian_Material.mat"

# РУЧКИ. Подобраны 01.10.2026 по рендеру «было / стало / Корнехват» (см. README превью).
SETTINGS = {
    # Граница «кора ↔ листва» по тону OKLCh, градусы; мягкая полоса шириной FAMILY_FEATHER.
    "family_hue": 97.0,
    "family_feather": 4.0,
    # Ниже этой насыщенности пиксель нейтральный (кремовые руны, серое, чёрное); полоса перехода.
    "neutral_chroma": (0.020, 0.038),
    # Акцент эталона (рыжие грибы Корнехвата) — НЕ часть его коры, в статистику коры не идёт.
    "reference_accent": {"min_chroma": 0.10, "max_hue": 72.0},
    # Доля переноса: 1 — как у эталона, 0 — как было.
    "strength_lightness": 0.80,
    "strength_chroma": 1.0,
    "strength_hue": 1.0,
    # Разброс тона листвы сжимается к эталону, но не сильнее этого множителя — иначе листья плоские.
    "min_hue_spread": 0.55,
    # Число полос светлоты в «растяжке» семьи (см. fit).
    "ramp_bins": 8,
    # Добор светлоты семьи (offset) включается между этими значениями L: щели и прожилки не трогаем.
    "offset_fade": (0.22, 0.38),
}

QUANTILES = np.linspace(0.0, 1.0, 101)


# ------------------------------------------------------------------ цвет: sRGB <-> OKLab

M1 = np.array([[0.4122214708, 0.5363325363, 0.0514459929],
               [0.2119034982, 0.6806995451, 0.1073969566],
               [0.0883024619, 0.2817188376, 0.6299787005]], np.float32)
M2 = np.array([[0.2104542553, 0.7936177850, -0.0040720468],
               [1.9779984951, -2.4285922050, 0.4505937099],
               [0.0259040371, 0.7827717662, -0.8086757660]], np.float32)
M2_INV = np.linalg.inv(M2.astype(np.float64)).astype(np.float32)
M1_INV = np.linalg.inv(M1.astype(np.float64)).astype(np.float32)


def srgb_to_oklab(rgb):
    linear = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    return np.cbrt(linear @ M1.T) @ M2.T


def oklab_to_srgb(lab):
    linear = ((lab @ M2_INV.T) ** 3) @ M1_INV.T
    linear = np.clip(linear, 0.0, 1.0)
    return np.where(linear <= 0.0031308, linear * 12.92, 1.055 * np.power(linear, 1 / 2.4) - 0.055)


def lch(lab):
    chroma = np.hypot(lab[..., 1], lab[..., 2])
    hue = (np.degrees(np.arctan2(lab[..., 2], lab[..., 1])) + 360.0) % 360.0
    return lab[..., 0], chroma, hue


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# ------------------------------------------------------------------ семьи и статистика

def memberships(chroma, hue):
    """Мягкие веса семей: кора, листва, нейтральные. Сумма = 1."""
    lo, hi = SETTINGS["neutral_chroma"]
    chromatic = smoothstep(lo, hi, chroma)
    edge, feather = SETTINGS["family_hue"], SETTINGS["family_feather"]
    # Тон > 200° (сине-фиолетовое) у мобов леса не встречается; если попадётся — считаем листвой.
    leafy = smoothstep(edge - feather, edge + feather, hue) * (hue < 330)
    return chromatic * (1 - leafy), chromatic * leafy, 1 - chromatic


def weighted_quantiles(values, weights, q=QUANTILES):
    order = np.argsort(values)
    v, w = values[order], weights[order]
    cumulative = np.cumsum(w)
    if cumulative[-1] <= 0:
        return np.full(len(q), np.nan)
    cumulative = (cumulative - 0.5 * w) / cumulative[-1]
    return np.interp(q, cumulative, v)


def weighted_circular(hue, weights):
    radians = np.radians(hue)
    mean = np.degrees(np.arctan2((np.sin(radians) * weights).sum(), (np.cos(radians) * weights).sum())) % 360
    delta = (hue - mean + 180.0) % 360.0 - 180.0
    spread = np.sqrt((weights * delta ** 2).sum() / max(weights.sum(), 1e-9))
    return float(mean), float(spread)


def uv_mask(name, resolution, work):
    triangles_path = work / "uv" / f"{name}.npy"
    if not triangles_path.exists():
        model = CHARACTERS / MOBS[name][0]
        if not BLENDER.exists():
            raise SystemExit(f"Нет UV {triangles_path} и не найден Blender ({BLENDER}).")
        subprocess.run([str(BLENDER), "-b", "--factory-startup", "-P", str(TOOL / "blender_uv_dump.py"),
                        "--", str(model), str(triangles_path)], check=True, capture_output=True)
    triangles = np.load(triangles_path)
    canvas = Image.new("L", (resolution, resolution), 0)
    draw = ImageDraw.Draw(canvas)
    for triangle in triangles:
        draw.polygon([(u * resolution, (1.0 - v) * resolution) for u, v in triangle], fill=255)
    return np.asarray(canvas) > 0


def sample(name, work, resolution=1024, path=None):
    """Пиксели тела моба в OKLab (атлас уменьшен до resolution усреднением)."""
    path = path or CHARACTERS / MOBS[name][1]
    image = Image.open(path).convert("RGB").resize((resolution, resolution), Image.BOX)
    pixels = np.asarray(image, np.float32) / 255.0
    return srgb_to_oklab(pixels[uv_mask(name, resolution, work)])


def describe(lab, accent_out=False):
    """Числа палитры: светлота, насыщенность, тон по семьям; доли семей."""
    lightness, chroma, hue = lch(lab)
    bark, leaf, neutral = memberships(chroma, hue)
    if accent_out:
        accent = (chroma >= SETTINGS["reference_accent"]["min_chroma"]) & (hue <= SETTINGS["reference_accent"]["max_hue"])
        bark = bark * ~accent
    result = {
        "all": {"L_p10": float(np.percentile(lightness, 10)), "L_p50": float(np.median(lightness)),
                "L_p90": float(np.percentile(lightness, 90)), "C_p50": float(np.median(chroma)),
                "warmth_b": float(lab[:, 2].mean()), "green_red_a": float(lab[:, 1].mean())},
    }
    for family, weight in (("bark", bark), ("leaf", leaf), ("neutral", neutral)):
        share = float(weight.sum() / len(weight))
        mean_hue, spread = weighted_circular(hue, weight)
        result[family] = {
            "share": share,
            "L_p50": float(weighted_quantiles(lightness, weight, [0.5])[0]),
            "C_p50": float(weighted_quantiles(chroma, weight, [0.5])[0]),
            "hue_mean": mean_hue, "hue_spread": spread,
        }
    return result


# ------------------------------------------------------------------ перенос

def ramp(lightness, chroma, hue, weight, bins):
    """Цветовая «растяжка» семьи: по каждой полосе светлоты (по квантилям) — медиана C и средний тон."""
    edges = weighted_quantiles(lightness, weight, np.linspace(0.0, 1.0, bins + 1))
    rows = []
    for k in range(bins):
        inside = (lightness >= edges[k]) & (lightness <= edges[k + 1])
        band = weight * inside
        mean_hue, spread = weighted_circular(hue, band)
        rows.append((float(weighted_quantiles(chroma, band, [0.5])[0]), mean_hue, spread))
    return np.array(rows)


def fit(source_lab, reference_lab):
    """Кривые переноса по семьям.

    Светлота — сопоставление квантилей. Насыщенность и тон — по «растяжке»: пиксель из k-й полосы
    светлоты коры Хранителя получает насыщенность и тон k-й полосы коры эталона (отношением и сдвигом,
    поэтому разница соседних пикселей — рисунок — сохраняется). Так у эталона кора сереет в средних
    тонах и желтеет к светлым кончикам, и то же самое происходит у Хранителя.
    """
    bins = SETTINGS["ramp_bins"]
    s_l, s_c, s_h = lch(source_lab)
    r_l, r_c, r_h = lch(reference_lab)
    s_bark, s_leaf, _ = memberships(s_c, s_h)
    r_bark, r_leaf, _ = memberships(r_c, r_h)
    accent = (r_c >= SETTINGS["reference_accent"]["min_chroma"]) & (r_h <= SETTINGS["reference_accent"]["max_hue"])
    r_bark = r_bark * ~accent
    overall = (weighted_quantiles(s_l, np.ones_like(s_l)), weighted_quantiles(r_l, np.ones_like(r_l)))
    curves = {"all": {"L": overall}}
    mapped = through(s_l, overall)
    for family, sw, rw in (("bark", s_bark, r_bark), ("leaf", s_leaf, r_leaf)):
        source, reference = ramp(s_l, s_c, s_h, sw, bins), ramp(r_l, r_c, r_h, rw, bins)
        # Общая кривая светлоты уже подогнала всё тело; семье остаётся добрать разницу медиан
        # (у эталона листва светлее коры, у Хранителя было наоборот).
        offset = float(weighted_quantiles(r_l, rw, [0.5])[0] - weighted_quantiles(mapped, sw, [0.5])[0])
        squeeze = float(np.clip(np.mean(reference[:, 2]) / max(np.mean(source[:, 2]), 1e-6),
                                SETTINGS["min_hue_spread"], 1.0))
        curves[family] = {
            "L": (weighted_quantiles(s_l, sw), weighted_quantiles(r_l, rw)),
            "offset": offset,
            "centers": (np.arange(bins) + 0.5) / bins,
            "chroma_ratio": reference[:, 0] / np.maximum(source[:, 0], 1e-6),
            "hue_from": source[:, 1],
            "hue_shift": (reference[:, 1] - source[:, 1] + 180.0) % 360.0 - 180.0,
            "squeeze": squeeze,
            "ramp_source": source.tolist(), "ramp_reference": reference.tolist(),
        }
    return curves


def through(values, curve):
    """Кривая квантилей с концами 0→0 и 1→1: ничего не обрезается, порядок сохраняется."""
    source, target = curve
    source = np.concatenate(([0.0], source, [max(1.0, source[-1] + 1e-3)]))
    target = np.concatenate(([0.0], target, [max(1.0, target[-1] + 1e-3)]))
    source = np.maximum.accumulate(source + np.arange(len(source)) * 1e-7)
    return np.interp(values, source, target)


def apply(lab, curves):
    lightness, chroma, hue = lch(lab)
    bark, leaf, neutral = memberships(chroma, hue)

    # Светлота — ОДНОЙ кривой для всего тела: порядок «темнее/светлее» между семьями не ломается,
    # тёмные щели между пластинами коры остаются тёмными (контраст p90−p10 как у эталона).
    l_target = through(lightness, curves["all"]["L"])
    c_target = neutral * chroma
    # Добор семьи гаснет в тенях: прожилки и щели не высветляются.
    lit = smoothstep(*SETTINGS["offset_fade"], lightness)
    h_shift = np.zeros_like(hue)
    for family, weight in (("bark", bark), ("leaf", leaf)):
        curve = curves[family]
        l_target += weight * lit * curve["offset"]
        # Место пикселя в растяжке своей семьи (0 — самый тёмный, 1 — самый светлый).
        rank = np.interp(lightness, curve["L"][0], QUANTILES)
        c_target += weight * chroma * np.interp(rank, curve["centers"], curve["chroma_ratio"])
        base = np.interp(rank, curve["centers"], curve["hue_from"])
        delta = (hue - base + 180.0) % 360.0 - 180.0
        shift = np.interp(rank, curve["centers"], curve["hue_shift"]) + delta * (curve["squeeze"] - 1.0)
        h_shift += weight * shift

    new_l = lightness + SETTINGS["strength_lightness"] * (l_target - lightness)
    new_c = chroma + SETTINGS["strength_chroma"] * (c_target - chroma)
    new_h = np.radians(hue + SETTINGS["strength_hue"] * h_shift)
    return np.stack([new_l, new_c * np.cos(new_h), new_c * np.sin(new_h)], axis=-1)


def recolour_image(path, curves, size=None, rows=512):
    image = Image.open(path).convert("RGB")
    if size:
        image = image.resize((size, size), Image.LANCZOS)
    pixels = np.asarray(image, np.float32) / 255.0
    out = np.empty_like(pixels)
    for top in range(0, pixels.shape[0], rows):
        block = pixels[top:top + rows]
        out[top:top + rows] = oklab_to_srgb(apply(srgb_to_oklab(block), curves))
    return Image.fromarray(np.clip(np.round(out * 255.0), 0, 255).astype(np.uint8))


# ------------------------------------------------------------------ превью

def render(model, albedo, out, floor):
    mask = out.with_name(out.stem + "_mask.png")
    subprocess.run([str(BLENDER), "-b", "--factory-startup", "-P", str(TOOL / "blender_render_mob.py"), "--",
                    str(model), str(albedo), str(out), "--floor", floor, "--size", "820", "--mask", str(mask)],
                   check=True, capture_output=True)
    return readability(out, mask)


def readability(frame_path, mask_path):
    """Отличие тела от земли арены в кадре: медиана ΔE OKLab и доля пикселей тела, сливающихся с землёй."""
    frame = np.asarray(Image.open(frame_path).convert("RGB"), np.float32) / 255.0
    body = np.asarray(Image.open(mask_path).convert("RGBA"))[..., 3] > 200
    ground = np.median(frame[:40, :40].reshape(-1, 3), axis=0)   # освещённая земля в углу кадра
    distance = np.linalg.norm(srgb_to_oklab(frame[body]) - srgb_to_oklab(ground[None, :]), axis=1)
    lightness = srgb_to_oklab(frame[body])[:, 0]
    return {"deltaE_median": float(np.median(distance)), "blend_share_dE_lt_0.06": float((distance < 0.06).mean()),
            "body_L_p50": float(np.median(lightness)), "ground_L": float(srgb_to_oklab(ground[None, :])[0, 0])}


def label(image, text, sub=""):
    draw = ImageDraw.Draw(image)
    try:
        font = ImageFont.truetype("arial.ttf", 30)
        small = ImageFont.truetype("arial.ttf", 20)
    except OSError:
        font = small = ImageFont.load_default()
    draw.rectangle([0, 0, image.width, 74], fill=(24, 20, 18))
    draw.text((18, 8), text, fill=(240, 232, 214), font=font)
    draw.text((18, 44), sub, fill=(190, 182, 166), font=small)
    return image


def swatch_strip(lab, width, height=70):
    """Полоса «палитра»: пиксели тела, отсортированные по семье и светлоте."""
    lightness, chroma, hue = lch(lab)
    bark, leaf, neutral = memberships(chroma, hue)
    family = np.argmax(np.stack([bark, leaf, neutral]), axis=0)
    order = np.lexsort((lightness, family))
    picked = lab[order][np.linspace(0, len(order) - 1, width).astype(int)]
    row = oklab_to_srgb(picked)[None, :, :].repeat(height, axis=0)
    return Image.fromarray((row * 255).astype(np.uint8))


def preview(work, preview_dir, new_texture, stats):
    renders = work / "render"
    floor = "0.36,0.24,0.16"   # земля арены 1 (замер кадра capture comic-final-crowd2, без комикс-слоя)
    guardian_model = CHARACTERS / "Forest_Guardian" / "Forest_Guardian@Mutant Idle.fbx"
    jobs = [
        ("guardian_before.png", guardian_model, CHARACTERS / MOBS[TARGET][1]),
        ("guardian_after.png", guardian_model, new_texture),
        ("snarer.png", CHARACTERS / MOBS[REFERENCE][0], CHARACTERS / MOBS[REFERENCE][1]),
    ]
    for name, model, albedo in jobs:
        stats.setdefault("readability", {})[name] = render(model, albedo, renders / name, floor)

    def fmt(s):
        return (f"кора L {s['bark']['L_p50']:.2f} C {s['bark']['C_p50']:.3f} · "
                f"листва L {s['leaf']['L_p50']:.2f} C {s['leaf']['C_p50']:.3f} h {s['leaf']['hue_mean']:.0f}°")

    def framed(name):
        """Кадр по силуэту (маска рендера) с полями, все три — одного размера."""
        frame = Image.open(renders / name).convert("RGB")
        alpha = np.asarray(Image.open(renders / name.replace(".png", "_mask.png")).convert("RGBA"))[..., 3] > 10
        rows, cols = np.where(alpha)
        cy, cx = (rows.min() + rows.max()) / 2, (cols.min() + cols.max()) / 2
        half = max(rows.max() - rows.min(), cols.max() - cols.min()) * 0.62
        box = tuple(int(round(v)) for v in (cx - half, cy - half, cx + half, cy + half))
        return frame.crop(box).resize((820, 820), Image.LANCZOS)

    panels = [
        label(framed("guardian_before.png"), "Хранитель — было", fmt(stats["guardian_before"])),
        label(framed("guardian_after.png"), "Хранитель — стало", fmt(stats["guardian_after"])),
        label(framed("snarer.png"), "Корнехват — эталон", fmt(stats["snarer"])),
    ]
    width = sum(p.width for p in panels)
    strips = [stats["_lab"]["guardian_before"], stats["_lab"]["guardian_after"], stats["_lab"]["snarer"]]
    sheet = Image.new("RGB", (width, panels[0].height + 80), (24, 20, 18))
    x = 0
    for panel, lab in zip(panels, strips):
        sheet.paste(panel, (x, 0))
        sheet.paste(swatch_strip(lab, panel.width - 20), (x + 10, panel.height + 5))
        x += panel.width
    preview_dir.mkdir(parents=True, exist_ok=True)
    sheet.save(preview_dir / "guardian-recolour-preview.png")

    # Те же три атласа рядом: видно, что рисунок и мелкая деталь не тронуты, сменилась только краска.
    atlases = [Image.open(CHARACTERS / MOBS[TARGET][1]).convert("RGB"), Image.open(new_texture).convert("RGB"),
               Image.open(CHARACTERS / MOBS[REFERENCE][1]).convert("RGB")]
    crop = Image.new("RGB", (3 * 700, 700 + 60), (24, 20, 18))
    draw = ImageDraw.Draw(crop)
    for i, (atlas, text) in enumerate(zip(atlases, ["атлас было (фрагмент)", "атлас стало (тот же фрагмент)", "атлас Корнехвата"])):
        scale = atlas.width / 4096
        box = (int(1400 * scale), int(1500 * scale), int(2400 * scale), int(2500 * scale))
        piece = atlas.crop(box).resize((700, 700), Image.LANCZOS) if i < 2 else atlas.resize((700, 700), Image.LANCZOS)
        crop.paste(piece, (i * 700, 60))
        draw.text((i * 700 + 14, 16), text, fill=(240, 232, 214), font=ImageFont.truetype("arial.ttf", 26))
    crop.save(preview_dir / "guardian-atlas-before-after.png")


# ------------------------------------------------------------------ установка в проект

def install(texture):
    """Атлас и .meta рядом со старым, материал — на новый GUID. Старый jpg и его .meta не трогаются.

    .meta пишется ПЕРВЫМ: если открытый редактор успеет увидеть картинку без неё, он выдаст свой
    случайный GUID, и ссылка материала уйдёт в пустоту. Настройки импорта — копия исходного атласа.
    """
    import shutil
    # Байтами, а не текстом: концы строк в .meta/.mat остаются как были, дифф — только GUID.
    old, new = SOURCE_GUID.encode(), OUTPUT_GUID.encode()
    source_meta = (CHARACTERS / (MOBS[TARGET][1] + ".meta")).read_bytes()
    assert old in source_meta, "GUID исходного атласа сменился — проверь SOURCE_GUID"
    Path(str(OUTPUT_TEXTURE) + ".meta").write_bytes(source_meta.replace(old, new))
    shutil.copyfile(texture, OUTPUT_TEXTURE)
    material = MATERIAL.read_bytes()
    if old in material:
        MATERIAL.write_bytes(material.replace(old, new))
    print(f"установлено: {OUTPUT_TEXTURE} (guid {OUTPUT_GUID}), материал {MATERIAL.name}")


# ------------------------------------------------------------------ main

def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--work", default=str(REPO / "artifacts" / "tools" / "guardian-colour"))
    parser.add_argument("--preview-dir", default=str(REPO / "ART" / "characters" / "act-1-enemies" /
                                                     "2.forest-guardian" / "recolour-2026-10-01"))
    parser.add_argument("--size", type=int, default=0, help="размер выходного атласа (0 — как у исходника)")
    parser.add_argument("--install", action="store_true",
                        help="положить атлас в Resources рядом со старым и перевести на него материал")
    parser.add_argument("--no-preview", action="store_true")
    args = parser.parse_args()
    work = Path(args.work)
    work.mkdir(parents=True, exist_ok=True)

    labs = {name: sample(name, work) for name in MOBS}
    curves = fit(labs[TARGET], labs[REFERENCE])

    texture = work / OUTPUT_TEXTURE.name
    recolour_image(CHARACTERS / MOBS[TARGET][1], curves, args.size or None).save(texture, optimize=True)
    after = sample(TARGET, work, path=texture)

    stats = {name: describe(lab, accent_out=name == REFERENCE) for name, lab in labs.items()}
    stats["guardian_before"] = stats.pop(TARGET)
    stats["guardian_after"] = describe(after)
    stats["settings"] = SETTINGS
    stats["curves"] = {family: {"chroma_ratio": c["chroma_ratio"].tolist(), "hue_shift": c["hue_shift"].tolist(),
                                "hue_spread_x": c["squeeze"], "L_offset": c["offset"], "ramp_source_C_h": c["ramp_source"],
                                "ramp_reference_C_h": c["ramp_reference"]}
                       for family, c in curves.items() if "squeeze" in c}
    (work / "palette-report.json").write_text(json.dumps(stats, ensure_ascii=False, indent=2), encoding="utf-8")

    for name in ("guardian_before", "guardian_after", REFERENCE):
        s = stats[name]
        print(f"{name:16s} всё L {s['all']['L_p10']:.3f}/{s['all']['L_p50']:.3f}/{s['all']['L_p90']:.3f} C {s['all']['C_p50']:.3f}"
              f" | кора {s['bark']['share']*100:4.1f}% L {s['bark']['L_p50']:.3f} C {s['bark']['C_p50']:.3f} h {s['bark']['hue_mean']:.1f}±{s['bark']['hue_spread']:.1f}"
              f" | листва {s['leaf']['share']*100:4.1f}% L {s['leaf']['L_p50']:.3f} C {s['leaf']['C_p50']:.3f} h {s['leaf']['hue_mean']:.1f}±{s['leaf']['hue_spread']:.1f}"
              f" | нейтр {s['neutral']['share']*100:4.1f}% L {s['neutral']['L_p50']:.3f}")
    print(f"атлас: {texture}")

    if args.install:
        install(texture)

    if not args.no_preview and BLENDER.exists():
        stats["_lab"] = {"guardian_before": labs[TARGET], "guardian_after": after, "snarer": labs[REFERENCE]}
        preview(work, Path(args.preview_dir), texture, stats)
        stats.pop("_lab")
        for name, r in stats["readability"].items():
            print(f"читаемость {name:22s} ΔE до земли (медиана) {r['deltaE_median']:.3f} · сливается {r['blend_share_dE_lt_0.06']*100:4.1f}%"
                  f" · L тела {r['body_L_p50']:.3f} / земли {r['ground_L']:.3f}")
        (work / "palette-report.json").write_text(json.dumps(stats, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"превью: {args.preview_dir}")


if __name__ == "__main__":
    sys.exit(main())
