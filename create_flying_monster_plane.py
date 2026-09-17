import bpy, math, os
from mathutils import Vector

# Reset
bpy.ops.wm.read_factory_settings(use_empty=True)

# Helpers

def mat(name, color, metallic=0.0, roughness=0.5, emission=None):
    m=bpy.data.materials.new(name)
    m.diffuse_color=(*color,1)
    m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Metallic'].default_value=metallic
    bs.inputs['Roughness'].default_value=roughness
    if emission:
        bs.inputs['Emission Color'].default_value=(*emission,1)
        bs.inputs['Emission Strength'].default_value=4.0
    return m

def apply(obj, material):
    obj.data.materials.append(material)
    bpy.context.view_layer.objects.active=obj
    obj.select_set(True)
    bpy.ops.object.shade_smooth()
    obj.select_set(False)

def ico(name, loc, scale, material, sub=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub, radius=1, location=loc)
    o=bpy.context.object; o.name=name; o.scale=scale; apply(o,material); return o

def uv(name, loc, scale, material, seg=24, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, location=loc)
    o=bpy.context.object; o.name=name; o.scale=scale; apply(o,material); return o

def cone(name, loc, radius1, radius2, depth, rot, material, verts=12):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=radius1, radius2=radius2, depth=depth, location=loc, rotation=rot)
    o=bpy.context.object; o.name=name; apply(o,material); return o

def cyl(name, loc, radius, depth, rot, material, verts=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot)
    o=bpy.context.object; o.name=name; apply(o,material); return o

def mesh_obj(name, verts, faces, material):
    me=bpy.data.meshes.new(name+'Mesh'); me.from_pydata(verts, [], faces); me.update()
    o=bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o); apply(o,material); return o

def look_at(obj, target):
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()

# Materials
body=mat('Monster Navy',(0.035,0.09,0.16), metallic=0.35, roughness=0.28)
body2=mat('Wing Steel',(0.07,0.16,0.25), metallic=0.45, roughness=0.3)
accent=mat('Demon Crimson',(0.42,0.018,0.025), metallic=0.15, roughness=0.32)
black=mat('Mouth Void',(0.006,0.002,0.003), roughness=0.24)
eye=mat('Toxic Eye',(0.9,0.2,0.015), roughness=0.2, emission=(1.0,0.03,0.0))
fang=mat('Fangs',(0.95,0.72,0.3), metallic=0.05, roughness=0.3)

# Main fuselage and nose
fuselage=uv('Fuselage',(0,0,0.25),(2.65,0.68,0.72),body,seg=24,rings=12)
# tapered nose cone pointing forward (+X)
nose=cone('Pointed_Nose',(2.65,0,0.25),0.7,0.06,1.65,(0,math.pi/2,0),body,verts=16)
# rear taper
rear=cone('Rear_Taper',(-2.3,0,0.28),0.55,0.16,1.2,(0,math.pi/2,0),body,verts=12)

# cockpit glass ridge
cockpit=uv('Cockpit',(0.65,-0.02,0.72),(0.95,0.48,0.3),black,seg=20,rings=10)
# cockpit highlight
cock_hi=uv('Cockpit_Glow',(0.72,-0.22,0.78),(0.52,0.08,0.13),eye,seg=16,rings=8)

# Wings as angular wedges
for side in (-1,1):
    y=side
    verts=[(0.75,0,0.18),(-0.75,0,0.18),(0.2,side*3.15,0.08),(-0.9,side*2.65,0.12),
           (0.75,0,-0.05),(-0.75,0,-0.05),(0.2,side*3.15,-0.02),(-0.9,side*2.65,0.0)]
    faces=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
    mesh_obj(('Left' if side<0 else 'Right')+'_Wing',verts,faces,body2)
    # wing edge crimson blade
    verts2=[(0.15,side*2.65,0.1),(-0.85,side*2.65,0.12),(0.2,side*3.15,0.08), (0.15,side*2.65,0.02),(-0.85,side*2.65,0.04),(0.2,side*3.15,0.0)]
    faces2=[(0,1,2),(3,5,4),(0,3,4,1),(2,5,3,0),(1,4,5,2)]
    mesh_obj(('Left' if side<0 else 'Right')+'_WingBlade',verts2,faces2,accent)

# Tail fins and stabilizers
for side in (-1,1):
    verts=[(-1.55,0,0.35),(-2.35,0,0.35),(-2.2,side*0.78,1.55),(-1.55,side*0.5,0.45),
           (-1.55,0,0.05),(-2.35,0,0.05),(-2.2,side*0.78,1.2),(-1.55,side*0.5,0.2)]
    faces=[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
    mesh_obj(('Left' if side<0 else 'Right')+'_TailFin',verts,faces,accent)
# horizontal tail
for side in (-1,1):
    verts=[(-1.6,0,0.3),(-2.55,0,0.3),(-2.85,side*1.15,0.15),(-1.9,side*0.7,0.25),(-1.6,0,0.1),(-2.55,0,0.1),(-2.85,side*1.15,0.05),(-1.9,side*0.7,0.08)]
    faces=[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
    mesh_obj(('Left' if side<0 else 'Right')+'_TailPlane',verts,faces,body2)

# Engines under wings
for side in (-1,1):
    y=side*1.55
    eng=cyl(('Left' if side<0 else 'Right')+'_Engine',(0.25,y,-0.52),0.38,1.9,(0,math.pi/2,0),body2,verts=16)
    # intake ring at front
    cyl(('Left' if side<0 else 'Right')+'_Intake',(1.2,y,-0.52),0.3,0.12,(0,math.pi/2,0),black,verts=16)
    cone(('Left' if side<0 else 'Right')+'_Exhaust',(-0.8,y,-0.52),0.3,0.16,0.45,(0,math.pi/2,0),accent,verts=12)

# Monster face: eyes on nose sides
for side in (-1,1):
    y=side*0.46
    e=uv(('Left' if side<0 else 'Right')+'_Eye',(2.15,y,0.42),(0.25,0.10,0.25),eye,seg=16,rings=8)
    # brow horn
    cone(('Left' if side<0 else 'Right')+'_Brow',(1.95,side*0.5,0.72),0.13,0.02,0.55,(0,side*0.45,0),accent,verts=8)

# Mouth under nose
mouth=uv('Mouth',(2.15,0,-0.25),(0.62,0.38,0.18),black,seg=20,rings=10)
# teeth cones, alternating
for i in range(5):
    yy=(i-2)*0.22
    cone('Fang_'+str(i),(2.18,yy,-0.34),0.07,0.015,0.28,(math.pi,0,0),fang,verts=8)

# dorsal spine spikes
for i,x in enumerate([-1.35,-0.75,-0.15,0.45,1.0]):
    cone('Spine_'+str(i),(x,0,0.9),0.16,0.0,0.5,(0,0,0),accent,verts=8)

# Small emblem ring on fuselage
bpy.ops.mesh.primitive_torus_add(major_radius=0.38, minor_radius=0.07, major_segments=16, minor_segments=8, location=(-0.45,-0.66,0.32), rotation=(math.pi/2,0,0))
em=bpy.context.object; em.name='Demon_Emblem'; apply(em,accent)

# Parent all mesh objects
bpy.ops.object.select_all(action='SELECT')
root=bpy.data.objects.new('FlyingMonsterPlane_ROOT',None); bpy.context.collection.objects.link(root)
for o in list(bpy.context.selected_objects):
    if o != root: o.parent=root
bpy.ops.object.select_all(action='DESELECT'); root.select_set(True); bpy.context.view_layer.objects.active=root

# Ground for render
bpy.ops.mesh.primitive_plane_add(size=30, location=(0,0,-1.15))
ground=bpy.context.object; ground.name='Display_Ground'; apply(ground,mat('Ground',(0.008,0.012,0.02),roughness=0.7))

# Lighting
bpy.ops.object.light_add(type='AREA', location=(4,-5,7)); key=bpy.context.object; key.name='Key'; key.data.energy=1000; key.data.shape='DISK'; key.data.size=5; look_at(key,(0,0,0))
bpy.ops.object.light_add(type='AREA', location=(-4,3,4)); fill=bpy.context.object; fill.name='Fill'; fill.data.energy=700; fill.data.size=4; look_at(fill,(0,0,0.3))
bpy.ops.object.light_add(type='POINT', location=(2,0,1)); rim=bpy.context.object; rim.data.energy=350; rim.data.color=(1,0.05,0.02)

# Camera
bpy.ops.object.camera_add(location=(8.5,-10.5,6.6))
cam=bpy.context.object; cam.name='Camera'; cam.data.lens=52; look_at(cam,(0,0,0.05)); bpy.context.scene.camera=cam

# World/render
world=bpy.data.worlds.new('MonsterWorld') if bpy.context.scene.world is None else bpy.context.scene.world
bpy.context.scene.world=world
world.color=(0.004,0.006,0.012)
world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(0.004,0.007,0.016,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=0.28
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=800; scene.render.resolution_y=800; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.filepath=os.path.abspath('Assets/FlyingMonsterPlane_preview.png')
scene.render.film_transparent=False
# color management
scene.view_settings.look='AgX - Medium High Contrast'
# Save and render
os.makedirs('Assets',exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath('Assets/FlyingMonsterPlane.blend'))
bpy.ops.render.render(write_still=True)
print('SAVED',os.path.abspath('Assets/FlyingMonsterPlane.blend'))
print('RENDER',os.path.abspath('Assets/FlyingMonsterPlane_preview.png'))



