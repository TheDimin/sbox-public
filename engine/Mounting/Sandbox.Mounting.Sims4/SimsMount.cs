using System;
using Mounting.Sims4;
using Sims4Reader;


public class SimsMount : BaseGameMount
{
	new internal static Sandbox.Diagnostics.Logger Log = new Sandbox.Diagnostics.Logger( "Sims4-Mount" );
	public override string Ident => "sims4";
	public override string Title => "The Sims 4";

	private List<DbpfPackage> packages = new List<DbpfPackage>();

	// TS4 on Steam
	const long AppId = 1222670;

	string? _gameDir;

	protected override void Initialize( InitializeContext context )
	{
		// 1. Steam
		if ( context.IsAppInstalled( AppId ) )
		{
			Log.Info( "Sims 4 Steam install detected" );
			var dir = context.GetAppDirectory( AppId );
			if ( System.IO.Directory.Exists( dir ) )
			{
				Log.Info( $"Sims 4 Steam directory: {dir}" );
				_gameDir = dir;
				IsInstalled = true;
				return;
			}
		}

		//Detecting it trough EA app requires us to know if the user owns the game...
		return;
	}

	protected override Task Mount( MountContext context )
	{
		if ( string.IsNullOrWhiteSpace( _gameDir ) || !System.IO.Directory.Exists( _gameDir ) )
			return Task.CompletedTask;

		var dataDir = System.IO.Path.Combine( _gameDir, "Data" );
		if ( !System.IO.Directory.Exists( dataDir ) )
			return Task.CompletedTask;

		MountPackage( context, "E:\\SteamLibrary\\steamapps\\common\\The Sims 4\\Data\\Client\\ClientDeltaBuild0.package" );

		//foreach ( var file in System.IO.Directory.EnumerateFiles( dataDir, "*.package", SearchOption.AllDirectories ) )
		//{
		//	MountPackage( context, file );
		//}

		IsMounted = true;
		return Task.CompletedTask;
	}


	private void MountPackage( MountContext context, string file )
	{
		Log.Info( $"Mounting package: {file}" );

		var package = DbpfPackage.Open( file );

		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			var key = entry.Key;
			var name = $"{key.Group:X8}_{key.Instance:X16}";

			try
			{
				switch ( key.Type )
				{
					// Images (DST, RLE)
					case Sims4Reader.ResourceType.DstImage:
					case Sims4Reader.ResourceType.RleImage:
					case Sims4Reader.ResourceType.RleImageAlt:
						context.Add( Sandbox.Mounting.ResourceType.Texture,
							$"textures/{name}",
							new Sims4TextureLoader( package, entry ) );
						break;

					// Materials (MATD in RCOL)
					case Sims4Reader.ResourceType.MaterialDefinition:
						context.Add( Sandbox.Mounting.ResourceType.Material,
							$"materials/{name}",
							new Sims4MaterialLoader( package, entry ) );
						break;

					// Models (GEOM in RCOL)
					case Sims4Reader.ResourceType.Geometry:
						context.Add( Sandbox.Mounting.ResourceType.Model,
							$"models/{name}",
							new ModelLoader( package, entry ) );
						break;

					// Catalog objects (COBJ)
					case Sims4Reader.ResourceType.CatalogObject:
						context.Add( Sandbox.Mounting.ResourceType.Text,
							$"objects/{name}",
							new CatalogObjectLoader( package, entry ) );
						break;
				}
			}
			catch ( Exception ex )
			{
				Log.Error( $"Error mounting {key.Type} {key}: {ex.Message}" );
			}
		}

		packages.Add( package );
	}


	protected override void Shutdown()
	{
		foreach ( var item in packages )
		{
			item.Dispose();
		}
	}
}
