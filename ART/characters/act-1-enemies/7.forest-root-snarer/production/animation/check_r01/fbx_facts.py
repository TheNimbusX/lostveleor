"""Facts straight from the FBX bytes (what Unity's importer reads), no Blender involved.
python fbx_facts.py <fbx> <out.json>"""
import json, sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from fbx_bin import KTIME, parse, props70  # noqa: E402


def cls_name(n):
    s = n.props[1]
    nm, _, cl = s.partition("\x00\x01")
    return nm, cl


def main(fbx, out):
    root = parse(fbx)
    R = {"fbx_version": root.props[0]}
    gs = props70(root.find("GlobalSettings"))
    R["time_mode"] = gs.get("TimeMode")
    R["custom_frame_rate"] = gs.get("CustomFrameRate")
    R["unit_scale"] = gs.get("UnitScaleFactor")
    R["axes"] = {k: gs.get(k) for k in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign")}
    objs = {n.props[0]: n for n in root.find("Objects").children}
    par = defaultdict(list)   # child -> [(parent, prop)]
    kids = defaultdict(list)  # parent -> [(child, prop)]
    for c in root.find("Connections").children:
        ch, pa = c.props[1], c.props[2]
        pr = c.props[3] if len(c.props) > 3 else None
        par[ch].append((pa, pr))
        kids[pa].append((ch, pr))
    models = {i: cls_name(n)[0] for i, n in objs.items() if n.name == "Model"}
    mtype = {i: n.props[2] for i, n in objs.items() if n.name == "Model"}
    R["models"] = {models[i]: mtype[i] for i in models}
    fps = 30.0
    stacks = {}
    for sid, n in objs.items():
        if n.name != "AnimationStack":
            continue
        nm = cls_name(n)[0]
        p = props70(n)
        ls, le = p.get("LocalStart", [0])[0], p.get("LocalStop", [0])[0]
        info = {"local_start_frame": round(ls * fps / KTIME, 4), "local_stop_frame": round(le * fps / KTIME, 4)}
        layers = [c for c, _ in kids[sid] if objs.get(c) is not None and objs[c].name == "AnimationLayer"]
        tmin, tmax, nkeys, offgrid = None, None, 0, 0
        scale_rng = [1e9, -1e9]
        per_model = defaultdict(lambda: defaultdict(dict))
        for L in layers:
            for cn, _ in kids[L]:
                if objs.get(cn) is None or objs[cn].name != "AnimationCurveNode":
                    continue
                tgt = [(pa, pr) for pa, pr in par[cn] if pa in models]
                if not tgt:
                    continue
                model, chan = models[tgt[0][0]], tgt[0][1]
                for cv, axis in kids[cn]:
                    node = objs.get(cv)
                    if node is None or node.name != "AnimationCurve":
                        continue
                    kt = node.find("KeyTime").props[0]
                    kv = node.find("KeyValueFloat").props[0]
                    fr = [t * fps / KTIME for t in kt]
                    nkeys += len(fr)
                    offgrid += sum(1 for f in fr if abs(f - round(f)) > 1e-3)
                    tmin = min(fr) if tmin is None else min(tmin, min(fr))
                    tmax = max(fr) if tmax is None else max(tmax, max(fr))
                    if chan == "Lcl Scaling" and mtype[tgt[0][0]] == "LimbNode":
                        scale_rng = [min(scale_rng[0], min(kv)), max(scale_rng[1], max(kv))]
                    per_model[model][chan][axis] = (min(kv), max(kv), kv[0], kv[-1])
        info.update({"key_first_frame": round(tmin, 4) if tmin is not None else None,
                     "key_last_frame": round(tmax, 4) if tmax is not None else None,
                     "keys": nkeys, "keys_off_integer_frames": offgrid,
                     "bone_scale_key_range": [round(v, 6) for v in scale_rng],
                     "animated_models": len(per_model)})
        for m in ("root", "ARM_ForestRootSnarer"):
            if m in per_model:
                info[m + "_channels"] = {ch: {ax: [round(x, 6) for x in v] for ax, v in d.items()}
                                         for ch, d in per_model[m].items()}
        stacks[nm] = info
    R["anim_stacks"] = stacks
    # geometry: triangles, per-vertex influences from skin clusters
    geos = [(i, n) for i, n in objs.items() if n.name == "Geometry" and n.props[2] == "Mesh"]
    R["geometries"] = []
    for gid, g in geos:
        pvi = g.find("PolygonVertexIndex").props[0]
        nverts = len(g.find("Vertices").props[0]) // 3
        tris, cnt = 0, 0
        for v in pvi:
            cnt += 1
            if v < 0:
                tris += cnt - 2
                cnt = 0
        infl = [0] * nverts
        wsum = [0.0] * nverts
        skins = [c for c, _ in kids[gid] if objs.get(c) is not None and objs[c].name == "Deformer"]
        nclusters = 0
        for sk in skins:
            for cl, _ in kids[sk]:
                cn = objs.get(cl)
                if cn is None or cn.name != "Deformer":
                    continue
                idx = cn.find("Indexes")
                if idx is None:
                    continue
                nclusters += 1
                for i, w in zip(idx.props[0], cn.find("Weights").props[0]):
                    if w > 0:
                        infl[i] += 1
                        wsum[i] += w
        hist = defaultdict(int)
        for k in infl:
            hist[k] += 1
        R["geometries"].append({"name": cls_name(g)[0], "vertices": nverts, "triangles": tris,
                                "clusters": nclusters, "max_influences": max(infl),
                                "influence_histogram": dict(sorted(hist.items())),
                                "weight_sum_min": round(min(wsum), 5), "weight_sum_max": round(max(wsum), 5)})
    Path(out).write_text(json.dumps(R, indent=1))
    print(json.dumps({k: v for k, v in R.items() if k != "models"}, indent=1)[:6000])


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
