import bpy, os
path=os.path.abspath('Assets/FlyingMonsterPlane.fbx')
# Select model meshes and root, exclude camera/lights/ground
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type=='MESH' and o.name!='Display_Ground': o.select_set(True)
if bpy.ops.object.mode_set.poll(): bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', add_leaf_bones=False, bake_anim=False)
print('FBX',path)
