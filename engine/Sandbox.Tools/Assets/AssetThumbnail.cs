using Native;
using System;

namespace Editor;

static class AssetThumbnail
{
	private static int MaximumConcurrentBuilds => AssetPipelineCompatibility.UseFastEditorPath
		? Math.Clamp( Math.Max( 1, Environment.ProcessorCount / 4 ), 1, 4 )
		: 1;

	private static readonly HashSet<Asset> NonResidentBuilds = new();
	private static int NonResidentBuildsSinceCollection;

	internal static int PendingBuildCount => RenderQueue.Count + RenderingList.Count;

	internal static string GetThumbnailFile( Asset asset, bool createDirectory )
	{
		bool isCloud = asset.AbsolutePath.Contains( ".sbox/cloud/" );
		var cacheFolder = $"/{(isCloud ? "thumbnails/.cloud" : "thumbnails")}/{System.IO.Path.GetDirectoryName( asset.Path )}";
		cacheFolder = cacheFolder.Replace( ":", "" );

		var cacheName = $"{cacheFolder}/{System.IO.Path.GetFileName( asset.Path )}.png";
		var fullPath = FileSystem.ProjectTemporary.GetFullPath( cacheName );

		if ( createDirectory )
			FileSystem.ProjectTemporary.CreateDirectory( cacheFolder );

		return fullPath;
	}

	internal static QPixmap GetAssetThumb( uint assetId )
	{
		var asset = AssetSystem.Get( assetId );
		return GetAssetThumb( asset )?.ptr ?? default;
	}

	/// <summary>
	/// Get an asset thumb. Will generate it if not immediately available, but will wait for it to be generated before returning.
	/// </summary>
	internal static async Task<Pixmap> GetAssetThumbAsync( Asset asset )
	{
		ArgumentNullException.ThrowIfNull( asset, nameof( asset ) );

		if ( asset.HasCachedThumbnail )
		{
			return asset.CachedThumbnail;
		}

		var fullPath = GetThumbnailFile( asset, false );
		if ( System.IO.File.Exists( fullPath ) )
		{
			var pix = Pixmap.FromFile( fullPath );
			asset.CachedThumbnail = pix;
			return asset.CachedThumbnail;
		}

		// start an async render
		QueueThumbBuild( asset );

		// wait for it to come out of the list
		while ( RenderQueue.Contains( asset ) || RenderingList.Contains( asset ) )
		{
			await Task.Delay( 100 );
		}

		// If it was null still
		asset.CachedThumbnail ??= asset.AssetType.Icon256;

		return asset.CachedThumbnail;
	}

	internal static Pixmap GetAssetThumb( Asset asset, bool generateIfNotInCache = true )
	{
		ArgumentNullException.ThrowIfNull( asset, nameof( asset ) );

		if ( asset.HasCachedThumbnail )
		{
			return asset.CachedThumbnail;
		}

		var fullPath = GetThumbnailFile( asset, false );
		if ( System.IO.File.Exists( fullPath ) )
		{
			var pix = Pixmap.FromFile( fullPath );
			asset.CachedThumbnail = pix;
			return asset.CachedThumbnail;
		}

		if ( asset.AssetType != null )
		{
			asset.CachedThumbnail = asset.AssetType.Icon256;
		}

		if ( generateIfNotInCache )
		{
			// start an async render
			QueueThumbBuild( asset );
		}

		return asset.CachedThumbnail;
	}

	internal static void RefreshThumbnail( uint assetId )
	{
		var asset = AssetSystem.Get( assetId );
		if ( asset is null ) return;
		NonResidentBuilds.Remove( asset );
		RenderQueue.EnqueueFirst( asset );
	}

	private static readonly UniqueWorkQueue<Asset> RenderQueue = new();
	private static readonly HashSet<Asset> RenderingList = new();

	internal static void DequeueThumbBuild( Asset asset )
	{
		if ( RenderingList.Contains( asset ) )
			return; // too late!

		RenderQueue.Remove( asset );
		NonResidentBuilds.Remove( asset );
	}

	internal static void QueueThumbBuild( Asset asset, bool add = true, bool keepResident = true )
	{
		if ( keepResident )
			NonResidentBuilds.Remove( asset );

		if ( RenderingList.Contains( asset ) )
			return;

		// A background cache-warm request must never downgrade an existing visible request.
		if ( !keepResident && !RenderQueue.Contains( asset ) )
			NonResidentBuilds.Add( asset );

		RenderQueue.Enqueue( asset, add );
	}

	internal static int QueueMissingThumbBuilds( IEnumerable<Asset> assets )
	{
		var queued = 0;

		foreach ( var asset in assets )
		{
			if ( System.IO.File.Exists( GetThumbnailFile( asset, false ) ) )
				continue;

			QueueThumbBuild( asset, keepResident: false );
			queued++;
		}

		return queued;
	}

	internal static void Frame()
	{
		while ( RenderingList.Count < MaximumConcurrentBuilds && RenderQueue.TryDequeue( out var asset ) )
		{
			RenderingList.Add( asset );
			_ = RenderThumbnailAsync( asset );
		}
	}

	static async Task RenderThumbnailAsync( Asset asset )
	{
		var keepResident = !NonResidentBuilds.Contains( asset );

		//
		// We always yield when calling this, so it'll be called
		// in the next frame, instead of RIGHT NOW. This prevents
		// issues with Qt, where we'd end up with recursive paint
		// errors and crashes.
		//
		await Task.Yield();

		try
		{
			using ( EditorUtility.DisableTextureStreaming() )
			{
				await asset.CacheAsync();
			}

			var pix = await RenderAssetThumb( asset );
			if ( pix != null )
			{
				asset.CachedThumbnail = pix;

				if ( keepResident && asset is NativeAsset nativeAsset )
				{
					IAssetPreviewSystem.OnThumbnailGenerated( nativeAsset.native, asset.CachedThumbnail.ptr );
				}

				var fullPath = GetThumbnailFile( asset, true );
				await Task.Run( () =>
				{
					var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
					try
					{
						pix.SavePng( temporaryPath );
						System.IO.File.Move( temporaryPath, fullPath, true );
					}
					finally
					{
						if ( System.IO.File.Exists( temporaryPath ) )
							System.IO.File.Delete( temporaryPath );
					}
				} );

				EditorEvent.RunInterface<AssetSystem.IEventListener>( x => x.OnAssetThumbGenerated( asset ) );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when compiling thumbnail for {asset.Path}" );
		}
		finally
		{
			if ( !keepResident )
				asset.CachedThumbnail = null;

			asset.Uncache();
			RenderingList.Remove( asset );
			NonResidentBuilds.Remove( asset );

			// Native model and material wrappers are weakly held, but their unmanaged allocations
			// are large enough that managed GC pressure does not trigger promptly. Bound their
			// lifetime during startup cache warming so thousands of previews cannot exhaust VRAM.
			if ( !keepResident && ++NonResidentBuildsSinceCollection >= 32 )
			{
				NonResidentBuildsSinceCollection = 0;
				GC.Collect( 2, GCCollectionMode.Forced, true, false );
				GC.WaitForPendingFinalizers();
			}
		}
	}

	internal static async Task<Pixmap> RenderAssetThumb( Asset asset )
	{
		ThreadSafe.AssertIsMainThread();

		//
		// If it's a resource, let it generate itself the thumbnail!
		//
		{
			var resource = asset.LoadResource();
			var bitmap = resource?.RenderThumbnail( new() { Width = 256, Height = 256 } );
			if ( bitmap != null )
			{
				return Pixmap.FromBitmap( bitmap );
			}
		}


		if ( asset.thumbnailOverride != null )
		{
			return asset.thumbnailOverride;
		}

		var extension = asset.AssetType.FileExtension;
		var methods = EditorTypeLibrary.GetMethodsWithAttribute<Asset.ThumbnailRendererAttribute>();

		foreach ( var t in methods.OrderByDescending( x => x.Attribute.Priority ) )
		{
			try
			{
				ThreadSafe.AssertIsMainThread();

				var pixmap = t.Method.InvokeWithReturn<Task<Bitmap>>( null, [asset] );
				if ( pixmap is null ) continue;

				ThreadSafe.AssertIsMainThread();

				var r = await pixmap;
				if ( r is null ) continue;

				ThreadSafe.AssertIsMainThread();

				return Pixmap.FromBitmap( r );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when rendering thumb via {t.Method} ({e.Message})" );
			}
		}

		if ( asset.AssetType.IsGameResource )
			return null;

		var pix = new Pixmap( 256, 256 );

		if ( asset is NativeAsset nativeAsset )
		{
			if ( IAssetPreviewSystem.RenderAssetThumbnail( nativeAsset.native, pix.ptr ) )
			{
				return pix;
			}
		}

		return null;
	}
}
