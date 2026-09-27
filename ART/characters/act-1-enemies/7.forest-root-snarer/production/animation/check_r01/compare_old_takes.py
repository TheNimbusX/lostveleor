"""The r01 takes must survive a package extension unchanged. Byte-level comparison (no Blender) of
every animation curve (key times + key values) of the old takes, and of the mesh / skin / bind data,
between the approved r01 FBX and the extended FBX.
python compare_old_takes.py <old.fbx> <new.fbx> <out.json>"""
import json, sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from fbx_bin import parse, props70  # noqa: E402


def cls_name(n):
    return n.props[1].partition("\x00\x01")[0]


def curves(path):
    root = parse(path)
    objs = {n.props[0]: n for n in root.find("Objects").children}
    par, kids = defaultdict(list), defaultdict(list)
    for c in root.find("Connections").children:
        ch, pa = c.props[1], c.props[2]
        pr = c.props[3] if len(c.props) > 3 else None
        par[ch].append((pa, pr))
        kids[pa].append((ch, pr))
    models = {i: cls_name(n) for i, n in objs.items() if n.name == "Model"}
    out = {}
    for sid, n in objs.items():
        if n.name != "AnimationStack":
            continue
        p = props70(n)
        take = {"_range": [p.get("LocalStart", [0])[0], p.get("LocalStop", [0])[0]]}
        for L, _ in kids[sid]:
            if objs.get(L) is None or objs[L].name != "AnimationLayer":
                continue
            for cn, _ in kids[L]:
                if objs.get(cn) is None or objs[cn].name != "AnimationCurveNode":
                    continue
                tgt = [(pa, pr) for pa, pr in par[cn] if pa in models]
                if not tgt:
                    continue
                for cv, axis in kids[cn]:
                    node = objs.get(cv)
                    if node is None or node.name != "AnimationCurve":
                        continue
                    key = f"{models[tgt[0][0]]}|{tgt[0][1]}|{axis}"
                    take[key] = (node.find("KeyTime").props[0], node.find("KeyValueFloat").props[0])
        out[cls_name(n)] = take
    static = {}
    for i, n in objs.items():
        if n.name == "Geometry":
            for f in ("Vertices", "PolygonVertexIndex"):
                static[f"geo|{f}"] = n.find(f).props[0]
        if n.name == "Deformer" and n.find("Indexes") is not None:
            nm = cls_name(n)
            static[f"cluster|{nm}|Indexes"] = n.find("Indexes").props[0]
            static[f"cluster|{nm}|Weights"] = n.find("Weights").props[0]
            static[f"cluster|{nm}|Transform"] = n.find("Transform").props[0]
            static[f"cluster|{nm}|TransformLink"] = n.find("TransformLink").props[0]
        if n.name == "Pose":
            for pn in n.all("PoseNode"):
                static[f"bind|{pn.find('Node').props[0] and models.get(pn.find('Node').props[0], '?')}"] = pn.find("Matrix").props[0]
    return out, static


def main(old, new, dst):
    a, sa = curves(old)
    b, sb = curves(new)
    rep = {"old_fbx": str(old), "new_fbx": str(new), "old_takes": sorted(a), "new_takes": sorted(b),
           "added_takes": sorted(set(b) - set(a)), "takes": {}}
    ok = True
    for take, ca in a.items():
        cb = b.get(take)
        if cb is None:
            rep["takes"][take] = {"missing_in_new": True}
            ok = False
            continue
        diff_keys = [k for k in ca if k not in cb or ca[k] != cb[k]]
        extra = [k for k in cb if k not in ca]
        maxdv = 0.0
        for k in diff_keys:
            if k in cb and len(ca[k][1]) == len(cb[k][1]) and not isinstance(ca[k][1], float):
                maxdv = max([maxdv] + [abs(x - y) for x, y in zip(ca[k][1], cb[k][1])])
        rep["takes"][take] = {"curves": len(ca) - 1, "keys": sum(len(v[0]) for k, v in ca.items() if k != "_range"),
                              "identical_curves": len(ca) - 1 - len([k for k in diff_keys if k != "_range"]),
                              "range_identical": ca["_range"] == cb["_range"],
                              "differing": diff_keys[:10], "extra_curves_in_new": extra[:10],
                              "max_value_diff": maxdv}
        ok &= not diff_keys and not extra
    sdiff = sorted(k for k in set(sa) | set(sb) if sa.get(k) != sb.get(k))
    rep["mesh_skin_bind_identical"] = not sdiff
    rep["mesh_skin_bind_differing"] = sdiff[:10]
    rep["static_items_compared"] = len(sa)
    rep["all_old_takes_bit_identical"] = bool(ok)
    Path(dst).write_text(json.dumps(rep, indent=1))
    print(json.dumps({k: v for k, v in rep.items() if k != "takes"}, indent=1))
    print(json.dumps({t: {k: v for k, v in d.items() if k in ("curves", "keys", "identical_curves", "range_identical")}
                      for t, d in rep["takes"].items()}))


if __name__ == "__main__":
    main(*sys.argv[1:4])
