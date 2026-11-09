using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Positive original LoadAsync identity/cache tests, written before integration.
/// Explicit compiled-artifact paths do not prove original .gltf package mapping.
/// </summary>
public static class OrientationLoadAsyncTests
{
	public sealed record Result( string Name, string Form, bool Passed, bool? NativeModelPresent, string Error );

	public static async Task<Result[]> RunAsync( string checkoutRoot, string oraclePath, CancellationToken cancellationToken )
	{
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var results = new List<Result>();
		foreach ( var form in OrientationCoreTests.OpenForms( checkoutRoot, oracle.RootElement ) )
		{
			cancellationToken.ThrowIfCancellationRequested();
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires an idle editor main thread outside rendering." );
			var directory = Path.Combine( Path.GetTempPath(), "gltf_orientation_async_" + Guid.NewGuid().ToString( "N" ) );
			var compiled = form.Name.EndsWith( "/compiled_glb", StringComparison.Ordinal );
			var extension = compiled ? ".glb_c" : form.Name.StartsWith( "glTF-Binary/", StringComparison.Ordinal ) ? ".glb" : ".gltf";
			var name = "orientation_async_" + Guid.NewGuid().ToString( "N" ) + extension;
			var owned = new List<Model>();
			BaseFileSystem aggregate = null;
			LocalFileSystem mount = null;
			bool? native = null;
			string failure = null;
			try
			{
				Directory.CreateDirectory( directory );
				File.WriteAllBytes( Path.Combine( directory, name ), form.Bytes );
				if ( form.Name == "glTF/source" )
					File.WriteAllBytes( Path.Combine( directory, "OrientationTest.bin" ), form.Resolve( "OrientationTest.bin" ) );
				aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
				mount = new LocalFileSystem( directory );
				aggregate.Mount( mount );
				var first = Track( await Model.LoadAsync( name ) );
				await MainThread.Wait();
				Require( first is { IsValid: true, IsError: false }, "Original Model.LoadAsync must return a valid non-error Model." );
				native = !first.native.IsNull;
				var repeated = Track( await Model.LoadAsync( name ) );
				await MainThread.Wait();
				var synchronous = Track( Model.Load( name ) );
				var canonical = Track( await Model.LoadAsync( first.ResourcePath ) );
				await MainThread.Wait();
				Require( ReferenceEquals( first, repeated ) && ReferenceEquals( first, synchronous ) && ReferenceEquals( first, canonical ), "Load, LoadAsync and canonical paths must share the same live resource identity." );
				first.Destroy();
				Require( !first.IsValid, "Explicit destruction must invalidate the managed resource immediately." );
				var replacement = Track( await Model.LoadAsync( name ) );
				await MainThread.Wait();
				Require( replacement is { IsValid: true, IsError: false } && !ReferenceEquals( first, replacement ), "LoadAsync must create a new resource after destruction, not revive the dead wrapper." );
				native = !replacement.native.IsNull;
				Require( replacement.native.IsNull, "LoadAsync is still constructing a native IModel rather than the managed GPU resource." );
			}
			catch ( Exception error ) { failure = error.ToString(); }
			finally
			{
				await MainThread.Wait();
				foreach ( var model in owned ) Clean( () => model.Destroy() );
				if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( () => mount.Dispose() ); }
				Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true ); } );
			}
			results.Add( new Result( "public_LoadAsync_managed_identity_cache_and_reload", form.Name, failure is null, native, failure ) );

			Model Track( Model value )
			{
				if ( value is not null && value.ResourcePath == Resource.FixPath( name ) && !owned.Any( model => ReferenceEquals( model, value ) ) ) owned.Add( value );
				return value;
			}
			void Clean( Action action )
			{
				try { action(); }
				catch ( Exception error ) { failure = (failure ?? "") + "\nCleanup: " + error; }
			}
		}
		return results.ToArray();
	}

	private static void Require( bool condition, string message ) => OrientationCoreTests.Require( condition, message );
}
