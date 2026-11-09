using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.DuckValidation;

/// <summary>Literal source, material, accessor and bounds controls; not rendered-image acceptance.</summary>
public static class DuckSourceTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error, string Scope );
	public static void VerifyStage()
	{
		CheckAssembly( typeof( Model ).Assembly, "b5b66ab1-06f5-4aee-b816-92b6b197666e", "ad6a78a7e4f2664fe1f74b1567dde6d15d1519ad78dbd1e21efbb21ebfa2ebd1" );
		CheckAssembly( typeof( GltfReader ).Assembly, "88f89cfc-81b4-4744-9888-ec746058a6de", "b86cb72e0c0e0f3c23af45bd115f68760655d1754d663f41df62a45e136f9e8b" );
		CheckAssembly( typeof( Editor.GltfAssetImport ).Assembly, "01d79f9c-a5ad-4d9b-80f2-721dddb2c092", "539c10536c6df8a769eb4f91488ebfe9a048fdf1bd7cd589d0383479b2641b4d" );
	}
	private static void CheckAssembly( System.Reflection.Assembly assembly, string mvid, string hash )
	{
		Require( System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default && assembly.ManifestModule.ModuleVersionId.ToString() == mvid, "Loaded producer identity changed." );
		using var stream = File.OpenRead( assembly.Location );
		Require( Convert.ToHexStringLower( SHA256.HashData( stream ) ) == hash, "On-disk producer identity changed." );
	}
	private static readonly Dictionary<string, string> Hashes = new( StringComparer.Ordinal )
	{
		["glTF/Duck.gltf"] = "98009069c87b165df92dbc8213d7c2b4f296074097ad2ec4efddf3dbc50cdcb0",
		["glTF/Duck0.bin"] = "4c851f5909095ecf77e66e0968c31d5354f17a49b62e1fad0c341e1635145ee9",
		["glTF/DuckCM.png"] = "8aedb428cbb815dffea650fe75bff032ea240f00ccad2f64dc8f62a0c5e30313",
		["glTF-Binary/Duck.glb"] = "65bf938f54d6073e619e76e007820bbf980cdc3dc0daec0d94830ffc4ae54ab5",
		["glTF-Draco/Duck.gltf"] = "022457ac914363a8a175b3f28bb2539f90315b7e44a03157cc0f9e5a651c56df",
		["glTF-Draco/Duck.bin"] = "f59b26a8caf37e4f8a2fac83407bc4f2a78fe76b24f943ca48f3add34462744e",
		["glTF-Draco/DuckCM.png"] = "8aedb428cbb815dffea650fe75bff032ea240f00ccad2f64dc8f62a0c5e30313",
		["glTF-Embedded/Duck.gltf"] = "b69c34f30ec2803a37c6546c890a202f4db618745a3fefa3e5ac360bff211931",
		["glTF-Quantized/Duck.gltf"] = "a0522c4c01b3a9f168ca95ed18810947c730d0bedb9ce8c3b46c7a58ba196821",
		["glTF-Quantized/Duck.bin"] = "a8f5a07454ca7bf0e6f8e029c7c626b71b6b60703b9e8240b78d849f01c3dbcb",
		["glTF-Quantized/DuckCM.png"] = "8aedb428cbb815dffea650fe75bff032ea240f00ccad2f64dc8f62a0c5e30313"
	};

	public static Result[] Run( string checkout )
	{
		var root = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/Duck" );
		var results = new List<Result>();
		foreach ( var variant in new[] { "glTF", "glTF-Binary", "glTF-Draco", "glTF-Embedded", "glTF-Quantized" } )
		foreach ( var compiled in new[] { false, true } )
		foreach ( var check in new[] { "geometry", "material", "upload" } )
		{
			string error = null;
			try
			{
				var name = variant == "glTF-Binary" ? "Duck.glb" : "Duck.gltf";
				byte[] Read( string relative )
				{
					if ( !Hashes.TryGetValue( relative, out var expected ) ) throw new InvalidDataException( "Unexpected Duck dependency: " + relative );
					var bytes = File.ReadAllBytes( Path.Combine( root, relative ) );
					Require( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == expected, "Pinned Duck input changed: " + relative );
					return bytes;
				}
				byte[] Resolve( string uri ) => Read( variant + "/" + uri );
				var source = Read( variant + "/" + name );
				// Check every external declared image even when the CPU reader does not load it.
				foreach ( var path in Hashes.Keys.Where( value => value.StartsWith( variant + "/", StringComparison.Ordinal ) && value.EndsWith( ".png", StringComparison.Ordinal ) ) ) _ = Read( path );
				var bytes = compiled ? GltfCompiler.Compile( source, Resolve, GltfModelLoader.SupportedExtensions ) : source;
				using var document = GltfModelLoader.ReadDocument( bytes, Resolve );
				if ( check == "geometry" ) Geometry( document, variant );
				else if ( check == "material" ) Material( document, variant );
				else Upload( document, variant );
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			results.Add( new Result( check, variant + (compiled ? "/compiled_glb" : "/source"), error is null, error,
				variant == "glTF-Draco" && check == "geometry" ? "Decoded count and finite bounds only; independent Draco bounds not established." : "Pinned source and literal geometry/material expectations; CPU only." ) );
		}
		return results.ToArray();
	}

	private static void Geometry( GltfDocument document, string variant )
	{
		Require( document.GetDefaultScene() == 0, "Expected authored default scene." );
		var scene = GltfStaticSceneAssembler.Assemble( document );
		Require( scene.Primitives.Count == 1 && scene.HasBounds, "Expected one bounded primitive." );
		var data = scene.Primitives[0].Data;
		Require( data.Positions.Length == 2399 && data.Normals.Length == 2399 && data.Indices.Length == 12636, "Duck topology or attributes changed." );
		Require( data.TexCoords.TryGetValue( 0, out var uv ) && uv.Length == 2399, "Duck UV0 missing." );
		if ( variant == "glTF-Draco" )
		{
			Require( float.IsFinite( scene.BoundsMin.X ) && float.IsFinite( scene.BoundsMax.X ) && scene.BoundsMax.X > scene.BoundsMin.X, "Invalid Draco bounds." );
			return;
		}
		var minimum = variant == "glTF-Quantized" ? new NVector( -24.144959f, -27.282874f, 3.9092005f ) : new NVector( -24.144959f, -27.282873f, 3.9092007f );
		var maximum = variant == "glTF-Quantized" ? new NVector( 21.232257f, 37.866100f, 64.556640f ) : new NVector( 21.230392f, 37.866102f, 64.555117f );
		Near( minimum, scene.BoundsMin, .02f ); Near( maximum, scene.BoundsMax, .02f );
	}
	private static void Material( GltfDocument document, string variant )
	{
		var material = document.ReadMaterial( 0 );
		Require( material.BaseColorFactor == System.Numerics.Vector4.One && material.MetallicFactor == 0 && material.RoughnessFactor == 1, "Duck authored PBR factors changed." );
		Require( material.BaseColorTexture is { Index: 0, TexCoord: 0, IsSrgb: true } && material.AlphaMode == "OPAQUE" && !material.Unlit, "Duck base-color material contract changed." );
		Require( material.NormalTexture is null && material.EmissiveTexture is null && material.OcclusionTexture is null, "Unexpected material textures." );
		if ( variant == "glTF-Quantized" )
		{
			Require( MathF.Abs( material.BaseColorTexture.Offset.X - .0264090002f ) < 1e-7f && MathF.Abs( material.BaseColorTexture.Offset.Y - .019963026f ) < 1e-7f, "Quantized UV offset changed." );
			Require( MathF.Abs( material.BaseColorTexture.Tiling.X - .000233684244f ) < 1e-9f && MathF.Abs( material.BaseColorTexture.Tiling.Y - .000234450286f ) < 1e-9f, "Quantized UV scale changed." );
		}
	}
	private static void Upload( GltfDocument document, string variant )
	{
		var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		Require( plan.SourceVertices.Length == 2399 && plan.Indices.Length == 12636 && plan.SourceMode == 4 && !plan.GenerateFlatNormals, "GPU staging topology changed." );
		Require( plan.Sources.ContainsKey( "POSITION" ) && plan.Sources.ContainsKey( "NORMAL" ) && plan.Sources.ContainsKey( "TEXCOORD_0" ), "GPU staging lost source accessors." );
		var attrs = document.Root.GetProperty( "meshes" )[0].GetProperty( "primitives" )[0].GetProperty( "attributes" );
		var accessors = document.Root.GetProperty( "accessors" );
		var positionType = accessors[attrs.GetProperty( "POSITION" ).GetInt32()].GetProperty( "componentType" ).GetInt32();
		Require( positionType == (variant == "glTF-Quantized" ? 5123 : 5126), "Source position component type changed." );
	}
	private static void Near( NVector expected, NVector actual, float tolerance )
	{
		Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ) &&
			MathF.Abs( expected.X - actual.X ) <= tolerance && MathF.Abs( expected.Y - actual.Y ) <= tolerance && MathF.Abs( expected.Z - actual.Z ) <= tolerance,
			$"Bounds differ: expected {expected}, actual {actual}." );
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
