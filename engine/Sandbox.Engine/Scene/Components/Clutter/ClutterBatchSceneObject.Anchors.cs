using Sandbox.Rendering;

namespace Sandbox.Clutter;

internal partial class ClutterBatchSceneObject
{
	private GpuBuffer<uint> _anchorNeighbors;
	private bool _anchorStorageDirty = true;
	private float _anchorFraction = -1f;
	private Vector3 _anchorShape;

	// Called under the batch lock, before command-list binding. Source slot indices
	// are valid only for this storage version, even when total count is unchanged.
	private void RefreshAnchorNeighbors()
	{
		if ( _paintedDrawOwner?.PaintedLod2AnchorModels.Contains( _model.ResourcePath ) != true
			|| !_paintedDrawOwner.PaintedLod2Densities.TryGetValue( _model.ResourcePath, out var requestedFraction ) || requestedFraction >= 1f )
		{
			if ( _anchorNeighbors is null ) return;
			ReleaseDrawPreparations();
			DisposeAnchorNeighbors();
			BuildCommandList();
			return;
		}
		var shape = Vector3.Zero;
		bool supported = _lodCount == 3;
		int materials = 0;
		foreach ( var mesh in _model.MeshInfo.Meshes )
		{
			if ( (mesh.LodMask & 4) == 0 ) continue;
			foreach ( var draw in mesh.DrawCalls )
			{
				var flags = draw.Material.Flags;
				var candidate = new Vector3( flags.GetFloat( "ClutterAnchorHeight" ), flags.GetFloat( "ClutterAnchorWidth" ), flags.GetFloat( "ClutterAnchorWindAngle" ) );
				supported &= candidate.IsFinite && candidate.x > 0f && candidate.y > 0f && candidate.z >= 0f && candidate.z < MathF.PI * 0.5f;
				if ( materials++ > 0 ) supported &= candidate == shape;
				shape = candidate;
			}
		}
		float fraction = 1f;
		bool enabled = supported && materials > 0 && _count > 0 && _instances is not null
			&& _paintedDrawOwner?.PaintedLod2AnchorModels.Contains( _model.ResourcePath ) == true
			&& _paintedDrawOwner.PaintedLod2Densities.TryGetValue( _model.ResourcePath, out fraction )
			&& float.IsFinite( fraction ) && fraction >= 0f && fraction < 1f;
		if ( !enabled )
		{
			if ( _anchorNeighbors is null ) return;
			ReleaseDrawPreparations();
			DisposeAnchorNeighbors();
			BuildCommandList();
			return;
		}
		if ( !_anchorStorageDirty && fraction == _anchorFraction && shape == _anchorShape ) return;

		var roots = new Vector3[_count];
		var active = new bool[_count];
		float maximumHeight = 0f;
		foreach ( var (tile, slot) in _tileSlots )
		{
			for ( int index = 0; index < tile.Count; ++index )
			{
				var transform = tile.Transforms[index];
				var root = new Vector3( transform.Row0.w, transform.Row1.w, transform.Row2.w );
				var vertical = new Vector3( transform.Row0.z, transform.Row1.z, transform.Row2.z );
				float height = shape.x * vertical.Length;
				if ( !root.IsFinite || !vertical.IsFinite || !float.IsFinite( height ) || height <= 0f ) continue;
				roots[slot.Offset + index] = root;
				active[slot.Offset + index] = true;
				maximumHeight = MathF.Max( maximumHeight, height );
			}
		}
		var result = ClutterAnchorNeighbors.Build( roots, active, maximumHeight, fraction );
		var replacement = new GpuBuffer<uint>( result.Length, GpuBuffer.UsageFlags.Structured, "ClutterAnchorNeighbors" );
		try { replacement.SetData( result ); }
		catch { replacement.Dispose(); throw; }
		ReleaseDrawPreparations();
		_anchorNeighbors?.Dispose();
		_anchorNeighbors = replacement;
		_anchorFraction = fraction;
		_anchorShape = shape;
		_anchorStorageDirty = false;
		BuildCommandList();
	}

	private void DisposeAnchorNeighbors()
	{
		_anchorNeighbors?.Dispose();
		_anchorNeighbors = null;
		_anchorStorageDirty = true;
		_anchorFraction = -1f;
	}
	private void BindAnchorCoverage( float fraction, bool viewValid )
	{
		bool enabled = viewValid && _paintedDrawOwner.PaintedAnchorCameraValid && _anchorNeighbors is not null && _anchorFraction == fraction
			&& _paintedDrawOwner.PaintedLod2AnchorModels.Contains( _model.ResourcePath );
		bool requested = _paintedDrawOwner?.PaintedLod2AnchorModels.Contains( _model.ResourcePath ) == true;
		Graphics.Attributes.Set( "ClutterAnchorCoverageEnabled", requested ? (enabled ? 1 : 2) : 0 );
		if ( !enabled ) return;
		var field = _paintedDrawOwner.PaintedLod2ContactField;
		Graphics.Attributes.Set( "ClutterAnchorShape", _anchorShape );
		Graphics.Attributes.Set( "ClutterAnchorHeightScale", field?.HeightScale ?? 1f );
		Graphics.Attributes.Set( "ClutterAnchorWorldToProjection", _paintedDrawOwner.PaintedDrawWorldToProjection );
		Graphics.Attributes.Set( "ClutterAnchorCameraForward", _paintedDrawOwner.PaintedDrawCameraForward );
		Graphics.Attributes.Set( "ClutterAnchorCameraRight", _paintedDrawOwner.PaintedDrawCameraRight );
		Graphics.Attributes.Set( "ClutterAnchorCameraUp", _paintedDrawOwner.PaintedDrawCameraUp );
		Graphics.Attributes.Set( "ClutterAnchorContactEnabled", field?.CurrentTexture is not null ? 1 : 0 );
		Graphics.Attributes.Set( "ClutterAnchorPreviousContactEnabled", field?.PreviousTexture is not null ? 1 : 0 );
		if ( field?.CurrentTexture is not null )
		{
			Graphics.Attributes.Set( "ClutterAnchorContactTexture", field.CurrentTexture );
			Graphics.Attributes.Set( "ClutterAnchorContactRegion", field.CurrentRegion );
		}
		if ( field?.PreviousTexture is not null )
		{
			Graphics.Attributes.Set( "ClutterAnchorPreviousContactTexture", field.PreviousTexture );
			Graphics.Attributes.Set( "ClutterAnchorPreviousContactRegion", field.PreviousRegion );
		}
	}
}
