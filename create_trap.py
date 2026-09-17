import bpy, math
from mathutils import Vector
# clear
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
# materials
def mat(name, color, metallic=0.0, rough=0.5):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.metallic=metallic; m.roughness=rough; return m
mat_base=mat('Trap Base Dark Metal',(0.12,0.15,0.18),0.75,0.28)
mat_edge=mat('Warning Edge',(0.75,0.22,0.035),0.35,0.3)
mat_inner=mat('Inner Groove',(0.035,0.045,0.055),0.6,0.22)
# base cylinder
bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=3.2, depth=0.38, location=(0,0,0.19))
base=bpy.context.object; base.name='Trap_Base_Disc'; base.data.materials.append(mat_base)
bev=base.modifiers.new('Softened edges','BEVEL'); bev.width=0.16; bev.segments=4
bpy.context.view_layer.objects.active=base; bpy.ops.object.modifier_apply(modifier=bev.name)
# top inset plate
bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=2.82, depth=0.12, location=(0,0,0.43))
top=bpy.context.object; top.name='Inset_Top_Plate'; top.data.materials.append(mat_inner)
bev=top.modifiers.new('Rounded inset','BEVEL'); bev.width=0.08; bev.segments=3
bpy.context.view_layer.objects.active=top; bpy.ops.object.modifier_apply(modifier=bev.name)
# outer orange ring torus
bpy.ops.mesh.primitive_torus_add(major_radius=3.02, minor_radius=0.12, major_segments=96, minor_segments=16, location=(0,0,0.48))
ring=bpy.context.object; ring.name='Outer_Warning_Ring'; ring.data.materials.append(mat_edge)
# inner groove ring
bpy.ops.mesh.primitive_torus_add(major_radius=2.35, minor_radius=0.055, major_segments=96, minor_segments=12, location=(0,0,0.51))
g=bpy.context.object; g.name='Inner_Groove_Ring'; g.data.materials.append(mat_base)
# center raised trigger disk
bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.72, depth=0.10, location=(0,0,0.53))
c=bpy.context.object; c.name='Center_Trigger_Pad'; c.data.materials.append(mat_base)
bev=c.modifiers.new('Trigger bevel','BEVEL'); bev.width=0.08; bev.segments=3
bpy.context.view_layer.objects.active=c; bpy.ops.object.modifier_apply(modifier=bev.name)
# radial spokes as slim boxes
for i in range(8):
    a=2*math.pi*i/8
    r=1.55
    bpy.ops.mesh.primitive_cube_add(location=(r*math.cos(a),r*math.sin(a),0.52), scale=(0.75,0.055,0.035))
    s=bpy.context.object; s.name=f'Radial_Lock_{i+1:02d}'; s.rotation_euler[2]=a; s.data.materials.append(mat_edge)
    bev=s.modifiers.new('Spoke bevel','BEVEL'); bev.width=0.04; bev.segments=2
# ground optional
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active=base
# set viewport
bpy.context.scene.world.color=(0.025,0.025,0.025)
# camera
bpy.ops.object.camera_add(location=(7.2,-7.2,6.4), rotation=(math.radians(58),0,math.radians(43)))
cam=bpy.context.object; bpy.context.scene.camera=cam
# point camera
def look(obj, target): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
look(cam,(0,0,0.3))
# light
bpy.ops.object.light_add(type='AREA', location=(2,-3,7)); bpy.context.object.data.energy=900; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=5
look(bpy.context.object,(0,0,0))
bpy.ops.object.light_add(type='AREA', location=(-4,2,3)); bpy.context.object.data.energy=500; bpy.context.object.data.size=4; look(bpy.context.object,(0,0,0.3))
# save
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc.blend')
