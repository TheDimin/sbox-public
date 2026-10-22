using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor
{
	/// <summary>
	/// A class to CRUD json files. This should probably be a generic class since it seems
	/// like we might want to do this with stuff other than meta files. But there's no need for
	/// that right now, so lets leave it simple.
	/// </summary>
	public class MetaData
	{
		/// <summary>
		/// File path to the metadata file.
		/// </summary>
		public string FilePath { get; }

		internal MetaData( string sourceFile )
		{
			FilePath = sourceFile;
		}

		[ThreadStatic] static MetaData _scopeOwner;
		[ThreadStatic] static JsonElement? _scopeRoot;
		[ThreadStatic] static ReadStamp? _scopeStamp;
		[ThreadStatic] static bool _scopeRead;
		[ThreadStatic] static int _scopeVersion;
		static int _writeVersion;

		internal readonly record struct ReadStamp( DateTime WriteTimeUtc, long Length, int WriteVersion );
		internal sealed record MissingProperty( MetaData Owner, string Key, ReadStamp Stamp );

		bool IsCurrent( ReadStamp stamp )
		{
			if ( AssetPipelineCompatibility.IsLegacyForced || stamp.WriteVersion != System.Threading.Volatile.Read( ref _writeVersion ) )
				return false;
			if ( _scopeOwner == this && _scopeRead && _scopeVersion == stamp.WriteVersion )
				return _scopeStamp == stamp;

			var file = new System.IO.FileInfo( FilePath );
			file.Refresh();
			return file.Exists && file.LastWriteTimeUtc == stamp.WriteTimeUtc && file.Length == stamp.Length
				&& stamp.WriteVersion == System.Threading.Volatile.Read( ref _writeVersion );
		}

		/// <summary>
		/// Reuse one document during a synchronous asset update. The bounded cache still
		/// validates the first read, and saves invalidate scopes across nested files and threads.
		/// </summary>
		internal static ReadScope CacheReads( MetaData meta ) => new( meta );

		internal readonly struct ReadScope : IDisposable
		{
			readonly MetaData _owner;
			readonly MetaData _previousOwner;
			readonly JsonElement? _previousRoot;
			readonly ReadStamp? _previousStamp;
			readonly bool _previousRead;
			readonly int _previousVersion;

			internal ReadScope( MetaData owner )
			{
				_owner = AssetPipelineCompatibility.IsLegacyForced ? null : owner;
				_previousOwner = _scopeOwner;
				_previousRoot = _scopeRoot;
				_previousStamp = _scopeStamp;
				_previousRead = _scopeRead;
				_previousVersion = _scopeVersion;
				if ( _owner is null ) return;
				_scopeOwner = _owner;
				_scopeRoot = null;
				_scopeStamp = null;
				_scopeRead = false;
			}

			public void Dispose()
			{
				if ( _owner is null ) return;
				_scopeOwner = _previousOwner;
				_scopeRoot = _previousRoot;
				_scopeStamp = _previousStamp;
				_scopeRead = _previousRead && _previousOwner != _owner;
				_scopeVersion = _previousVersion;
			}
		}

		JsonElement? Read() => Read( out _ );

		JsonElement? Read( out ReadStamp? stamp )
		{
			if ( AssetPipelineCompatibility.IsLegacyForced )
			{
				_scopeRead = false;
				return ReadUnscoped( out stamp );
			}
			if ( _scopeOwner != this )
				return ReadUnscoped( out stamp );

			var version = System.Threading.Volatile.Read( ref _writeVersion );
			if ( !_scopeRead || _scopeVersion != version )
			{
				_scopeRoot = ReadUnscoped( out _scopeStamp );
				_scopeRead = true;
				_scopeVersion = version;
			}
			stamp = _scopeStamp;
			return _scopeRoot;
		}

		JsonElement? ReadUnscoped( out ReadStamp? stamp )
		{
			stamp = null;
			const int stableReadAttempts = 3;
			var bypassCache = AssetPipelineCompatibility.IsLegacyForced;
			var writeVersion = System.Threading.Volatile.Read( ref _writeVersion );

			if ( bypassCache )
				MetaDataDocumentCache.Remove( FilePath );

			for ( var attempt = 0; attempt < stableReadAttempts; attempt++ )
			{
				var before = new System.IO.FileInfo( FilePath );
				before.Refresh();

				if ( !before.Exists )
				{
					MetaDataDocumentCache.Remove( FilePath );
					return null;
				}

				if ( !bypassCache
					&& MetaDataDocumentCache.TryGet( FilePath, before.LastWriteTimeUtc, before.Length, out var cachedRoot ) )
				{
					stamp = new ReadStamp( before.LastWriteTimeUtc, before.Length, writeVersion );
					return cachedRoot;
				}

				try
				{
					var json = System.IO.File.ReadAllText( FilePath );
					using var document = JsonDocument.Parse( json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip } );
					var root = document.RootElement.Clone();

					var after = new System.IO.FileInfo( FilePath );
					after.Refresh();

					if ( !after.Exists )
					{
						MetaDataDocumentCache.Remove( FilePath );
						return null;
					}

					var stableIdentity = before.LastWriteTimeUtc == after.LastWriteTimeUtc
						&& before.Length == after.Length;

					if ( !stableIdentity && attempt + 1 < stableReadAttempts )
						continue;

					if ( stableIdentity && !bypassCache )
					{
						MetaDataDocumentCache.Store( FilePath, after.LastWriteTimeUtc, after.Length, root );
						stamp = new ReadStamp( after.LastWriteTimeUtc, after.Length, writeVersion );
					}

					return root;
				}
				catch ( System.Exception e )
				{
					if ( attempt + 1 < stableReadAttempts
						&& (e is System.IO.IOException || e is JsonException) )
						continue;

					MetaDataDocumentCache.Remove( FilePath );
					Log.Warning( e, $"Couldn't parse '{FilePath}' ({e.Message})" );
					return null;
				}
			}

			return null;
		}

		/// <summary>
		/// Start from the cached document when possible.
		/// </summary>
		JsonObject StartWrite()
		{
			var o = new JsonNodeOptions { PropertyNameCaseInsensitive = true };

			var e = Read();
			if ( e is null || e.Value.ValueKind == JsonValueKind.Null )
				return new JsonObject( o );

			return JsonObject.Create( e.Value, o );
		}

		void Save( JsonObject obj )
		{
			const int retries = 10;
			for ( var i = 0; i < retries; i++ )
			{
				try
				{
					using ( var stream = System.IO.File.Open( FilePath, System.IO.FileMode.Create ) )
					{
						using ( Utf8JsonWriter writer = new Utf8JsonWriter( stream, new JsonWriterOptions { Indented = true, SkipValidation = true } ) )
						{
							obj.WriteTo( writer );
						}
					}

					MetaDataDocumentCache.Remove( FilePath );
					System.Threading.Interlocked.Increment( ref _writeVersion );
					return;
				}
				catch ( System.IO.DirectoryNotFoundException ex )
				{
					// Not a transient failure like a locked file - waiting won't make the folder appear
					Log.Warning( $"Failed to save {FilePath} ({ex.Message})" );
					return;
				}
				catch ( System.IO.IOException ex )
				{
					const int delay = 100;
					Log.Warning( $"Failed to save {FilePath} ({ex.Message}). Retrying in {delay}ms... ({i + 1}/{retries})" );
					System.Threading.Thread.Sleep( delay );
				}
			}
		}

		public JsonElement? GetElement( string keyName )
		{
			var root = Read();

			if ( root == null )
				return null;

			if ( root.Value.ValueKind != JsonValueKind.Object )
				return null;

			if ( !root.Value.TryGetProperty( keyName, out var value ) )
				return null;

			return value;
		}

		public T Get<T>( string keyName, T defaultValue = default( T ) )
		{
			try
			{
				// Handle errors from Read() as well but only for reading.
				// If we do it for StartSave, we risk losing data..
				var e = GetElement( keyName );
				if ( e == null ) return defaultValue;

				return e.Value.Deserialize<T>( JsonSerializerOptions.Default ) ?? defaultValue;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"MetaData.Get<{typeof( T )}>( '{keyName}' ) - {e.Message}" );

				// if it was the wrong type, we don't care
				return defaultValue;
			}
		}

		/// <summary>
		/// Cache only the absence of a property, without retaining its JSON document.
		/// Failed reads and invalid values are retried through the normal read path.
		/// </summary>
		internal T GetWithMissingPropertyCache<T>( string keyName, ref MissingProperty missingProperty )
		{
			try
			{
				if ( missingProperty is { } previous && previous.Owner == this && previous.Key == keyName && IsCurrent( previous.Stamp ) ) return default;
				missingProperty = null;

				var root = Read( out var stamp );
				if ( root is null || root.Value.ValueKind != JsonValueKind.Object ) return default;
				if ( !root.Value.TryGetProperty( keyName, out var value ) )
				{
					if ( stamp is { } identity ) missingProperty = new MissingProperty( this, keyName, identity );
					return default;
				}

				return value.Deserialize<T>( JsonSerializerOptions.Default );
			}
			catch ( System.Exception e )
			{
				missingProperty = null;
				Log.Warning( e, $"MetaData.Get<{typeof( T )}>( '{keyName}' ) - {e.Message}" );
				return default;
			}
		}

		public bool TryGet<T>( string keyName, out T value )
		{
			try
			{
				// Handle errors from Read() as well but only for reading.
				// If we do it for StartSave, we risk losing data..
				if ( GetElement( keyName ) is { } e && e.Deserialize<T>( JsonSerializerOptions.Default ) is T result )
				{
					value = result;
					return true;
				}
			}
			catch
			{
				// if it was the wrong type, we don't care
			}

			value = default;
			return false;
		}

		public string GetString( string keyName, string defaultValue = default ) => Get<string>( keyName, defaultValue );
		public bool GetBool( string keyName, bool defaultValue = default ) => Get<bool>( keyName, defaultValue );
		public int GetInt( string keyName, int defaultValue = default ) => Get<int>( keyName, defaultValue );
		public float GetFloat( string keyName, float defaultValue = default ) => Get<float>( keyName, defaultValue );

		/// <summary>
		/// Set a value in the metadata file. If the value is null, the key will be removed.
		/// </summary>
		public void Set<T>( string name, T value )
		{
			var writer = StartWrite();

			if ( writer.ContainsKey( name ) )
				writer.Remove( name );

			if ( value != null )
			{
				var valueNode = JsonSerializer.SerializeToNode( value );
				writer.Add( name, valueNode );
			}

			Save( writer );
		}
	}
}
