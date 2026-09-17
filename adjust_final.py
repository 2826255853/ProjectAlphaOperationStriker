import bpy, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.blend')
# shrink base XY to 1.9m diameter
base_names=['Trap_Base_Disc','Inset_Top_Plate','Outer_Warning_Ring','Inner_Groove_Ring','Center_Trigger_Pad']+[f'Radial_Lock_{i:02d}' for i in range(1,9)]
for n in base_names:\n o=bpy.data.objects.get(n)\n if o: pass
# raise and lengthen pylon
p=bpy.data.objects.get('Launcher_Rotating_Pylon')
if p:
 p.scale.z*=1.30; p.location.z+=0.20
for n in ['Pylon_Base_Collar']:\n o=bpy.data.objects.get(n)\n if o: pass; o.location.x*=0.297; o.location.y*=0.297
# move upper assembly up .30m
upper_prefix=['Dual_Missile_Launcher_Body','Missile_Rail_1','Missile_Rail_2','Launcher_End_Stop']
for o in bpy.data.objects:
 if o.name.startswith('Missile_') or o.name in upper_prefix or o.name=='Pylon_Top_Cap': o.location.z+=0.30
# X fin arrangement: rotate each fin around missile X axis 45 degrees and positions around center
for idx,yc in [(1,-0.23),(2,0.23)]:
 cx=-0.82; cz=2.53+0.30
 for j in range(1,5):
  o=bpy.data.objects.get(f'Missile_{idx:02d}_TailFin_{j}')
  if not o: continue
  dy=o.location.y-yc; dz=o.location.z-cz
  a=math.radians(45); o.location.y=yc+dy*math.cos(a)-dz*math.sin(a); o.location.z=cz+dy*math.sin(a)+dz*math.cos(a); o.rotation_euler.x+=a
# save
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Final.blend')


