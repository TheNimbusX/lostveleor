# Download finished jobs: python fetch.py <name> <url> [<name> <url> ...]; files go where prompts.json says.
import json, os, subprocess, sys

ROOT = os.path.dirname(os.path.abspath(__file__))
jobs = {j["name"]: j for j in json.load(open(os.path.join(ROOT, "prompts.json")))["jobs"]}
args = sys.argv[1:]
for name, url in zip(args[::2], args[1::2]):
    out = os.path.join(ROOT, jobs[name]["file"])
    os.makedirs(os.path.dirname(out), exist_ok=True)
    tmp = out + ".dl"
    r = subprocess.run(["curl", "-s", "-L", "-o", tmp, "-w", "%{http_code}", url], capture_output=True, text=True)
    from PIL import Image
    Image.open(tmp).save(out)
    os.remove(tmp)
    print(name, r.stdout, out, Image.open(out).size)
