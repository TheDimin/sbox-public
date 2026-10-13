HEADER
{
	DevShader = true;
	Description = "Clutter GPU frustum culling";
}

MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	#include "system.fxc"
	#include "common.fxc"
	#include "transform_buffer.fxc" // TransformBufferData_t
}

CS
{
	StructuredBuffer<TransformBufferData_t> AllInstances < Attribute( "AllInstances" ); >;
	StructuredBuffer<float4> AllInstanceSpheres < Attribute( "AllInstanceSpheres" ); >;
	int InstanceCount < Attribute( "InstanceCount" ); >;
	// Selected painted models only. Zero keeps existing unlimited drawing.
	float ClutterDrawDistance < Attribute( "ClutterDrawDistance" ); Default( 0.0 ); >;
	int ClutterDrawCameraValid < Attribute( "ClutterDrawCameraValid" ); Default( 0 ); >;
	float3 ClutterDrawCameraPosition < Attribute( "ClutterDrawCameraPosition" ); Default3( 0, 0, 0 ); >;

	float ClutterModelRadius < Attribute( "ClutterModelRadius" ); >;
	float ClutterLodBlendWidth < Attribute( "ClutterLodBlendWidth" ); Default( 0.0 ); >;
	float ClutterVertexBoundsLocalMargin < Attribute( "ClutterVertexBoundsLocalMargin" ); Default( 0.0 ); >;
	float ClutterVertexBoundsWorldMargin < Attribute( "ClutterVertexBoundsWorldMargin" ); Default( 0.0 ); >;
	int ClutterVertexBoundsPerLod < Attribute( "ClutterVertexBoundsPerLod" ); Default( 0 ); >;
	float4 ClutterVertexBoundsLocalMargins < Attribute( "ClutterVertexBoundsLocalMargins" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
	float4 ClutterVertexBoundsWorldMargins < Attribute( "ClutterVertexBoundsWorldMargins" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;

	// Survivors per LOD - each survivor appends its full transform to its bucket.
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod0 < Attribute( "VisibleLod0" ); >;
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod1 < Attribute( "VisibleLod1" ); >;
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod2 < Attribute( "VisibleLod2" ); >;
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod3 < Attribute( "VisibleLod3" ); >;

	float3 ClutterLodCameraPos     < Attribute( "ClutterLodCameraPos" ); >;
	float  ClutterLodTanHalfFov    < Attribute( "ClutterLodTanHalfFov" ); >;
	float  ClutterLodViewportWidth < Attribute( "ClutterLodViewportWidth" ); >;
	float  ClutterLodOrthoWidth    < Attribute( "ClutterLodOrthoWidth" ); >;
	int    ClutterLodCount         < Attribute( "ClutterLodCount" ); >;
	StructuredBuffer<float> ClutterLodSwitchDistances < Attribute( "ClutterLodSwitchDistances" ); >;

	// Debug: >1 narrows the cull frustum so culling is visible on-screen.
	float ClutterFrustumScale < Attribute( "ClutterFrustumScale" ); >;

	// Plain float4x4: attribute matrices are stored raw and read column-major, so the CPU uploads
	// the transpose and we consume it as mul( M, pos ). A row_major qualifier here breaks the planes.
	float4x4 ClutterWorldToProjection < Attribute( "ClutterWorldToProjection" ); >;

	// Bounding-sphere frustum test - cheaper and slightly looser than an 8-corner OBB test.
	bool SphereInFrustum( float3 center, float radius )
	{
		float4x4 vp = ClutterWorldToProjection;

		bool ortho = abs( vp[3].x ) < 1e-5 && abs( vp[3].y ) < 1e-5 && abs( vp[3].z ) < 1e-5;
		float4 keepAll = float4( 0, 0, 0, 1 );

		// Scaling the X/Y rows narrows the cull frustum.
		float frustumScale = max( ClutterFrustumScale, 1e-3 );
		float4 rowX = vp[0] * frustumScale;
		float4 rowY = vp[1] * frustumScale;

		float4 planes[6] =
		{
			vp[3] + rowX, // left
			vp[3] - rowX, // right
			vp[3] + rowY, // bottom
			vp[3] - rowY, // top
			ortho ? keepAll : vp[2],             // near
			ortho ? keepAll : ( vp[3] - vp[2] ), // far
		};

		[unroll]
		for ( int i = 0; i < 6; i++ )
		{
			// Normalize for a true signed distance; the keepAll plane has a zero normal, so skip it.
			float len = length( planes[i].xyz );
			if ( len < 1e-6 )
				continue;

			float dist = ( dot( planes[i].xyz, center ) + planes[i].w ) / len;
			if ( dist < -radius )
				return false;
		}
		return true;
	}

	// Matches the native LOD metric: screen coverage of a 0.5-radius sphere, then walk switch distances.
	float LodMetric( float3 worldPos )
	{
		float dist = length( worldPos - ClutterLodCameraPos );
		float tanHalf = max( ClutterLodTanHalfFov, 1e-5 );
		// Orthographic coverage depends on the view width, not distance from the camera.
		float screen = ClutterLodOrthoWidth > 0.0
			? saturate( 1.0 / ClutterLodOrthoWidth )
			: saturate( 0.5 / max( dist * tanHalf, 1e-5 ) );
		float pixels = screen * ClutterLodViewportWidth;
		float metric = ( pixels > 0.0 ) ? ( 50.0 / pixels ) : 0.0;

		return metric;
	}

	int ComputeLod( float metric, float scale )
	{
		int lod = max( ClutterLodCount - 1, 0 );
		[loop]
		while ( lod > 0 )
		{
			float d = ClutterLodSwitchDistances[lod] * scale;
			if ( d > 0.0 && d < metric )
				break;
			lod--;
		}
		return lod;
	}

	// At most two adjacent buckets. Each bucket still has at most InstanceCount
	// entries, so the existing append-buffer capacities remain sufficient.
	int BlendBoundary( float metric, float scale )
	{
		if ( ClutterLodBlendWidth <= 0.0 ) return -1;
		for ( int boundary = 1; boundary < min( ClutterLodCount, 4 ); boundary++ )
		{
			float threshold = ClutterLodSwitchDistances[boundary] * scale;
			float width = threshold * ClutterLodBlendWidth;
			if ( boundary > 1 ) width = min( width, (threshold - ClutterLodSwitchDistances[boundary - 1] * scale) * 0.49 );
			if ( boundary + 1 < ClutterLodCount ) width = min( width, (ClutterLodSwitchDistances[boundary + 1] * scale - threshold) * 0.49 );
			if ( threshold > 0 && width > 0 && abs( metric - threshold ) < width ) return boundary;
		}
		return -1;
	}

	void AppendLod( int lod, TransformBufferData_t t )
	{
		switch ( lod )
		{
			case 0: VisibleLod0.Append( t ); break;
			case 1: VisibleLod1.Append( t ); break;
			case 2: VisibleLod2.Append( t ); break;
			default: VisibleLod3.Append( t ); break;
		}
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID )
	{
		uint id = vThreadId.x;
		if ( id >= (uint)InstanceCount )
			return;

		float4 sphere = AllInstanceSpheres[id]; // xyz = world center, w = world radius
		if ( sphere.w < 0.0 ) // Free tile slot.
			return;

		float scale = ( ClutterModelRadius > 1e-6 ) ? ( sphere.w / ClutterModelRadius ) : 1.0;
		// LOD stays on the resting radius. Use that LOD's full deformation envelope.
		float metric = LodMetric( sphere.xyz );
		int boundary = BlendBoundary( metric, scale );
		int lod = boundary > 0 ? boundary - 1 : ComputeLod( metric, scale );
		float localMargin = ClutterVertexBoundsPerLod != 0 ? ClutterVertexBoundsLocalMargins[lod] : ClutterVertexBoundsLocalMargin;
		float worldMargin = ClutterVertexBoundsPerLod != 0 ? ClutterVertexBoundsWorldMargins[lod] : ClutterVertexBoundsWorldMargin;
		if ( boundary > 0 && ClutterVertexBoundsPerLod != 0 )
		{
			localMargin = max( localMargin, ClutterVertexBoundsLocalMargins[boundary] );
			worldMargin = max( worldMargin, ClutterVertexBoundsWorldMargins[boundary] );
		}
		float cullRadius = sphere.w + localMargin * scale + worldMargin;
		if ( !SphereInFrustum( sphere.xyz, cullRadius ) )
			return;

		// Remove the full wind-expanded sphere before transform fetch and native draw-list append.
		// Use the owning main-camera snapshot in every pass, including light views.
		if ( ClutterDrawDistance > 0.0 && ClutterDrawCameraValid != 0
			&& length( sphere.xyz - ClutterDrawCameraPosition ) - cullRadius >= ClutterDrawDistance )
			return;

		// Only the full transform is fetched once an instance survives.

		TransformBufferData_t transform = AllInstances[id];
		AppendLod( lod, transform );
		if ( boundary > 0 ) AppendLod( boundary, transform );
	}
}
