"""Registry of ForestSplitter game takes: name -> pose function, length, loop, contract frames."""
import takes_bite as tb
import takes_loops as tl
import takes_react as tr

TAKES = {
    "ForestSplitter_Idle": {"fn": tl.idle, "frames": tl.IDLE_N, "loop": True,
                            "ref": "idle_walk", "note": "breathing, shells rock slightly, head looks around"},
    "ForestSplitter_Walk": {"fn": tl.walk, "frames": tl.WALK_N, "loop": True, "ref": "idle_walk",
                            "speed_mps": tl.SPEED, "stride_m": round(tl.STEP * tl.WALK_N, 4),
                            "note": "in-place diagonal trot; planted feet slide back at exactly 3.1 m/s"},
    "ForestSplitter_Bite": {"fn": tb.bite, "frames": tb.BITE_N, "loop": False, "ref": "bite",
                            "contact": tb.CONTACT, "windup_ticks": 18, "recovery_ticks": 12,
                            "note": "crouch back + shells open 0-12, lunge, beak snap + shell clap + paw stomp at 18"},
    "ForestSplitter_Hit": {"fn": tr.hit, "frames": tr.HIT_N, "loop": False, "ref": "bite",
                           "note": "flinch back, head retracts, shells clamp then jolt open"},
    "ForestSplitter_Death": {"fn": tr.death, "frames": tr.DEATH_N, "loop": False, "ref": "death",
                             "release": tr.DEATH_N,
                             "note": "short crack: shells jolt apart, body sags; view hides parent and pops 2 children on the last frame"},
    "ForestSplitter_Pop": {"fn": tr.pop, "frames": tr.POP_N, "loop": False, "ref": "death",
                           "contact": tr.LAND, "takeoff": 2,
                           "note": "child: crouched ball, hop with splayed legs, lands on frame 8 = end of the 8-tick SplitPop"},
}
R01_ORDER = list(TAKES)          # approved 26.09 package; bake_takes.py / validate_bake.py stay r01-only

# 27.09 roll attack (r02): baked by bake_roll.py and appended to the r01 package
import takes_roll as tro  # noqa: E402

TAKES.update({
    "ForestSplitter_RollCurl": {"fn": tro.roll_curl, "frames": tro.CURL_N, "loop": False, "ref": "roll",
                                "lock": tro.LOCK, "shake": [tro.SHAKE0, tro.CURL_N], "launch": tro.CURL_N,
                                "note": "curls into the ball: body sinks, legs fold under the rims, head pulls in, "
                                        "shells clamp; tucked by 12 (direction lock), tension, shake 24-30, "
                                        "ends in the ball pose = RollLoop f0"},
    "ForestSplitter_RollLoop": {"fn": tro.roll_loop, "frames": tro.LOOP_N, "loop": True, "ref": "roll",
                                "note": "ball hold with a tiny squash wobble; the view spins the whole body about "
                                        "its lateral axis and moves it (0.40 m/tick)"},
    "ForestSplitter_RollUncurl": {"fn": tro.roll_uncurl, "frames": tro.UNCURL_N, "loop": False, "ref": "roll",
                                  "note": "punish window: stop jolt, stunned beat, head peeks out, legs unfold and "
                                          "lift the body, shells exhale open, head shake; ends = Idle f0"},
    "ForestSplitter_RollDizzy": {"fn": tro.roll_dizzy, "frames": tro.DIZZY_N, "loop": False, "ref": "roll",
                                 "note": "after a wall hit: bounce back off the wall, graceless unfold onto splayed "
                                         "feet, dizzy sway with the head circling, shake-off, feet step home; "
                                         "ends = Idle f0"},
})
R02_NEW = ["ForestSplitter_RollCurl", "ForestSplitter_RollLoop", "ForestSplitter_RollUncurl",
           "ForestSplitter_RollDizzy"]
ORDER = R01_ORDER + R02_NEW
