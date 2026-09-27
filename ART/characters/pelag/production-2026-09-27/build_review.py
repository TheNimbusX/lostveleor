"""Пересобирает локальную доску приёмки из паспортов и реальных файлов модели."""
from pathlib import Path
import json
import html

ROOT = Path(__file__).resolve().parent


def read_json(path, fallback):
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else fallback


def main():
    design = read_json(ROOT / "design/ability-passports.json", {})
    audit = read_json(ROOT / "design/production-audit.json", {})
    model = read_json(ROOT / "review/model-manifest.json", {})
    brief = read_json(ROOT / "design/short-review.json", {})
    concepts = read_json(ROOT / "review/concepts-manifest.json", {})
    data = {"design": design, "audit": audit, "model": model, "brief": brief, "concepts": concepts}
    template = (ROOT / "review-template.html").read_text(encoding="utf-8")
    payload = json.dumps(data, ensure_ascii=False).replace("<", "\\u003c")
    page = template.replace("__REVIEW_DATA__", payload)
    review = ROOT / "review"
    review.mkdir(exist_ok=True)
    (review / "index.html").write_text(page, encoding="utf-8")
    print(json.dumps({"page": str(review / "index.html"), "skills": len(design.get("skills", [])), "hasModelManifest": bool(model)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
