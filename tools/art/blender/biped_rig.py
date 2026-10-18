# Prepares a Tripo biped auto-rig (animations/rig, model v1.0, spec tripo) for the toon pipeline.
# usage: blender -b --python biped_rig.py -- <creature> <rig.glb> <out.blend> [target triangles]
# Unlike quadruped_rig.py there is no joint table for the body and no bind: Tripo's skeleton and
# skin weights are kept (they were clean on Kuafu, see art/kuafu/README.md). The creature table in
# creatures/ only adds bones for parts the auto-rig does not know about (a weapon, ear snakes...),
# hands their vertices over by distance, and names the chains for the toon shader's part masks.
# Space is the rig glb's: feet at z 0, facing +X, left +Y.
import bpy, importlib, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from quadruped_rig import TARGET_TRIS, decimate, paint_parts  # noqa: E402


def add_bones(arm, bones):
    """bones: {name: (head, tail, parent)}, all deforming."""
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for name, (h, t, parent) in bones.items():
        b = eb.new(name)
        b.head, b.tail, b.use_deform = Vector(h), Vector(t), True
    for name, (h, t, parent) in bones.items():
        b = eb[name]
        b.parent = eb[parent]
        b.use_connect = (b.head - eb[parent].tail).length < 1e-4
    bpy.ops.object.mode_set(mode='OBJECT')


def claim(mesh, arm, parts):
    """Hands vertices to the added bones by distance to their segment.
    parts: {bone: (r, fade, owners, avoid)}: a vertex whose strongest weight is on one of `owners`
    and lies within r of the bone goes fully to it, fading out by r + fade; its other weights
    shrink to make room. avoid = (bone, d) or None skips vertices within d of that bone (a fist
    around a staff keeps its own weights)."""
    names = {g.index: g.name for g in mesh.vertex_groups}

    def seg(name):
        b = arm.data.bones[name]
        return arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local

    def dist(co, a, b):
        u = max(0.0, min(1.0, (co - a).dot(b - a) / (b - a).length_squared))
        return (co - a.lerp(b, u)).length

    claims = {}  # vertex -> {bone: w}
    for name, (r, fade, owners, avoid) in parts.items():
        a, b = seg(name)
        av = seg(avoid[0]) + (avoid[1],) if avoid else None
        n = 0
        for v in mesh.data.vertices:
            if not v.groups or names[max(v.groups, key=lambda g: g.weight).group] not in owners:
                continue
            co = mesh.matrix_world @ v.co
            if av and dist(co, av[0], av[1]) < av[2]:
                continue
            w = max(0.0, min(1.0, (r + fade - dist(co, a, b)) / fade))
            if w > 0:
                claims.setdefault(v.index, {})[name] = w
                n += 1
        print("CLAIM", name, n, "verts")
    groups = {name: mesh.vertex_groups.get(name) or mesh.vertex_groups.new(name=name) for name in parts}
    for vi, ws in claims.items():
        total = sum(ws.values())
        take = min(1.0, total)
        v = mesh.data.vertices[vi]
        for g in v.groups:
            g.weight *= 1 - take
        for name, w in ws.items():
            groups[name].add([vi], take * w / total, 'REPLACE')


def paint_region(mesh, owners, test):
    """Adds a region to the horn mask (B) that no bone covers on its own, e.g. hair: vertices whose
    strongest weight is on one of `owners` and whose position (rig space) passes `test`."""
    names = {g.index: g.name for g in mesh.vertex_groups}
    attr = mesh.data.color_attributes["parts"]
    n = 0
    for v in mesh.data.vertices:
        if v.groups and names[max(v.groups, key=lambda g: g.weight).group] in owners and test(mesh.matrix_world @ v.co):
            r, g, _, a = attr.data[v.index].color_srgb
            attr.data[v.index].color_srgb = (r, g, 1, a)
            n += 1
    print("REGION horn", n, "verts")


def build(spec, src, out, target=TARGET_TRIS):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    arm.name = arm.data.name = "rig"
    for o in [o for o in bpy.context.scene.objects if o.type == 'MESH' and not o.vertex_groups]:
        bpy.data.objects.remove(o)  # Tripo ships a stray unskinned icosphere
    mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    mesh.name = spec.NAME
    # Decimate without the armature modifier in the way, then put it back.
    for m in list(mesh.modifiers):
        mesh.modifiers.remove(m)
    decimate(mesh, target)  # vertex groups follow the collapse
    mesh.modifiers.new("rig", 'ARMATURE').object = arm
    add_bones(arm, spec.BONES)
    claim(mesh, arm, spec.PARTS)
    paint_parts(mesh, spec.TAIL_CHAINS, spec.HORN)
    if hasattr(spec, "HORN_REGION"):
        paint_region(mesh, *spec.HORN_REGION)
    ws = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    print("RIG height", round(max(v.z for v in ws) - min(v.z for v in ws), 3),
          "bones", len(arm.data.bones), "tris", len(mesh.data.polygons))
    bpy.ops.wm.save_as_mainfile(filepath=out)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    build(importlib.import_module(f"creatures.{a[0]}"), a[1], a[2], int(a[3]) if len(a) > 3 else TARGET_TRIS)
