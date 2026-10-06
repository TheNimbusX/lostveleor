import json, math, sys
d = json.load(open(sys.argv[1]))
print(d.get('contacts'))
for fr in d['frames'][: int(sys.argv[2]) if len(sys.argv) > 2 else 999]:
    r = fr['root']; p = fr['p']; yaw = fr['yaw']; g = fr['grip']
    fx, fz = math.sin(yaw), math.cos(yaw)
    rx, rz = p[0] - r[0], p[2] - r[2]
    f = rx * fx + rz * fz; side = rx * fz - rz * fx
    gx, gz = g[0] - r[0], g[2] - r[2]; gf = gx * fx + gz * fz; gs = gx * fz - gz * fx
    ang = math.degrees(math.atan2(side, f)); gang = math.degrees(math.atan2(gs, gf))
    print("%5.3f %-6s f%5.2f head ang%+5.0f r%4.2f h%4.2f v%5.1f | grip ang%+5.0f r%4.2f h%4.2f | span %.2f/%.2f %s %s" % (
        fr['t'], fr['clip'][-6:], fr['frame'], ang, math.hypot(f, side), p[1], fr['v'], gang, math.hypot(gf, gs), g[1], fr['span'], fr['cable'], fr['mode'][:5], 'G' if fr['grounded'] else ''))
