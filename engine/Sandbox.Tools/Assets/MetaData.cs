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

		/// <summary>
		/// Read and parse the metadata document when it changes on disk. Asset hydration often
		/// asks for multiple keys in succession, so retaining the parsed root avoids repeating
		/// the same file read and JSON parse for every key.
		/// </summary>
		JsonElement? Read()
		{
			const int stableReadAttempts = 3;
			var bypassCache = AssetPipelineCompatibility.IsLegacyForced;

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
						MetaDataDocumentCache.Store( FilePath, after.LastWriteTimeUtc, after.Length, root );

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
			if ( e == null ) return new JsonObject( o );

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
				// Handle errprs from Read() as well but only for reading.
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
