# Dangkang: cartoon boar with tusks, a bristle crest, one curly tail and a rice-ear pendant on
# its left (art/dangkang). Measured on art/dangkang/model/gen_v1.glb: bounds x -0.5..0.5 (snout
# tip +0.5, tail curl -0.5), y -0.31..0.31 (body centre y -0.025), z -0.425 (hooves)..0.425
# (bristle tips). Legs are short: only z -0.43..-0.29 shows below the belly.
import math
from quadruped_anim import X, Y, Z, add, bump, ease

NAME = "dangkang"
C = -0.025  # body centre line (y)

# name: (head, tail, parent, deform); glb space, head +X, left +Y, up +Z
BONES = {
    "root":       ((0.0, C, -0.425), (0.0, C, -0.325), None, False),
    "hips":       ((-0.3, C, -0.04), (-0.12, C, -0.06), "root", True),
    "spine":      ((-0.12, C, -0.06), (0.02, C, -0.05), "hips", True),
    "chest":      ((0.02, C, -0.05), (0.13, C, -0.02), "spine", True),
    "neck":       ((0.13, C, -0.02), (0.22, C, 0.02), "chest", True),
    "head":       ((0.22, C, 0.02), (0.46, C, 0.0), "neck", True),
    # tusks from the mouth corners, curving out and up; they carry the "horn" mask
    "tusk.L":     ((0.4, 0.15, -0.03), (0.435, 0.18, 0.09), "head", True),
    "tusk.R":     ((0.4, -0.19, -0.03), (0.435, -0.23, 0.09), "head", True),
    "ear.L":      ((0.17, 0.11, 0.2), (0.18, 0.19, 0.29), "head", True),
    "ear.R":      ((0.17, -0.15, 0.2), (0.18, -0.23, 0.29), "head", True),
    # rice-ear bundle hanging from the rope on the left of the neck
    "rice":       ((0.17, 0.23, -0.02), (0.175, 0.27, -0.17), "chest", True),
    # legs: upper (from just inside the belly, so it does not claim the flanks), lower, hoof;
    # L = +Y side of the body
    "thigh.L":    ((-0.25, 0.12, -0.24), (-0.26, 0.12, -0.31), "hips", True),
    "shin.L":     ((-0.26, 0.12, -0.31), (-0.265, 0.12, -0.39), "thigh.L", True),
    "foot.L":     ((-0.265, 0.12, -0.39), (-0.22, 0.12, -0.425), "shin.L", True),
    "thigh.R":    ((-0.25, -0.17, -0.24), (-0.26, -0.17, -0.31), "hips", True),
    "shin.R":     ((-0.26, -0.17, -0.31), (-0.265, -0.17, -0.39), "thigh.R", True),
    "foot.R":     ((-0.265, -0.17, -0.39), (-0.22, -0.17, -0.425), "shin.R", True),
    "upperarm.L": ((0.12, 0.136, -0.24), (0.11, 0.136, -0.31), "chest", True),
    "forearm.L":  ((0.11, 0.136, -0.31), (0.115, 0.136, -0.39), "upperarm.L", True),
    "hand.L":     ((0.115, 0.136, -0.39), (0.155, 0.136, -0.425), "forearm.L", True),
    "upperarm.R": ((0.12, -0.187, -0.24), (0.11, -0.187, -0.31), "chest", True),
    "forearm.R":  ((0.11, -0.187, -0.31), (0.115, -0.187, -0.39), "upperarm.R", True),
    "hand.R":     ((0.115, -0.187, -0.39), (0.155, -0.187, -0.425), "forearm.R", True),
}

# Curly tail from the rump out to the curl on the +Y side; root to tip.
TAIL = [(-0.36, 0.0, 0.08), (-0.41, 0.04, 0.06), (-0.45, 0.09, 0.04), (-0.49, 0.12, 0.06)]
for i in range(len(TAIL) - 1):
    BONES[f"tail.{i + 1}"] = (TAIL[i], TAIL[i + 1], "hips" if i == 0 else f"tail.{i}", True)

TAIL_CHAINS = [[f"tail.{i}" for i in range(1, len(TAIL))]]
HORN = ["tusk.L", "tusk.R"]
# Small parts keep their weights only near their bones (quadruped_rig.limit_weights); unlimited,
# the ears took the bristle crest and half the head, the tail root the rear of the crest, and the
# rice bundle the whole left shoulder.
LOCAL = {"ear.L": 0.05, "ear.R": 0.05, "tusk.L": 0.035, "tusk.R": 0.035, "rice": 0.05,
         "tail.1": 0.03, "tail.2": 0.03, "tail.3": 0.03}
VOXEL = 0.08  # weight proxy resolution at bind scale (same as Zheng)
WHISKERS = None  # no hair-thin parts


def sec(pose, t, amp=8, speed=1, spread=0, lift=0):
    """Curly tail wags (each segment lags), ears flick, the rice bundle swings a beat behind.
    spread perks the ears out sideways; lift raises (negative) or drops the tail."""
    for i, b in enumerate(TAIL_CHAINS[0]):
        ph = 2 * math.pi * (speed * t - 0.15 * i)
        add(pose, b, Z, 1.5 * amp * math.sin(ph))
        add(pose, b, Y, 0.6 * amp * math.cos(ph) + (lift if i == 0 else 0))
    flick = math.sin(2 * math.pi * (speed * t - 0.3))
    for ear, side in (("ear.L", 1), ("ear.R", -1)):
        add(pose, ear, X, -side * (spread * 0.6 + amp * 0.5 * flick))  # - on the L side tips it outward
        add(pose, ear, Y, -amp * 0.4 * math.cos(2 * math.pi * (speed * t - 0.3)))
    swing = 2 * math.pi * (speed * t - 0.4)
    add(pose, "rice", X, -amp * 0.8 * math.sin(swing))
    add(pose, "rice", Y, amp * 0.6 * math.cos(swing))


def gore(t, cr):
    """Tusk gore instead of the shared paw rake, 0.8 s, contact at 0.42 like the shared attack:
    backs up with the head low and the hind legs coiled, charges, tosses the tusks up through
    the target and holds the pose a beat, then settles."""
    p = {}
    wind = bump(0, 0.32, 0.42, t)
    strike = ease(0.34, 0.42, t) * (1 - ease(0.62, 1, t))
    over = bump(0.42, 0.48, 0.62, t)
    add(p, "hips", Y, 8 * wind - 6 * strike)
    add(p, "chest", Y, 10 * wind - 8 * strike)
    add(p, "neck", Y, 12 * wind - 15 * strike)
    add(p, "head", Y, 20 * wind - 30 * strike - 8 * over)
    add(p, "head", X, 12 * strike)  # tusks hook up and to one side
    for arm in ("upperarm.L", "upperarm.R"):
        add(p, arm, Y, -18 * wind + 10 * strike)  # brace forward, then reach
    for thigh in ("thigh.L", "thigh.R"):
        add(p, thigh, Y, -20 * wind + 35 * strike)  # coil, then kick off
    for shin in ("shin.L", "shin.R"):
        add(p, shin, Y, 20 * wind - 15 * strike)
    cr.sec(p, t, amp=6 + 10 * strike, speed=2, spread=-25 * wind + 20 * strike, lift=-25 * strike)
    return p, (-0.06 * wind + 0.16 * strike, 0, -0.02 * wind + 0.03 * strike)


CLIPS = {"attack": gore}
