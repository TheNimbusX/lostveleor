# Writes manifest.json and prompts.md for final/ from prompts.json, try1/prompts.json and the job ids below.
import json, os

ROOT = os.path.dirname(os.path.abspath(__file__))
P = json.load(open(os.path.join(ROOT, "prompts.json")))["jobs"]
T1 = {j["name"]: j for j in json.load(open(os.path.join(ROOT, "try1", "prompts.json")))["jobs"]}
REFS = json.load(open(os.path.join(ROOT, "uploaded_refs.json")))

JOBS = {
    "Wreck-base-v1": "3a2f2586-1e28-4f66-8b3d-d986581ce293",
    "Wreck-base-v2": "68c42612-451e-4d61-8456-68bc4312888e",
    "Breakwater-v1": "b5cc2e2f-675c-479e-8302-e9566459c305",
    "Breakwater-v2": "45d60112-0965-4956-9488-24d0b3f397a6",
    "Breakwater-v3": "5b5180c7-d0ff-487d-8613-2c18efd481ae",
    "NinthWave-v1": "7f21a2e2-fa75-4cd3-87a3-7e9a5d8622cd",
    "NinthWave-v2": "4e50ec18-12b5-40e2-97e2-ad14dbb37716",
    "WaterShell-v1": "176754aa-8821-462b-b7c3-f1853df8ab3a",
    "WaterShell-v2": "555408cb-163c-452a-ab2b-f096fb093be9",
    "WaterShell-v3": "491538d1-3e16-4e7d-84a8-1f09720bd1df",
    "base-icons-v1": "e455c687-8aa4-4af4-9ffc-d1357b57e5b8",
    "base-icons-v2": "9b9f8a28-717f-4601-9eaa-2857449a5f25",
    "forms-icons-v1": "55096ee3-a243-4cd0-93ef-5fc4a7fb9582",
    "forms-icons-v2": "3eadceae-3b43-467c-b7be-11e17ec9107f",
}
TRY1 = {
    "Wreck-base-v1": "23e63eae-cdce-483c-acea-1db5707d1158",
    "Wreck-base-v2": "edcc4f0d-921c-48dd-ba02-cb521428530c",
}
COST = {("1k", "medium"): 0.5, ("2k", "medium"): 1.0}


def job_entry(j, job_id, file, refs_prefix=""):
    return {"name": j["name"], "job_id": job_id, "file": file, "model": "gpt_image_2_5", "quality": j["quality"],
            "resolution": j["resolution"], "aspect_ratio": j["aspect_ratio"], "refs": j["refs"],
            "credits": COST[(j["resolution"], j["quality"])], "status": "completed"}


jobs = [job_entry(j, JOBS[j["name"]], j["file"]) for j in P if j["name"] in JOBS]
try1 = [job_entry(T1[n], TRY1[n], "try1/frames/%s.png" % n) for n in TRY1]
for t in try1:
    t["refs"] = ["try1/" + t["refs"][0]] + t["refs"][1:]
spent = sum(j["credits"] for j in jobs + try1)

manifest = {
    "date": "2026-10-06",
    "task": "Wreck final look: owner chose 'C + earth from A' (iron, chain-link imprints, steel shavings and shards, plus A's crater with clods and torn turf); game-scale frames, forms, icons.",
    "credits": {"before": 45.3, "after": 35.3, "spent": spent, "budget": 15,
                "note": "2 jobs (Breakwater-v2, NinthWave-v2) were first rejected with 429 rate_limit_reached, no job, no charge, resubmitted."},
    "family_accent": {"proposal": "cold steel blue-white over dark iron", "highlight": "#9CC8F0", "glint": "#FFFFFF",
                      "iron": "#23272E-#3A4048 (dark iron/charcoal)",
                      "forms": {"Breakwater": "#1FB37E", "NinthWave": "#4B3FD0", "WaterShell": "#E4EEF6"}},
    "scale": {
        "source": "razlom/Assets/Game.Sim/Core/Simulation.Wreck.Numbers.cs + .Forms.cs, Game.View/CameraFollow.cs (ortho size 6.2, pitch 48 -> 87 px/m at 1920x1080)",
        "base": "slam circle R 1.2 m at 2.2 m in front; lane from the impact to 6 m from the hero, 1.5 m wide",
        "Breakwater": "wall from 2.2 m to 8 m, 2 m wide", "NinthWave": "reach up to 2.6 m, R up to 1.8 m, lane up to 3 m wide",
        "WaterShell": "burst R 2.5 m around the hero, no ground ring",
        "guide": "guides/guide-*.jpg = r1_scene cropped to 16:9 with the zones painted in magenta (make_guides.py); ruler = hero height (142 px ~ 95 px/m on r1); the crop is ~1.2-1.3x the live zoom, ratios to the hero are the game ones.",
        "check": "frames/zones/*-zones.jpg = each frame with its game zone outlined (make_board.py).",
    },
    "uploaded_refs": REFS,
    "jobs": jobs,
    "rejected_try1": {
        "jobs": try1,
        "why": "padded guide with thin outlines + whole C/A frames as refs: the model recomposed the scene and drew lanes ~2x the game again (v1 ~13 m from the hero). Try 2 = 'edit the screenshot, paint only inside the magenta zones' + close-up swatches of C/A instead of whole frames.",
    },
    "review_notes": [
        "Wreck-base-v2 fits its zone almost exactly; Wreck-base-v1 runs ~1.5-2 m past the lane end and its crater rim came out as a ring of standing stakes.",
        "The model moved Pelag ~0.5 m back in most frames; zones overlays show the real fit.",
        "Breakwater v1/v2: sea-green accent too faint; v3 (retry with a stronger accent line) reads as the form at a glance.",
        "WaterShell-v1 reads like a ring lying on the ground; v3 (retry) shows a floating cage of plates around him. WaterShell-v2 = the burst, plates stop near 2.5 m.",
        "Icons: base-icons-v1 Abordage is the clearest Pelag (front view, light shirt, red sash); v2 Abordage is a cropped back view.",
        "Forms icons: base steel blue-white and Water Shell pearl are close at 64 px (compare-forms.jpg). Either push the base accent to a more saturated steel blue or give the shell a lilac-pink nacre sheen.",
    ],
    "derived": ["guides/", "refs/ (swatches, jpg copies of refs)", "frames/zones/", "board-frames.jpg",
                "icons/base-icons-v*-{Wreck,AnchorThrow,Abordage}-512.png", "icons/forms-icons-v*-{Wreck,Breakwater,NinthWave,WaterShell}-512.png",
                "icons/compare-base.jpg", "icons/compare-forms.jpg"],
    "scripts": ["make_guides.py", "build_prompts.py", "upload_refs.py", "fetch.py", "make_icons.py", "make_board.py", "write_docs.py"],
}
json.dump(manifest, open(os.path.join(ROOT, "manifest.json"), "w"), indent=1)

lines = ["# Wreck final look 2026-10-06: exact prompts", "",
         "Model gpt_image_2_5 (Flare), role image_references, every prompt ONE line, ASCII only (higgsfield-prompt-gotchas).",
         "Frames: medium 1k 16:9 (0.5 cr). Icon sheets: medium 2k (1.0 cr). Source of truth: prompts.json (build_prompts.py).",
         "Frame refs in order: 1 guide (guides/), 2 swatch_C-iron-lane, 3 swatch_A-earth-crater, 4 r2_model, 5 r3_anchor.", ""]
for j in jobs:
    lines += ["## %s  (job %s)" % (j["file"], j["job_id"]), "", "refs: " + ", ".join(j["refs"]), "", "```",
              next(p["prompt"] for p in P if p["name"] == j["name"]), "```", ""]
lines += ["## Rejected try 1 (try1/)", "", manifest["rejected_try1"]["why"], ""]
for t in try1:
    lines += ["### %s  (job %s)" % (t["file"], t["job_id"]), "", "refs: " + ", ".join(t["refs"]), "", "```",
              T1[t["name"]]["prompt"], "```", ""]
open(os.path.join(ROOT, "prompts.md"), "w", encoding="utf-8").write("\n".join(lines))
print("spent", spent, "jobs", len(jobs), "try1", len(try1))
