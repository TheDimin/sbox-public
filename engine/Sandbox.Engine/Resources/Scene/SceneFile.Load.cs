using System.IO;

namespace Sandbox;

public partial class SceneFile
{
	bool _isSourceView;

	internal override bool RequiresMainThreadForManifest => IsValid && (_isSourceView || base.RequiresMainThreadForManifest);

	internal override AsyncResourceLoader EnsureManifest()
	{
		if ( !IsValid ) return null;
		if ( !_isSourceView ) return base.EnsureManifest();

		// Source views keep their unsaved data separate from the resource catalog. Use the
		// catalog's dependency manifest only at actual use, and leave its ownership there.
		ThreadSafe.AssertIsMainThread();
		var catalog = Game.Resources.Get<SceneFile>( Guid );
		if ( catalog is null && !string.IsNullOrEmpty( ResourcePath ) )
			catalog = Game.Resources.Get<SceneFile>( ResourcePath );

		return catalog is { IsValid: true, _isSourceView: false } ? catalog.EnsureManifest() : null;
	}

	internal void InitializeSource( string path, Guid guid )
	{
		_isSourceView = true;
		ResourcePath = FixPath( path );
		ResourceName = Path.GetFileNameWithoutExtension( ResourcePath );
		Guid = guid;
		ResourceIdLong = ResourcePath.FastHash64();
#pragma warning disable CS0618
		ResourceId = ResourcePath.FastHash();
#pragma warning restore CS0618
	}

	internal static SceneFile FromSource( string path, Guid guid, string json, byte[] binaryData )
	{
		var file = new SceneFile();
		file.InitializeSource( path, guid );
		file.BinaryData = binaryData ?? [];
		file.LoadFromJson( json );
		file.BinaryData = binaryData ?? [];
		file.LastSavedSourceHash = json.FastHash();
		return file;
	}

	internal static SceneFile FromCompiled( string path, Guid guid, byte[] data )
	{
		var file = new SceneFile();
		file.InitializeSource( path, guid );
		if ( !file.TryLoadFromData( data ) )
			throw new InvalidDataException( $"Could not read compiled scene '{path}'." );

		return file;
	}

	internal override bool LoadFromResource( Span<byte> data )
	{
		BinaryData = Game.Resources.ReadCompiledResourceBlock( BlobDataSerializer.CompiledBlobName, data ) ?? [];
		return true;
	}
}
