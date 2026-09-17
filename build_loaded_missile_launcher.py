import bpy
import math
from mathutils import Vector

SOURCE = r"C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher.blend"
OUTPUT = r"C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.blend"

bpy.ops.wm.open_mainfile(filepath=SOURCE)


def material(name, color, metallic=0.0, roughness=0.4):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, 1.0)
    mat.metallic = metallic
    mat.roughness = roughness
    bsdf = next((node for node in mat.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    return mat


def collection(name):
    col = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if col.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(col)
    return col


def move_to(obj, col):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    col.objects.link(obj)


def bevel(obj, width, segments=3):
    mod = obj.modifiers.new("Edge softening", "BEVEL")
    mod.width = width
    mod.segments = segments
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


loaded = collection("Loaded_Missiles")
post = collection("Post_Firing_State")

body_mat = material("Missile Body", (0.075, 0.10, 0.14), 0.82, 0.23)
nose_mat = material("Missile Nose", (0.28, 0.34, 0.40), 0.72, 0.20)
fin_mat = material("Missile Fins", (0.78, 0.12, 0.025), 0.48, 0.27)
detail_mat = material("Missile Details", (0.025, 0.032, 0.045), 0.80, 0.18)
guide_mat = material("Post Firing Guide", (0.95, 0.34, 0.035), 0.3, 0.28)


def add_x_fin(name, yc, zc, angle):
    # A swept trapezoid, transformed around the missile's longitudinal X axis.
    ca, sa = math.cos(angle), math.sin(angle)
    verts = []
    profile = [(-1.03, 0.12), (-0.72, 0.12), (-0.66, 0.47), (-0.94, 0.42)]
    for tangent in (-0.035, 0.035):
        for x, radial in profile:
            y = yc + radial * ca - tangent * sa
            z = zc + radial * sa + tangent * ca
            verts.append((x, y, z))
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2),
             (2, 6, 7, 3), (3, 7, 4, 0)]
    mesh = bpy.data.meshes.new(name + " Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(fin_mat)
    obj = bpy.data.objects.new(name, mesh)
    loaded.objects.link(obj)
    bevel(obj, 0.018, 2)
    return obj


def missile(index, yc):
    zc = 2.54
    root = bpy.data.objects.new(f"Missile_{index:02d}", None)
    loaded.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root["state_1"] = "Loaded on launcher"
    root["state_20"] = "Missile fired; bay empty"

    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.145, depth=2.10,
                                        location=(0.0, yc, zc), rotation=(0, math.radians(90), 0))
    body = bpy.context.object
    body.name = f"Missile_{index:02d}_Body"
    body.data.materials.append(body_mat)
    move_to(body, loaded)
    body.parent = root
    bevel(body, 0.035, 3)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24,
                                         location=(1.10, yc, zc), scale=(0.34, 0.145, 0.145))
    nose = bpy.context.object
    nose.name = f"Missile_{index:02d}_Rounded_Nose"
    nose.data.materials.append(nose_mat)
    move_to(nose, loaded)
    nose.parent = root

    bpy.ops.mesh.primitive_torus_add(major_radius=0.151, minor_radius=0.018,
                                     major_segments=48, minor_segments=10,
                                     location=(-0.86, yc, zc), rotation=(0, math.radians(90), 0))
    band = bpy.context.object
    band.name = f"Missile_{index:02d}_Tail_Band"
    band.data.materials.append(detail_mat)
    move_to(band, loaded)
    band.parent = root

    # 45, 135, 225, 315 degrees produces the requested X silhouette.
    for fin_no, deg in enumerate((45, 135, 225, 315), 1):
        fin = add_x_fin(f"Missile_{index:02d}_TailFin_{fin_no}", yc, zc, math.radians(deg))
        fin.parent = root

    return root


roots = [missile(1, -0.23), missile(2, 0.23)]

# Frame 1 is loaded, frame 20 has the first bay empty, frame 40 has both bays empty.
scene = bpy.context.scene
scene.frame_start = 1
scene.frame_end = 40
scene.frame_set(1)
scene["firing_states"] = "Frame 1: loaded | Frame 20: Missile 01 fired | Frame 40: both missiles fired"
for root, fire_frame in zip(roots, (20, 40)):
    parts = [root] + list(root.children)
    for part in parts:
        part.hide_viewport = False
        part.hide_render = False
        part.keyframe_insert(data_path="hide_viewport", frame=1)
        part.keyframe_insert(data_path="hide_render", frame=1)
        part.keyframe_insert(data_path="hide_viewport", frame=fire_frame - 1)
        part.keyframe_insert(data_path="hide_render", frame=fire_frame - 1)
        part.hide_viewport = True
        part.hide_render = True
        part.keyframe_insert(data_path="hide_viewport", frame=fire_frame)
        part.keyframe_insert(data_path="hide_render", frame=fire_frame)

# Persistent empty markers make the post-firing bays explicit without adding clutter to the render.
for i, y in enumerate((-0.23, 0.23), 1):
    marker = bpy.data.objects.new(f"Post_Firing_Empty_Bay_{i}", None)
    post.objects.link(marker)
    marker.empty_display_type = "CUBE"
    marker.empty_display_size = 0.22
    marker.location = (-0.15, y, 2.44)
    marker.hide_render = True
    marker["description"] = "Reserved empty missile bay after launch"

# Keep the original framing and set a useful default material preview/render state.
cam = scene.camera
if cam:
    cam.location = (8.8, -8.8, 7.5)
    cam.rotation_euler = (Vector((0.0, 0.0, 1.0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.frame_set(1)
scene.render.filepath = r"C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.png"
bpy.ops.wm.save_as_mainfile(filepath=OUTPUT)
