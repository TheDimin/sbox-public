using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>Document validity and drawable capability remain separate.</summary>
public static class GltfGpuEmptyDefinitionTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	public static Result[] RunAll()
	{
		var rows = new List<Result>();
		Run( "valid_default_scene_without_nodes_has_empty_drawable_metadata", """
			{"asset":{"version":"2.0"},"scenes":[{}],"scene":0}
			""", 0 );
		Run( "valid_primitive_without_position_is_preserved_and_skipped_as_nondrawable", """
			{"asset":{"version":"2.0"},"accessors":[{"componentType":5121,"normalized":true,"type":"VEC4","count":3}],
			 "meshes":[{"primitives":[{"attributes":{"COLOR_0":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
			""", 1 );
		return rows.ToArray();
		void Run( string name, string source, int sourceMeshes )
		{
			try
			{
				using var document = GltfReader.Read( Encoding.UTF8.GetBytes( source ) );
				if ( sourceMeshes != 0 ) _ = document.ReadPrimitive( 0, 0 ); // Valid core metadata even without POSITION.
				var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				var prepare = typeof( GltfGpuAsset ).GetMethod( "Prepare", flags, null,
					new[] { typeof( GltfDocument ), typeof( string ), typeof( Func<int?, Material> ), typeof( Action ) }, null );
				Require( prepare is not null, "CPU-only Prepare is missing." );
				using var asset = (GltfGpuAsset)prepare.Invoke( null, new object[] { document, "shaders/__empty_definition_must_not_load.shader", null, null } );
				Require( (int)typeof( GltfGpuAsset ).GetProperty( "MeshCount", flags ).GetValue( asset ) == 0, "MeshCount counts drawable selected primitive instances; nondrawable source metadata must not fabricate render meshes." );
				var bounds = (BBox)typeof( GltfGpuAsset ).GetProperty( "RestBounds", flags ).GetValue( asset );
				Require( bounds.Mins == global::Vector3.Zero && bounds.Maxs == global::Vector3.Zero, "Empty drawable bounds must be finite and empty." );
				if ( sourceMeshes != 0 ) Require( document.Root.GetProperty( "meshes" ).GetArrayLength() == 1 && document.Root.GetProperty( "meshes" )[0].GetProperty( "primitives" ).GetArrayLength() == 1, "Skipping a primitive destroyed its source data." );
				rows.Add( new Result( name, true, null ) );
			}
			catch ( Exception exception ) { rows.Add( new Result( name, false, (exception is TargetInvocationException { InnerException: { } inner } ? inner : exception).ToString() ) ); }
		}
	}
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
