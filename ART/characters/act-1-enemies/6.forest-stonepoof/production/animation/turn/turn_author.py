"""Author one turn direction onto the editable IK rig (carrier yaw, hooves, spine, crouch)."""
import bpy, math
from mathutils import Vector, Quaternion
import turn_params as P
import turn_rig as R


def reach_deficit(ctx, tag):
    """Metres the body must drop so this leg's IK target is inside reach - margin."""
    arm, leg = ctx['arm'], ctx['legs'][tag]
    hip = arm.matrix_world @ arm.pose.bones['MCH_Upper_' + tag].head
    tgt = leg['foot'].matrix_world.translation
    reach = leg['length'] - P.REACH_MARGIN
    dh = (hip.xy - tgt.xy).length
    v = hip.z - tgt.z
    if dh >= reach:
        return v  # degenerate: would need the hip on the ground; report as-is
    return max(0.0, v - math.sqrt(reach * reach - dh * dh))


def circular(values, radius, op):
    n = len(values)
    return [op([values[(i + k) % n] for k in range(-radius, radius + 1)]) for i in range(n)]


def author(ctx, scene, name, s):
    R.clear_animation(ctx)
    arm, carrier, legs = ctx['arm'], ctx['carrier'], ctx['legs']
    roles = P.roles_for(s)
    fs = P.samples()
    for f in fs:
        carrier.rotation_quaternion = Quaternion((0, 0, 1), math.radians(s * P.DEG_PER_FRAME * f))
        carrier.keyframe_insert('rotation_quaternion', frame=f)
    R.set_interp(carrier, 'LINEAR')
    contacts = {}
    for tag, role in roles.items():
        bow = P.ROLES[role]['bow']
        lifts = []
        prev = True
        for f in fs:
            phi, dz, pitch, bulge, planted = P.hoof_state(role, f)
            extra = (s * bow[0] * bulge, -bow[1] * bulge)
            R.hoof_key(ctx, tag, f, s * phi, dz, pitch, extra)
            if 0 <= f < P.FRAMES and prev != planted:
                lifts.append((f, 'plant' if planted else 'lift'))
            prev = planted
        contacts[tag] = {'role': role, 'events': lifts}
        R.set_interp(legs[tag]['foot'])
    for f in fs:
        ang, off = P.spine(s, f)
        R.spine_pose(ctx, f, ang, off)
    R.set_interp(arm)
    # Crouch: legs are almost straight at rest, so drop the body just enough
    # that every IK chain keeps REACH_MARGIN of slack (periodic, smoothed).
    period = [i * P.SAMPLE_STEP for i in range(int(P.FRAMES / P.SAMPLE_STEP))]
    need = []
    for f in period:
        R.goto(scene, f)
        need.append(max(reach_deficit(ctx, t) for t in legs))
    smooth = circular(circular(need, 6, max), 6, lambda w: sum(w) / len(w))
    smooth = [max(a, b) for a, b in zip(smooth, need)]
    n = len(period)
    for f in fs:
        i = int(round((f % P.FRAMES) / P.SAMPLE_STEP)) % n
        ang, off = P.spine(s, f)
        off = (off[0], off[1], off[2] - smooth[i])
        pb = arm.pose.bones['body']
        pb.location = ctx['rest']['body'].to_quaternion().inverted() @ Vector(off)
        pb.keyframe_insert('location', frame=f, group='body')
    R.set_interp(arm)
    for b in arm.pose.bones:
        for c in b.constraints:
            c.influence = 1
            c.keyframe_insert('influence', frame=0)
            c.keyframe_insert('influence', frame=P.FRAMES)
    names = {}
    for o in [arm, carrier] + [l['foot'] for l in legs.values()]:
        suffix = 'Rig' if o == arm else ('Carrier' if o == carrier else o.name)
        act = o.animation_data.action
        act.name = 'AN_Stonehoof_%s_%s' % (name, suffix)
        act.use_fake_user = True
        act.use_frame_range = True
        act.frame_start, act.frame_end = 0, P.FRAMES
        names[o.name] = act.name
    return {'actions': names, 'contacts': contacts,
            'crouch_m': {'min': min(smooth), 'max': max(smooth)},
            'raw_reach_deficit_max_m': max(need)}


def measure(ctx, scene, s):
    """IK error, planted world slip and ground clearance at 1/8 frame over one cycle."""
    arm, legs = ctx['arm'], ctx['legs']
    roles = P.roles_for(s)
    ik = 0.0
    slip = {t: 0.0 for t in legs}
    anchor = {}
    for i in range(P.FRAMES * 8 + 1):
        f = i / 8
        R.goto(scene, f)
        for t, leg in legs.items():
            ank = arm.matrix_world @ arm.pose.bones['leg_' + t + '_bot2'].head
            tgt = leg['foot'].matrix_world.translation
            ik = max(ik, (ank - tgt).length)
            planted = P.hoof_state(roles[t], f)[4]
            if planted:
                if t not in anchor:
                    anchor[t] = ank.copy()
                slip[t] = max(slip[t], (ank - anchor[t]).length)
            else:
                anchor.pop(t, None)
    return {'max_ik_target_error_m': ik, 'max_planted_ankle_world_slip_m': slip}
