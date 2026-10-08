# Builds a quadruped rig on a Tripo mesh and binds it with automatic (bone heat) weights.
# usage: blender -b --python quadruped_rig.py -- <creature> <in.glb> <out.blend> [target triangles]
# <creature> names a joint table in creatures/ (e.g. zheng, dangkang). Joint positions are in the
# imported glb's space (Z up, head toward +X, left +Y), read from orthographic renders and mesh
# slices of the model (see art/<creature>/README.md).
import bpy, bmesh, importlib, os, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

BIND_SCALE = 10  # bone heat fails on tiny meshes, so bind at 10x and scale back
TARGET_TRIS = 3000  # design/08 §2: 46 units on a full board; 5000 for close-ups is indistinguishable on the board


def creature(name):
    return importlib.import_module(f"creatures.{name}")


def remove_whiskers(mesh, w):
    """Tripo models whiskers as hair-thin tubes that render as floating sticks under the toon
    outline. Compare the mesh with a coarse voxel remesh of itself: whisker tips stick out of it,
    and flood-filling from the tips while staying outside picks the rest.
    w: voxel (glb scale, coarse enough that whiskers vanish), tip (seed distance outside the
    remesh), grow (keep growing while this far outside), max_z (thin parts above it are kept)."""
    proxy = mesh.copy()
    proxy.data = mesh.data.copy()
    bpy.context.scene.collection.objects.link(proxy)
    rm = proxy.modifiers.new("remesh", 'REMESH')
    rm.mode, rm.voxel_size = 'VOXEL', w["voxel"]
    pb = bmesh.new()
    pb.from_object(proxy, bpy.context.evaluated_depsgraph_get())
    tree = BVHTree.FromBMesh(pb)
    pb.free()
    bpy.data.objects.remove(proxy)

    def outside(co):
        loc, n, _, d = tree.find_nearest(co)
        return d if (co - loc).dot(n) > 0 else -d

    # glb meshes are split at UV seams; weld a copy for connectivity, map back by position.
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    seeds = [v for v in bm.verts if v.co.z < w["max_z"] and outside(v.co) > w["tip"]]
    sel, stack = set(seeds), list(seeds)
    while stack:
        v = stack.pop()
        for e in v.link_edges:
            o = e.other_vert(v)
            if o not in sel and outside(o.co) > w["grow"]:
                sel.add(o)
                stack.append(o)
    kd = KDTree(len(sel))
    for i, v in enumerate(sel):
        kd.insert(v.co, i)
    kd.balance()
    bm.free()

    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    doomed = [v for v in bm.verts if sel and kd.find(v.co)[2] < 1e-5]
    bmesh.ops.delete(bm, geom=doomed, context='VERTS')
    bm.to_mesh(mesh.data)
    bm.free()
    print("WHISKERS seeds", len(seeds), "removed verts", len(doomed))


def decimate(mesh, target):
    """Welds the glb's UV-seam splits (UVs stay per corner), then collapse-decimates to ~target triangles."""
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    before = len(bm.faces)
    bm.to_mesh(mesh.data)
    bm.free()
    mod = mesh.modifiers.new("decimate", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = min(1.0, target / before)
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.modifier_apply(modifier=mod.name)
    print("DECIMATE tris", before, "->", len(mesh.data.polygons), "verts", len(mesh.data.vertices))


def keep_largest_island(obj):
    """Whiskers and other thin bits become floating blobs after remesh; one disconnected
    island is enough to make the heat solve fail for every bone."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    seen, islands = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        stack, comp = [v], []
        seen.add(v)
        while stack:
            c = stack.pop()
            comp.append(c)
            for e in c.link_edges:
                o = e.other_vert(c)
                if o not in seen:
                    seen.add(o)
                    stack.append(o)
        islands.append(comp)
    islands.sort(key=len, reverse=True)
    print("PROXY islands", [len(i) for i in islands[:8]])
    bmesh.ops.delete(bm, geom=[v for i in islands[1:] for v in i], context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()


def bind(mesh, arm, voxel):
    """Bone heat fails on AI meshes (open shells, floating whiskers), so weight a watertight
    voxel-remeshed proxy instead and transfer its weights to the real mesh.
    voxel: proxy resolution at bind scale, small enough to keep thin limbs apart."""
    proxy = mesh.copy()
    proxy.data = mesh.data.copy()
    proxy.name = "weight_proxy"
    bpy.context.scene.collection.objects.link(proxy)
    rm = proxy.modifiers.new("remesh", 'REMESH')
    rm.mode, rm.voxel_size = 'VOXEL', voxel
    bpy.ops.object.select_all(action='DESELECT')
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = proxy
    bpy.ops.object.modifier_apply(modifier="remesh")
    keep_largest_island(proxy)
    print("PROXY verts", len(proxy.data.vertices))

    for obj in (proxy, mesh):
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.parent_set(type='ARMATURE_AUTO' if obj is proxy else 'ARMATURE_NAME')

    print("PROXY weighted verts", sum(1 for v in proxy.data.vertices if v.groups))
    dt = mesh.modifiers.new("weights", 'DATA_TRANSFER')
    dt.object, dt.use_vert_data, dt.data_types_verts = proxy, True, {'VGROUP_WEIGHTS'}
    dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src, dt.layers_vgroup_select_dst = 'ALL', 'NAME'
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.modifier_move_to_index(modifier="weights", index=0)
    bpy.ops.object.modifier_apply(modifier="weights")
    bpy.data.objects.remove(proxy)


def limit_weights(mesh, arm, local):
    """On a round body the heat solve lets small parts (ears, tusks, a pendant) claim the torso
    around them. local: {bone: r}; the bone keeps its weight within r of its segment and fades
    out by 2r. What it loses is shared among the vertex's other bones in proportion to their
    weights (to the parent bone if it has none)."""
    for name, r in local.items():
        bone = arm.data.bones[name]
        a, b = arm.matrix_world @ bone.head_local, arm.matrix_world @ bone.tail_local
        src, parent = mesh.vertex_groups[name], mesh.vertex_groups[bone.parent.name]
        moved = 0
        for v in mesh.data.vertices:
            w = next((g.weight for g in v.groups if g.group == src.index), 0)
            if not w:
                continue
            co = mesh.matrix_world @ v.co
            u = max(0.0, min(1.0, (co - a).dot(b - a) / (b - a).length_squared))
            keep = max(0.0, min(1.0, (2 * r - (co - a.lerp(b, u)).length) / r))
            if keep == 1:
                continue
            lost = w * (1 - keep)
            others = [(g.group, g.weight) for g in v.groups if g.group != src.index and g.weight > 0]
            total = sum(x for _, x in others)
            src.add([v.index], w * keep, 'REPLACE')
            if total:
                for gi, x in others:
                    mesh.vertex_groups[gi].add([v.index], x + lost * x / total, 'REPLACE')
            else:
                parent.add([v.index], lost, 'ADD')
            moved += 1
        print("LIMIT", name, r, "verts", moved)


def paint_parts(mesh, tails, horn):
    """Body-part masks for the toon shader's material variants, from the skin weights:
    R = position along a tail chain (0 root, 1 tip), G = tail, B = horn (or tusks).
    tails: bone chains, root to tip; horn: bone names."""
    pos = {mesh.vertex_groups[b].index: (i + 0.5) / len(chain) for chain in tails for i, b in enumerate(chain)}
    horn = {mesh.vertex_groups[b].index for b in horn}
    attr = mesh.data.color_attributes.new("parts", 'BYTE_COLOR', 'POINT')
    for v in mesh.data.vertices:
        tail = sum(g.weight for g in v.groups if g.group in pos)
        p = sum(g.weight * pos[g.group] for g in v.groups if g.group in pos) / tail if tail else 0
        h = sum(g.weight for g in v.groups if g.group in horn)
        attr.data[v.index].color_srgb = (p, min(tail, 1), min(h, 1), 1)


def build(spec, src, out, target=TARGET_TRIS):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
    mesh.name = spec.NAME
    # Bake the importer's transform into the mesh so mesh and armature share one space.
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    if mesh.parent:
        bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if spec.WHISKERS:
        remove_whiskers(mesh, spec.WHISKERS)
    decimate(mesh, target)
    mesh.scale = (BIND_SCALE,) * 3
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o.type == 'EMPTY':
            bpy.data.objects.remove(o)

    arm = bpy.data.objects.new("rig", bpy.data.armatures.new("rig"))
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for name, (h, t, parent, deform) in spec.BONES.items():
        b = eb.new(name)
        b.head, b.tail, b.use_deform = Vector(h) * BIND_SCALE, Vector(t) * BIND_SCALE, deform
    for name, (h, t, parent, deform) in spec.BONES.items():
        if parent:
            b = eb[name]
            b.parent = eb[parent]
            b.use_connect = (b.head - eb[parent].tail).length < 1e-4
    bpy.ops.object.mode_set(mode='OBJECT')

    bind(mesh, arm, spec.VOXEL)
    # Back to glb scale: the mesh is parented to the rig, so scale the rig only, then apply on both.
    bpy.ops.object.select_all(action='DESELECT')
    arm.scale = (1 / BIND_SCALE,) * 3
    for o in (mesh, arm):
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    limit_weights(mesh, arm, getattr(spec, "LOCAL", {}))
    paint_parts(mesh, spec.TAIL_CHAINS, spec.HORN)
    empty = [g.name for g in mesh.vertex_groups
             if not any(g.index in [e.group for e in v.groups if e.weight > 0.01] for v in mesh.data.vertices)]
    bpy.context.view_layer.update()
    ws = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    print("RIG height", round(max(v.z for v in ws) - min(v.z for v in ws), 3))
    print("RIG bones", len(spec.BONES), "groups", len(mesh.vertex_groups), "empty groups", empty)
    bpy.ops.wm.save_as_mainfile(filepath=out)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    build(creature(a[0]), a[1], a[2], int(a[3]) if len(a) > 3 else TARGET_TRIS)
