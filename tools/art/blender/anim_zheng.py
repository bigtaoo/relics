# Procedural keyframe animations for the Zheng rig built by rig_zheng.py.
# usage: blender -b --python anim_zheng.py -- <rig.blend> <out.blend>
# Rotations are authored around creature-space axes (glb space: head +X, left +Y, up +Z)
# and converted into each bone's local rest frame, so bone roll does not matter.
import bpy, sys, math
from mathutils import Quaternion, Vector

FPS = 30
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
# Rotation about +Y pitches +X down: head/body "nod down" and legs swing backward.
TAILS = [f"tail{t}" for t in range(1, 6)]
TAIL_SIDE = {"tail1": -1, "tail2": -0.5, "tail3": 0, "tail4": 0.5, "tail5": 1}  # -Y side .. +Y side
SPINE = ["hips", "spine", "chest", "neck", "head"]
LEGS = ["thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R",
        "upperarm.L", "forearm.L", "hand.L", "upperarm.R", "forearm.R", "hand.R"]


def local_q(arm, bone, axis, deg):
    """Quaternion in the bone's local rest frame for a rotation about a creature-space axis."""
    rest = arm.data.bones[bone].matrix_local.to_quaternion()
    return rest.inverted() @ Quaternion(axis, math.radians(deg)) @ rest


def tail_chain(t):
    return [f"{t}.{i}" for i in range(1, 5)]


class Clip:
    def __init__(self, arm, name, seconds, loop=False):
        self.arm, self.n = arm, int(seconds * FPS)
        self.action = bpy.data.actions.new(name)
        self.action.use_fake_user = True
        self.loop = loop
        arm.animation_data_create()
        arm.animation_data.action = self.action
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'

    def key(self, frame, pose, root_loc=None):
        """pose: {bone: [(axis, deg), ...]}, bones not listed go back to rest; root_loc in creature space."""
        for pb in self.arm.pose.bones:
            q = Quaternion()
            for axis, deg in pose.get(pb.name, []):
                q = local_q(self.arm, pb.name, axis, deg) @ q
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=frame)
        root = self.arm.pose.bones["root"]
        rest = self.arm.data.bones["root"].matrix_local.to_quaternion()
        root.location = rest.inverted() @ Vector(root_loc or (0, 0, 0))
        root.keyframe_insert("location", frame=frame)

    def sample(self, fn, step=2):
        """Key fn(t in 0..1) every `step` frames; looping clips end on the first pose."""
        for f in range(0, self.n + 1, step):
            pose, loc = fn(f / self.n)
            self.key(f + 1, pose, loc)
        self.action.frame_range = (1, self.n + 1)

    def push(self):
        track = self.arm.animation_data.nla_tracks.new()
        track.name = self.action.name
        track.strips.new(self.action.name, 1, self.action)
        track.mute = True
        self.arm.animation_data.action = None


def add(pose, bone, axis, deg):
    pose.setdefault(bone, []).append((axis, deg))


def tails_wave(pose, t, amp=8, speed=1, spread=0, lift=0):
    """Secondary motion: each segment lags the previous one; tails are out of phase."""
    for k, tail in enumerate(TAILS):
        side = TAIL_SIDE[tail]
        for i, b in enumerate(tail_chain(tail)):
            ph = 2 * math.pi * (speed * t - 0.12 * i - 0.2 * k)
            add(pose, b, X, amp * math.sin(ph) * (0.6 + 0.2 * i))
            add(pose, b, Y, amp * 0.5 * math.cos(ph))
            if i == 0:
                add(pose, b, X, -spread * side)  # fan out: -Y tails rotate toward -Y
                add(pose, b, Y, lift)


def ease(a, b, t):
    """0 before a, 1 after b, smoothstep between."""
    if t <= a:
        return 0.0
    if t >= b:
        return 1.0
    u = (t - a) / (b - a)
    return u * u * (3 - 2 * u)


def bump(a, m, b, t):
    """0 -> 1 at m -> 0, smooth."""
    return ease(a, m, t) * (1 - ease(m, b, t))


def idle(t):
    """4 s loop, big enough to read from the whole-table camera: two breaths with a body bob,
    one weight shift side to side, the head looking left and right, a front paw tap, tails
    swaying and fanning. Every term is periodic in t, so the loop is seamless."""
    p = {}
    br = math.sin(2 * math.pi * 2 * t)
    sway = math.sin(2 * math.pi * t)
    add(p, "chest", Y, 6 * br)
    add(p, "neck", Y, -4 * br)
    add(p, "hips", X, 4 * sway)
    add(p, "chest", X, -4 * sway)
    add(p, "head", Z, 20 * math.sin(2 * math.pi * t + 0.6))
    add(p, "head", Y, 7 * math.sin(2 * math.pi * 2 * t + 0.8))
    tap = bump(0.55, 0.63, 0.72, t)
    add(p, "upperarm.L", Y, -22 * tap)
    add(p, "forearm.L", Y, 45 * tap)
    fan = math.sin(2 * math.pi * t + 1.5)
    tails_wave(p, t, amp=16, speed=2, spread=10 + 10 * fan, lift=-6 * fan)
    return p, (0, 0.012 * sway, 0.014 * (br + 1) / 2)


def attack(t):
    p = {}
    wind, strike = bump(0, 0.3, 0.45, t), bump(0.3, 0.45, 0.85, t)
    add(p, "hips", Y, 10 * wind - 6 * strike)
    add(p, "chest", Y, -12 * wind + 14 * strike)
    add(p, "neck", Y, -15 * wind + 10 * strike)
    add(p, "head", Y, -10 * wind + 20 * strike)
    add(p, "upperarm.L", Y, 25 * wind - 55 * strike)  # paw swipe forward
    add(p, "forearm.L", Y, -40 * wind + 20 * strike)
    add(p, "thigh.L", Y, -10 * wind)
    add(p, "thigh.R", Y, -10 * wind)
    tails_wave(p, t, amp=5 + 10 * strike, speed=2, spread=15 * strike)
    return p, (-0.03 * wind + 0.06 * strike, 0, -0.02 * wind)


def cast(t):
    p = {}
    up = ease(0, 0.35, t) * (1 - ease(0.8, 1, t))
    pulse = up * math.sin(2 * math.pi * 3 * t)
    add(p, "hips", Y, 15 * up)       # sit back
    add(p, "chest", Y, -20 * up)     # rear up
    add(p, "neck", Y, -15 * up)
    add(p, "head", Y, -20 * up + 3 * pulse)
    add(p, "thigh.L", Y, -20 * up)
    add(p, "thigh.R", Y, -20 * up)
    add(p, "upperarm.L", Y, -30 * up)
    add(p, "upperarm.R", Y, -20 * up)
    add(p, "forearm.L", Y, 40 * up)
    tails_wave(p, t, amp=10, speed=3, spread=25 * up, lift=-15 * up)
    return p, (0, 0, 0.02 * up)


def hit(t):
    p = {}
    k = bump(0, 0.15, 1, t)
    add(p, "chest", Y, -8 * k)
    add(p, "neck", Y, -15 * k)
    add(p, "head", Y, -20 * k)
    add(p, "head", X, 10 * k)
    tails_wave(p, t, amp=12 * k, speed=2, spread=-10 * k)
    return p, (-0.04 * k, 0, 0)


def death(t):
    p = {}
    fall, slump = ease(0.1, 0.6, t), ease(0.4, 0.9, t)
    add(p, "root", X, 80 * fall)  # roll onto the right side
    add(p, "neck", Y, 20 * slump)
    add(p, "head", Y, 25 * slump)
    for leg in ("thigh.L", "thigh.R"):
        add(p, leg, Y, -30 * fall)
    for leg in ("upperarm.L", "upperarm.R"):
        add(p, leg, Y, 30 * fall)
    for leg in ("shin.L", "shin.R", "forearm.L", "forearm.R"):
        add(p, leg, Y, 20 * slump)
    tails_wave(p, t, amp=8 * (1 - slump), speed=1.5, lift=25 * slump)
    return p, (0, 0, 0.12 * fall)  # lift by about half the body width so it lies on its side


def awaken(t):
    """Statue (rest pose) -> tremble -> head lifts and tails unfurl -> settle into idle's first pose."""
    p = {}
    shake = bump(0.05, 0.35, 0.55, t) * math.sin(2 * math.pi * 14 * t)
    rise = bump(0.45, 0.7, 1, t)
    add(p, "chest", X, 3 * shake)
    add(p, "head", Z, 4 * shake)
    add(p, "neck", Y, -20 * rise)
    add(p, "head", Y, -25 * rise)
    add(p, "chest", Y, -8 * rise)
    tails_wave(p, t, amp=12 * rise, speed=2, spread=30 * rise, lift=-10 * rise)
    idle_p, _ = idle(0)
    w = ease(0.85, 1, t)
    for b, rots in idle_p.items():
        for axis, deg in rots:
            add(p, b, axis, deg * w)
    return p, (0, 0, 0.03 * rise)


CLIPS = [("idle", 4.0, idle, True), ("attack", 1.0, attack, False), ("cast", 1.5, cast, False),
         ("hit", 0.5, hit, False), ("death", 1.5, death, False), ("awaken", 2.0, awaken, False)]


def main(src, out):
    bpy.ops.wm.open_mainfile(filepath=src)
    arm = bpy.data.objects["rig"]
    bpy.context.scene.render.fps = FPS
    for name, sec, fn, loop in CLIPS:
        c = Clip(arm, name, sec, loop)
        c.sample(fn)
        c.push()
        print("CLIP", name, c.n + 1, "frames")
    bpy.ops.wm.save_as_mainfile(filepath=out)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    main(a[0], a[1])
