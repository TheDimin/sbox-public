using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

internal enum Kind { Scene, Prefab, Model, DictionaryModel, Texture, Collision, Fragment }

internal readonly record struct Entry( Kind Kind, uint File, uint Member, string Name );

public sealed class GtaMount : BaseGameMount, IGtaWorldSource
{
	public override string Ident => "gtav";
	public override string Title => "Grand Theft Auto V Enhanced";
	public override long? SteamAppId => 3240220;

	string directory;
	ArchiveReader archive;
	internal readonly Dictionary<uint, string> Prefabs = new();
	internal readonly Dictionary<string, string> TexturePaths = new( StringComparer.OrdinalIgnoreCase );

	protected override void Initialize( InitializeContext context )
	{
		if ( !OperatingSystem.IsWindows() ) return;
		var rockstar = Registry.GetValue( @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\GTAV Enhanced", "InstallFolder", null ) as string;
		directory = new[] { Environment.GetEnvironmentVariable( "GTA_V_ENHANCED_DIR" ), context.GetAppDirectory( 3240220 ), rockstar }
			.FirstOrDefault( x => !string.IsNullOrWhiteSpace( x ) && File.Exists( Path.Combine( x, "GTA5_Enhanced.exe" ) ) && File.Exists( Path.Combine( x, "common.rpf" ) ) );
		IsInstalled = directory is not null;
	}

	protected override Task Mount( MountContext context )
	{
		archive = ArchiveReader.Open( directory );
		try
		{
			using var catalog = archive.OpenCatalog();
			while ( catalog.BaseStream.Position < catalog.BaseStream.Length )
			{
				var entry = new Entry( (Kind)catalog.ReadInt32(), catalog.ReadUInt32(), catalog.ReadUInt32(), catalog.Text() );
				if ( (int)entry.Kind == -2 )
				{
					Log.Warning( $"Skipping GTA dictionary {entry.Name}" );
					continue;
				}
				switch ( entry.Kind )
				{
					case Kind.Scene:
						context.Add( ResourceType.Scene, $"maps/{entry.Name}", new MapLoader( entry ) );
						break;
					case Kind.Prefab:
						Prefabs[entry.Member] = $"mount://{Ident}/archetypes/{entry.Name}.prefab";
						context.Add( ResourceType.PrefabFile, $"archetypes/{entry.Name}", new ArchetypeLoader( entry ) );
						break;
					case Kind.Texture:
						var texturePath = $"mount://{Ident}/textures/{entry.Name}.vtex";
						TexturePaths[entry.Name] = texturePath;
						context.Add( ResourceType.Texture, $"textures/{entry.Name}", new AssetLoader( entry ) );
						break;
					case Kind.Collision:
						context.Add( ResourceType.Model, $"collision/{entry.Name}", new AssetLoader( entry ) );
						break;
					default:
						context.Add( ResourceType.Model, $"models/{entry.Name}", new AssetLoader( entry ) );
						break;
				}
			}
			context.Add( ResourceType.Scene, "gtaV", new WorldLoader() );
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
	internal Task<WorldCatalog> GetWorldCatalog( ArchiveReader reader ) => worldCatalog ??= Task.Run( () => WorldCatalog.Read( reader ) );

	IGtaWorldSession IGtaWorldSource.StartStreaming( GameObject root ) => new WorldSession( this, archive, root );

	internal IReadOnlyList<ArchetypeExtension> GetExtensions( Entry entry ) => archive.GetExtensions( entry.File, entry.Member );

	internal BinaryReader ReadObjectCollision( Entry entry ) => archive.Read( -6, entry.File, entry.Member );

	internal BinaryReader Read( Entry entry ) => archive.Read( (int)entry.Kind, entry.File, entry.Member );

	protected override void Shutdown()
	{
		worldCatalog = null;
		archive?.Dispose();
		archive = null;
		Prefabs.Clear();
		TexturePaths.Clear();
	}
}
