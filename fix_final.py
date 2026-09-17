import bpy,math
bpy.ops.wm.open_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.blend')
p=bpy.data.objects['Launcher_Rotating_Pylon']; p.scale.z*=1.3; p.location.z+=.2
for o in bpy.data.objects:
 if o.name.startswith('Missile_') or o.name in ['Dual_Missile_Launcher_Body','Missile_Rail_1','Missile_Rail_2','Launcher_End_Stop','Pylon_Top_Cap']: o.location.z+=.3
for idx,yc in [(1,-.23),(2,.23)]:
 for j in range(1,5):
  o=bpy.data.objects.get(f'Missile_{idx:02d}_TailFin_{j}')
  if o: o.rotation_euler.x+=math.radians(45)
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Final.blend')
