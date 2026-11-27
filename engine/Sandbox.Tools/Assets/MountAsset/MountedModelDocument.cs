using System;
using System.IO;
using System.Security.Cryptography;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace Editor;

internal static class MountedModelDocument
{
	internal static void Open( Asset asset )
	{
		if ( !EngineTools.EnsureAvailable( "modeldoc_editor" ) )
			return;

		try
		{
			var model = asset.LoadResource<Model>();
			if ( !model.IsValid() || model.IsError )
				throw new InvalidDataException( "The mount could not load the model." );

			var bytes = model.SaveToVmdl();
			var hash = Convert.ToHexString( SHA256.HashData( bytes ) );
			var folder = $"mounted_modeldoc/v1/{hash}";
			var name = string.Concat( asset.Name.Take( 64 ).Select( c => char.IsAsciiLetterOrDigit( c ) || c == '-' ? c : '_' ) );
			var relativePath = $"{folder}/mounted_{name}.vmdl";
			var path = FileSystem.Transient.GetFullPath( relativePath );

			if ( !File.Exists( path ) )
				Extract( bytes, relativePath, path );

			var document = AssetSystem.RegisterFile( path ) ?? throw new IOException( "The editor could not register the inspection copy." );
			document.OpenInEditor( "modeldoc_editor" );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"Could not open mounted model '{asset.Path}' in ModelDoc." );
			EditorUtility.DisplayDialog( "Could not open mounted model", $"{asset.Path}\n\n{ex.Message}" );
		}
	}

	private static void Extract( byte[] bytes, string relativePath, string path )
	{
		using var stream = new MemoryStream( bytes, false );
		using var resource = new ValveResourceFormat.Resource { FileName = relativePath + "_c" };
		resource.Read( stream );
		if ( resource.DataBlock is not ValveResourceFormat.ResourceTypes.Model model )
			throw new InvalidDataException( "The mount did not produce model data." );

		// ModelExtract uses this name for the DMX references in the new document.
		model.Data["m_name"] = relativePath;
		var extractor = new ModelExtract( resource, new NullFileLoader() );
		// Procedural draw calls can name materials absent from the external reference table.
		foreach ( var mesh in extractor.RenderMeshesToExtract )
		{
			foreach ( var sceneObject in mesh.Mesh.Data.GetArray( "m_sceneObjects" ) )
			{
				foreach ( var draw in sceneObject.GetArray( "m_drawCalls" ) )
				{
					var material = draw.GetStringProperty( "m_material" ) ?? draw.GetStringProperty( "m_pMaterial" );
					if ( material is not null )
						extractor.MaterialInputSignatures.TryAdd( material, ValveResourceFormat.ResourceTypes.Material.VsInputSignature.Empty );
				}
			}
		}

		using var source = extractor.ToContentFile();
		var directory = Path.GetDirectoryName( path );
		Directory.CreateDirectory( directory );
		foreach ( var file in source.SubFiles )
		{
			if ( Path.GetFileName( file.FileName ) != file.FileName || Path.GetExtension( file.FileName ) != ".dmx" )
				throw new InvalidDataException( $"Unexpected model dependency '{file.FileName}'." );

			WriteFile( Path.Combine( directory, file.FileName ), file.Extract() );
		}

		// Publish the document only after all of its mesh and animation files are ready.
		WriteFile( path, source.Data );
	}

	private static void WriteFile( string path, byte[] data )
	{
		var temporary = path + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		try
		{
			File.WriteAllBytes( temporary, data );
			File.Move( temporary, path, true );
		}
		finally
		{
			File.Delete( temporary );
		}
	}
}
