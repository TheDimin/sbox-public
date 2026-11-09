using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>
/// Written before extraction: CPU metadata must not eagerly realize shaders,
/// materials or GPU buffers. Reflection keeps a missing Prepare API an honest
/// executable failure rather than preventing other model probes from compiling.
/// </summary>
public static class GltfGpuAssetDefinitionTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
	public static Result[] RunAll()
	{
		var result = new List<Result>();
		Run( "prepare_metadata_never_invokes_shader_or_material_realization", MetadataWithoutGpu );
		Run( "authored_used_material_slots_are_ordered_independently_of_cache_equivalence", AuthoredSlots );
		Run( "asset_owner_release_calls_final_release_once_and_rejects_new_use", FinalRelease );
		Run( "prepare_failure_does_not_take_document_ownership", FailedTransfer );
		Run( "borrowed_document_lifetime_is_not_taken_implicitly", BorrowedDocument );
		return result.ToArray();
		void Run( string name, Action test )
		{
			try { test(); result.Add( new Result( name, true, null ) ); }
			catch ( Exception exception ) { result.Add( new Result( name, false, Unwrap( exception ).ToString() ) ); }
		}
	}

	private static void MetadataWithoutGpu()
	{
		using var document = GltfGpuBackendContractTests.InterleavedTriangle(); var materialCalls = 0;
		using var asset = Prepare( document, _ => { materialCalls++; throw new InvalidOperationException( "CPU metadata invoked a material resolver." ); } );
		Require( Read<int>( asset, "MeshCount" ) == 1, "CPU mesh metadata is unavailable or incorrect." );
		Require( Slots( asset ).SequenceEqual( new int?[] { null } ), "The implicit glTF default material needs one explicit fallback slot." );
		var bounds = Read<BBox>( asset, "RestBounds" );
		Require( bounds.Mins.x == 0 && bounds.Mins.y == 0 && bounds.Mins.z == 0 && bounds.Maxs.x == 0 && bounds.Maxs.y == 1f / .0254f && bounds.Maxs.z == 1f / .0254f,
			"Resource rest bounds must be exact; culling padding belongs only to the renderer instance." );
		Require( materialCalls == 0, "Metadata eagerly realized native materials." );
		AssertNoGpuBuffers( asset );
	}

	private static void AuthoredSlots()
	{
		// All five authored materials are equivalent, but only indices1/4 are
		// used in primitive order4,1,4. Runtime cache identity must not reorder,
		// merge or invent their authored slots, and unused slots stay absent.
		const string json = """
		{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[0,0,0]}],
		 "materials":[{},{},{},{},{}],"meshes":[{"primitives":[
		 {"attributes":{"POSITION":0},"material":4},{"attributes":{"POSITION":0},"material":1},{"attributes":{"POSITION":0},"material":4}]}],
		 "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
		""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		using var asset = Prepare( document, _ => throw new InvalidOperationException( "Slot metadata invoked material realization." ) );
		Require( Read<int>( asset, "MeshCount" ) == 3, "Selected primitive-instance count changed." );
		Require( Slots( asset ).SequenceEqual( new int?[] { 1, 4 } ), "Used slots must follow authored material indices, not traversal/cache order." );
		AssertNoGpuBuffers( asset );
	}

	private static void FinalRelease()
	{
		using var document = GltfGpuBackendContractTests.InterleavedTriangle(); var released = 0;
		var asset = Prepare( document, null, () => released++ );
		Require( released == 0, "A live prepared asset released its document generation." );
		asset.Dispose(); asset.Dispose();
		Require( released == 1, "Final document release must happen exactly once." );
		var rejected = false;
		try { _ = asset.CreateInstance( null ); } catch ( ObjectDisposedException ) { rejected = true; }
		Require( rejected, "Released asset ownership still accepted a renderer instance." );
	}

	private static void FailedTransfer()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" ) );
		var released = 0; var rejected = false;
		try { using var asset = Prepare( document, null, () => released++ ); }
		catch ( GltfException ) { rejected = true; }
		Require( rejected, "Scene import must report the absent explicit default scene." );
		Require( released == 0 && document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() == "2.0", "A failed Prepare stole or released the caller's document." );
	}

	private static void BorrowedDocument()
	{
		using var document = GltfGpuBackendContractTests.InterleavedTriangle();
		using ( var asset = Prepare( document ) ) AssertNoGpuBuffers( asset );
		Require( document.Root.GetProperty( "scene" ).GetInt32() == 0, "Default Prepare implicitly disposed a borrowed document." );
		var pose = document.EvaluatePose(); Require( pose.GetWorldTransform( 0 ) == System.Numerics.Matrix4x4.Identity, "Borrowed document was invalid after asset disposal." );
	}

	private static GltfGpuAsset Prepare( GltfDocument document, Func<int?, Material> resolver = null, Action finalRelease = null )
	{
		var method = typeof( GltfGpuAsset ).GetMethod( "Prepare", Flags, null,
			new[] { typeof( GltfDocument ), typeof( string ), typeof( Func<int?, Material> ), typeof( Action ) }, null );
		Require( method is not null, "CPU-only GltfGpuAsset.Prepare(document,compiledShaderPath,resolver,finalRelease) is not implemented." );
		try { return (GltfGpuAsset)method.Invoke( null, new object[] { document, "shaders/__missing_cpu_prepare_must_not_load.shader", resolver, finalRelease } ); }
		catch ( TargetInvocationException exception ) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture( exception.InnerException ?? exception ).Throw(); throw; }
	}
	private static int?[] Slots( GltfGpuAsset asset )
	{
		var property = typeof( GltfGpuAsset ).GetProperty( "MaterialSlotSourceIndices", Flags );
		Require( property is not null, "Authored material slot provenance is missing." );
		return ((IEnumerable)property.GetValue( asset )).Cast<object>().Select( x => x is null ? (int?)null : Convert.ToInt32( x ) ).ToArray();
	}
	private static T Read<T>( object value, string name )
	{
		var property = value.GetType().GetProperty( name, Flags ); Require( property is not null, "Missing metadata property " + name ); return (T)property.GetValue( value );
	}
	private static void AssertNoGpuBuffers( GltfGpuAsset asset )
	{
		foreach ( var field in typeof( GltfGpuAsset ).GetFields( Flags ).Where( x => !x.IsStatic && typeof( GpuBuffer ).IsAssignableFrom( x.FieldType ) ) )
			Require( field.GetValue( asset ) is null, "CPU metadata allocated a GPU buffer: " + field.Name );
		var primitives = typeof( GltfGpuAsset ).GetField( "Primitives", Flags ); Require( primitives is not null, "Cannot inspect the asset's actual primitive ownership." );
		foreach ( var primitive in (IEnumerable)primitives.GetValue( asset ) )
			foreach ( var field in primitive.GetType().GetFields( Flags ).Where( x => !x.IsStatic && typeof( GpuBuffer ).IsAssignableFrom( x.FieldType ) ) )
				Require( field.GetValue( primitive ) is null, "CPU metadata allocated a primitive GPU buffer: " + field.Name );
	}
	private static Exception Unwrap( Exception error ) => error is TargetInvocationException { InnerException: { } inner } ? inner : error;
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
