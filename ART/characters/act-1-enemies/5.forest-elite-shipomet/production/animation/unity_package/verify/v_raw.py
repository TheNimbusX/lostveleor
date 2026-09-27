"""Check 1 (raw): parse the binary FBX itself -> time mode, anim stacks and their frame ranges, key counts.

blender -b -P v_raw.py
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from io_scene_fbx import parse_fbx  # noqa: E402

import vcommon as vc  # noqa: E402

KT = 46186158000  # FBX ticks per second
root, version = parse_fbx.parse(str(vc.FBX))


def kids(e, name):
    return [c for c in e.elems if c.id == name.encode()]


def props70(e):
    out = {}
    for p in kids(e, "Properties70"):
        for q in p.elems:
            k = q.props[0].decode() if isinstance(q.props[0], bytes) else q.props[0]
            out[k] = q.props[4:] if len(q.props) > 4 else ()
    return out


res = {"fbx_version": version}
gs = next(e for e in root.elems if e.id == b"GlobalSettings")
p = props70(gs)
res["TimeMode"] = p.get("TimeMode")
res["CustomFrameRate"] = p.get("CustomFrameRate")
objs = next(e for e in root.elems if e.id == b"Objects")
stacks = {}
for e in objs.elems:
    if e.id == b"AnimationStack":
        name = e.props[1].split(b"\x00")[0].decode()
        pp = props70(e)
        ls, le = pp.get("LocalStart", (0,))[0], pp.get("LocalStop", (0,))[0]
        stacks[name] = {"local_frames": [ls * 30 / KT, le * 30 / KT],
                        "reference_frames": [x * 30 / KT for x in (pp.get("ReferenceStart", (0,))[0], pp.get("ReferenceStop", (0,))[0])]}
res["stacks"] = stacks
# key counts / key time grid of every curve
nkeys, offgrid = {}, 0
for e in objs.elems:
    if e.id == b"AnimationCurve":
        t = next(c for c in e.elems if c.id == b"KeyTime").props[0]
        fr = [x * 30 / KT for x in t]
        offgrid += sum(1 for f in fr if abs(f - round(f)) > 1e-3)
        nkeys[len(fr)] = nkeys.get(len(fr), 0) + 1
res["curve_key_count_histogram"] = nkeys
res["keys_off_integer_frames"] = offgrid
takes = [e for e in root.elems if e.id == b"Takes"]
res["takes_section"] = []
for t in takes:
    for c in t.elems:
        if c.id == b"Take":
            lt = next((x for x in c.elems if x.id == b"LocalTime"), None)
            res["takes_section"].append({"name": c.props[0].decode(),
                                         "local_frames": [v * 30 / KT for v in lt.props] if lt else None})
vc.dump("raw.json", res)
