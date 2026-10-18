# Procedural keyframe animations for a humanoid prepared by biped_rig.py (Tripo biped skeleton:
# Root at the feet, Hip > Pelvis > thighs, Hip > Waist > Spine01 > Spine02 > neck, clavicles, Head).
# usage: blender -b --python biped_anim.py -- <creature> <rig.blend> <out.blend>
# Same clip set, lengths and conventions as quadruped_anim.py: rotations are authored around
# creature-space axes (facing +X, left +Y, up +Z) and converted into each bone's rest frame. The
# game's timings hold: attack contact at 0.42 s, cast release at 0.5 s.
# Tripo's retargeted mocap presets were tried first and dropped (art/kuafu/README.md): they last
# 1.3..8.4 s, there is no death, and a chibi whose head is as wide as its shoulders folds over in
# them. Rotation conventions used below: +Y pitches forward (a hanging limb swings back), -Y swings
# a hanging limb forward; +X raises the left arm sideways, -X the right.
import bpy, importlib, os, sys, math
from mathutils import Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from quadruped_anim import X, Y, Z, add, bump, ease  # noqa: E402

FPS = 30
LEG = 0.28  # thigh + calf length, measured from the rig in main()
ARM = None  # the armature, for aim()


def rot(entries):
    """Creature-space rotation of a pose entry list: (axis, deg) pairs or Quaternions, applied in order."""
    q = Quaternion()
    for e in entries:
        q = (e if isinstance(e, Quaternion) else Quaternion(e[0], math.radians(e[1]))) @ q
    return q


def aim(pose, bone, rest_dir, target, w):
    """Turns `bone` so that rest_dir (creature space, e.g. a staff held in the hand) points at
    `target` in the posed body, blended by w. Call it last: it reads the whole parent chain.
    A bone's change from rest in creature space is the product of its ancestors' rotations and its own."""
    if w <= 0:
        return
    chain = Quaternion()
    for b in reversed(ARM.data.bones[bone].parent_recursive):
        chain = chain @ rot(pose.get(b.name, []))
    own = rot(pose.get(bone, []))
    want = chain.inverted() @ Vector(rest_dir).normalized().rotation_difference(Vector(target).normalized())
    if own.dot(want) < 0:
        want.negate()
    pose[bone] = [own.slerp(want, w)]


def crouch(p, deg):
    """Bends both knees by deg with the feet kept flat where they are; returns how far the hips drop."""
    for s in ("L", "R"):
        add(p, f"{s}_Thigh", Y, -deg)
        add(p, f"{s}_Calf", Y, 2 * deg)
        add(p, f"{s}_Foot", Y, -deg)
    return LEG * (1 - math.cos(math.radians(deg)))


def base(p, w=1.0):
    """Relaxed stance shared by every clip: the T-ish bind pose (the statue) lowers its arms."""
    add(p, "L_Upperarm", X, -22 * w)
    add(p, "R_Upperarm", X, 18 * w)
    add(p, "L_Forearm", Y, -10 * w)


class Clip:
    def __init__(self, arm, name, seconds):
        self.arm, self.n = arm, int(seconds * FPS)
        self.action = bpy.data.actions.new(name)
        self.action.use_fake_user = True
        arm.animation_data_create()
        arm.animation_data.action = self.action
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'

    def key(self, frame, pose, root):
        """pose: {bone: entries}, unlisted bones rest; root: Root offset in creature space."""
        for pb in self.arm.pose.bones:
            rest = self.arm.data.bones[pb.name].matrix_local.to_quaternion()
            pb.rotation_quaternion = rest.inverted() @ rot(pose.get(pb.name, [])) @ rest
            pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb = self.arm.pose.bones["Root"]
        pb.location = self.arm.data.bones["Root"].matrix_local.to_quaternion().inverted() @ Vector(root)
        pb.keyframe_insert("location", frame=frame)

    def sample(self, fn, step):
        for f in range(0, self.n + 1, step):
            pose, root = fn(f / self.n)
            self.key(f + 1, pose, root)
        self.action.frame_range = (1, self.n + 1)

    def push(self):
        track = self.arm.animation_data.nla_tracks.new()
        track.name = self.action.name
        track.strips.new(self.action.name, 1, self.action)
        track.mute = True
        self.arm.animation_data.action = None


def weapon(cr, p, target, w):
    if hasattr(cr, "WEAPON"):
        aim(p, cr.WEAPON[0], cr.WEAPON[1], target, w)


def idle(t, cr):
    """4 s loop, sized for the whole-table camera: two breaths with a bob, a weight shift side to
    side, the head looking left and right, and once per loop the weapon hand lifts and thumps
    the staff down with a little knee dip. Every term is periodic in t."""
    p = {}
    base(p)
    br = math.sin(2 * math.pi * 2 * t)
    sway = math.sin(2 * math.pi * t)
    add(p, "Spine01", Y, -5 * br)
    add(p, "Spine02", Y, -4 * br)
    add(p, "Head", Y, 6 * br)
    add(p, "Waist", X, 6 * sway)
    add(p, "Spine02", X, -3 * sway)
    add(p, "Waist", Z, 8 * math.sin(2 * math.pi * t + 0.6))
    add(p, "Head", Z, 24 * math.sin(2 * math.pi * t + 0.6))
    add(p, "L_Upperarm", Y, 10 * sway)
    add(p, "L_Upperarm", X, 6 * br)
    lift = bump(0.5, 0.62, 0.7, t)
    thump = bump(0.66, 0.72, 0.86, t)
    add(p, "R_Upperarm", Y, -40 * lift)
    add(p, "R_Forearm", Y, -25 * lift)
    drop = crouch(p, 14 * thump)
    cr.sec(p, t, amp=14, speed=2)
    return p, (0, 0.015 * sway, 0.012 * (br + 1) / 2 - drop)


def attack(t, cr):
    """Overhead smash, 0.8 s, contact at 0.42 s (t 0.525): winds the weapon arm up and back over
    the head with the body coiled back, brings it over the top and down in front with a step and a
    deep bend, holds the pose a beat, then settles."""
    p = {}
    base(p)
    wind = bump(0, 0.4, 0.525, t)
    strike = ease(0.43, 0.525, t) * (1 - ease(0.72, 1, t))
    over = bump(0.525, 0.58, 0.72, t)
    add(p, "Waist", Y, -12 * wind + 18 * strike)
    add(p, "Spine01", Y, -10 * wind + 14 * strike + 6 * over)
    add(p, "Spine02", Z, -20 * wind + 15 * strike)  # twist the weapon shoulder back, then through
    add(p, "Head", Y, -10 * wind + 8 * strike)
    add(p, "R_Upperarm", X, 30 * (wind + strike))  # into the body's plane, then over the top
    add(p, "R_Upperarm", Y, -190 * wind - 75 * strike)
    add(p, "R_Forearm", Y, -40 * wind)
    add(p, "L_Upperarm", Y, -30 * wind + 40 * strike)  # counter swing
    add(p, "L_Upperarm", X, 20 * wind)
    drop = crouch(p, 8 * wind + 22 * strike)
    add(p, "L_Thigh", Y, -25 * strike)  # lead leg steps out
    add(p, "L_Calf", Y, 10 * strike)
    weapon(cr, p, (-0.5, -0.2, 0.85), wind)
    weapon(cr, p, (0.9, -0.15, -0.4), strike)
    cr.sec(p, t, amp=8 + 14 * strike, speed=2)
    return p, (-0.04 * wind + 0.12 * strike, 0, -drop + 0.02 * wind)


def cast(t, cr):
    """Ability, 1.5 s, release at 0.5 s (t 0.33): rises on the toes with the weapon raised to the
    sky and the free hand up, looking up; at the release thrusts the weapon forward at the target
    and leans in, then drops back to the stance."""
    p = {}
    base(p)
    up = ease(0, 0.28, t) * (1 - ease(0.36, 0.5, t))
    throw = ease(0.3, 0.36, t) * (1 - ease(0.62, 0.95, t))
    shake = up * math.sin(2 * math.pi * 9 * t)
    add(p, "Waist", Y, -14 * up + 16 * throw)
    add(p, "Spine01", Y, -10 * up + 10 * throw)
    add(p, "Head", Y, -25 * up + 5 * throw)
    add(p, "R_Upperarm", X, -95 * up + 25 * throw)  # side raise to overhead
    add(p, "R_Upperarm", Y, -80 * throw)
    add(p, "L_Upperarm", X, 110 * up + 30 * throw + 4 * shake)
    add(p, "L_Upperarm", Y, -50 * throw)
    add(p, "L_Forearm", Y, -20 * up)
    for s in ("L", "R"):
        add(p, f"{s}_Foot", Y, 20 * up)  # on the toes
    drop = crouch(p, 18 * throw)
    add(p, "L_Thigh", Y, -18 * throw)
    weapon(cr, p, (0, -0.15, 1), up)
    weapon(cr, p, (1, -0.1, 0.15), throw)
    cr.sec(p, t, amp=10 + 10 * up, speed=3)
    return p, (0.06 * throw, 0, 0.035 * up - drop)


def hit(t, cr):
    """Knocked back, 0.5 s: snaps away from the blow in two frames, then a damped wobble."""
    p = {}
    base(p)
    k = ease(0, 0.08, t) * math.exp(-5 * max(0.0, t - 0.08))
    wob = math.exp(-6 * t) * math.sin(2 * math.pi * 3 * t)
    add(p, "Waist", Y, -16 * k)
    add(p, "Spine01", Y, -14 * k)
    add(p, "Head", Y, -30 * k + 8 * wob)
    add(p, "Head", X, 15 * k)
    add(p, "Waist", X, 8 * wob)
    add(p, "L_Upperarm", X, 45 * k)
    add(p, "R_Upperarm", X, -35 * k)
    add(p, "L_Upperarm", Y, 25 * k)
    add(p, "R_Upperarm", Y, 25 * k)
    drop = crouch(p, 12 * k)
    cr.sec(p, t, amp=18 * k + 6, speed=2)
    return p, (-0.1 * k, 0, -drop)


def leap(t, cr):
    """Jump across the board (the presenter moves the unit along an arc, roughly 0.1..0.85):
    crouch, spring up stretched with the arms thrown up, knees tucked in the air, land with a dip."""
    p = {}
    base(p)
    coil, stretch = bump(0, 0.12, 0.3, t), bump(0.1, 0.3, 0.65, t)
    tuck, land = bump(0.45, 0.65, 0.88, t), bump(0.8, 0.9, 1, t)
    add(p, "Waist", Y, 18 * coil - 10 * stretch + 10 * tuck + 12 * land)
    add(p, "Head", Y, -10 * coil - 10 * stretch)
    add(p, "L_Upperarm", X, 60 * stretch - 20 * coil)
    add(p, "L_Upperarm", Y, 40 * coil - 50 * stretch)
    add(p, "R_Upperarm", X, -60 * stretch + 20 * coil)
    add(p, "R_Upperarm", Y, 40 * coil - 50 * stretch)
    drop = crouch(p, 35 * coil + 30 * land)
    for s in ("L", "R"):
        add(p, f"{s}_Thigh", Y, 15 * stretch - 70 * tuck)
        add(p, f"{s}_Calf", Y, 90 * tuck)
        add(p, f"{s}_Foot", Y, 25 * stretch)
    cr.sec(p, t, amp=12, speed=1.5)
    return p, (0, 0, -drop + 0.04 * tuck)


def death(t, cr):
    """Staggers back from the killing blow, knees buckle, topples over backwards and goes limp,
    arms flung out."""
    p = {}
    base(p, 1 - ease(0.3, 0.7, t))
    rear = bump(0, 0.15, 0.35, t)
    buckle = bump(0.15, 0.35, 0.6, t)
    fall, slump = ease(0.3, 0.65, t), ease(0.55, 0.9, t)
    add(p, "Root", Y, -88 * fall)  # topple backwards about the feet
    add(p, "Waist", Y, -18 * rear + 10 * buckle)
    add(p, "Head", Y, -30 * rear + 20 * buckle - 25 * slump)
    add(p, "Head", Z, 25 * slump)
    add(p, "L_Upperarm", X, 50 * rear + 30 * fall)
    add(p, "R_Upperarm", X, -50 * rear - 30 * fall)
    add(p, "L_Upperarm", Y, -40 * fall)
    add(p, "R_Upperarm", Y, -40 * fall)
    drop = crouch(p, 30 * buckle + 15 * slump)
    for s in ("L", "R"):
        add(p, f"{s}_Thigh", Y, -20 * fall + 25 * slump)
    cr.sec(p, t, amp=14 * (1 - slump), speed=1.5)
    return p, (-0.08 * rear - 0.05 * fall, 0, -drop * (1 - fall) + 0.16 * fall)


def awaken(t, cr):
    """Statue (bind pose) -> tremble -> big stretch with both arms up and the head thrown back ->
    settles into idle's first pose."""
    p = {}
    shake = bump(0.05, 0.35, 0.55, t) * math.sin(2 * math.pi * 14 * t)
    stretch = bump(0.4, 0.62, 0.88, t)
    add(p, "Spine02", X, 3 * shake)
    add(p, "Head", Z, 4 * shake)
    add(p, "Waist", Y, -12 * stretch)
    add(p, "Head", Y, -25 * stretch)
    add(p, "L_Upperarm", X, 100 * stretch)
    add(p, "R_Upperarm", X, -100 * stretch)
    for s in ("L", "R"):
        add(p, f"{s}_Foot", Y, 18 * stretch)
    cr.sec(p, t, amp=12 * stretch, speed=2)
    idle_p, idle_root = idle(0, cr)
    w = ease(0.85, 1, t)
    for b, rots in idle_p.items():
        for axis, deg in rots:
            add(p, b, axis, deg * w)
    return p, (0, 0, 0.03 * stretch + idle_root[2] * w)


# name: (seconds, fn)
CLIPS = {"idle": (4.0, idle), "attack": (0.8, attack), "cast": (1.5, cast), "hit": (0.5, hit),
         "leap": (0.4, leap), "death": (1.5, death), "awaken": (2.0, awaken)}


def main(cr, src, out):
    global LEG, ARM
    bpy.ops.wm.open_mainfile(filepath=src)
    ARM = arm = bpy.data.objects["rig"]
    LEG = arm.data.bones["L_Thigh"].length + arm.data.bones["L_Calf"].length
    bpy.context.scene.render.fps = FPS
    for name, (sec, fn) in CLIPS.items():
        fn = getattr(cr, "CLIPS", {}).get(name, fn)
        c = Clip(arm, name, sec)
        c.sample(lambda t: fn(t, cr), 1 if sec <= 1 else 2)
        c.push()
        print("CLIP", name, c.n + 1, "frames")
    bpy.ops.wm.save_as_mainfile(filepath=out)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    main(importlib.import_module(f"creatures.{a[0]}"), a[1], a[2])
