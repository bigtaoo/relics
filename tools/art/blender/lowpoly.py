# Low-poly copy of an animated rig for summons and other crowd units (design/08 §2 crowd test):
# collapse-decimates the skinned mesh to ~target triangles, keeping weights, vertex colours, UVs
# and every action, and saves a new .blend for export_fbx.py.
# usage: blender -b --python lowpoly.py -- <anim.blend> <out.blend> [target_tris]
import bpy, sys


def main(src, out, target):
    bpy.ops.wm.open_mainfile(filepath=src)
    arm = bpy.data.objects["rig"]
    for mesh in [o for o in arm.children if o.type == 'MESH']:
        before = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
        mod = mesh.modifiers.new("decimate", 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = min(1.0, target / before)
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = mesh
        # First in the stack, or applying it would also bake the armature's current pose.
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        print("LOWPOLY", mesh.name, "tris", before, "->", len(mesh.data.polygons), "verts", len(mesh.data.vertices))
    bpy.ops.wm.save_as_mainfile(filepath=out)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    main(a[0], a[1], int(a[2]) if len(a) > 2 else 400)
