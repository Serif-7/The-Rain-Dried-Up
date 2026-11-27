# Retopologizes a rigged character .glb to a Kenshi-style budget (docs/retopology.md): decimated body, alpha hair cards rebuilt as a solid
# shell, one material with one baked texture atlas, joints renamed to 3ds Max Biped names for RaceImport. Run with Blender as Python:
#   python tools/Retopo/retopo.py --source <in.glb> --out <out stem> [--bones <map.json>] [--body-tris 6200] [--hair-tris 800]
#                                 [--hair <material>]... [--drop <material>]... [--hair-shade 1.0] [--atlas 2048]
# Writes <out>.glb and the baked <out>_d.png, <out>_n.png, <out>_mr.png.
import argparse
import json
import math
import sys

import bpy  # first: bmesh and mathutils come with it
import bmesh
import numpy as np
from mathutils import Vector


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    p = argparse.ArgumentParser()
    p.add_argument("--source", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--bones", help="JSON object: source joint name -> Biped name without 'Bip001 ' (e.g. \"pelvis\": \"Pelvis\")")
    p.add_argument("--body-tris", type=int, default=6200)
    p.add_argument("--hair-tris", type=int, default=800)
    p.add_argument("--hair", action="append", default=[], help="material of alpha hair cards to rebuild as a solid shell")
    p.add_argument("--drop", action="append", default=[], help="material to leave out (alpha decals, duplicate hair layers)")
    p.add_argument("--hair-shade", type=float, default=1.0, help="multiplies the hair colour (strand cards look darker than their texels)")
    p.add_argument("--atlas", type=int, default=2048)
    return p.parse_args(argv)


def tris(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


def only(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def copy(o, name):
    c = o.copy()
    c.data = o.data.copy()
    c.name = name
    bpy.context.collection.objects.link(c)
    return c


def drop_faces(o, materials):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    doomed = [f for f in bm.faces if o.data.materials[f.material_index].name in materials]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bm.to_mesh(o.data)
    bm.free()


def load_scene(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.find_armature() == arm]
    for o in bpy.data.objects:
        if o.type == "MESH" and o not in meshes: bpy.data.objects.remove(o)
    only(meshes[0])
    for o in meshes: o.select_set(True)
    if len(meshes) > 1: bpy.ops.object.join()
    return arm, bpy.context.view_layer.objects.active


def rename_joints(arm, bones_path):
    if bones_path:
        names = json.load(open(bones_path))
        for b in arm.data.bones:
            if b.name in names: b.name = "Bip001 " + names[b.name]
    missing = [n for n in ("Pelvis", "Spine", "Neck", "Head", "L UpperArm", "R Thigh") if "Bip001 " + n not in arm.data.bones
               and not any(b.name.startswith("mixamorig:") for b in arm.data.bones)]
    if missing: sys.exit(f"no Biped joints {missing}: pass --bones with a map from this rig's joint names")


def add_head_top(arm, src, hair_materials):
    """RaceImport scales by the head joint's height and points the head at mixamorig:HeadTop_End: the top of the skull, hair left out."""
    if "mixamorig:HeadTop_End" in arm.data.bones: return
    head_group = src.vertex_groups.get("Bip001 Head")
    hair = {i for i, m in enumerate(src.data.materials) if m.name in hair_materials}
    hair_verts = {v for p in src.data.polygons if p.material_index in hair for v in p.vertices}
    top = max((src.matrix_world @ v.co).z for v in src.data.vertices
              if v.index not in hair_verts and head_group and any(g.group == head_group.index and g.weight > 0.5 for g in v.groups))
    only(arm)
    bpy.ops.object.mode_set(mode="EDIT")
    head = arm.data.edit_bones["Bip001 Head"]
    bone = arm.data.edit_bones.new("mixamorig:HeadTop_End")
    bone.parent = head
    inv = arm.matrix_world.inverted()
    at = arm.matrix_world @ head.head
    bone.head = inv @ Vector((at.x, at.y, top))
    bone.tail = inv @ Vector((at.x, at.y, top + 0.05))
    bpy.ops.object.mode_set(mode="OBJECT")


def opaque_hair_image(image, shade):
    """The hair texture with its transparent texels filled by the opaque ones' mean colour, so rays that hit them bake hair, not black."""
    w, h = image.size
    px = np.array(image.pixels[:]).reshape(-1, 4)
    opaque = px[:, 3] > 0.5
    px[:, :3] *= shade
    px[~opaque, :3] = px[opaque, :3].mean(axis=0)
    px[:, 3] = 1
    filled = bpy.data.images.new(image.name + "_opaque", w, h)
    filled.pixels = px.ravel()
    return filled


def prepare_bake_source(src, hair_materials, shade):
    """Base colour wired to emission (a diffuse bake turns metallic parts black); hair cards made opaque."""
    for m in src.data.materials:
        nt = m.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        if m.name in hair_materials:
            tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
            tex.image = opaque_hair_image(tex.image, shade)
            if bsdf.inputs["Alpha"].is_linked: nt.links.remove(bsdf.inputs["Alpha"].links[0])
        colour = bsdf.inputs["Base Color"]
        if colour.is_linked: nt.links.new(colour.links[0].from_socket, bsdf.inputs["Emission Color"])
        else: bsdf.inputs["Emission Color"].default_value = colour.default_value
        bsdf.inputs["Emission Strength"].default_value = 1.0


def decimate(o, target):
    only(o)
    d = o.modifiers.new("decimate", "DECIMATE")
    d.decimate_type = "COLLAPSE"
    d.use_collapse_triangulate = True
    d.ratio = min(1.0, target / tris(o))
    bpy.ops.object.modifier_move_to_index(modifier="decimate", index=0)
    bpy.ops.object.modifier_apply(modifier="decimate")
    return d


def build_body(src, hair_materials, target):
    body = copy(src, "low")
    drop_faces(body, set(hair_materials))
    only(body)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    d = body.modifiers.new("decimate", "DECIMATE")
    d.decimate_type = "COLLAPSE"
    d.use_collapse_triangulate = True
    d.use_symmetry = True
    d.symmetry_axis = "X"
    d.ratio = min(1.0, target / tris(body))
    bpy.ops.object.modifier_move_to_index(modifier="decimate", index=0)
    bpy.ops.object.modifier_apply(modifier="decimate")
    return body


def build_hair(src, arm, hair_materials, target):
    """Kenshi's body textures carry gloss in alpha, not transparency: hair cards become one voxel-remeshed shell with their weights."""
    cards = copy(src, "hair_cards")
    drop_faces(cards, {m.name for m in src.data.materials if m.name not in hair_materials})
    shell = copy(cards, "hair_shell")
    shell.modifiers.clear()
    only(shell)
    r = shell.modifiers.new("remesh", "REMESH")
    r.mode = "VOXEL"
    r.voxel_size = 0.012
    bpy.ops.object.modifier_apply(modifier="remesh")
    decimate(shell, target)
    for g in cards.vertex_groups: shell.vertex_groups.new(name=g.name)
    t = shell.modifiers.new("weights", "DATA_TRANSFER")
    t.object = cards
    t.use_vert_data = True
    t.data_types_verts = {"VGROUP_WEIGHTS"}
    t.vert_mapping = "POLYINTERP_NEAREST"
    t.layers_vgroup_select_src = "ALL"
    t.layers_vgroup_select_dst = "NAME"
    bpy.ops.object.modifier_move_to_index(modifier="weights", index=0)
    bpy.ops.object.modifier_apply(modifier="weights")
    shell.modifiers.new("Armature", "ARMATURE").object = arm
    shell.parent = arm
    bpy.data.objects.remove(cards)
    return shell


def finish_low(low, hair):
    if hair:
        bpy.ops.object.select_all(action="DESELECT")
        hair.select_set(True)
        low.select_set(True)
        bpy.context.view_layer.objects.active = low
        bpy.ops.object.join()
    only(low)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    bpy.ops.object.shade_smooth()


def unwrap(low):
    uvs = low.data.uv_layers
    while len(uvs): uvs.remove(uvs[0])
    uvs.new(name="UVMap")
    only(low)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
    bpy.ops.uv.pack_islands(margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_atlas(src, low, size, out):
    low.data.materials.clear()
    mat = bpy.data.materials.new("atlas")
    mat.use_nodes = True
    low.data.materials.append(mat)
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]

    def image_node(name, px, colour):
        image = bpy.data.images.new(name, px, px, alpha=False)
        if not colour: image.colorspace_settings.name = "Non-Color"
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = image
        return node

    colour = image_node("d", size, True)
    normal = image_node("n", size, False)
    rough = image_node("mr", size // 2, False)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    bake = scene.render.bake
    bake.use_selected_to_active = True
    bake.cage_extrusion = 0.03
    bake.max_ray_distance = 0.08
    bake.margin = 8

    def run(node, kind):
        for n in nt.nodes: n.select = False
        node.select = True
        nt.nodes.active = node
        bpy.ops.object.select_all(action="DESELECT")
        src.select_set(True)
        low.select_set(True)
        bpy.context.view_layer.objects.active = low
        bpy.ops.object.bake(type=kind)

    run(colour, "EMIT")
    run(normal, "NORMAL")
    run(rough, "ROUGHNESS")

    # glTF metallic-roughness: roughness in green, metallic (blue) 0.
    px = np.array(rough.image.pixels[:]).reshape(-1, 4)
    r = px[:, 0].copy()
    px[:, 0] = 0
    px[:, 1] = r
    px[:, 2] = 0
    rough.image.pixels = px.ravel()

    nt.links.new(colour.outputs["Color"], bsdf.inputs["Base Color"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(normal.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    split = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(rough.outputs["Color"], split.inputs["Color"])
    nt.links.new(split.outputs["Green"], bsdf.inputs["Roughness"])
    bsdf.inputs["Metallic"].default_value = 0
    for node in (colour, normal, rough):
        node.image.filepath_raw = f"{out}_{node.image.name}.png"
        node.image.file_format = "PNG"
        node.image.save()


def main():
    a = parse_args()
    arm, src = load_scene(a.source)
    print(f"source: {len(src.data.vertices)} vertices, {tris(src)} triangles, {len(src.data.materials)} materials")
    rename_joints(arm, a.bones)
    add_head_top(arm, src, set(a.hair))
    if a.drop: drop_faces(src, set(a.drop))
    prepare_bake_source(src, set(a.hair), a.hair_shade)
    low = build_body(src, a.hair, a.body_tris)
    hair = build_hair(src, arm, a.hair, a.hair_tris) if a.hair else None
    finish_low(low, hair)
    unwrap(low)
    bake_atlas(src, low, a.atlas, a.out)
    bpy.data.objects.remove(src)
    print(f"low poly: {len(low.data.vertices)} vertices, {tris(low)} triangles, 1 material")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=a.out + ".glb", export_format="GLB", use_selection=True, export_skins=True,
                              export_animations=False, export_apply=False, export_image_format="JPEG")


main()
