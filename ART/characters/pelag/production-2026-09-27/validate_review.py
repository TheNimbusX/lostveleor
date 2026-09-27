"""Проверяет полноту листа и локальных материалов; браузер не запускает."""
from pathlib import Path
import json
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    design = load(ROOT / "design/ability-passports.json")
    audit = load(ROOT / "design/production-audit.json")
    skills = design["skills"]
    assert len(skills) == 10
    assert [s["poolIndex"] for s in skills] == list(range(10))
    assert all(len(s["talents"]) == 8 for s in skills)
    talents = [t for s in skills for t in s["talents"]]
    ids = [t["id"] for t in talents]
    assert len(set(ids)) == 80
    old_ids = {t["key"] for t in audit["talent_capacity"]["identifier_slots"] if t["status"] == "runtime_existing"}
    assert len(old_ids) == 64
    assert {t["id"] for s in skills[:8] for t in s["talents"]} == old_ids
    assert len(set(ids) - old_ids) == 16
    for skill in skills:
        for key in ("name", "role", "currentFacts", "proposedBaseline", "phaseContract", "movementContract", "cancelContract", "productionNeeds"):
            assert skill.get(key), (skill["name"], key)
        assert [t["index"] for t in skill["talents"]] == list(range(8))
        for talent in skill["talents"]:
            for key in ("proposedName", "proposedEffect", "approval", "visualRequirement", "interactions"):
                assert talent.get(key), (talent["id"], key)
    page_path = ROOT / "review/index.html"
    page = page_path.read_text(encoding="utf-8")
    assert "__REVIEW_DATA__" not in page
    data_text = re.search(r'<script id="review-data" type="application/json">(.*?)</script>', page, re.S)[1]
    embedded = json.loads(data_text)
    assert embedded["design"] == design, "Доску нужно пересобрать после изменения листа"
    brief = load(ROOT / "design/short-review.json")
    assert embedded["brief"] == brief
    assert len(brief["battleBasics"]) == 6
    assert len(brief["skills"]) == 10
    assert all(len(s["talents"]) == 8 for s in brief["skills"])
    assert all(t["name"] and t["effect"] for s in brief["skills"] for t in s["talents"])
    model = embedded["model"]
    paths = [x["path"] for key in ("images", "videos", "downloads") for x in model.get(key, [])]
    paths += [x["path"] for x in model.get("posePreflight", {}).get("images", [])]
    paths += [x["path"] for x in embedded.get("concepts", {}).get("images", [])]
    for path in paths:
        assert (page_path.parent / path).resolve().is_file(), path
    out = ROOT / "validation"
    out.mkdir(exist_ok=True)
    javascript = re.findall(r'<script>(.*?)</script>', page, re.S)
    assert len(javascript) == 1
    js_path = out / "review-script.js"
    js_path.write_text(javascript[0], encoding="utf-8")
    node = shutil.which("node")
    result = subprocess.run([node, "--check", str(js_path)], capture_output=True, text=True, encoding="utf-8") if node else None
    if result:
        assert result.returncode == 0, result.stderr
    report = {
        "skills": 10, "talents": 80, "existingIdsPreserved": 64, "newProposedIds": 16,
        "requiredFieldsPresent": True, "embeddedDataMatchesSources": True,
        "shortReview": {"basics": 6, "skills": 10, "talents": 80}, "modelManifestPresent": bool(model), "localMediaAndDownloadsChecked": len(paths),
        "javascriptSyntax": "passed" if result else "not_run_node_missing",
        "browserVisualCheck": "not_run_browser_URL_policy_blocked_file_access",
        "runtimeChanged": False, "ownerAcceptance": "pending",
    }
    (out / "review-validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
