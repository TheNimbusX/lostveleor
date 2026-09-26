"""Solve every claw-contact key and store contact_solutions.json (merged by pose_keys.py)."""
import json
from concurrent.futures import ProcessPoolExecutor

from pathlib import Path
import solve_contact as sc

HERE = Path(__file__).resolve().parent
EL = {'L_elbow': (-.6, -1, .5), 'R_elbow': (-.6, 1, .5)}
HOLD = dict(knee=(1, 62), L_tip=(.75, -.95), R_tip=(.75, .95), **EL)
SPECS = {
    # impact (body still coming down), compression, rebound, hold, end of hold
    24: (dict(HOLD, L_contact=.018, R_contact=.018), dict(arm_ext=.88, clear=.12, chest=42, face=58, head_u=1.32, pel_u=-.40, com_f=.27, com_u=.92)),
    29: (dict(HOLD, L_contact=.018, R_contact=.018), dict(arm_ext=.80, clear=.10, chest=51, face=54, head_u=1.18, pel_u=-.52, com_f=.34, com_u=.84)),
    32: (dict(HOLD, L_contact=.018, R_contact=.018), dict(arm_ext=.83, clear=.12, chest=48, face=64, head_u=1.25, pel_u=-.48, com_f=.33, com_u=.865)),
    36: (dict(HOLD, L_contact=.018, R_contact=.018), dict(arm_ext=.85, clear=.12, chest=46, face=66, head_u=1.27, pel_u=-.47, com_f=.31, com_u=.87)),
    # push back off the planted claws: hips go back over the feet before the claws lift
    38: (dict(HOLD, L_contact=.018, R_contact=.018), dict(arm_ext=.93, clear=.12, chest=36, face=68, head_u=1.38, pel_u=-.38, com_f=.15, com_u=.91)),
}
V0 = [.05, -.45, 30, 10, 0, 15, -35, -8, 8, -8, 8, 22, 5, 22, 5]


CHAINS = [[24, 29, 32, 36, 38]]


def run_chain(chain):
    """Solve a chain sequentially; each key starts from (and is pulled towards) the previous
    solution so the hold keys stay one coherent pose family instead of jumping styles."""
    v0 = list(V0); res = []
    for k in chain:
        base, tg = SPECS[k]
        if k != chain[0]:
            # planted claws: the hand keeps the world rotation it struck/touched with,
            # so no claw blade pivots or slides while the tips stay on their ground points
            tg = dict(tg, fix={n: v0[sc.VARS.index(n)] for n in ('Lhp', 'Lhr', 'Rhp', 'Rhr')})
        v, r = sc.solve(base, tg, v0)
        P = sc.to_P(v, base)
        res.append((k, sc.fmt(P), sc.report(P), float((r ** 2).sum())))
        v0 = list(v)
    return res


if __name__ == '__main__':
    out = {}
    with ProcessPoolExecutor(1) as ex:
        for chain in ex.map(run_chain, CHAINS):
            for k, P, rep, res in chain:
                out[str(k)] = {'P': P, 'report': rep, 'residual': res}
                print(k, rep, round(res, 3), flush=True)
    (HERE / 'contact_solutions.json').write_text(json.dumps(out, indent=1))
