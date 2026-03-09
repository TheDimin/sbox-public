using System;
using System.Text;
using Mounting.Sims4;
using Sims4.Dbpf;


public class SimsMount : BaseGameMount
{
	new internal static Sandbox.Diagnostics.Logger Log = new Sandbox.Diagnostics.Logger( "Sims4-Mount" );
	public override string Ident => "sims4";
	public override string Title => "The Sims 4";

	private List<DbpfPackage> packages = new List<DbpfPackage>();

	// TS4 on Steam
	const long AppId = 1222670;

	string _gameDir;

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
		// 2. EA App common paths
		//foreach ( var path in EaAppPaths )
		//{
		//	if ( System.IO.Directory.Exists( path ) )
		//	{
		//		_gameDir = path;
		//		IsInstalled = true;
		//		return;
		//	}
		//}
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

		int count = 0;
		foreach ( var item in package.GetEntriesOfType( Sims4.Dbpf.Enums.ResourceType.GEOM ) )
		{
			count++;
			try
			{
				//Log.Info( $"Found GEOM record: type=0x{(uint)item.Type:X8} group=0x{item.Group:X8} instance=0x{item.Instance:X16}" );
				string DisplayName = package.GetResourceName( item.Instance );


				if ( DisplayName.Length > 0 )
				{
					//Log.Warning( $"Failed to find displayname.. fallback instanceID used" );
					DisplayName = $"{item.Instance}";
				}

				context.Add( Sandbox.Mounting.ResourceType.Model, $"BodyGeometry/{item.Group}/{DisplayName}", new ModelLoader( package, item, count ) );
			}
			catch ( Exception ex )
			{
				Log.Error( $"Error processing GEOM record: {ex.Message}" );
			}
			//foreach ( var record in package.GetTextureRecords() )
			//{
			//	var mountPath = $"mount://{Ident}/textures/{record.ResourceGroup:X8}/{record.InstanceId:X16}";
			//	context.Add( ResourceType.Texture, mountPath, new Sims4TextureLoader( package, record ) );
			//	Log.Info( $"Mounted texture: {mountPath}" );
			//}
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
