using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Rendering;

namespace Sandbox.Clutter;

/// <summary>
/// Batched clutter, GPU frustum-culled per view and drawn indirect through each model's material.
/// </summary>
internal class ClutterBatchSceneObject : SceneCustomObject
{
	private static ComputeShader CullShader = new( "shaders/clutter_cull_cs.shader" );

	[ConVar( "clutter_cull_frustum_scale", ConVarFlags.Cheat )]
	internal static float CullFrustumScale { get; set; } = 1.0f;

	private const int MaxLods = 4; // dont think we need more than that
	private const uint EmptySphereBits = 0xBF800000; // -1.0f radius marks an unused slot.

	internal struct LodParams
	{
		public Vector3 CameraPos;
		public float TanHalfFov;
		public float ViewportWidth;
		public float OrthoWidth;
	}

	internal static LodParams Lod { get; set; } = new() { TanHalfFov = 1.0f, ViewportWidth = 1920.0f };

	private static readonly int ArgsStride = Marshal.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>();
	private static readonly int ArgsInstanceCountOffset = Marshal.OffsetOf<GpuBuffer.IndirectDrawIndexedArguments>( nameof( GpuBuffer.IndirectDrawIndexedArguments.InstanceCount ) ).ToInt32();

	private readonly Model _model;
	private readonly ClutterGridSystem _paintedDrawOwner;
	private readonly int _lodCount;
	private readonly float _modelRadius;
	private Vector4 _lodSwitches = default;
	private float _lodBlendWidth;
	private BBox _baseBounds;
	private float _maximumScale;
	private float _localVertexBoundsMargin;
	private float _worldVertexBoundsMargin;
	private Vector4 _localVertexBoundsMargins;
	private Vector4 _worldVertexBoundsMargins;
	private readonly GpuBuffer<float> _lodDistances;

	private readonly int[] _drawCallCounts;

	private readonly CommandList _commandList = new( "ClutterBatch" );
	private readonly CommandList _shadowCommandList = new( "ClutterBatch.Shadow" );

	// A LOD remains shadow eligible unless every draw call in that LOD opts out
	// through its material. Mixed LODs keep casting shadows.
	private uint _shadowCastingLodMask;
	private bool _lastShadowPassDetected;
	private SceneObjectFlags _lastShadowRequiredFlags;
	private int _shadowPassCount;
	private int _shadowLodDrawsSkipped;

	internal uint ShadowCastingLodMask => _shadowCastingLodMask;
	internal bool LastShadowPassDetected => _lastShadowPassDetected;
	internal SceneObjectFlags LastShadowRequiredFlags => _lastShadowRequiredFlags;
	internal int ShadowPassCount => _shadowPassCount;
	internal int ShadowLodDrawsSkipped => _shadowLodDrawsSkipped;

	private GpuBuffer<GpuInstanceTransform>[] _visible;

	private GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>[] _args;

	private int _count;
	private int _capacity;

	private GpuBuffer<GpuInstanceTransform> _instances;

	private GpuBuffer<Vector4> _spheres;

	private readonly record struct TileSlot( int Offset, int Count )
	{
		public int End => Offset + Count;
	}

	private readonly Dictionary<PreparedInstances, TileSlot> _tileSlots = [];
	private readonly List<TileSlot> _freeSlots = [];
	private readonly HashSet<PreparedInstances> _incomingTiles = [];
	private readonly List<PreparedInstances> _removedTiles = [];
	private readonly List<PreparedInstances> _addedTiles = [];
	private readonly List<TileSlot> _slotsToClear = [];
	private int _highWater;

	/// <summary>
	/// Prepared tile data owned by the layer. Buffers return to the pool after the tile leaves its batch.
	/// </summary>
	internal sealed class PreparedInstances : IDisposable
	{
		public GpuInstanceTransform[] Transforms { get; }
		public Vector4[] Spheres { get; }
		public BBox Bounds { get; private set; }
		public int Count { get; }
		public float MaximumScale { get; private set; }

		private int _next;
		private readonly BBox _modelBounds;
		private readonly float _modelRadius;

		public PreparedInstances( Model model, int count )
		{
			Count = count;
			Transforms = ArrayPool<GpuInstanceTransform>.Shared.Rent( count );
			Spheres = ArrayPool<Vector4>.Shared.Rent( count );
			_modelBounds = model.Bounds;
			_modelRadius = _modelBounds.Size.Length * 0.5f;
		}

		public void Add( Transform transform )
		{
			var center = transform.PointToWorld( _modelBounds.Center );
			var scale = transform.Scale;
			var maximumScale = MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) );
			MaximumScale = MathF.Max( MaximumScale, maximumScale );
			var radius = _modelRadius * maximumScale;
			Transforms[_next] = GpuInstanceTransform.From( transform );
			Spheres[_next] = new Vector4( center.x, center.y, center.z, radius );
			var bounds = _modelBounds.Transform( transform );
			Bounds = _next == 0 ? bounds : Bounds.AddBBox( bounds );
			_next++;
		}

		public void Dispose()
		{
			ArrayPool<GpuInstanceTransform>.Shared.Return( Transforms );
			ArrayPool<Vector4>.Shared.Return( Spheres );
		}
	}

	public ClutterBatchSceneObject( SceneWorld world, Model model, bool castShadows = true, ClutterGridSystem paintedDrawOwner = null ) : base( world )
	{
		_model = model;
		_paintedDrawOwner = paintedDrawOwner;
		_modelRadius = model.Bounds.Size.Length * 0.5f;

		var switches = model.GetLodSwitchDistances() ?? [];
		_lodCount = Math.Clamp( switches.Length, 1, MaxLods );
		_shadowCastingLodMask = (uint)((1 << _lodCount) - 1);

		var distances = new float[_lodCount];
		_drawCallCounts = new int[_lodCount];
		for ( int i = 0; i < _lodCount; i++ )
		{
			distances[i] = i < switches.Length ? switches[i] : 0f;
			_drawCallCounts[i] = Math.Max( 1, model.GetLodDrawCallCount( i ) );
		}

		for ( int i = 0; i < _lodCount; i++ ) _lodSwitches[i] = distances[i];
		_lodDistances = new GpuBuffer<float>( _lodCount, GpuBuffer.UsageFlags.Structured );
		_lodDistances.SetData( distances );

		_visible = new GpuBuffer<GpuInstanceTransform>[_lodCount];
		_args = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>[_lodCount];

		Flags.IsOpaque = true;
		Flags.IsTranslucent = false;
		Flags.CastShadows = castShadows;
		Flags.WantsPrePass = true;
	}

	/// <summary>
	/// Uploads the instance set to the persistent GPU buffers. Only called when the set changes.
	/// </summary>
	public void SetInstances( List<PreparedInstances> tiles )
	{
		_incomingTiles.Clear();
		foreach ( var tile in tiles )
			_incomingTiles.Add( tile );

		_removedTiles.Clear();
		foreach ( var tile in _tileSlots.Keys )
		{
			if ( !_incomingTiles.Contains( tile ) )
				_removedTiles.Add( tile );
		}

		_addedTiles.Clear();
		foreach ( var tile in tiles )
		{
			if ( !_tileSlots.ContainsKey( tile ) )
				_addedTiles.Add( tile );
		}

		if ( _removedTiles.Count == 0 && _addedTiles.Count == 0 )
			return;

		_slotsToClear.Clear();
		foreach ( var tile in _removedTiles )
		{
			var slot = _tileSlots[tile];
			_slotsToClear.Add( slot );
			_tileSlots.Remove( tile );
			FreeSlot( slot );
		}

		foreach ( var tile in _addedTiles )
			_tileSlots[tile] = AllocateSlot( tile.Count );

		_count = _highWater;
		if ( _count == 0 )
		{
			BuildCommandList();
			return;
		}

		bool resized = EnsureCapacity( _count );
		if ( resized )
		{
			// New buffers have no tile data. Mark unused slots, then restore every active tile.
			_spheres.Clear( EmptySphereBits );
			foreach ( var (tile, slot) in _tileSlots )
				UploadTile( tile, slot );
		}
		else
		{
			foreach ( var slot in _slotsToClear )
			{
				using var empty = new PooledSpan<Vector4>( slot.Count );
				empty.Span.Fill( new Vector4( 0, 0, 0, -1 ) );
				_spheres.SetData( empty.Span, slot.Offset );
			}
			foreach ( var tile in _addedTiles )
				UploadTile( tile, _tileSlots[tile] );
		}

		var worldBounds = tiles[0].Bounds;
		foreach ( var tile in tiles )
			worldBounds = worldBounds.AddBBox( tile.Bounds );

		_baseBounds = worldBounds;
		_maximumScale = tiles.Max( tile => tile.MaximumScale );
		UpdateDeformationBounds( true );
		BuildCommandList();
	}

	/// <summary>
	/// Shader margins cover vertex motion without changing the resting spheres used for LOD.
	/// Refresh after material hotload, including while the instance set stays unchanged.
	/// </summary>
	internal void UpdateDeformationBounds( bool force = false )
	{
		if ( _count == 0 )
		{
			if ( Attributes.GetInt( "VertexMotionVectorsAvailable" ) != 0 )
				Attributes.Set( "VertexMotionVectorsAvailable", 0 );
			return;
		}
		bool supportsMotionVectors = true;
		var shadowCastingLodMask = CalculateShadowCastingLodMask();
		bool shadowCastingLodMaskChanged = shadowCastingLodMask != _shadowCastingLodMask;
		_shadowCastingLodMask = shadowCastingLodMask;
		float blendWidth = 0.15f;
		int materialCount = 0;
		var locals = Vector4.Zero;
		var worlds = Vector4.Zero;
		foreach ( var mesh in _model.MeshInfo.Meshes )
		{
			foreach ( var draw in mesh.DrawCalls )
			{
				var material = draw.Material;
				materialCount++;
				supportsMotionVectors &= material.Flags.GetBool( "VertexMotionVectors" );
				var width = material.Flags.GetFloat( "ClutterLodBlendWidth" );
				blendWidth = !float.IsFinite( width ) || width <= 0f ? 0f : MathF.Min( blendWidth, width );
				var localMargin = material.Flags.GetFloat( "VertexBoundsLocalMargin" );
				var worldMargin = material.Flags.GetFloat( "VertexBoundsWorldMargin" );
				if ( !float.IsFinite( localMargin ) || localMargin < 0f || !float.IsFinite( worldMargin ) || worldMargin < 0f )
					throw new InvalidOperationException( $"{material.ResourcePath} has invalid vertex bounds margins." );
				for ( int lod = 0; lod < _lodCount; lod++ )
				{
					if ( (mesh.LodMask & (1 << lod)) == 0 ) continue;
					locals[lod] = MathF.Max( locals[lod], localMargin );
					worlds[lod] = MathF.Max( worlds[lod], worldMargin );
				}
			}
		}
		// All LOD draws must implement the same pass. Refresh before the bounds
		// early return so material hotload can remove support without moving plants.
		// Crossfade is opt-in on every material, including every shadow/depth draw.
		// Unordered or missing thresholds use the existing hard LOD selector.
		for ( int lod = 1; lod < _lodCount; lod++ )
			if ( !float.IsFinite( _lodSwitches[lod] ) || _lodSwitches[lod] <= _lodSwitches[lod - 1] ) blendWidth = 0f;
		_lodBlendWidth = materialCount > 0 && _lodCount > 1 ? blendWidth : 0f;
		int motionVectorsAvailable = supportsMotionVectors && materialCount > 0 ? 1 : 0;
		if ( Attributes.GetInt( "VertexMotionVectorsAvailable" ) != motionVectorsAvailable )
			Attributes.Set( "VertexMotionVectorsAvailable", motionVectorsAvailable );
		if ( !force && !shadowCastingLodMaskChanged && locals == _localVertexBoundsMargins && worlds == _worldVertexBoundsMargins ) return;
		_localVertexBoundsMargins = locals;
		_worldVertexBoundsMargins = worlds;
		float local = 0f, world = 0f, total = 0f;
		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			local = MathF.Max( local, locals[lod] );
			world = MathF.Max( world, worlds[lod] );
			total = MathF.Max( total, locals[lod] * _maximumScale + worlds[lod] );
		}
		_localVertexBoundsMargin = local;
		_worldVertexBoundsMargin = world;
		Bounds = _baseBounds.Grow( total );
		if ( shadowCastingLodMaskChanged && !force && _instances is not null )
			BuildCommandList();
	}

	private uint CalculateShadowCastingLodMask()
	{
		uint mask = 0;
		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			bool hasDrawCall = false;
			bool hasCastingMaterial = false;
			foreach ( var mesh in _model.MeshInfo.Meshes )
			{
				if ( (mesh.LodMask & (1 << lod)) == 0 ) continue;
				foreach ( var draw in mesh.DrawCalls )
				{
					hasDrawCall = true;
					if ( draw.Material is null || !draw.Material.Flags.GetBool( "DoNotCastShadows" ) )
					{
						hasCastingMaterial = true;
						break;
					}
				}
				if ( hasCastingMaterial ) break;
			}

			// Missing draw-call metadata is treated as casting to preserve the old
			// behaviour and avoid removing a shadow from an unknown model.
			if ( !hasDrawCall || hasCastingMaterial ) mask |= (uint)(1 << lod);
		}

		return mask;
	}

	private void UploadTile( PreparedInstances tile, TileSlot slot )
	{
		_instances.SetData( tile.Transforms.AsSpan( 0, tile.Count ), slot.Offset );
		_spheres.SetData( tile.Spheres.AsSpan( 0, tile.Count ), slot.Offset );
	}

	private TileSlot AllocateSlot( int count )
	{
		int bestSlotIndex = -1;
		for ( int i = 0; i < _freeSlots.Count; i++ )
		{
			if ( _freeSlots[i].Count >= count && (bestSlotIndex < 0 || _freeSlots[i].Count < _freeSlots[bestSlotIndex].Count) )
				bestSlotIndex = i;
		}

		if ( bestSlotIndex >= 0 )
		{
			var free = _freeSlots[bestSlotIndex];
			if ( free.Count == count )
				_freeSlots.RemoveAt( bestSlotIndex );
			else
				_freeSlots[bestSlotIndex] = new TileSlot( free.Offset + count, free.Count - count );

			return new TileSlot( free.Offset, count );
		}

		var slot = new TileSlot( _highWater, count );
		_highWater += count;
		return slot;
	}

	private void FreeSlot( TileSlot slot )
	{
		_freeSlots.Add( slot );
		_freeSlots.Sort( static ( a, b ) => a.Offset.CompareTo( b.Offset ) );

		// Merge adjacent ranges so future tiles can reuse the space without growing the buffers.
		for ( int i = 1; i < _freeSlots.Count; )
		{
			var previous = _freeSlots[i - 1];
			var next = _freeSlots[i];
			if ( previous.End == next.Offset )
			{
				_freeSlots[i - 1] = new TileSlot( previous.Offset, previous.Count + next.Count );
				_freeSlots.RemoveAt( i );
			}
			else
			{
				i++;
			}
		}

		while ( _freeSlots.Count > 0 && _freeSlots[^1].End == _highWater )
		{
			_highWater = _freeSlots[^1].Offset;
			_freeSlots.RemoveAt( _freeSlots.Count - 1 );
		}
	}

	private bool EnsureCapacity( int count )
	{
		if ( _instances != null && count <= _capacity )
			return false;

		var capacity = Math.Max( count, _capacity + Math.Max( _capacity / 2, 1 ) );
		DisposeBuffers();
		_capacity = capacity;

		_instances = new GpuBuffer<GpuInstanceTransform>( capacity, GpuBuffer.UsageFlags.Structured );
		_spheres = new GpuBuffer<Vector4>( capacity, GpuBuffer.UsageFlags.Structured );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_visible[lod] = new GpuBuffer<GpuInstanceTransform>( capacity, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Append );

			var drawCallCount = _drawCallCounts[lod];
			var args = new GpuBuffer.IndirectDrawIndexedArguments[drawCallCount];
			for ( int d = 0; d < drawCallCount; d++ )
			{
				_model.GetLodDrawCallRange( lod, d, out int startIndex, out int indexCount, out int baseVertex );
				args[d] = new GpuBuffer.IndirectDrawIndexedArguments
				{
					IndexCount = (uint)indexCount,
					FirstIndex = (uint)startIndex,
					BaseVertex = baseVertex
				};
			}

			_args[lod] = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( drawCallCount, GpuBuffer.UsageFlags.IndirectDrawArguments );
			_args[lod].SetData( args );
		}

		return true;
	}

	/// <summary>
	/// Bakes the cull dispatch and indirect draws into the normal and shadow command lists for the
	/// current instance set. Per-view inputs are pushed through Graphics.Attributes in RenderSceneObject.
	/// </summary>
	private void BuildCommandList()
	{
		BuildCommandList( _commandList, (uint)((1 << _lodCount) - 1) );
		BuildCommandList( _shadowCommandList, _shadowCastingLodMask );
	}

	private void BuildCommandList( CommandList commandList, uint drawLodMask )
	{
		commandList.Reset();

		if ( _instances == null || _count == 0 )
			return;

		commandList.Attributes.Set( "AllInstances", _instances );
		commandList.Attributes.Set( "AllInstanceSpheres", _spheres );
		commandList.Attributes.Set( "InstanceCount", _count );
		commandList.Attributes.Set( "ClutterModelRadius", _modelRadius );
		commandList.Attributes.Set( "DisableScreenSpaceShadows", Flags.CastShadows ? 0 : 1 );
		commandList.Attributes.Set( "ClutterLodCount", _lodCount );
		commandList.Attributes.Set( "ClutterModelBoundsCenter", _model.Bounds.Center );
		commandList.Attributes.Set( "ClutterLodSwitches", _lodSwitches );
		commandList.Attributes.Set( "ClutterLodSwitchDistances", _lodDistances );

		for ( int slot = 0; slot < MaxLods; slot++ )
			commandList.Attributes.Set( $"VisibleLod{slot}", _visible[slot < _lodCount ? slot : 0] );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			commandList.ResourceBarrierTransition( _visible[lod], ResourceState.UnorderedAccess );
			commandList.SetCounterValue( _visible[lod], 0 );
		}

		commandList.DispatchCompute( CullShader, _count, 1, 1 );

		// Appends must complete before reading each bucket's count.
		for ( int lod = 0; lod < _lodCount; lod++ )
			commandList.UavBarrier( _visible[lod] );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			commandList.ResourceBarrierTransition( _args[lod], ResourceState.CopyDestination );

			// Every draw call at this LOD draws the same visible-instance set, just a different
			// material's index range, so the same counter is replicated into each entry's InstanceCount.
			for ( int d = 0; d < _drawCallCounts[lod]; d++ )
				commandList.CopyStructureCount( _visible[lod], _args[lod], d * ArgsStride + ArgsInstanceCountOffset );
		}

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			commandList.ResourceBarrierTransition( _visible[lod], ResourceState.GenericRead );
			commandList.ResourceBarrierTransition( _args[lod], ResourceState.IndirectArgument );
			if ( (drawLodMask & (1u << lod)) != 0 )
				commandList.DrawModelInstancedIndirect( _model, _visible[lod], _args[lod], 0, lod );
		}
	}

	public override void RenderSceneObject()
	{
		if ( _instances == null || _count == 0 )
			return;

		// Per-view inputs, read by the cull dispatch during replay.
		Graphics.Attributes.Set( "ClutterFrustumScale", CullFrustumScale );
		Graphics.Attributes.Set( "ClutterLodBlendWidth", _lodBlendWidth );
		Graphics.Attributes.Set( "ClutterVertexBoundsLocalMargin", _localVertexBoundsMargin );
		Graphics.Attributes.Set( "ClutterVertexBoundsWorldMargin", _worldVertexBoundsMargin );
		Graphics.Attributes.Set( "ClutterVertexBoundsPerLod", 1 );
		Graphics.Attributes.Set( "ClutterVertexBoundsLocalMargins", _localVertexBoundsMargins );
		Graphics.Attributes.Set( "ClutterVertexBoundsWorldMargins", _worldVertexBoundsMargins );
		Graphics.Attributes.Set( "ClutterLodCameraPos", Lod.CameraPos );
		Graphics.Attributes.Set( "ClutterLodTanHalfFov", Lod.TanHalfFov );
		Graphics.Attributes.Set( "ClutterLodViewportWidth", Lod.ViewportWidth );
		Graphics.Attributes.Set( "ClutterLodOrthoWidth", Lod.OrthoWidth );
		Graphics.Attributes.Set( "ClutterWorldToProjection", Graphics.ViewFrustum.GetReverseZViewProjTranspose() );
		float drawDistance = 0;
		bool drawCameraValid = _paintedDrawOwner?.PaintedDrawCameraValid == true;
		if ( _paintedDrawOwner is not null
			&& _paintedDrawOwner.PaintedDrawDistances.TryGetValue( _model.ResourcePath, out var configuredDistance ) )
		{
			// In-place dictionary edits are permitted, so validate at the native boundary too.
			if ( !float.IsFinite( configuredDistance ) || configuredDistance < 0 )
				throw new InvalidOperationException( $"{_model.ResourcePath}: painted draw distance must be finite non-negative world inches." );
			drawDistance = drawCameraValid ? configuredDistance : 0;
		}
		Graphics.Attributes.Set( "ClutterDrawDistance", drawDistance );
		Graphics.Attributes.Set( "ClutterDrawCameraValid", drawCameraValid ? 1 : 0 );
		Graphics.Attributes.Set( "ClutterDrawCameraPosition", drawCameraValid ? _paintedDrawOwner.PaintedDrawCameraPosition : Vector3.Zero );

		var shadowPass = IsShadowPass( out var requiredFlags );
		_lastShadowPassDetected = shadowPass;
		if ( shadowPass )
		{
			_lastShadowRequiredFlags = requiredFlags;
			_shadowPassCount++;
			_shadowLodDrawsSkipped += CountBits( ((uint)((1 << _lodCount) - 1)) & ~_shadowCastingLodMask );
			// No shadow draw can consume these buffers when every LOD opts out.
			// Keep the batch shadow flag for normal-view shadow receiving.
			if ( _shadowCastingLodMask == 0 ) return;
		}
		(shadowPass ? _shadowCommandList : _commandList).ExecuteOnRenderThread();
	}

	private static int CountBits( uint value )
	{
		int count = 0;
		while ( value != 0 )
		{
			count += (int)(value & 1);
			value >>= 1;
		}
		return count;
	}

	private static bool IsShadowPass( out SceneObjectFlags requiredFlags )
	{
		var layer = Graphics.SceneLayer;
		if ( layer.IsValid )
		{
			requiredFlags = layer.GetObjectFlagsRequiredMask();
			if ( (requiredFlags & SceneObjectFlags.CastShadows) == SceneObjectFlags.CastShadows )
				return true;
		}

		requiredFlags = SceneObjectFlags.None;
		return Graphics.LayerType == SceneLayerType.Shadow;
	}

	private void DisposeBuffers()
	{
		_instances?.Dispose();
		_instances = null;

		_spheres?.Dispose();
		_spheres = null;

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_visible[lod]?.Dispose();
			_visible[lod] = null;
			_args[lod]?.Dispose();
			_args[lod] = null;
		}

		_capacity = 0;
	}

	internal override void OnNativeDestroy()
	{
		DisposeBuffers();
		_lodDistances?.Dispose();
		base.OnNativeDestroy();
	}
}
