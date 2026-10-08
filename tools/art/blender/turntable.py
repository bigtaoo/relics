# Turntable check sheet of one or more Tripo glbs: four sides plus the board camera, one row each.
# usage: blender -b --python turntable.py -- <out.png> <a.glb> [b.glb ...]
# Workbench with the base colour texture, so the sheet shows shape and texture, not lighting.
# glb space: head +X, left +Y, up +Z.
import bpy, sys, math, os, tempfile
from mathutils import Vector

SIZE = 320
# (name, camera direction from the centre, looking back at it)
VIEWS = [("front", (1, 0, 0)), ("left", (0, 1, 0)), ("back", (-1, 0, 0)), ("right", (0, -1, 0)),
         ("board", (0.5, 0.5, 1))]  # roughly the 45 degree tabletop camera


def render_row(glb, tmp, row):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.render.resolution_x = scene.render.resolution_y = SIZE
    scene.world = bpy.data.worlds.new("white")
    scene.world.color = (1, 1, 1)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = 'ORTHO'
    scene.collection.objects.link(cam)
    scene.camera = cam
    pts = [o.matrix_world @ Vector(c) for o in scene.objects if o.type == 'MESH' for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    centre, size = (lo + hi) / 2, max(hi - lo)
    cam.data.ortho_scale = size * 1.3
    files = []
    for name, d in VIEWS:
        d = Vector(d).normalized()
        cam.location = centre + d * size * 4
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(tmp, f"{row}_{name}.png")
        bpy.ops.render.render(write_still=True)
        files.append(scene.render.filepath)
    return files


def main(out, glbs):
    tmp = tempfile.mkdtemp()
    rows = [render_row(g, tmp, i) for i, g in enumerate(glbs)]
    sheet = bpy.data.images.new("sheet", SIZE * len(VIEWS), SIZE * len(rows))
    px = [1.0] * (SIZE * len(VIEWS) * SIZE * len(rows) * 4)
    for r, files in enumerate(rows):
        y0 = (len(rows) - 1 - r) * SIZE  # first glb on top
        for c, f in enumerate(files):
            img = bpy.data.images.load(f)
            src = list(img.pixels)
            for y in range(SIZE):
                i0 = ((y0 + y) * SIZE * len(VIEWS) + c * SIZE) * 4
                px[i0:i0 + SIZE * 4] = src[y * SIZE * 4:(y + 1) * SIZE * 4]
    sheet.pixels = px
    sheet.filepath_raw, sheet.file_format = out, 'PNG'
    sheet.save()
    print("TURNTABLE", out, [os.path.basename(g) for g in glbs])


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    main(a[0], a[1:])
