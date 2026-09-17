import bpy, math
from mathutils import Vector
path=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.blend'
bpy.ops.wm.open_mainfile(filepath=path)
# scale modeled geometry: base diameter 6.4 -> 1.92; height ~2.65 -> ~2.97
xy=0.30; zf=1.12
for o in bpy.context.scene.objects:
    if o.type not in {'MESH'}: continue
    o.location.x*=xy; o.location.y*=xy; o.location.z*=zf
    o.scale.x*=xy; o.scale.y*=xy; o.scale.z*=zf
# rotate each fin around missile longitudinal X axis to X layout
for o in bpy.data.objects:
    if 'TailFin' in o.name:
        # rotate position around each missile root axis (origin at 0,0,0)
        x=o.location.x; y=o.location.y; z=o.location.z
        ang=math.radians(45)
        o.location.y = y*math.cos(ang)-z*math.sin(ang)
        o.location.z = y*math.sin(ang)+z*math.cos(ang)
        o.rotation_euler.x += ang
# keep camera framing and save over original
bpy.ops.wm.save_as_mainfile(filepath=path)
