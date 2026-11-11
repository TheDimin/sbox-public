using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NativeEngine;
using Sandbox;

namespace Editor;

/// <summary>Creates one private compiled-model alias for editor readback.</summary>
public static partial class ModelReadbackAlias
{
	public const int MaxSourceBytes = 128 * 1024 * 1024;

	/// <summary>The alias owns its resource and only the two files it created.</summary>
	public sealed class Handle : IDisposable
	{
		internal Project Owner;
		internal string DirectoryPath;
		internal string CompiledPath;
		internal string MetaPath;
		internal string RelativePath;
		internal string CompiledHash;
		internal string MetaHash;
		internal bool CompiledCreated;
		internal bool MetaCreated;
		internal bool Registered;
		internal bool Disposed;
		internal Model OwnedModel;
		internal readonly System.Collections.Generic.List<FrozenOwnedFile> DependencyFiles = new();
		public Model Model => !Disposed ? OwnedModel : null;
		public Guid Guid { get; internal set; }
		public string SourceSha256 { get; internal set; }
		public string NonGuidSha256 { get; internal set; }
		public string AliasSha256 => CompiledHash;

		/// <summary>Release the private resource and remove only unchanged files created by this handle.</summary>
		public void Dispose()
		{
			if ( Disposed ) return;
			ThreadSafe.AssertIsMainThread();
			if ( !ReferenceEquals( Project.Current, Owner ) ) throw new InvalidOperationException( "The project changed before private alias cleanup." );
			Disposed = true;
			Exception failure = null;
			try
			{
				if ( OwnedModel != null )
				{
					Game.Resources.Unregister( OwnedModel );
					NativeResourceCache.Remove( OwnedModel.native.GetBindingPtr().ToInt64() );
					OwnedModel.Destroy();
					OwnedModel = null;
				}
			}
			catch ( Exception e ) { failure = e; }
			bool safePath = true;
			try { CheckNoLinks( DirectoryPath ); } catch ( Exception e ) { failure ??= e; safePath = false; }
			if ( safePath )
			{
				try { DeleteOwned( MetaPath, MetaHash, MetaCreated ); } catch ( Exception e ) { failure ??= e; }
				try { DeleteOwned( CompiledPath, CompiledHash, CompiledCreated ); } catch ( Exception e ) { failure ??= e; }
				foreach ( var file in DependencyFiles )
				{
					try { DeleteOwned( file.Meta, file.MetaHash, file.MetaCreated ); } catch ( Exception e ) { failure ??= e; }
					try { DeleteOwned( file.Compiled, file.CompiledHash, file.CompiledCreated ); } catch ( Exception e ) { failure ??= e; }
				}
			}
			try { if ( Registered ) g_pResourceSystem.NotifyMetaFileChanged( RelativePath, false ); } catch ( Exception e ) { failure ??= e; }
			foreach ( var file in DependencyFiles ) try { if ( file.Registered ) g_pResourceSystem.NotifyMetaFileChanged( file.Relative, false ); } catch ( Exception e ) { failure ??= e; }
			try { if ( safePath && Directory.Exists( DirectoryPath ) ) Directory.Delete( DirectoryPath, false ); } catch ( IOException ) { /* Preserve any foreign files. */ }
			if ( failure != null ) throw new InvalidOperationException( "Private model alias cleanup failed; inspect the owned directory.", failure );
		}
	}

	private static string Hash( byte[] value ) => Convert.ToHexString( SHA256.HashData( value ) ).ToLowerInvariant();
	private static string HashExceptGuid( byte[] data, int offset )
	{
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		hash.AppendData( data.AsSpan( 0, offset ) );
		hash.AppendData( data.AsSpan( offset + 16 ) );
		return Convert.ToHexString( hash.GetHashAndReset() ).ToLowerInvariant();
	}
	private static void DeleteOwned( string path, string expectedHash, bool created )
	{
		if ( !created || !File.Exists( path ) ) return;
		CheckNoLinks( path );
		using var input = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read );
		if ( input.Length > MaxSourceBytes || Convert.ToHexString( SHA256.HashData( input ) ).ToLowerInvariant() != expectedHash )
			throw new InvalidOperationException( "An owned alias file changed; it was not removed." );
		input.Close();
		File.Delete( path );
	}

	private static void CheckNoLinks( string absolute )
	{
		string current = Path.GetPathRoot( absolute );
		foreach ( var segment in absolute.Substring( current.Length ).Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
		{
			current = Path.Combine( current, segment );
			if ( (Directory.Exists( current ) || File.Exists( current )) && (File.GetAttributes( current ) & FileAttributes.ReparsePoint) != 0 )
				throw new InvalidOperationException( "Private alias paths cannot contain symbolic links." );
		}
	}

	private static int FindGuidBlock( byte[] data )
	{
		if ( data.Length < 16 || BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 4 ) ) != 12 || BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0 ) ) != data.Length ) throw new InvalidDataException( "Invalid or streaming model resource." );
		int table = checked(8 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 8 ) ));
		uint count = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 12 ) );
		if ( count > 4096 || table < 16 || table + 12L * count > data.Length ) throw new InvalidDataException( "Invalid model block table." );
		int found = -1;
		var ranges = new (int Start, int End)[count];
		for ( int i = 0; i < count; i++ )
		{
			int entry = checked(table + i * 12);
			int start = checked(entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 4 ) ));
			uint length = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( entry + 8 ) );
			if ( start < table + count * 12L || start > data.Length || length > data.Length - (long)start ) throw new InvalidDataException( "Invalid model block range." );
			ranges[i] = (start, checked(start + (int)length));
			if ( !data.AsSpan( entry, 4 ).SequenceEqual( "GUID"u8 ) ) continue;
			if ( found >= 0 || length != 16 ) throw new InvalidDataException( "Model must have one 16-byte GUID block." );
			found = i;
		}
		if ( found < 0 ) throw new InvalidDataException( "Model has no GUID block." );
		var guid = ranges[found];
		for ( int i = 0; i < ranges.Length; i++ )
			if ( i != found && ranges[i].Start < guid.End && guid.Start < ranges[i].End ) throw new InvalidDataException( "GUID block overlaps another resource block." );
		return guid.Start;
	}

	/// <summary>
	/// Copy original compiled bytes into a unique path under the current project's Assets,
	/// change only the GUID block, and load that alias. Never pass a source model to this API.
	/// Source content, cached source models and their GUIDs are not modified.
	/// </summary>
	public static Handle Create( byte[] source )
	{
		ThreadSafe.AssertIsMainThread();
		if ( source == null || source.Length < 16 || source.Length > MaxSourceBytes ) throw new InvalidOperationException( "Compiled model size is outside the alias limit." );
		var project = Project.Current ?? throw new InvalidOperationException( "Open a project before creating an alias." );
		string assets = Path.GetFullPath( project.GetAssetsPath() );
		if ( !Directory.Exists( assets ) ) throw new InvalidOperationException( "Project Assets directory is unavailable." );
		CheckNoLinks( assets );
		int offset = FindGuidBlock( source );
		var id = Guid.NewGuid();
		if ( Game.Resources.TryGet<Model>( id, out _ ) ) throw new InvalidOperationException( "Generated alias GUID is already in use." );
		var idText = id.ToString( "N" );
		string relative = $"geometrynodes/gib-readback/{idText}/source.vmdl";
		string directory = Path.Combine( assets, "geometrynodes", "gib-readback", idText );
		CheckNoLinks( directory );
		if ( Directory.Exists( directory ) || File.Exists( directory ) ) throw new InvalidOperationException( "Private alias directory already exists." );
		var handle = new Handle { Owner = project, DirectoryPath = directory, RelativePath = relative, Guid = id, SourceSha256 = Hash( source ) };
		try
		{
			Directory.CreateDirectory( directory ); CheckNoLinks( directory );
			byte[] copy = (byte[])source.Clone();
			if ( !id.TryWriteBytes( copy.AsSpan( offset, 16 ) ) ) throw new InvalidOperationException( "Could not write the private GUID." );
			handle.NonGuidSha256 = HashExceptGuid( source, offset );
			if ( handle.NonGuidSha256 != HashExceptGuid( copy, offset ) ) throw new InvalidOperationException( "Private alias differs outside the GUID block." );
			byte[] meta = Encoding.UTF8.GetBytes( $"{{\"guid\":\"{id}\"}}" );
			handle.CompiledPath = Path.Combine( directory, "source.vmdl_c" ); handle.MetaPath = Path.Combine( directory, "source.vmdl.meta" );
			handle.CompiledHash = Hash( copy ); handle.MetaHash = Hash( meta );
			using ( var file = new FileStream( handle.CompiledPath, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) { handle.CompiledCreated = true; file.Write( copy ); }
			using ( var file = new FileStream( handle.MetaPath, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) { handle.MetaCreated = true; file.Write( meta ); }
			if ( !ReferenceEquals( Project.Current, project ) ) throw new InvalidOperationException( "The active project changed during private alias creation." );
			g_pResourceSystem.NotifyMetaFileChanged( relative, true ); handle.Registered = true;
			var model = Model.Load( relative );
			if ( model == null || model.IsError || model.IsProcedural || model.Guid != id || !string.Equals( model.ResourcePath, relative, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidOperationException( "Private alias did not load with the expected GUID and path." );
			handle.OwnedModel = model;
			return handle;
		}
		catch { handle.Dispose(); throw; }
	}
}
