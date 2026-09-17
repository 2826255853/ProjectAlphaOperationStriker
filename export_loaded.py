import bpy
bpy.ops.wm.open_mainfile(filepath=r"C:\\Unity Project\\ProjectAlphaOperationStriker\\Trap_Base_Disc_Dual_Launcher_Loaded.blend")
# ensure frame loaded
bpy.context.scene.frame_set(1)
for o in bpy.context.scene.objects:
    o.hide_viewport=False; o.hide_render=False
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=r"C:\\Unity Project\\ProjectAlphaOperationStriker\\Assets\\Models\\Trap_Base_Disc_Dual_Launcher_Loaded.fbx", use_selection=True, apply_scale_options='FBX_SCALE_ALL', object_types={'EMPTY','MESH'}, add_leaf_bones=False, bake_anim=False)
