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
ORDER = list(TAKES)
