# PUT the reference files to the presigned Higgsfield URLs with curl (urllib gets 403 / Cloudflare 1010).
import json, os, subprocess, sys

ROOT = os.path.dirname(os.path.abspath(__file__))
resp = json.load(open(sys.argv[1], encoding="utf-8"))
names = sys.argv[2].split(",")
local = {}
for n in names:
    for sub in ("guides", "refs", "icons", "frames"):
        p = os.path.join(ROOT, sub, n)
        if os.path.exists(p):
            local[n] = p
            break

ids = {}
for n, u in zip(names, resp["uploads"]):
    assert u["upload_url"].split("?")[0].endswith(u["media_id"] + os.path.splitext(n)[1]), (n, u["upload_url"][:120])
    path = local[n]
    r = subprocess.run(["curl", "-s", "-o", os.devnull, "-w", "%{http_code}", "-X", "PUT",
                        "-H", "Content-Type: " + u["content_type"], "-H", "If-None-Match: *",
                        "--data-binary", "@" + path, u["upload_url"]], capture_output=True, text=True)
    print(n, r.stdout, u["media_id"])
    ids[n] = {"media_id": u["media_id"], "local": os.path.relpath(path, ROOT).replace("\\", "/"), "http": r.stdout}

out = os.path.join(ROOT, "uploaded_refs.json")
prev = json.load(open(out)) if os.path.exists(out) else {}
prev.update(ids)
json.dump(prev, open(out, "w"), indent=1)
