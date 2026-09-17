import bpy
bpy.ops.wm.open_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.blend')
old=bpy.data.objects.get('Launcher_Rotating_Pylon')
if old: bpy.data.objects.remove(old, do_unlink=True)
m=bpy.data.materials.get('Launcher Pylon Metal') or bpy.data.materials.new('Launcher Pylon Metal'); m.diffuse_color=(0.16,0.19,0.23,1); m.metallic=0.85; m.roughness=0.24
bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.26, depth=2.0, location=(0,0,1.525))
p=bpy.context.object; p.name='Launcher_Rotating_Pylon_2m'; p.data.materials.append(m)
bev=p.modifiers.new('Pylon edge bevel','BEVEL'); bev.width=0.07; bev.segments=4; bpy.context.view_layer.objects.active=p; bpy.ops.object.modifier_apply(modifier=bev.name)
for o in bpy.context.scene.objects:
    if o.name.startswith(('Dual_Missile_Launcher_Body','Missile_Rail_','Launcher_End_Stop')) or o.name in ('Missile_01','Missile_02'):
        o.location.z += 0.65
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded_2m_Pylon.blend')
