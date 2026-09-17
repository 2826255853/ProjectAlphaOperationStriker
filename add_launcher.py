import bpy, math
bpy.ops.wm.open_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_with_Pylon.blend')
# materials
m=bpy.data.materials.get('Launcher Body Metal') or bpy.data.materials.new('Launcher Body Metal')
m.diffuse_color=(0.10,0.13,0.17,1); m.metallic=0.82; m.roughness=0.26
railmat=bpy.data.materials.get('Launcher Rail') or bpy.data.materials.new('Launcher Rail')
railmat.diffuse_color=(0.32,0.36,0.42,1); railmat.metallic=0.7; railmat.roughness=0.3
# body, horizontal X axis, sized for two missiles side-by-side
bpy.ops.mesh.primitive_cube_add(location=(0,0,2.08), scale=(1.45,0.52,0.22))
body=bpy.context.object; body.name='Dual_Missile_Launcher_Body'; body.data.materials.append(m)
bev=body.modifiers.new('Launcher rounded edges','BEVEL'); bev.width=0.12; bev.segments=4
bpy.context.view_layer.objects.active=body; bpy.ops.object.modifier_apply(modifier=bev.name)
# two parallel top rails indicating missile bays
for i,y in enumerate((-0.23,0.23),1):
    bpy.ops.mesh.primitive_cube_add(location=(0,y,2.34), scale=(1.18,0.075,0.035))
    rail=bpy.context.object; rail.name=f'Missile_Rail_{i}'; rail.data.materials.append(railmat)
    bev=rail.modifiers.new('Rail bevel','BEVEL'); bev.width=0.035; bev.segments=2
    bpy.context.view_layer.objects.active=rail; bpy.ops.object.modifier_apply(modifier=bev.name)
# end caps / bay separators
for x in (-1.18,1.18):
    bpy.ops.mesh.primitive_cube_add(location=(x,0,2.33), scale=(0.06,0.46,0.07))
    e=bpy.context.object; e.name='Launcher_End_Stop'; e.data.materials.append(railmat)
# save
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher.blend')
