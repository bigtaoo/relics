# Side-view check sheet of a creature's clips, straight from the animated .blend (no Unity round trip).
# usage: blender -b --python anim_sheet.py -- <anim.blend> <out_dir> [clip ...]
# Writes <out_dir>/<clip>_<k>.png, eight frames per clip across its length, rendered with
# Workbench from the creature's left side (head to the right of the image).
import bpy, sys, os, math
from mathutils import Vector

FRAMES = 8


def main(src, out, names):
    bpy.ops.wm.open_mainfile(filepath=src)
    scene = bpy.context.scene
    arm = bpy.data.objects["rig"]
    for track in arm.animation_data.nla_tracks:
        track.mute = True
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.render.resolution_x = scene.render.resolution_y = 320
    scene.render.film_transparent = False
    cam_data = bpy.data.cameras.new("side")
    cam_data.type = 'ORTHO'
    cam = bpy.data.objects.new("side", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    # Creature space: head +X, left +Y, up +Z; look from +Y (its left side) towards -Y.
    centre = arm.matrix_world @ Vector((0, 0, 0))
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    centre = (lo + hi) / 2
    size = max(hi - lo)
    cam_data.ortho_scale = size * 1.8
    cam.location = centre + Vector((0, size * 4, 0))
    cam.rotation_euler = (math.pi / 2, 0, math.pi)
    os.makedirs(out, exist_ok=True)
    for action in bpy.data.actions:
        name = action.name
        if names and name not in names:
            continue
        arm.animation_data.action = action
        if hasattr(arm.animation_data, "action_slot") and action.slots:
            arm.animation_data.action_slot = action.slots[0]
        a, b = action.frame_range
        for k in range(FRAMES):
            scene.frame_set(int(round(a + (b - a) * k / (FRAMES - 1))))
            scene.render.filepath = os.path.join(out, f"{name}_{k}.png")
            bpy.ops.render.render(write_still=True)
        print("SHEET", name)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    main(a[0], a[1], a[2:])
