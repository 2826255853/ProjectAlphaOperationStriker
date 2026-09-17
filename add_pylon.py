import bpy
# open source
bpy.ops.wm.open_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc.blend')
# material
m=bpy.data.materials.get('Launcher Pylon Metal') or bpy.data.materials.new('Launcher Pylon Metal')
m.diffuse_color=(0.16,0.19,0.23,1); m.metallic=0.85; m.roughness=0.24
# central pylon
bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.26, depth=1.35, location=(0,0,1.20))
p=bpy.context.object; p.name='Launcher_Rotating_Pylon'; p.data.materials.append(m)
bev=p.modifiers.new('Pylon edge bevel','BEVEL'); bev.width=0.07; bev.segments=4
bpy.context.view_layer.objects.active=p; bpy.ops.object.modifier_apply(modifier=bev.name)
# collar at base
bpy.ops.mesh.primitive_torus_add(major_radius=0.33, minor_radius=0.065, major_segments=64, minor_segments=12, location=(0,0,0.58))
col=bpy.context.object; col.name='Pylon_Base_Collar'; col.data.materials.append(m)
# top cap
bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.31, depth=0.10, location=(0,0,1.90))
cap=bpy.context.object; cap.name='Pylon_Top_Cap'; cap.data.materials.append(m)
bev=cap.modifiers.new('Cap bevel','BEVEL'); bev.width=0.04; bev.segments=3
bpy.context.view_layer.objects.active=cap; bpy.ops.object.modifier_apply(modifier=bev.name)
# save new file
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_with_Pylon.blend')
