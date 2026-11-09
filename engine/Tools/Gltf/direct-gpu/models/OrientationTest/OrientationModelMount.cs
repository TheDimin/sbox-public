using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Owns only a temporary source/artifact mount and its loaded Models.</summary>
internal sealed class OrientationModelMount : IDisposable
{
	private readonly string directory;
	private readonly BaseFileSystem aggregate;
	private readonly List<Model> owned = new();
	private LocalFileSystem mount;
	internal string Path { get; }

	internal OrientationModelMount( OrientationCoreTests.Form form )
	{
		directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "gltf_orientation_renderer_" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			Directory.CreateDirectory( directory );
			var extension = form.Name.EndsWith( "/compiled_glb", StringComparison.Ordinal ) ? ".glb_c" : form.Name.StartsWith( "glTF-Binary/", StringComparison.Ordinal ) ? ".glb" : ".gltf";
			Path = "orientation_renderer_" + Guid.NewGuid().ToString( "N" ) + extension;
			File.WriteAllBytes( System.IO.Path.Combine( directory, Path ), form.Bytes );
			if ( form.Name == "glTF/source" ) File.WriteAllBytes( System.IO.Path.Combine( directory, "OrientationTest.bin" ), form.Resolve( "OrientationTest.bin" ) );
			aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
			mount = new LocalFileSystem( directory );
			aggregate.Mount( mount );
		}
		catch { Dispose(); throw; }
	}

	internal Model Load()
	{
		var model = Model.Load( Path );
		if ( model is not null && model.ResourcePath == Resource.FixPath( Path ) && !owned.Any( candidate => ReferenceEquals( candidate, model ) ) ) owned.Add( model );
		return model;
	}

	public void Dispose()
	{
		var errors = new List<Exception>();
		foreach ( var model in owned ) Clean( model.Destroy );
		owned.Clear();
		if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( mount.Dispose ); mount = null; }
		Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true ); } );
		if ( errors.Count > 0 ) throw new AggregateException( "Temporary model mount cleanup failed.", errors );

		void Clean( Action action ) { try { action(); } catch ( Exception error ) { errors.Add( error ); } }
	}
}
