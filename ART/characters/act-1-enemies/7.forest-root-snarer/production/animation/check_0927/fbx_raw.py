"""27.09 independent check, part A (system python, no Blender): what Unity reads from the FBX bytes.
Own binary FBX reader (written for this check, does not reuse check_r01/fbx_bin.py).
python fbx_raw.py <new.fbx> <approved_r01.fbx> <export.json> <out.json>"""
import json, struct, sys, zlib
from collections import defaultdict
from pathlib import Path

TICKS = 46186158000


def read(path):
    b = Path(path).read_bytes()
    assert b[:21] == b"Kaydara FBX Binary  \x00", "not binary fbx"
    ver = struct.unpack_from("<I", b, 23)[0]
    hdr, fmt = (25, "<QQQ") if ver >= 7500 else (13, "<III")

    def prop(p):
        t = b[p:p + 1].decode(); p += 1
        simple = {"Y": "<h", "C": "<?", "I": "<i", "F": "<f", "D": "<d", "L": "<q"}
        if t in simple:
            v = struct.unpack_from(simple[t], b, p)[0]
            return v, p + struct.calcsize(simple[t])
        if t in "fdlib":
            n, enc, ln = struct.unpack_from("<III", b, p); p += 12
            raw = b[p:p + ln]; p += ln
            if enc:
                raw = zlib.decompress(raw)
            code = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "?"}[t]
            return tuple(struct.unpack("<%d%s" % (n, code), raw)), p
        if t in "SR":
            n = struct.unpack_from("<I", b, p)[0]; p += 4
            v = b[p:p + n]
            return (v.decode("utf-8", "replace") if t == "S" else v), p + n
        raise ValueError(t)

    def node(p):
        end, np_, _ = struct.unpack_from(fmt, b, p)
        nl = b[p + (hdr - 1)]
        if end == 0:
            return None, p + hdr
        q = p + hdr
        name = b[q:q + nl].decode(); q += nl
        props = []
        for _ in range(np_):
            v, q = prop(q)
            props.append(v)
        kids = []
        while q < end - hdr or (q < end and any(b[q:end])):
            c, q = node(q)
            if c is None:
                break
            kids.append(c)
        return (name, props, kids), end

    out, p = [], 27
    while p < len(b) - 200:
        c, p = node(p)
        if c is None:
            break
        out.append(c)
    return ver, out


def child(n, name):
    return next((c for c in n[2] if c[0] == name), None)


def p70(n):
    blk = child(n, "Properties70")
    return {c[1][0]: c[1][4:] for c in (blk[2] if blk else [])}


def nm(n):
    return n[1][1].split("\x00\x01")[0]


def model(path):
    ver, top = read(path)
    T = {n[0]: n for n in top}
    gs = p70(T["GlobalSettings"])
    objs = {n[1][0]: n for n in T["Objects"][2]}
    up, down = defaultdict(list), defaultdict(list)
    for c in T["Connections"][2]:
        pr = c[1][3] if len(c[1]) > 3 else None
        up[c[1][1]].append((c[1][2], pr))
        down[c[1][2]].append((c[1][1], pr))
    models = {i: (nm(n), n[1][2]) for i, n in objs.items() if n[0] == "Model"}
    stacks = {}
    for sid, n in objs.items():
        if n[0] != "AnimationStack":
            continue
        pp = p70(n)
        curves = {}
        for lid, _ in down[sid]:
            if objs.get(lid, ("",))[0] != "AnimationLayer":
                continue
            for cnid, _ in down[lid]:
                cn = objs.get(cnid)
                if cn is None or cn[0] != "AnimationCurveNode":
                    continue
                tg = [(m, pr) for m, pr in up[cnid] if m in models]
                for cid, ax in down[cnid]:
                    cv = objs.get(cid)
                    if cv is None or cv[0] != "AnimationCurve":
                        continue
                    key = (models[tg[0][0]][0], tg[0][1], ax)
                    curves[key] = (child(cv, "KeyTime")[1][0], child(cv, "KeyValueFloat")[1][0])
        stacks[nm(n)] = {"range_ticks": (pp.get("LocalStart", [0])[0], pp.get("LocalStop", [0])[0]), "curves": curves}
    geo = [n for n in objs.values() if n[0] == "Geometry" and n[1][2] == "Mesh"]
    clusters = {nm(n): n for n in objs.values() if n[0] == "Deformer" and n[1][2] == "Cluster"}
    poses = [n for n in objs.values() if n[0] == "Pose"]
    return {"ver": ver, "gs": gs, "models": models, "stacks": stacks, "geo": geo, "clusters": clusters,
            "poses": poses}


def main(new, old, export_json, dst):
    E = json.loads(Path(export_json).read_text())
    A, B = model(new), model(old)
    R = {"fbx": str(new), "baseline": str(old), "fbx_version": A["ver"]}
    # 1. fps + takes + ranges
    R["time_mode"] = A["gs"].get("TimeMode", [None])[0]
    R["custom_frame_rate"] = A["gs"].get("CustomFrameRate", [None])[0]
    R["fps_ok"] = R["time_mode"] == 6  # eFrames30
    want = E["fbx_anim_stacks"]
    R["stacks"] = sorted(A["stacks"])
    R["stacks_exact"] = sorted(want) == sorted(A["stacks"])
    rng, offgrid, ok_rng, armature_curves = {}, {}, True, {}
    arm_models = {n for n, (name, kind) in A["models"].items() if kind == "Null"}
    arm_names = {A["models"][i][0] for i in arm_models}
    for take, s in A["stacks"].items():
        f0, f1 = (t * 30 / TICKS for t in s["range_ticks"])
        rng[take] = [round(f0, 4), round(f1, 4)]
        exp = E["takes"].get(take, {}).get("frames")
        ok_rng &= exp is not None and abs(f0 - exp[0]) < 1e-6 and abs(f1 - exp[1]) < 1e-6
        off, kmin, kmax = 0, 1e9, -1e9
        for (mname, prop, ax), (kt, kv) in s["curves"].items():
            for t in kt:
                f = t * 30 / TICKS
                off += abs(f - round(f)) > 1e-6
                kmin, kmax = min(kmin, f), max(kmax, f)
        offgrid[take] = {"keys_off_integer_frames": off, "key_span": [round(kmin, 4), round(kmax, 4)]}
        # animated armature Null node would be baked root motion on the Unity root
        moving = {}
        for (mname, prop, ax), (kt, kv) in s["curves"].items():
            if mname in arm_names and max(kv) - min(kv) > 1e-6:
                moving[f"{prop}.{ax}"] = [min(kv), max(kv)]
        armature_curves[take] = moving
    R["stack_frame_ranges"] = rng
    R["ranges_match_export_json"] = bool(ok_rng)
    R["keys"] = offgrid
    R["armature_null_animated_channels"] = armature_curves
    # root bone translation curves (Unity root for a Generic rig with no root node set)
    root_t = {}
    for take, s in A["stacks"].items():
        c = {ax: kv for (m, prop, ax), (kt, kv) in s["curves"].items() if m == "root" and prop == "Lcl Translation"}
        root_t[take] = {ax: round(max(v) - min(v), 9) for ax, v in c.items()}
    R["root_bone_translation_range"] = root_t
    # scale keys
    sc = {}
    for take, s in A["stacks"].items():
        vals = [x for (m, prop, ax), (kt, kv) in s["curves"].items() if prop == "Lcl Scaling" and m not in arm_names for x in kv]
        sc[take] = [min(vals), max(vals)] if vals else None
    R["bone_scale_key_range"] = sc
    # 2. mesh budget + weights
    g = A["geo"]
    tris, verts = 0, 0
    for gn in g:
        pvi = child(gn, "PolygonVertexIndex")[1][0]
        n = 0
        for i in pvi:
            n += 1
            if i < 0:
                tris += n - 2
                n = 0
        verts += len(child(gn, "Vertices")[1][0]) // 3
    R["mesh"] = {"geometries": len(g), "triangles": tris, "vertices": verts}
    inf, wsum = defaultdict(int), defaultdict(float)
    for cl in A["clusters"].values():
        ix = child(cl, "Indexes")
        if ix is None:
            continue
        for i, w in zip(ix[1][0], child(cl, "Weights")[1][0]):
            if w > 0:
                inf[i] += 1
                wsum[i] += w
    hist = defaultdict(int)
    for v in range(verts):
        hist[inf.get(v, 0)] += 1
    R["skin"] = {"clusters": len(A["clusters"]), "max_influences": max(inf.values()),
                 "influence_histogram": dict(sorted(hist.items())),
                 "weight_sum_range": [round(min(wsum.values()), 5), round(max(wsum.values()), 5)]}
    # 3. old takes vs the approved r01 FBX (the file the game ships, md5 equal)
    per, all_same = {}, True
    for take, s in B["stacks"].items():
        t = A["stacks"].get(take)
        if t is None:
            per[take] = {"missing": True}
            all_same = False
            continue
        ka, kb = s["curves"], t["curves"]
        same = sum(1 for k in ka if k in kb and ka[k] == kb[k])
        maxd = 0.0
        for k in ka:
            if k in kb and ka[k] != kb[k] and len(ka[k][1]) == len(kb[k][1]):
                maxd = max(maxd, max(abs(x - y) for x, y in zip(ka[k][1], kb[k][1])))
        per[take] = {"curves": len(ka), "identical_curves": same, "extra_curves_in_new": len(set(kb) - set(ka)),
                     "keys": sum(len(v[0]) for v in ka.values()), "range_identical": s["range_ticks"] == t["range_ticks"],
                     "max_value_diff": maxd}
        all_same &= same == len(ka) == len(kb) and s["range_ticks"] == t["range_ticks"]
    R["old_takes_vs_r01"] = per
    R["old_takes_bit_identical"] = bool(all_same)
    R["added_takes"] = sorted(set(A["stacks"]) - set(B["stacks"]))

    def static(M):
        d = {}
        for gn in M["geo"]:
            for f in ("Vertices", "PolygonVertexIndex"):
                d["geo." + f] = child(gn, f)[1][0]
            le = child(gn, "LayerElementNormal")
            if le:
                d["geo.normals"] = child(le, "Normals")[1][0]
            uv = child(gn, "LayerElementUV")
            if uv:
                d["geo.uv"] = child(uv, "UV")[1][0]
        for k, cl in M["clusters"].items():
            for f in ("Indexes", "Weights", "Transform", "TransformLink"):
                c = child(cl, f)
                d[f"cl.{k}.{f}"] = c[1][0] if c else None
        for po in M["poses"]:
            for pn in po[2]:
                if pn[0] == "PoseNode":
                    d["bind." + M["models"].get(child(pn, "Node")[1][0], ("?",))[0]] = child(pn, "Matrix")[1][0]
        return d

    sa, sb = static(B), static(A)
    diff = sorted(k for k in set(sa) | set(sb) if sa.get(k) != sb.get(k))
    R["mesh_uv_normals_skin_bind_identical"] = not diff
    R["static_items_compared"] = len(sa)
    R["static_differing"] = diff[:10]
    Path(dst).write_text(json.dumps(R, indent=1))
    print(json.dumps({k: v for k, v in R.items() if k not in ("old_takes_vs_r01", "keys")}, indent=1))
    print(json.dumps(R["old_takes_vs_r01"]))
    print(json.dumps(R["keys"]))


if __name__ == "__main__":
    main(*sys.argv[1:5])
