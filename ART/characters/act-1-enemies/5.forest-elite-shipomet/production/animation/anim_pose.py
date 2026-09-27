"""Pose parameters -> Pose for the Thorncaster.

Params (degrees / metres, armature space; character faces -Y, left = +X):
  hip (x,y,z) offset of Hips, hip_rot (pitch,roll,yaw) about the Hips head
  spine (pitch,roll,yaw) spread over Spine02/Spine01/Spine, neck (p,r,y), head (p,r,y)
  per side S in L/R:
    S_tip  spike tip position in CHEST space (= where it would be with the chest at rest)
    S_dir  spike direction wrist->tip, chest space;  S_pole elbow pole, chest space
    S_shrug clavicle raise (deg), S_lock 0..1 blend to world targets S_wtip / S_wdir
    S_ank  ankle target (world), S_foot (pitch,roll,yaw) world foot rotation vs rest, S_knee knee pole (world)
    S_toe  toe pitch (deg, + curls claws down)
"""
import math

from mathutils import Quaternion, Vector

from anim_rig import Pose, euler_q

SIDES = {"L": "Left", "R": "Right"}
DIRS = ("L_dir", "R_dir", "L_pole", "R_pole", "L_wdir", "R_wdir", "L_knee", "R_knee")
SPINE_SPLIT = (("Spine02", 0.35), ("Spine01", 0.35), ("Spine", 0.30))


def _pole_from_rest(sk, a, b, end_point):
    A, K = sk.rest_head(a), sk.rest_head(b)
    axis = (end_point - A).normalized()
    v = K - A
    p = v - axis * v.dot(axis)
    return p.normalized()


def defaults(sk):
    d = {"hip": (0.0, 0.0, 0.0), "hip_rot": (0.0, 0.0, 0.0), "spine": (0.0, 0.0, 0.0),
         "neck": (0.0, 0.0, 0.0), "head": (0.0, 0.0, 0.0)}
    for s, side in SIDES.items():
        hand = side + "Hand"
        d[s + "_tip"] = tuple(sk.rest_tail(hand))
        d[s + "_dir"] = tuple((sk.rest_tail(hand) - sk.rest_head(hand)).normalized())
        d[s + "_pole"] = tuple(_pole_from_rest(sk, side + "Arm", side + "ForeArm", sk.rest_head(hand)))
        d[s + "_shrug"] = 0.0
        d[s + "_lock"] = 0.0
        d[s + "_wtip"] = d[s + "_tip"]
        d[s + "_wdir"] = d[s + "_dir"]
        d[s + "_ank"] = tuple(sk.rest_head(side + "Foot"))
        d[s + "_foot"] = (0.0, 0.0, 0.0)
        knee = Vector(_pole_from_rest(sk, side + "UpLeg", side + "Leg", sk.rest_head(side + "Foot")))
        d[s + "_knee"] = tuple((knee + Vector((0, -0.3, 0))).normalized())
        d[s + "_toe"] = 0.0
    return d


def build(sk, p):
    """Return (Pose, info) where info has IK shortfalls and the chest delta."""
    pose = Pose(sk)
    info = {}
    pose.translate("Hips", p["hip"])
    pose.rotate("Hips", euler_q(*p["hip_rot"]))
    for bone, k in SPINE_SPLIT:
        pose.rotate(bone, euler_q(*(k * a for a in p["spine"])))
    pose.rotate("neck", euler_q(*p["neck"]))
    pose.rotate("Head", euler_q(*p["head"]))
    C = pose.delta("Spine")
    Cq = C.to_quaternion()
    # arms
    for s, side in SIDES.items():
        sh = p[s + "_shrug"]
        if sh:
            axis = Cq @ Vector((0, 1, 0))
            pose.rotate(side + "Shoulder", Quaternion(axis, math.radians(-sh if s == "L" else sh)))
        tip = C @ Vector(p[s + "_tip"])
        dr = (Cq @ Vector(p[s + "_dir"])).normalized()
        lk = p[s + "_lock"]
        if lk > 0:
            tip = tip.lerp(Vector(p[s + "_wtip"]), lk)
            dr = dr.slerp(Vector(p[s + "_wdir"]).normalized(), lk) if dr.dot(Vector(p[s + "_wdir"])) > -0.99 else Vector(p[s + "_wdir"])
        pole = Cq @ Vector(p[s + "_pole"])
        wrist = tip - dr * sk.length[side + "Hand"]
        info[s + "_arm_short"] = pose.ik2(side + "Arm", side + "ForeArm", wrist, pole)
        pose.aim(side + "Hand", dr)
        info[s + "_tip"] = tuple(pose.tail(side + "Hand"))
    # legs
    for s, side in SIDES.items():
        info[s + "_leg_short"] = pose.ik2(side + "UpLeg", side + "Leg", p[s + "_ank"], p[s + "_knee"])
        fq = euler_q(*p[s + "_foot"])
        pose.set_rotation(side + "Foot", fq)
        if p[s + "_toe"]:
            axis = pose.M[side + "Foot"].to_quaternion() @ Vector((1, 0, 0))
            pose.rotate(side + "ToeBase", Quaternion(axis, math.radians(p[s + "_toe"])))
    info["chest"] = C
    return pose, info


def foot_roll(sk, side, pivot_world, pitch_deg, yaw_deg=0.0):
    """Ankle target + foot rotation that keeps the front claw contact at pivot_world while the heel lifts by pitch."""
    pivot_rest = Vector(sk.contact_front[side])
    ank_rest = sk.rest_head(side + "Foot")
    q = euler_q(pitch_deg, 0.0, yaw_deg)
    ank = Vector(pivot_world) + q @ (ank_rest - pivot_rest)
    return tuple(ank), (pitch_deg, 0.0, yaw_deg)
