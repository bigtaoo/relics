# Kuafu: cartoon giant holding a staff in his right hand, a small snake hanging from each ear
# (art/kuafu). Humanoid family: Tripo's biped auto-rig supplies the body (biped_rig.py), this table
# only adds the staff and the snakes. Measured on art/kuafu/model/rig_v1.glb (feet z 0, topknot
# z 1.0, facing +X, left +Y) with orthographic grid renders and z slices: the staff runs from
# (0.077, -0.35, 0) to (0.125, -0.465, 0.72) through the right fist at z 0.37..0.47; the snakes
# coil round the ears at z 0.81 and hang to z 0.62 (x 0.05..0.07).
import math
from quadruped_anim import X, Y, add

NAME = "kuafu"

STAFF = ((0.077, -0.35, 0.0), (0.125, -0.465, 0.72))
SNAKES = {"L": [(0.06, 0.15, 0.81), (0.065, 0.185, 0.75), (0.055, 0.17, 0.685), (0.05, 0.165, 0.62)],
          "R": [(0.06, -0.16, 0.81), (0.065, -0.2, 0.75), (0.055, -0.185, 0.685), (0.05, -0.175, 0.62)]}

# name: (head, tail, parent)
BONES = {"staff": (STAFF[0], STAFF[1], "R_Hand")}
TAIL_CHAINS = []
for side, pts in SNAKES.items():
    chain = [f"snake.{side}.{i + 1}" for i in range(len(pts) - 1)]
    for i, b in enumerate(chain):
        BONES[b] = (pts[i], pts[i + 1], "Head" if i == 0 else chain[i - 1])
    TAIL_CHAINS.append(chain)  # the snakes carry the tail mask (living colour: yellow)
HORN = ["staff"]  # the staff carries the horn mask (living colour: dark wood)


def _hair(co):
    """Hair, topknot and beard: everything on the head but the face. Measured on head close-ups
    (rig_v1.glb): the face spans z 0.715..0.86 in front of x 0.06 (ears included), the beard hangs
    below the mouth from z 0.715 to the beads at 0.6, the hair covers the rest."""
    x, y, z = co
    return z > 0.6 and (x < 0.06 or z > 0.86 or z < 0.715)


# The shader has four part masks (body, patina, horn, tail); a dark-haired, bearded giant read as a
# pink blob in his living state, so hair and beard share the horn mask (and its brown) with the staff.
HORN_REGION = (["Head", "NeckTwist01", "NeckTwist02"], _hair)

# bone: (r, fade, owners, avoid), see biped_rig.claim. Tripo gives the whole staff to R_Hand and
# the snakes to Head; the fist around the staff keeps its weights.
PARTS = {"staff": (0.03, 0.012, ["R_Hand"], ("R_Hand", 0.05))}
for chain in TAIL_CHAINS:
    for b in chain:
        PARTS[b] = (0.018, 0.012, ["Head", "NeckTwist01", "NeckTwist02"], None)

# The weapon the shared clips aim: (bone that holds it, its direction at rest).
WEAPON = ("R_Hand", (STAFF[1][0] - STAFF[0][0], STAFF[1][1] - STAFF[0][1], STAFF[1][2] - STAFF[0][2]))


def sec(pose, t, amp=8, speed=1):
    """The snakes sway from the ears, each segment a little behind the one above."""
    for side, s in (("L", 1), ("R", -1)):
        for i in range(3):
            ph = 2 * math.pi * (speed * t - 0.12 * i - (0.25 if s < 0 else 0))
            add(pose, f"snake.{side}.{i + 1}", X, s * amp * math.sin(ph))
            add(pose, f"snake.{side}.{i + 1}", Y, 0.6 * amp * math.cos(ph))
