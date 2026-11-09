using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

/// <summary>Temporary filesystem packaging only; actual model creation is public Model.Load.</summary>
internal sealed class BoxTexturedModelMount : IDisposable
{
	private readonly string directory;
	private readonly BaseFileSystem aggregate;
	private readonly List<Model> owned = new();
	private LocalFileSystem mount;
	internal string Path { get; }

	internal BoxTexturedModelMount( BoxTexturedReference.Form form )
	{
		directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "gltf_box_textured_" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			Directory.CreateDirectory( directory );
			var extension = form.Compiled ? ".glb_c" : form.Name.StartsWith( "glTF-Binary/", StringComparison.Ordinal ) ? ".glb" : ".gltf";
			Path = "box_textured_" + Guid.NewGuid().ToString( "N" ) + extension;
			File.WriteAllBytes( System.IO.Path.Combine( directory, Path ), form.Bytes );
			// Only the external glTF source package carries relative dependencies.
			// Its compiled geometry embeds the buffer but keeps its explicit PNG.
			if ( form.Name.StartsWith( "glTF/", StringComparison.Ordinal ) )
			{
				File.WriteAllBytes( System.IO.Path.Combine( directory, "CesiumLogoFlat.png" ), form.Resolve( "CesiumLogoFlat.png" ) );
				if ( !form.Compiled ) File.WriteAllBytes( System.IO.Path.Combine( directory, "BoxTextured0.bin" ), form.Resolve( "BoxTextured0.bin" ) );
			}
			aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
			mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
		}
		catch { Dispose(); throw; }
	}
	internal Model Load()
	{
		var model = Model.Load( Path );
		if ( model is not null && model.ResourcePath == Resource.FixPath( Path ) && !owned.Any( value => ReferenceEquals( value, model ) ) ) owned.Add( model );
		return model;
	}
	public void Dispose()
	{
		var errors = new List<Exception>();
		foreach ( var model in owned ) Clean( model.Destroy ); owned.Clear();
		if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( mount.Dispose ); mount = null; }
		Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, true ); } );
		if ( errors.Count > 0 ) throw new AggregateException( "BoxTextured mount cleanup failed.", errors );
		void Clean( Action action ) { try { action(); } catch ( Exception error ) { errors.Add( error ); } }
	}
}
