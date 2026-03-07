using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Sandbox.Mounting;
using Sandbox.Mounting.Sims4;

public readonly record struct ObjectDefinitionSet(
	string Label,
	DbpfRecord Record,
	string ResolvedName,
	IReadOnlyList<ulong> ModelInstances,
	IReadOnlyList<ulong> GeomInstances,
	IReadOnlyList<ulong> MaterialInstances,
	IReadOnlyList<ulong> TextureInstances )
{
	public override string ToString()
	{
		var sb = new StringBuilder();
		sb.AppendLine( $"[{Label}] type_id=0x{Record.TypeId:X8}" );
		sb.AppendLine( $"[{Label}] group_id=0x{Record.GroupId:X8}" );
		sb.AppendLine( $"[{Label}] instance_id=0x{Record.InstanceId:X16}" );
		sb.AppendLine( $"[{Label}] resolved_name={(string.IsNullOrWhiteSpace( ResolvedName ) ? "<unresolved>" : ResolvedName)}" );
		sb.AppendLine( $"[{Label}] file_offset=0x{Record.FileOffset:X}" );
		sb.AppendLine( $"[{Label}] compression_type=0x{Record.CompressionType:X4}" );
		sb.AppendLine( $"[{Label}] compressed_size={Record.CompressedSize}" );
		sb.AppendLine( $"[{Label}] decompressed_size={Record.DecompressedSize}" );
		sb.AppendLine( $"[{Label}] model_instances={FormatInstances( ModelInstances )}" );
		sb.AppendLine( $"[{Label}] geom_instances={FormatInstances( GeomInstances )}" );
		sb.AppendLine( $"[{Label}] material_instances={FormatInstances( MaterialInstances )}" );
		sb.AppendLine( $"[{Label}] texture_instances={FormatInstances( TextureInstances )}" );

		return sb.ToString().TrimEnd();
	}

	private static string FormatInstances( IReadOnlyList<ulong> instances ) =>
		instances.Count == 0
			? "[]"
			: $"[{string.Join( ", ", instances.Select( x => $"0x{x:X16}" ) )}]";
}

/// <summary>
/// Mounts The Sims 4 .package files into S&amp;box, exposing their textures
/// through the standard mounting API.
///
/// Supported resource types
/// ────────────────────────
///   Texture  (TypeID 0x00B2D882 — DDS image)
///
/// Discovery order
/// ───────────────
///   1. Steam (App ID 1222670)
///   2. EA App default install path
///   3. The path set in SIMS4_DIR environment variable (for custom installs)
///
/// Sims 4 resources do not have human-readable file names — each resource is
/// identified by a 64-bit instance hash. Textures are mounted at:
///   mount://sims4/textures/&lt;groupId&gt;/&lt;instanceId&gt;
/// </summary>
public class GameMount : BaseGameMount
{
	new internal static Sandbox.Diagnostics.Logger Log = new Sandbox.Diagnostics.Logger( "Sims4-Mount" );
	public override string Ident => "sims4";
	public override string Title => "The Sims 4";

	// TS4 on Steam
	const long AppId = 1222670;

	// Well-known EA App install paths (checked when Steam is not found)
	static readonly string[] EaAppPaths =
	[
		@"C:\Program Files\EA Games\The Sims 4",
		@"C:\Program Files (x86)\EA Games\The Sims 4",
	];

	string _gameDir;

	// All open package handles — kept alive for lazy data reads
	readonly List<DbpfPackage> _packages = new();
	private const int MaxMountedModels = 20;
	private int _mountedModelCount;

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
		_mountedModelCount = 0;

		var dataDir = System.IO.Path.Combine( _gameDir, "Data" );
		Log.Info( $"Looking for packages in: {dataDir}" );

		if ( !System.IO.Directory.Exists( dataDir ) )
		{
			Log.Warning( $"Sims 4 Data directory not found at: {dataDir}" );
			return Task.CompletedTask;
		}

		var packageFiles = System.IO.Directory.EnumerateFiles( dataDir, "*.package", System.IO.SearchOption.AllDirectories ).ToList();
		Log.Info( $"Found {packageFiles.Count} .package files" );

		foreach ( var pkgPath in packageFiles )
		{
			Log.Trace( $"Processing package: {pkgPath}" );
			try
			{
				MountPackage( context, pkgPath );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to mount package {pkgPath}: {ex.Message}" );
			}
		}

		Log.Info( $"Mount complete. Total packages loaded: {_packages.Count}" );
		IsMounted = true;
		return Task.CompletedTask;
	}

	private void MountPackage( MountContext context, string pkgPath )
	{
		DbpfPackage package = null;

		try
		{
			package = DbpfPackage.Open( pkgPath );
			_packages.Add( package );

			var resolvedNames = package.ReadResolvedNames();
			Log.Info( $"Resolved {resolvedNames.Count} names in package: {System.IO.Path.GetFileName( pkgPath )}" );
			foreach ( var entry in resolvedNames.OrderBy( x => x.Key ) )
				Log.Info( $"Name 0x{entry.Key:X16} = {entry.Value}" );

			int textureCount = 0;
			int modelCount = 0;
			foreach ( var asset in package.GetValidRecords() )
			{
				var record = asset.Record;
				Log.Trace( $"Found record: Type=0x{record.TypeId:X8} Group=0x{record.GroupId:X8} Instance=0x{record.InstanceId:X16}" );

				if ( asset.Kind == Sims4AssetKind.ObjectDefinition )
				{
					Log.Info( $"Found a object definition record (Type=0x{record.TypeId:X8} Group=0x{record.GroupId:X8} Instance=0x{record.InstanceId:X16}) - currently skipping as we don't support it yet" );

				}

				if ( asset.Kind == Sims4AssetKind.Model )
				{
					if ( _mountedModelCount >= MaxMountedModels )
						break;

					modelCount++;
					_mountedModelCount++;
					// Build a stable virtual path from the group and instance IDs.
					// Group 0 is common so we elide it to reduce path clutter.
					var folder = record.GroupId == 0
						? "models"
						: $"models/{record.GroupId:X8}";

					// Try to resolve friendly name from name map
					string path;
					if ( package.TryResolveName( record.InstanceId, out var resolvedName ) && !string.IsNullOrWhiteSpace( resolvedName ) )
					{
						path = $"{folder}/{SanitizePathSegment( resolvedName )}_{record.InstanceId:X16}";
					}
					else
					{
						Log.Warning( $"Could not resolve name for model instance 0x{record.InstanceId:X16} in package {System.IO.Path.GetFileName( pkgPath )}" );
						path = $"{folder}/unresolved_{record.InstanceId:X16}";
					}

					Log.Trace( $"Mounting model: {path}" );
					context.Add( ResourceType.Model, path, new Sims4ModelLoader( package, record ) );
				}

				if ( asset.Kind == Sims4AssetKind.Texture )
				{
					textureCount++;
					// Build a stable virtual path from the group and instance IDs.
					// Group 0 is common so we elide it to reduce path clutter.
					var folder = record.GroupId == 0
						? "textures"
						: $"textures/{record.GroupId:X8}";

					// Try to resolve friendly name from name map
					string path;
					if ( package.TryResolveName( record.InstanceId, out var resolvedName ) && !string.IsNullOrWhiteSpace( resolvedName ) )
					{
						path = $"{folder}/{SanitizePathSegment( resolvedName )}_{record.InstanceId:X16}";
					}
					else
					{
						Log.Warning( $"Could not resolve name for texture instance 0x{record.InstanceId:X16} in package {System.IO.Path.GetFileName( pkgPath )}" );
						// Fallback: use shortened instance ID for readability
						// Only use last 8 hex chars to make names shorter
						path = $"{folder}/texture_{record.InstanceId:X8}";
					}

					Log.Trace( $"Mounting texture: {path}" );
					context.Add( ResourceType.Texture, path, new Sims4TextureLoader( package, record ) );
				}
			}

			Log.Info( $"Mounted {System.IO.Path.GetFileName( pkgPath )}: {textureCount} textures, {modelCount} models from {package.Records.Count} total records" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to mount package {pkgPath}: {ex}" );
			package?.Dispose();
			throw;
		}
	}

	private static string SanitizePathSegment( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return "unnamed";

		Span<char> invalid = stackalloc char[]
		{
			'<', '>', ':', '"', '/', '\\', '|', '?', '*'
		};

		var result = value.Trim();
		foreach ( var ch in invalid )
			result = result.Replace( ch, '_' );

		return result;
	}
	//
	protected override void Shutdown()
	{
		foreach ( var pkg in _packages )
			pkg.Dispose();

		_packages.Clear();
	}
}
