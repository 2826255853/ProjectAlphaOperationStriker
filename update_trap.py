import bpy, math
from mathutils import Vector
path=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.blend'
bpy.ops.wm.open_mainfile(filepath=path)
# rotate missile fins from cross to X around missile longitudinal X axis
for i in (1,2):
    root=bpy.data.objects.get(f'Missile_{i:02d}')
    if not root: continue
    for j in range(1,5):
        f=bpy.data.objects.get(f'Missile_{i:02d}_TailFin_{j}')
        if not f: continue
        # rotate child world/local offset in YZ plane by 45 degrees
        rel=f.location - root.location
        a=math.radians(45); y,z=rel.y,rel.z
        f.location = root.location + Vector((rel.x, y*math.cos(a)-z*math.sin(a), y*math.sin(a)+z*math.cos(a)))
        f.rotation_euler.rotate_axis('X', a)
# scale all mesh model objects: XY gives base diameter 2m; Z gives overall height ~3m
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
for o in meshes:
    o.location.x*=0.3125; o.location.y*=0.3125; o.location.z*=1.17
    o.scale.x*=0.3125; o.scale.y*=0.3125; o.scale.z*=1.17
# adjust camera framing slightly
cam=bpy.context.scene.camera
if cam:
    cam.location.x*=0.55; cam.location.y*=0.55; cam.location.z*=0.75
# save overwrite original requested file
bpy.ops.wm.save_as_mainfile(filepath=path)
