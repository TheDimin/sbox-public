using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

internal static class ArchetypePhysics
{
	// Initial and maximum health for a mounted prop.
	internal const float ObjectHealth = 1000f;

	// Keep the intact parking hut anchored. Its fragment pieces retain their own physics.
	internal const uint ParkingHut2 = 0x122c438c; // joaat("prop_parking_hut_2")
}

public sealed class GtaMount : BaseGameMount, IGtaWorldSource
{
	public override string Ident => "gtav";
	public override string Title => "Grand Theft Auto V Enhanced";
	public override long? SteamAppId => 3240220;

	string directory;
	MountArchive archive;
	internal readonly Dictionary<uint, string> MapNames = new();
	internal readonly Dictionary<uint, string> Prefabs = new();
	internal IReadOnlyDictionary<string, AssetLoader> TextureAssets { get; private set; }

	protected override void Initialize( InitializeContext context )
	{
		if ( !OperatingSystem.IsWindows() ) return;
		var rockstar = Registry.GetValue( @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\GTAV Enhanced", "InstallFolder", null ) as string;
		directory = new[] { Environment.GetEnvironmentVariable( "GTA_V_ENHANCED_DIR" ), context.GetFileSystem( 3240220 )?.GetFullPath( "/" ), rockstar }
			.FirstOrDefault( x => !string.IsNullOrWhiteSpace( x ) && File.Exists( Path.Combine( x, "GTA5_Enhanced.exe" ) ) && File.Exists( Path.Combine( x, "common.rpf" ) ) );
		IsInstalled = directory is not null;
	}

	protected override Task Mount( MountContext context )
	{
#if GTAV_BOREALIS_GEOMETRY
		archive = new MountArchive( directory, storedGeometry: Graphics.BorealisGpuRequested || Application.IsDedicatedServer );
#else
		archive = new MountArchive( directory );
#endif
		Textures = new( archive );
		var textureAssets = new Dictionary<string, AssetLoader>( StringComparer.OrdinalIgnoreCase );
		try
		{
			using var catalog = archive.OpenCatalog( Application.IsDedicatedServer );
			var reader = archive;
			worldCatalog = Task.Run( () => WorldCatalog.Read( reader ) );
			while ( catalog.BaseStream.Position < catalog.BaseStream.Length )
			{
				var entry = new Entry( (Kind)catalog.ReadInt32(), catalog.ReadUInt32(), catalog.ReadUInt32(), catalog.Text() );
				if ( entry.Kind == Kind.Invalid )
				{
					Log.Warning( $"Skipping GTA dictionary {entry.Name}" );
					continue;
				}
				if ( entry.Kind == Kind.Fragment && entry.Name == "fragments/adder" )
				{
					foreach ( var (kind, part) in new[] { (Kind.VehicleBody, "body"), (Kind.VehicleWheel, "wheel") } )
					{
						var piece = entry with { Kind = kind, Name = $"{entry.Name}/{part}" };
						context.Add( ResourceType.Model, $"models/{piece.Name}", new AssetLoader( piece ) );
					}
				}
				switch ( entry.Kind )
				{
					case Kind.Scene:
						MapNames[entry.File] = entry.Name;
						context.Add( ResourceType.Scene, $"maps/{entry.Name}", new MapLoader( entry ) );
						break;
					case Kind.Prefab:
						Prefabs[entry.Member] = $"mount://{Ident}/archetypes/{entry.Name}.prefab";
						context.Add( ResourceType.PrefabFile, $"archetypes/{entry.Name}", new ArchetypeLoader( entry ) );
						break;
					case Kind.Texture:
						var texture = new AssetLoader( entry );
						textureAssets[entry.Name] = texture;
						context.Add( ResourceType.Texture, $"textures/{entry.Name}", texture );
						break;
					case Kind.Collision:
						context.Add( ResourceType.Model, $"collision/{entry.Name}", new AssetLoader( entry ) );
						break;
					default:
						var asset = new AssetLoader( entry );
						context.Add( ResourceType.Model, $"models/{entry.Name}", asset );
						if ( entry.Kind is Kind.Model or Kind.Fragment )
							WeaponPrefab.Register( context, entry.Name, asset );
						break;
				}
			}
			context.Add( ResourceType.GameResource, CoronaLoader.PathName, new CoronaLoader() );
			context.Add( ResourceType.Scene, "gtaV", new WorldLoader() );
			TextureAssets = textureAssets;
			IsMounted = true;
			return Task.CompletedTask;
		}
		catch
		{
			Shutdown();
			throw;
		}
	}

	Task<WorldCatalog> worldCatalog;
	internal Task<WorldCatalog> GetWorldCatalog( MountArchive reader ) => worldCatalog ??= Task.Run( () => WorldCatalog.Read( reader ) );

	IGtaWorldSession IGtaWorldSource.StartStreaming( GameObject root ) => new WorldSession( this, archive, root );

	internal IReadOnlyList<ArchetypeExtension> GetExtensions( Entry entry ) => archive.GetExtensions( entry.File, entry.Member );

	internal BinaryReader ReadObjectCollision( Entry entry ) => archive.Read( -6, entry.File, entry.Member, entry.Name );

	internal TextureRequests Textures { get; private set; }
	internal StreamingIdleBudget IdleBudget { get; } = new();

	internal BinaryReader Read( Entry entry ) => archive.Read( entry );
	internal PreparedModelData ReadModel( Entry entry ) => archive.ReadModel( entry );
	internal void StartDictionaryModels( DictionaryModelRead request )
	{
		var reader = archive;
		if ( reader is not null )
		{
			_ = Task.Run( () => reader.ReadDictionaryModels( request ) );
			return;
		}

		var error = new ObjectDisposedException( nameof(GtaMount) );
		while ( request.Take() is { } member ) member.Completion.TrySetException( error );
	}

	internal readonly Dictionary<AssetLoader.MaterialKey, WeakReference<Material>> SharedMaterials = new();
	readonly Dictionary<AssetLoader, List<(WeakReference<Material> Material, bool Color, bool Lettering, bool Surface, string Parameter)>> decalBindings = new();
	bool readingDecals;

	readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture, Texture> letteringMasks = new();

	internal void BindLettering( Material material, Texture atlas )
	{
		if ( !atlas.IsValid() ) return;
		var mask = letteringMasks.GetValue( atlas, source =>
		{
			var pixels = source.GetPixels();
			var rgba = new byte[checked( pixels.Length * 4 )];
			for ( int i = 0; i < pixels.Length; i++ )
			{
				int offset = i * 4;
				rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = 255;
				// The distance atlas stores the glyph contour at the midpoint of red.
				rgba[offset + 3] = (byte)Math.Clamp( (pixels[i].r - 124) * 32, 0, 255 );
			}
			return Texture.Create( source.Width, source.Height ).WithData( rgba ).WithMips().Finish();
		} );
		material.Set( "g_tColor", mask );
	}

	internal bool IsBindingTexture( AssetLoader asset ) => decalBindings.ContainsKey( asset );

	internal void BindTexture( AssetLoader asset, Material material, bool color, bool lettering = false, bool surface = false, string parameter = null )
	{
		if ( !decalBindings.TryGetValue( asset, out var targets ) )
			decalBindings.Add( asset, targets = new() );
		targets.Add( (new( material ), color, lettering, surface, parameter) );
		if ( !readingDecals ) _ = ReadDecalTextures( archive );
	}

	async Task ReadDecalTextures( MountArchive reader )
	{
		readingDecals = true;
		var batch = new Queue<(AssetLoader Asset, Task<BinaryReader> Read)>();
		var demand = new Queue<AssetLoader>();
		try
		{
			while ( reader == archive && decalBindings.Count > 0 )
			{
				// Yield before each upload so the facade can activate first.
				await GameTask.Yield();
				if ( reader != archive ) return;
				var first = decalBindings.First();
				if ( batch.Count == 0 && !first.Key.TextureReady && first.Value.Any( t => t.Material.TryGetTarget( out var m ) && m.IsValid() ) )
				{
					// Material creation has finished for this frame. Only submit bindings with a live user.
					foreach ( var (candidate, users) in decalBindings.Take( 16 ) )
						if ( !candidate.TextureReady && users.Any( t => t.Material.TryGetTarget( out var m ) && m.IsValid() ) )
							demand.Enqueue( candidate );
					Textures.ReadMany( demand, batch, fullResolution: true );
					demand.Clear();
				}
				var hasRead = batch.TryDequeue( out var item );
				var asset = hasRead ? item.Asset : first.Key;
				try
				{
					using var data = hasRead ? await item.Read : null;
					await GameTask.MainThread();
					if ( reader != archive ) return;
					var targets = decalBindings[asset];
					if ( targets.Any( t => t.Material.TryGetTarget( out var m ) && m.IsValid() ) )
					{
						if ( data is not null && !asset.TextureReady ) asset.UploadTexture( data );
						if ( asset.TextureReady )
						{
							var texture = Texture.Load( asset.Path );
							foreach ( var (target, color, lettering, surface, parameter) in targets )
							{
								if ( !target.TryGetTarget( out var mat ) || !mat.IsValid() ) continue;
								if ( lettering )
								{
									BindLettering( mat, texture );
									continue;
								}
								mat.Set( parameter ?? (color ? "g_tColor" : "g_tNormal"), texture );
								if ( !surface ) mat.Set( color ? "g_flColorReady" : "g_flHasNormal", 1f );
							}
						}
					}
				}
				catch ( Exception ex ) when ( reader == archive ) { Log.Warning( $"GTA decal texture {asset.Path}: {ex.Message}" ); }
				if ( reader == archive ) decalBindings.Remove( asset );
			}
		}
		catch ( ObjectDisposedException ) when ( reader != archive ) { }
		finally
		{
			if ( reader == archive ) readingDecals = false;
			while ( batch.TryDequeue( out var unused ) )
			{
				try { (await unused.Read.ConfigureAwait( false )).Dispose(); }
				catch ( Exception ) when ( reader != archive ) { }
			}
		}
	}

	protected override void Shutdown()
	{
		IdleBudget.Clear();
		decalBindings.Clear();
		SharedMaterials.Clear();
		readingDecals = false;
		worldCatalog = null;
		Textures?.Dispose();
		System.Threading.Interlocked.Exchange( ref archive, null )?.Dispose();
		MapNames.Clear();
		Prefabs.Clear();
		TextureAssets = null;
	}
}
