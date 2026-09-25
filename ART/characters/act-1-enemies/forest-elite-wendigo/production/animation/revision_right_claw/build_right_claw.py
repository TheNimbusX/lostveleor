"""Forest Wendigo right-claw slash art probe. Contact is frame 18 / 30 Hz.

This is an independent source clip, not the shared production animation.
The rig root stays at the origin; locomotion and hit detection belong to Sim.
"""

from pathlib import Path
import math

import bpy
from mathutils import Quaternion, Vector

HERE = Path(__file__).resolve().parent
OUT = HERE / "Wendigo_RightClaw_Probe.blend"
S = bpy.context.scene
S.render.fps = 30
R = next(o for o in S.objects if o.type == 'ARMATURE')
R.animation_data_create()
for b in R.pose.bones:
    b.rotation_mode = 'QUATERNION'


def reset():
    for b in R.pose.bones:
        b.location = (0, 0, 0)
        b.rotation_quaternion = (1, 0, 0, 0)
        b.scale = (1, 1, 1)
        for c in b.constraints:
            if c.type == 'IK':
                c.influence = 0


def turn(name, axis, deg):
    b = R.pose.bones[name]
    rest = b.bone.matrix_local.to_3x3()
    q = Quaternion(Vector(axis), math.radians(deg))
    b.rotation_quaternion = b.rotation_quaternion @ (rest.inverted() @ q.to_matrix() @ rest).to_quaternion()


def move(name, x=0, y=0, z=0):
    b = R.pose.bones[name]
    rest = b.bone.matrix_local.to_3x3()
    b.location = rest.inverted() @ Vector((x, y, z))


def ik(side, limb):
    lower = 'leg_lower' if limb == 'foot' else 'arm_lower'
    R.pose.bones[f'{side}_{lower}'].constraints[f'IK_{side}_{limb}_Plant'].influence = 1


def key(frame):
    for b in R.pose.bones:
        b.keyframe_insert(data_path='location', frame=frame, group=b.name)
        b.keyframe_insert(data_path='rotation_quaternion', frame=frame, group=b.name)
        for c in b.constraints:
            if c.type == 'IK':
                c.keyframe_insert(data_path='influence', frame=frame)


def sample(points, frame):
    ordered = sorted(points.items())
    if frame <= ordered[0][0]:
        return ordered[0][1]
    if frame >= ordered[-1][0]:
        return ordered[-1][1]
    for i in range(len(ordered)-1):
        t0, v0 = ordered[i]
        t1, v1 = ordered[i+1]
        if not t0 <= frame <= t1:
            continue
        tp, vp = ordered[max(i-1, 0)]
        tn, vn = ordered[min(i+2, len(ordered)-1)]
        m0 = (v1-vp)/max(1, t1-tp)
        m1 = (vn-v0)/max(1, tn-t0)
        d = t1-t0
        u = (frame-t0)/d
        return ((2*u**3-3*u*u+1)*v0 + (u**3-2*u*u+u)*d*m0
                + (-2*u**3+3*u*u)*v1 + (u**3-u*u)*d*m1)
    raise AssertionError(frame)


DEFAULT = dict(px=0, py=0, pz=0, hip=0, lower_yaw=0, chest=0,
               pitch=0, roll=0, head_yaw=0, head_pitch=0,
               rx=0, ry=0, rz=0, lx=0, ly=0, lz=0,
               rclav_yaw=0, rclav_pitch=0, lclav_yaw=0,
               rwrist=0, lwrist=0, rfoot_x=0, rfoot_y=0,
               rfoot_z=0, lfoot_x=0, lfoot_y=0,
               lfoot_z=0, rtoe=0, ltoe=0)

# Three readable shapes: loaded diagonal, open arc, low follow-through.
# Front camera sees right claw at screen-left during the tell. The mask
# stays visible because the claw swings below it during frames 17-19.
STATIONS = {
    0: {},
    3: dict(px=.015, py=-.025, pz=-.045, hip=-3,
            lower_yaw=-2, chest=-5, pitch=-3, head_yaw=2,
            rx=.06, ry=.12, rz=.20, lx=-.02, ly=.02, lz=.02,
            rfoot_x=.02, rfoot_y=.03, rfoot_z=.045),
    7: dict(px=.045, py=-.055, pz=-.11, hip=-5,
            lower_yaw=-4, chest=-8, pitch=-8, head_yaw=5,
            head_pitch=4, rx=.18, ry=.43, rz=.73,
            lx=-.10, ly=.16, lz=-.07,
            rclav_yaw=-4, rclav_pitch=8,
            rfoot_x=.07, rfoot_y=.13, rfoot_z=.065),
    11: dict(px=.07, py=-.085, pz=-.15, hip=-10,
             lower_yaw=-6, chest=-13, pitch=-11,
             head_yaw=7, head_pitch=4,
             rx=.27, ry=.58, rz=1.11, lx=-.08,
             ly=.16, lz=-.07, rclav_yaw=-6,
             rclav_pitch=11, rwrist=-11,
             rfoot_x=.08, rfoot_y=.16),
    14: dict(px=.07, py=-.08, pz=-.16, hip=-11,
             lower_yaw=-7, chest=-16, pitch=-12,
             head_yaw=8, head_pitch=4,
             rx=.29, ry=.59, rz=1.15, lx=-.09,
             ly=.17, lz=-.08, rclav_yaw=-7,
             rclav_pitch=12, rwrist=-15,
             rfoot_x=.08, rfoot_y=.16),
    15: dict(px=.07, py=-.075, pz=-.16, hip=-11,
             lower_yaw=-7, chest=-17, pitch=-13,
             head_yaw=9, head_pitch=4,
             rx=.30, ry=.59, rz=1.16, lx=-.09,
             ly=.17, lz=-.08, rclav_yaw=-8,
             rclav_pitch=12, rwrist=-16,
             rfoot_x=.08, rfoot_y=.16),
    16: dict(px=.06, py=-.025, pz=-.18, hip=-7,
             lower_yaw=-6, chest=-13, pitch=-15,
             head_yaw=8, head_pitch=2,
             rx=.28, ry=.72, rz=1.05,
             lx=-.11, ly=.20, lz=-.06,
             rclav_yaw=-6, rclav_pitch=11,
             rwrist=-4, rfoot_x=.08, rfoot_y=.16),
    17: dict(px=-.11, py=.10, pz=-.31, hip=3,
             lower_y=-3, chest=6, pitch=-29,
             head_yaw=4, head_pitch=1,
             rx=-.18, ry=1.03, rz=.58,
             lx=-.15, ly=.24, lz=.02,
             rclav_yaw=-2, rclav_pitch=2,
             rwrist=24, rfoot_x=.08, rfoot_y=.16),
    18: dict(px=-.30, py=.17, pz=-.55, hip=8,
             lower_yaw=3, chest=12, pitch=-43,
             roll=-4, head_yaw=-9, head_pitch=7,
             rx=-1.07, ry=.94, rz=-.18,
             lx=-.11, ly=.14, lz=.09,
             rclav_yaw=18, rclav_pitch=-8,
             rwrist=51, lwrist=-5,
             rfoot_x=.08, rfoot_y=.16),
    19: dict(px=-.36, py=.19, pz=-.57, hip=13,
             lower_yaw=6, chest=20, pitch=-46,
             roll=-6, head_yaw=-14, head_pitch=10,
             rx=-1.13, ry=.92, rz=-.20,
             lx=-.06, ly=.05, lz=.12,
             rclav_yaw=21, rclav_pitch=-12,
             rwrist=59, lwrist=-8,
             rfoot_x=.08, rfoot_y=.16),
    21: dict(px=-.30, py=.17, pz=-.47, hip=12,
             lower_yaw=6, chest=18, pitch=-39,
             roll=-5, head_yaw=-10, head_pitch=10,
             rx=-1.02, ry=.91, rz=-.18,
             lx=-.05, ly=.04, lz=.13,
             rclav_yaw=17, rclav_pitch=-9,
             rwrist=53, rfoot_x=.08, rfoot_y=.16),
    24: dict(px=-.16, py=.10, pz=-.23, hip=13,
             lower_yaw=10, chest=23, pitch=-22,
             head_yaw=-5, head_pitch=7,
             rx=-.50, ry=.73, rz=-.15,
             lx=-.04, ly=.06, lz=.08,
             rclav_yaw=8, rclav_pitch=-3,
             rwrist=31, rfoot_x=.08, rfoot_y=.16,
             rfoot_z=.01),
    27: dict(px=-.02, py=.03, pz=-.075, hip=4,
             lower_yaw=3, chest=9, pitch=-10,
             head_yaw=-2, head_pitch=2,
             rx=-.12, ry=.20, rz=-.03,
             lx=-.02, ly=.02, lz=.02,
             rclav_yaw=1, rclav_pitch=0,
             rwrist=9, rfoot_x=.04,
             rfoot_y=.08, rfoot_z=.055),
    30: {},
}


def pose(frame, v):
    S.frame_set(frame)
    reset()
    for side in ('L', 'R'):
        ik(side, 'foot')
        ik(side, 'hand')
    move('pelvis', v['px'], v['py'], v['pz'])
    turn('pelvis', (0,0,1), v['hip'])
    turn('spine_01', (0,0,1), v['lower_yaw'])
    turn('spine_01', (1,0,0), v['pitch']*.48)
    turn('spine_02', (0,0,1), v['chest'])
    turn('spine_02', (1,0,0), v['pitch']*.52)
    turn('spine_02', (0,1,0), v['roll'])
    turn('neck', (0,0,1), v['head_yaw']*.60)
    turn('head', (0,0,1), v['head_yaw']*.40)
    turn('neck', (1,0,0), v['head_pitch']*.55)
    turn('head', (1,0,0), v['head_pitch']*.45)
    turn('R_clavicle', (0,0,1), v['rclav_yaw'])
    turn('R_clavicle', (1,0,0), v['rclav_pitch'])
    turn('L_clavicle', (0,0,1), v['lclav_yaw'])
    turn('R_hand', (1,0,0), v['rwrist'])
    turn('L_hand', (1,0,0), v['lwrist'])
    turn('R_toe', (1,0,0), v['rtoe'])
    turn('L_toe', (1,0,0), v['ltoe'])
    move('CTRL_R_hand', v['rx'], v['ry'], v['rz'])
    move('CTRL_L_hand', v['lx'], v['ly'], v['lz'])
    move('CTRL_R_foot', v['rfoot_x'], v['rfoot_y'], max(0,v['rfoot_z']))
    move('CTRL_L_foot', v['lfoot_x'], v['lfoot_y'], max(0,v['lfoot_z']))
    key(frame)


A = bpy.data.actions.new('AN_ForestWendigo_Claw')
A.use_fake_user = True
A.frame_start = 0
A.frame_end = 30
R.animation_data.action = A
points = {}
for name, default in DEFAULT.items():
    points[name] = {0: default, 30: default}
    for f, values in STATIONS.items():
        if name in values:
            points[name][f] = values[name]
for f in range(31):
    v = {name: sample(channel, f) for name, channel in points.items()}
    pose(f, v)

R.animation_data.action = A
S.frame_set(0)
S.frame_start = 0
S.frame_end = 30
bpy.ops.wm.save_as_mainfile(filepath=str(OUT))
print('PROBE_SAVED', OUT)
