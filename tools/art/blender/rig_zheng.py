# Builds the Zheng quadruped rig on a Tripo mesh and binds it with automatic (bone heat) weights.
# usage: blender -b --python rig_zheng.py -- <in.glb> <out.blend> [target triangles]
# Joint positions are in the imported glb's space (Z up, head toward +X), read from
# orthographic renders and mesh slices of toon_v1.glb (see art/zheng/model/README.md).
import bpy, bmesh, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

# name: (head, tail, parent, deform)
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
BIND_SCALE = 10  # bone heat fails on tiny meshes, so bind at 10x and scale back
VOXEL = 0.08  # proxy resolution at bind scale; small enough to keep tails and legs apart

for t, pts in enumerate(TAILS, 1):
    for i in range(len(pts) - 1):
        BONES[f"tail{t}.{i + 1}"] = (pts[i], pts[i + 1], "hips" if i == 0 else f"tail{t}.{i}", True)


TARGET_TRIS = 3000  # design/08 §2: 46 units on a full board; 5000 for close-ups is indistinguishable on the board

WHISKER_VOXEL = 0.01  # glb scale; coarse enough that whiskers vanish from the remesh
WHISKER_TIP = 0.02  # vertices this far outside the remesh seed the whisker selection
WHISKER_GROW = 0.004  # the selection grows along the mesh while still this far outside
WHISKER_MAX_Z = 0.2  # the horn tip is thin too; keep it


def remove_whiskers(mesh):
    """Tripo models the whiskers as hair-thin tubes that render as floating sticks under the
    toon outline. Compare the mesh with a coarse voxel remesh of itself: whisker tips stick
    out of it, and flood-filling from the tips while staying outside picks the rest."""
    proxy = mesh.copy()
    proxy.data = mesh.data.copy()
    bpy.context.scene.collection.objects.link(proxy)
    rm = proxy.modifiers.new("remesh", 'REMESH')
    rm.mode, rm.voxel_size = 'VOXEL', WHISKER_VOXEL
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
    seeds = [v for v in bm.verts if v.co.z < WHISKER_MAX_Z and outside(v.co) > WHISKER_TIP]
    sel, stack = set(seeds), list(seeds)
    while stack:
        v = stack.pop()
        for e in v.link_edges:
            o = e.other_vert(v)
            if o not in sel and outside(o.co) > WHISKER_GROW:
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


def bind(mesh, arm):
    """Bone heat fails on AI meshes (open shells, floating whiskers), so weight a watertight
    voxel-remeshed proxy instead and transfer its weights to the real mesh."""
    proxy = mesh.copy()
    proxy.data = mesh.data.copy()
    proxy.name = "weight_proxy"
    bpy.context.scene.collection.objects.link(proxy)
    rm = proxy.modifiers.new("remesh", 'REMESH')
    rm.mode, rm.voxel_size = 'VOXEL', VOXEL
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


def paint_parts(mesh):
    """Body-part masks for the toon shader's material variants, from the skin weights:
    R = position along the tail (0 root, 1 tip), G = tail, B = horn."""
    tails = {mesh.vertex_groups[f"tail{t}.{i}"].index: (i - 0.5) / 4 for t in range(1, 6) for i in range(1, 5)}
    horn = mesh.vertex_groups["horn"].index
    attr = mesh.data.color_attributes.new("parts", 'BYTE_COLOR', 'POINT')
    for v in mesh.data.vertices:
        tail = sum(g.weight for g in v.groups if g.group in tails)
        pos = sum(g.weight * tails[g.group] for g in v.groups if g.group in tails) / tail if tail else 0
        h = sum(g.weight for g in v.groups if g.group == horn)
        attr.data[v.index].color_srgb = (pos, min(tail, 1), min(h, 1), 1)


def build(src, out, target=TARGET_TRIS):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
    mesh.name = "zheng"
    # Bake the importer's transform into the mesh so mesh and armature share one space.
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    if mesh.parent:
        bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    remove_whiskers(mesh)
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
    for name, (h, t, parent, deform) in BONES.items():
        b = eb.new(name)
        b.head, b.tail, b.use_deform = Vector(h) * BIND_SCALE, Vector(t) * BIND_SCALE, deform
    for name, (h, t, parent, deform) in BONES.items():
        if parent:
            b = eb[name]
            b.parent = eb[parent]
            b.use_connect = (b.head - eb[parent].tail).length < 1e-4
    bpy.ops.object.mode_set(mode='OBJECT')

    bind(mesh, arm)
    # Back to glb scale: the mesh is parented to the rig, so scale the rig only, then apply on both.
    bpy.ops.object.select_all(action='DESELECT')
    arm.scale = (1 / BIND_SCALE,) * 3
    for o in (mesh, arm):
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    paint_parts(mesh)
    empty =[g.name for g in mesh.vertex_groups
             if not any(g.index in [e.group for e in v.groups if e.weight > 0.01] for v in mesh.data.vertices)]
    bpy.context.view_layer.update()
    ws = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    print("RIG height", round(max(v.z for v in ws) - min(v.z for v in ws), 3))
    print("RIG bones", len(BONES), "groups", len(mesh.vertex_groups), "empty groups", empty)
    bpy.ops.wm.save_as_mainfile(filepath=out)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    build(a[0], a[1], int(a[2]) if len(a) > 2 else TARGET_TRIS)
