import bpy
import math
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
def material(name,color,metallic=0.0,roughness=0.65):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1.0); m.metallic=metallic; m.roughness=roughness; return m
dark=material('Sentry_Dark',(0.035,0.045,0.055),0.65,0.3)
body=material('Sentry_Armor',(0.16,0.20,0.22),0.75,0.32)
accent=material('Sentry_Accent',(0.72,0.24,0.055),0.35,0.4)
glass=material('Sentry_Sensor',(0.02,0.24,0.32),0.5,0.18)
def cube(name,loc,scale,mat,parent=None,bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=loc); o=bpy.context.object; o.name=name; o.scale=scale; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); o.data.materials.append(mat)
    if bevel: mod=o.modifiers.new('Edge Bevel','BEVEL'); mod.width=bevel; mod.segments=2
    if parent: o.parent=parent
    return o
def cyl(name,loc,radius,depth,mat,parent=None,rot=(0,0,0),verts=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=radius,depth=depth,location=loc,rotation=rot); o=bpy.context.object; o.name=name; o.data.materials.append(mat)
    if parent: o.parent=parent
    return o
def sphere(name,loc,scale,mat,parent=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,location=loc); o=bpy.context.object; o.name=name; o.scale=scale; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); o.data.materials.append(mat)
    if parent: o.parent=parent
    return o
root=bpy.data.objects.new('SentryMachineGun',None); bpy.context.collection.objects.link(root)
base=bpy.data.objects.new('Base',None); bpy.context.collection.objects.link(base); base.parent=root
cube('Base_Plate',(0,0,0.10),(0.62,0.62,0.10),dark,base,0.06)
cyl('Base_Turntable',(0,0,0.27),0.46,0.22,body,base,verts=20)
cyl('Base_Ring',(0,0,0.40),0.33,0.08,accent,base,verts=20)
pivot=bpy.data.objects.new('Aim Pivot',None); bpy.context.collection.objects.link(pivot); pivot.parent=root; pivot.location=(0,0,0.48)
cube('Armored_Housing',(0,0,0.82),(0.42,0.30,0.28),body,pivot,0.08)
cube('Front_Armor',(0,-0.30,0.84),(0.30,0.05,0.18),dark,pivot,0.03)
sphere('Target_Sensor',(0,-0.37,1.00),(0.10,0.035,0.10),glass,pivot)
for x in (-0.13,0.13):
    cyl('MachineGun_Barrel',(x,0,1.28),0.055,0.92,dark,pivot,rot=(math.radians(90),0,0),verts=12)
    cyl('Barrel_HeatGuard',(x,0,1.10),0.09,0.16,accent,pivot,rot=(math.radians(90),0,0),verts=12)
cube('Rear_Ammo_Box',(0,0.22,0.83),(0.25,0.18,0.22),dark,pivot,0.04)
muzzle=bpy.data.objects.new('Muzzle',None); bpy.context.collection.objects.link(muzzle); muzzle.parent=pivot; muzzle.location=(0,0,1.76)
out=r'C:\Unity Project\ProjectAlphaOperationStriker\Assets\Models\SentryMachineGun.fbx'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out,use_selection=True,apply_scale_options='FBX_SCALE_ALL',object_types={'MESH','EMPTY'},bake_space_transform=False)
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Assets\Models\SentryMachineGun.blend')

