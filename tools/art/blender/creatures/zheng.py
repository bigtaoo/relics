# Zheng: leopard with one horn and five tails (art/zheng). Measured on art/zheng/model/toon_v1.glb.
import math
from quadruped_anim import X, Y, add

NAME = "zheng"

# name: (head, tail, parent, deform); glb space, head +X, left +Y, up +Z
BONES = {
    "root":       ((0.0, 0.08, -0.5), (0.0, 0.08, -0.4), None, False),
    "hips":       ((-0.16, 0.17, -0.22), (-0.04, 0.11, -0.22), "root", True),
    "spine":      ((-0.04, 0.11, -0.22), (0.08, 0.04, -0.2), "hips", True),
    "chest":      ((0.08, 0.04, -0.2), (0.18, -0.01, -0.16), "spine", True),
    "neck":       ((0.18, -0.01, -0.16), (0.25, 0.0, -0.06), "chest", True),
    "head":       ((0.25, 0.0, -0.06), (0.38, 0.01, 0.02), "neck", True),
    "horn":       ((0.32, 0.03, 0.1), (0.34, 0.045, 0.3), "head", True),
    # legs: upper, lower, foot (L = +Y side of the body, the creature faces +X)
    "thigh.L":    ((-0.11, 0.24, -0.22), (-0.12, 0.26, -0.33), "hips", True),
    "shin.L":     ((-0.12, 0.26, -0.33), (-0.16, 0.3, -0.43), "thigh.L", True),
    "foot.L":     ((-0.16, 0.3, -0.43), (-0.17, 0.32, -0.5), "shin.L", True),
    "thigh.R":    ((-0.2, 0.13, -0.22), (-0.24, 0.14, -0.33), "hips", True),
    "shin.R":     ((-0.24, 0.14, -0.33), (-0.3, 0.18, -0.43), "thigh.R", True),
    "foot.R":     ((-0.3, 0.18, -0.43), (-0.32, 0.18, -0.5), "shin.R", True),
    "upperarm.L": ((0.2, 0.05, -0.2), (0.22, 0.06, -0.32), "chest", True),
    "forearm.L":  ((0.22, 0.06, -0.32), (0.27, 0.03, -0.43), "upperarm.L", True),
    "hand.L":     ((0.27, 0.03, -0.43), (0.3, 0.02, -0.5), "forearm.L", True),
    "upperarm.R": ((0.12, -0.05, -0.2), (0.135, -0.07, -0.32), "chest", True),
    "forearm.R":  ((0.135, -0.07, -0.32), (0.18, -0.1, -0.43), "upperarm.R", True),
    "hand.R":     ((0.18, -0.1, -0.43), (0.2, -0.11, -0.5), "forearm.R", True),
}

# Five tails, numbered from the creature's right (-Y) to its left (+Y); points from root to tip.
TAILS = [
    [(-0.22, 0.0, -0.1), (-0.25, -0.22, 0.0), (-0.27, -0.36, 0.05), (-0.29, -0.42, 0.11), (-0.3, -0.49, 0.16)],
    [(-0.22, 0.0, -0.1), (-0.24, -0.17, 0.13), (-0.27, -0.26, 0.24), (-0.3, -0.31, 0.35), (-0.31, -0.32, 0.42)],
    [(-0.22, 0.0, -0.1), (-0.2, 0.02, 0.1), (-0.21, -0.01, 0.24), (-0.24, 0.04, 0.36), (-0.27, -0.01, 0.48)],
    [(-0.22, 0.0, -0.1), (-0.22, 0.14, 0.16), (-0.25, 0.2, 0.3), (-0.29, 0.3, 0.4), (-0.31, 0.33, 0.48)],
    [(-0.22, 0.0, -0.1), (-0.25, 0.16, 0.03), (-0.32, 0.3, 0.08), (-0.35, 0.36, 0.15), (-0.39, 0.48, 0.2)],
]
for t, pts in enumerate(TAILS, 1):
    for i in range(len(pts) - 1):
        BONES[f"tail{t}.{i + 1}"] = (pts[i], pts[i + 1], "hips" if i == 0 else f"tail{t}.{i}", True)

TAIL_CHAINS = [[f"tail{t}.{i}" for i in range(1, 5)] for t in range(1, 6)]
HORN = ["horn"]
VOXEL = 0.08  # weight proxy resolution at bind scale; small enough to keep tails and legs apart
# Hair-thin whisker tubes on the face; the horn tip (z > 0.2) is thin too and is kept.
WHISKERS = {"voxel": 0.01, "tip": 0.02, "grow": 0.004, "max_z": 0.2}

TAIL_SIDE = [-1, -0.5, 0, 0.5, 1]  # tail1 .. tail5, -Y side .. +Y side


def sec(pose, t, amp=8, speed=1, spread=0, lift=0):
    """Five tails: each segment lags the previous one; tails are out of phase.
    spread fans them out sideways, lift raises (negative) or drops them."""
    for k, chain in enumerate(TAIL_CHAINS):
        side = TAIL_SIDE[k]
        for i, b in enumerate(chain):
            ph = 2 * math.pi * (speed * t - 0.12 * i - 0.2 * k)
            add(pose, b, X, amp * math.sin(ph) * (0.6 + 0.2 * i))
            add(pose, b, Y, amp * 0.5 * math.cos(ph))
            if i == 0:
                add(pose, b, X, -spread * side)  # fan out: -Y tails rotate toward -Y
                add(pose, b, Y, lift)
