# Procedural keyframe animations for a quadruped rig built by quadruped_rig.py.
# usage: blender -b --python quadruped_anim.py -- <creature> <rig.blend> <out.blend>
# Rotations are authored around creature-space axes (glb space: head +X, left +Y, up +Z)
# and converted into each bone's local rest frame, so bone roll does not matter.
# The clips here drive the shared bones (root, spine, legs). Each creature in creatures/ adds its
# own secondary motion (tails, ears...) through cr.sec(pose, t, amp, speed, spread, lift), and may
# replace whole clips through its CLIPS dict {name: fn(t, cr)}.
import bpy, importlib, os, sys, math
from mathutils import Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

FPS = 30
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
# Rotation about +Y pitches +X down: head/body "nod down" and legs swing backward.
# hips is the root of the spine (spine, thighs and tails hang off it, pivot at the rump): -Y on
# hips tips the whole body nose up, as when rearing; thighs then need +Y to stay under it.
SPINE = ["hips", "spine", "chest", "neck", "head"]
LEGS = ["thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R",
        "upperarm.L", "forearm.L", "hand.L", "upperarm.R", "forearm.R", "hand.R"]


def local_q(arm, bone, axis, deg):
    """Quaternion in the bone's local rest frame for a rotation about a creature-space axis."""
    rest = arm.data.bones[bone].matrix_local.to_quaternion()
    return rest.inverted() @ Quaternion(axis, math.radians(deg)) @ rest


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


def idle(t, cr):
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
    cr.sec(p, t, amp=16, speed=2, spread=10 + 10 * fan, lift=-6 * fan)
    return p, (0, 0.012 * sway, 0.014 * (br + 1) / 2)


def leap(t, cr):
    """Pounce across the board (the presenter moves the unit along an arc, roughly 0.1..0.85):
    crouch, spring with the body stretched out (forelegs reaching, hind legs kicked back),
    tuck the legs in the air, land with a dip."""
    p = {}
    crouch, stretch = bump(0, 0.12, 0.3, t), bump(0.1, 0.3, 0.65, t)
    tuck, land = bump(0.5, 0.7, 0.88, t), bump(0.8, 0.9, 1, t)
    add(p, "hips", Y, 12 * crouch - 10 * stretch)
    add(p, "chest", Y, 10 * crouch - 18 * stretch + 8 * land)
    add(p, "neck", Y, 10 * crouch - 10 * stretch)
    add(p, "head", Y, -12 * stretch + 10 * land)
    for arm in ("upperarm.L", "upperarm.R"):
        add(p, arm, Y, 30 * crouch - 60 * stretch + 35 * tuck)
    for fore in ("forearm.L", "forearm.R"):
        add(p, fore, Y, -30 * crouch + 15 * stretch - 50 * tuck)
    for thigh in ("thigh.L", "thigh.R"):
        add(p, thigh, Y, -25 * crouch + 55 * stretch - 30 * tuck)
    for shin in ("shin.L", "shin.R"):
        add(p, shin, Y, 30 * crouch - 25 * stretch + 45 * tuck)
    cr.sec(p, t, amp=10, speed=1.5, spread=10, lift=20 * stretch - 10 * land)
    return p, (0, 0, -0.04 * crouch - 0.03 * land)


def attack(t, cr):
    """Claw and bite, 0.8 s, contact at 0.42: rears back with a paw raised (anticipation),
    snaps forward fast, overshoots and holds the pose a beat, then settles."""
    p = {}
    wind = bump(0, 0.32, 0.42, t)
    strike = ease(0.34, 0.42, t) * (1 - ease(0.62, 1, t))
    over = bump(0.42, 0.48, 0.62, t)
    add(p, "hips", Y, -14 * wind + 6 * strike)
    add(p, "chest", Y, -20 * wind + 10 * strike + 4 * over)
    add(p, "neck", Y, -25 * wind + 8 * strike)
    add(p, "head", Y, -25 * wind + 15 * strike + 6 * over)
    add(p, "head", Z, 10 * wind - 12 * strike)
    add(p, "upperarm.L", Y, -75 * wind + 70 * strike)  # paw high, then raked down
    add(p, "forearm.L", Y, 70 * wind - 30 * strike)
    add(p, "upperarm.R", Y, -20 * wind + 25 * strike)
    for thigh in ("thigh.L", "thigh.R"):
        add(p, thigh, Y, 14 * wind - 12 * strike)
    for shin in ("shin.L", "shin.R"):
        add(p, shin, Y, 10 * wind + 15 * strike)
    cr.sec(p, t, amp=8 + 12 * strike, speed=2, spread=30 * wind + 10 * strike, lift=-20 * wind)
    return p, (-0.08 * wind + 0.14 * strike, 0, 0.05 * wind - 0.02 * strike)


def cast(t, cr):
    """Ability, 1.5 s, release at 0.5 s (t = 0.33): rears up on the hind legs (Zheng: tails fanned
    wide and raised), front paws beating; then throws the head and chest forward as the ability
    leaves (Zheng: flames from the tail tips), and drops back to all fours."""
    p = {}
    up = ease(0, 0.28, t) * (1 - ease(0.6, 0.95, t))
    throw = bump(0.28, 0.36, 0.6, t)
    paws = up * (1 - throw) * math.sin(2 * math.pi * 4 * t)
    add(p, "hips", Y, -40 * up + 20 * throw)  # rear up from the rump
    add(p, "chest", Y, -10 * up + 15 * throw)
    add(p, "neck", Y, -15 * up + 20 * throw)
    add(p, "head", Y, -10 * up + 30 * throw)
    for thigh in ("thigh.L", "thigh.R"):
        add(p, thigh, Y, 38 * up - 15 * throw)  # hind legs stay under the body
    for shin in ("shin.L", "shin.R"):
        add(p, shin, Y, 20 * up)
    add(p, "upperarm.L", Y, -55 * up + 30 * throw + 15 * paws)
    add(p, "upperarm.R", Y, -45 * up + 30 * throw - 15 * paws)
    add(p, "forearm.L", Y, 60 * up - 20 * throw)
    add(p, "forearm.R", Y, 50 * up - 20 * throw)
    cr.sec(p, t, amp=8 + 6 * throw, speed=3, spread=40 * up, lift=-35 * up + 15 * throw)
    return p, (-0.06 * up + 0.08 * throw, 0, 0.04 * up - 0.02 * throw)


def hit(t, cr):
    """Knocked back, 0.5 s: snaps away from the blow in two frames, then a damped wobble."""
    p = {}
    k = ease(0, 0.08, t) * math.exp(-5 * max(0.0, t - 0.08))
    wob = math.exp(-6 * t) * math.sin(2 * math.pi * 3 * t)
    add(p, "chest", Y, -20 * k)
    add(p, "neck", Y, -25 * k)
    add(p, "head", Y, -35 * k + 8 * wob)
    add(p, "head", X, 18 * k)
    add(p, "hips", X, 8 * wob)
    for thigh in ("thigh.L", "thigh.R"):
        add(p, thigh, Y, 15 * k)
    for arm in ("upperarm.L", "upperarm.R"):
        add(p, arm, Y, -20 * k)
    cr.sec(p, t, amp=18 * k + 6, speed=2, spread=-15 * k, lift=20 * k)
    return p, (-0.12 * k, 0, 0.03 * k)


def death(t, cr):
    """Staggers back from the killing blow, rears once, rolls onto its side and goes limp."""
    p = {}
    rear = bump(0, 0.15, 0.35, t)
    fall, slump = ease(0.2, 0.55, t), ease(0.45, 0.85, t)
    add(p, "chest", Y, -30 * rear)
    add(p, "head", Y, -30 * rear + 25 * slump)
    add(p, "root", X, 85 * fall)  # roll onto the right side
    add(p, "neck", Y, 20 * slump)
    for leg in ("thigh.L", "thigh.R"):
        add(p, leg, Y, -30 * fall + 15 * rear)
    for leg in ("upperarm.L", "upperarm.R"):
        add(p, leg, Y, 30 * fall - 30 * rear)
    for leg in ("shin.L", "shin.R", "forearm.L", "forearm.R"):
        add(p, leg, Y, 20 * slump)
    cr.sec(p, t, amp=14 * (1 - slump), speed=1.5, lift=25 * slump)
    return p, (-0.1 * rear, 0, 0.12 * fall + 0.04 * rear)


def awaken(t, cr):
    """Statue (rest pose) -> tremble -> head lifts and tails unfurl -> settle into idle's first pose."""
    p = {}
    shake = bump(0.05, 0.35, 0.55, t) * math.sin(2 * math.pi * 14 * t)
    rise = bump(0.45, 0.7, 1, t)
    add(p, "chest", X, 3 * shake)
    add(p, "head", Z, 4 * shake)
    add(p, "neck", Y, -20 * rise)
    add(p, "head", Y, -25 * rise)
    add(p, "chest", Y, -8 * rise)
    cr.sec(p, t, amp=12 * rise, speed=2, spread=30 * rise, lift=-10 * rise)
    idle_p, _ = clip_fn(cr, "idle")(0, cr)
    w = ease(0.85, 1, t)
    for b, rots in idle_p.items():
        for axis, deg in rots:
            add(p, b, axis, deg * w)
    return p, (0, 0, 0.03 * rise)


# name: (seconds, default fn, loops)
CLIPS = {"idle": (4.0, idle, True), "attack": (0.8, attack, False), "cast": (1.5, cast, False),
         "hit": (0.5, hit, False), "leap": (0.4, leap, False), "death": (1.5, death, False),
         "awaken": (2.0, awaken, False)}


def clip_fn(cr, name):
    return getattr(cr, "CLIPS", {}).get(name, CLIPS[name][1])


def main(cr, src, out):
    bpy.ops.wm.open_mainfile(filepath=src)
    arm = bpy.data.objects["rig"]
    bpy.context.scene.render.fps = FPS
    for name, (sec, _, loop) in CLIPS.items():
        fn = clip_fn(cr, name)
        c = Clip(arm, name, sec, loop)
        c.sample(lambda t: fn(t, cr), 1 if sec <= 1 else 2)  # fast moves need every frame
        c.push()
        print("CLIP", name, c.n + 1, "frames")
    bpy.ops.wm.save_as_mainfile(filepath=out)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    main(importlib.import_module(f"creatures.{a[0]}"), a[1], a[2])
