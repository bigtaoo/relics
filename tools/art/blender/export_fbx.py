# Exports a rigged, animated .blend to FBX for Unity: every action becomes one take.
# usage: blender -b --python export_fbx.py -- <in.blend> <out.fbx>
# Also writes the base colour texture next to the FBX as <name>_basecolor.png (the toon
# material in Unity only needs that one; PBR maps are not exported).
# The rig is authored facing +X (Tripo glb space); it is turned to face -Y, Blender's front,
# which Unity imports as facing +Z.
import bpy, sys, math, os


def save_basecolor(objs, path):
    for o in objs:
        for slot in o.material_slots:
            bsdf = next(n for n in slot.material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
            link = bsdf.inputs["Base Color"].links[0]
            img = link.from_node.image
            img.filepath_raw, img.file_format = path, 'PNG'
            img.save()
            print("TEXTURE", path, tuple(img.size))
            return


def main(src, out):
    bpy.ops.wm.open_mainfile(filepath=src)
    arm = bpy.data.objects["rig"]
    arm.animation_data.action = None
    for tr in arm.animation_data.nla_tracks:
        tr.mute = True
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    arm.rotation_euler = (0, 0, -math.pi / 2)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    for o in arm.children:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        path_mode='STRIP', embed_textures=False,
        colors_type='SRGB')  # the parts mask is stored as raw bytes (rig_zheng.paint_parts)
    save_basecolor(arm.children, os.path.splitext(out)[0] + "_basecolor.png")
    print("FBX", out, [a.name for a in bpy.data.actions])


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:]
    main(a[0], a[1])
