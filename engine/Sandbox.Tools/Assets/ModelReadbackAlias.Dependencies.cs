using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NativeEngine;
using Sandbox;
using Sandbox.Compression;

namespace Editor;

public static partial class ModelReadbackAlias
{
	public sealed record FrozenFile( string ResourcePath, Guid Guid, byte[] Bytes );
	internal sealed class FrozenOwnedFile { internal string Relative, Compiled, Meta, CompiledHash, MetaHash; internal bool CompiledCreated, MetaCreated, Registered; }

	/// <summary>Load an already frozen, privately named resource closure. Original bytes/hashes remain owned by the caller.</summary>
	public static Handle CreateFrozen( FrozenFile parent, IReadOnlyList<FrozenFile> dependencies, string originalSourceSha256 )
	{
		ThreadSafe.AssertIsMainThread();
		if ( parent == null || dependencies == null || dependencies.Count > 511 || originalSourceSha256 == null || originalSourceSha256.Length != 64 || originalSourceSha256.Any( c => !Uri.IsHexDigit( c ) ) ) throw new InvalidOperationException( "Invalid frozen resource closure." );
		var project = Project.Current ?? throw new InvalidOperationException( "Open a project before creating frozen aliases." );
		var parts = parent.ResourcePath?.Split( '/' ); if ( parts == null || parts.Length != 4 || parts[0] != "geometrynodes" || parts[1] != "gib-readback" || parts[2].Length != 32 || parts[2].Any( c => !Uri.IsHexDigit( c ) ) || parts[3] != "source.vmdl" ) throw new InvalidOperationException( "Frozen parent must use one private readback directory." );
		string prefix = string.Join( '/', parts.Take( 3 ) ); string assets = Path.GetFullPath( project.GetAssetsPath() ); string directory = Path.Combine( assets, prefix ); CheckNoLinks( directory ); if ( Directory.Exists( directory ) || File.Exists( directory ) ) throw new InvalidOperationException( "Frozen alias directory already exists." );
		var paths = new HashSet<string>( StringComparer.OrdinalIgnoreCase ); long total = 0; var copies = new List<FrozenFile>();
		foreach ( var item in dependencies.Append( parent ) )
		{
			if ( item == null || item.Guid == Guid.Empty || item.Bytes == null || item.Bytes.Length < 16 || item.Bytes.Length > MaxSourceBytes || item.ResourcePath == null || !item.ResourcePath.StartsWith( prefix + "/", StringComparison.Ordinal ) || item.ResourcePath[(prefix.Length + 1)..].Contains( '/' ) || item.ResourcePath.Contains( '\\' ) || item.ResourcePath.Contains( ':' ) || item.ResourcePath.Split( '/' ).Any( p => p == "." || p == ".." ) || !paths.Add( item.ResourcePath ) ) throw new InvalidOperationException( "Invalid frozen dependency path or bytes." );
			total = checked(total + item.Bytes.Length); if ( total > MaxSourceBytes ) throw new InvalidOperationException( "Frozen closure exceeds aggregate bytes." ); var copy = (byte[])item.Bytes.Clone(); int offset = FindGuidBlock( copy ); if ( new Guid( copy.AsSpan( offset, 16 ) ) != item.Guid ) throw new InvalidOperationException( "Frozen resource GUID differs from metadata." ); copies.Add( item with { Bytes = copy } );
		}
		var root = copies[^1]; var handle = new Handle { Owner = project, DirectoryPath = directory, RelativePath = root.ResourcePath, Guid = root.Guid, SourceSha256 = originalSourceSha256, NonGuidSha256 = HashExceptGuid( root.Bytes, FindGuidBlock( root.Bytes ) ) };
		try
		{
			Directory.CreateDirectory( directory ); CheckNoLinks( directory );
			foreach ( var item in copies.Take( copies.Count - 1 ) )
			{
				var file = new FrozenOwnedFile { Relative = item.ResourcePath, Compiled = Path.Combine( assets, item.ResourcePath + "_c" ), Meta = Path.Combine( assets, item.ResourcePath + ".meta" ) }; handle.DependencyFiles.Add( file ); var meta = Encoding.UTF8.GetBytes( $"{{\"guid\":\"{item.Guid}\"}}" ); file.CompiledHash = Hash( item.Bytes ); file.MetaHash = Hash( meta );
				using ( var output = new FileStream( file.Compiled, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) { file.CompiledCreated = true; output.Write( item.Bytes ); }
				using ( var output = new FileStream( file.Meta, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) { file.MetaCreated = true; output.Write( meta ); }
				g_pResourceSystem.NotifyMetaFileChanged( file.Relative, true ); file.Registered = true;
			}
			var rootMeta = Encoding.UTF8.GetBytes( $"{{\"guid\":\"{root.Guid}\"}}" ); handle.CompiledPath = Path.Combine( assets, root.ResourcePath + "_c" ); handle.MetaPath = Path.Combine( assets, root.ResourcePath + ".meta" ); handle.CompiledHash = Hash( root.Bytes ); handle.MetaHash = Hash( rootMeta );
			using ( var output = new FileStream( handle.CompiledPath, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) { handle.CompiledCreated = true; output.Write( root.Bytes ); }
			using ( var output = new FileStream( handle.MetaPath, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) { handle.MetaCreated = true; output.Write( rootMeta ); }
			g_pResourceSystem.NotifyMetaFileChanged( root.ResourcePath, true ); handle.Registered = true;
			var model = Model.Load( root.ResourcePath ); if ( model == null || model.IsError || model.IsProcedural || model.Guid != root.Guid || !string.Equals( model.ResourcePath, root.ResourcePath, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidOperationException( "Frozen private model did not load with expected identity." ); handle.OwnedModel = model; return handle;
		}
		catch { handle.Dispose(); throw; }
	}

	/// <summary>Retain native KV3v2 numeric/type lanes while replacing complete resource path strings. No arbitrary byte substitution.</summary>
	public static byte[] RewriteReferenceStrings( byte[] serialized, IReadOnlyDictionary<string, string> paths )
	{
		if ( serialized == null || paths == null || serialized.Length < 64 || serialized.Length > MaxSourceBytes || U32( serialized, 0 ) != 0x4b563302 || I32( serialized, 20 ) is not (0 or 1) || I32( serialized, 56 ) != 0 || I32( serialized, 60 ) != 0 ) throw new InvalidDataException( "Unsupported bounded native KV3 dependency encoding." );
		int c1 = I32( serialized, 28 ), c4 = I32( serialized, 32 ), c8 = I32( serialized, 36 ), section = I32( serialized, 40 ), length = I32( serialized, 48 ), compressed = I32( serialized, 52 );
		if ( c1 < 0 || c4 < 1 || c8 < 0 || section < 1 || length < 4 || length > MaxSourceBytes || compressed < 1 || compressed != serialized.Length - 64 ) throw new InvalidDataException( "Invalid KV3 dependency lanes." );
		var payload = new byte[length]; int compression = I32( serialized, 20 ); if ( compression == 0 ) { if ( compressed != length ) throw new InvalidDataException( "Uncompressed KV3 dependency size differs." ); serialized.AsSpan( 64 ).CopyTo( payload ); } else if ( LZ4.DecompressBlock( serialized.AsSpan( 64 ), payload ) != length ) throw new InvalidDataException( "KV3 dependency decompression differs." );
		long lane4 = ((long)c1 + 3) & ~3L; long lane8 = (lane4 + 4L * c4 + 7) & ~7L; long strings = lane8 + 8L * c8;
		if ( strings > length - 4 || lane4 > strings - 4 || strings + section != length - 4 || U32( payload, length - 4 ) != 0xffeedd00 ) throw new InvalidDataException( "KV3 dependency string section differs." );
		int count = I32( payload, (int)lane4 ); if ( count < 0 || count > 1000000 ) throw new InvalidDataException( "KV3 dependency string count exceeds limit." );
		var utf8 = new UTF8Encoding( false, true ); int cursor = (int)strings; bool changed = false; using var rewritten = new MemoryStream(); rewritten.Write( payload, 0, (int)strings );
		for ( int i = 0; i < count; i++ ) { int end = Array.IndexOf( payload, (byte)0, cursor ); if ( end < cursor || end >= length - 4 || end - cursor > 16 * 1024 * 1024 ) throw new InvalidDataException( "Invalid KV3 dependency string." ); string text = utf8.GetString( payload, cursor, end - cursor ); if ( paths.TryGetValue( text, out var replacement ) ) { if ( string.IsNullOrWhiteSpace( replacement ) || replacement.Length > 1024 ) throw new InvalidDataException( "Invalid private resource string." ); text = replacement; changed = true; } rewritten.Write( utf8.GetBytes( text ) ); rewritten.WriteByte( 0 ); if ( rewritten.Length > MaxSourceBytes ) throw new InvalidDataException( "Rewritten KV3 dependency exceeds size limit." ); cursor = end + 1; }
		if ( !changed ) return (byte[])serialized.Clone(); rewritten.Write( payload, cursor, payload.Length - cursor ); if ( rewritten.Length > MaxSourceBytes ) throw new InvalidDataException( "Rewritten KV3 dependency exceeds size limit." ); var plain = rewritten.ToArray(); var encoded = compression == 0 ? plain : LZ4.CompressBlock( plain ); var output = new byte[checked(64 + encoded.Length)]; serialized.AsSpan( 0, 64 ).CopyTo( output ); encoded.CopyTo( output, 64 ); BinaryPrimitives.WriteInt32LittleEndian( output.AsSpan( 40 ), checked(section + plain.Length - payload.Length) ); BinaryPrimitives.WriteInt32LittleEndian( output.AsSpan( 48 ), plain.Length ); BinaryPrimitives.WriteInt32LittleEndian( output.AsSpan( 52 ), encoded.Length ); return output;
	}
	private static int I32( byte[] data, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset, 4 ) );
	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset, 4 ) );
}
