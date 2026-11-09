#!/usr/bin/env python3
"""Author a reusable candidate from provided graph nodes; never instantiate tools."""
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent


def graph():
    nodes = []

    def add(kind, identifier, **properties):
        node = {"_class": kind, "Identifier": identifier, **properties}
        nodes.append(node)
        return identifier

    def link(identifier, output="Result"):
        return {"Identifier": identifier, "Output": output}

    def parameter(kind, identifier, name, value):
        dimension = {"Float": 1, "Float2": 2, "Float3": 3}[kind]
        zero = 0 if dimension == 1 else ",".join(["0"] * dimension)
        return add(kind, identifier, Name=name, Value=value, Min=zero, Max=zero, IsAttribute=False, UI={"Type": "Default"})

    uv0 = add("TextureCoord", "uv0", UseSecondaryCoord=False, Tiling="1,1")
    uv1 = add("TextureCoord", "uv1", UseSecondaryCoord=True, Tiling="1,1")
    one = parameter("Float", "constant_one", "", 1)

    def texture(role, srgb):
        key = role.lower()
        selector = parameter("Float", key + "_uv1", "Gltf" + role + "Uv1", 0)
        selected = add("Lerp", key + "_uv_selected", A=link(uv0), B=link(uv1), C=link(selector))
        affine_input = add("AppendVector", key + "_uv_affine", A=link(selected), B=link(one))
        row_u = parameter("Float3", key + "_row_u", "Gltf" + role + "UvRowU", "1,0,0")
        row_v = parameter("Float3", key + "_row_v", "Gltf" + role + "UvRowV", "0,1,0")
        u = add("DotProduct", key + "_u", InputA=link(affine_input, "Output"), InputB=link(row_u))
        v = add("DotProduct", key + "_v", InputA=link(affine_input, "Output"), InputB=link(row_v))
        transformed = add("CombineVector", key + "_uv", X=link(u), Y=link(v))
        sampled = add("TextureSampler", key + "_texture", Image="", Coords=link(transformed, "XY"),
                   Sampler={"Filter": "Bilinear", "AddressU": "Wrap", "AddressV": "Wrap", "Attribute": "Gltf" + role + "Sampler"},
                   UI={"Name": "Gltf" + role + "Texture", "IsAttribute": False, "Default": "1,1,1,1", "Extension": "Color", "CustomExtension": "gltf_" + key,
                       "Processor": "None", "ColorSpace": "Srgb" if srgb else "Linear", "ImageFormat": "RGBA8888", "SrgbRead": srgb})
        present = parameter("Float", key + "_has_texture", "GltfHas" + role + "Texture", 0)
        # Conditional selection keeps an unselected nonfinite texel from
        # contaminating the neutral value; arithmetic lerp cannot do that.
        # This is a provided dynamic Branch, without static shader variants.
        selected = add("Branch", key + "_present_or_one", Name="", Operator="GreaterThan",
                       A=link(present), B={}, **{"True": link(sampled), "False": link(one)})
        return add("SplitVector", key + "_effective", Input=link(selected))

    def rgb(identifier, texture_id):
        return add("CombineVector", identifier, X=link(texture_id, "X"), Y=link(texture_id, "Y"), Z=link(texture_id, "Z"))

    base = texture("BaseColor", True)
    mr = texture("MetallicRoughness", False)
    occlusion = texture("Occlusion", False)
    emissive = texture("Emissive", True)
    base_factor = parameter("Float3", "base_factor", "GltfBaseColor", "1,1,1")
    metallic_factor = parameter("Float", "metallic_factor", "GltfMetallicFactor", 1)
    roughness_factor = parameter("Float", "roughness_factor", "GltfRoughnessFactor", 1)
    occlusion_strength = parameter("Float", "occlusion_strength", "GltfOcclusionStrength", 1)
    emissive_factor = parameter("Float3", "emissive_factor", "GltfEmissiveFactor", "0,0,0")
    vertex = add("VertexColor", "vertex_color")
    vertex_clamped = add("Saturate", "vertex_rgb", Input=link(vertex, "RGB"))
    base_product = add("Multiply", "base_texture_factor", A=link(rgb("base_rgb", base), "XYZ"), B=link(base_factor))
    albedo = add("Multiply", "base_product", A=link(base_product), B=link(vertex_clamped))
    roughness = add("Multiply", "roughness_product", A=link(mr, "Y"), B=link(roughness_factor))
    metallic = add("Multiply", "metallic_product", A=link(mr, "Z"), B=link(metallic_factor))
    ao = add("Lerp", "occlusion_product", A=link(one), B=link(occlusion, "X"), C=link(occlusion_strength))
    emission = add("Multiply", "emissive_product", A=link(rgb("emissive_rgb", emissive), "XYZ"), B=link(emissive_factor))
    add("Result", "surface", Albedo=link(albedo), Roughness=link(roughness), Metalness=link(metallic), AmbientOcclusion=link(ao), Emission=link(emission), Opacity={}, DefaultOpacity=1)
    identifiers = {node["Identifier"] for node in nodes}
    assert len(identifiers) == len(nodes)
    for node in nodes:
        for value in node.values():
            if isinstance(value, dict) and "Identifier" in value:
                assert value["Identifier"] in identifiers
    return {"Description": "Provided Surface/Lit direct source glTF texture candidate; opaque UV0/UV1 only until runtime proof", "Domain": "Surface", "ShadingModel": "Lit", "BlendMode": "Opaque", "Version": 1, "nodes": nodes}


if __name__ == "__main__":
    target = (Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest') / "surface_orm_candidate.shdrgrph"
    target.write_text(json.dumps(graph(), indent=2) + "\n")
    print(target)
