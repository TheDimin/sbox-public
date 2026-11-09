using System.Numerics;
using Sandbox.Gltf.Internal.MikkTSpaceSharp;
using static Sandbox.Gltf.Internal.MikkTSpaceSharp.MikkTSpace;

namespace Sandbox.Gltf;

/// <summary>Unindexed tangent frames and their corresponding immutable source vertex indices.</summary>
public sealed record GltfGeneratedTangentResult( uint[] SourceVertices, Vector4[] CornerTangents );

/// <summary>Generates glTF object-space tangent frames with the pinned managed MikkTSpace port.</summary>
public static class GltfGeneratedTangents
{
	/// <summary>
	/// Generates one tangent per expanded triangle corner. Input arrays remain untouched.
	/// The UV rows apply the normal texture's effective transform. A null normal array
	/// uses flat face normals. The workspace estimate is a conservative admission
	/// estimate, not a measured memory cap or a bound on other loader allocations.
	/// </summary>
	public static GltfGeneratedTangentResult Generate( Vector3[] positions, Vector3[] normals, Vector2[] texcoords, uint[] indices, int mode,
		Vector3 uvRowU, Vector3 uvRowV, long maxWorkspaceBytes = 256L * 1024 * 1024 )
	{
		ArgumentNullException.ThrowIfNull( positions ); ArgumentNullException.ThrowIfNull( texcoords ); ArgumentNullException.ThrowIfNull( indices );
		if ( positions.Length == 0 || texcoords.Length != positions.Length || (normals is not null && normals.Length != positions.Length) )
			throw new GltfException( "Tangent input attribute counts do not match." );
		if ( mode is not (4 or 5 or 6) ) throw new GltfException( "Generated tangent frames require triangle topology." );
		long corners = mode == 4 ? indices.LongLength : Math.Max( 0L, indices.LongLength - 2 ) * 3;
		var uvBytes = positions.LongLength * 8;
		if ( corners == 0 || corners > Array.MaxLength || maxWorkspaceBytes <= 0 || uvBytes > maxWorkspaceBytes || corners > (maxWorkspaceBytes - uvBytes) / 512 )
			throw new GltfException( "Generated tangent workspace exceeds its admission budget." );
		if ( !Finite( uvRowU ) || !Finite( uvRowV ) ) throw new GltfException( "Normal texture UV transform must be finite." );
		var transformedUvs = new Vector2[texcoords.Length];
		for ( var i = 0; i < positions.Length; i++ )
		{
			if ( !Finite( positions[i] ) || !float.IsFinite( texcoords[i].X ) || !float.IsFinite( texcoords[i].Y ) || (normals is not null && !Finite( normals[i] )) )
				throw new GltfException( "Generated tangent input contains a non-finite attribute." );
			var uv = texcoords[i];
			transformedUvs[i] = new( uvRowU.X * uv.X + uvRowU.Y * uv.Y + uvRowU.Z, uvRowV.X * uv.X + uvRowV.Y * uv.Y + uvRowV.Z );
			if ( !float.IsFinite( transformedUvs[i].X ) || !float.IsFinite( transformedUvs[i].Y ) )
				throw new GltfException( "Normal texture UV transform produced non-finite coordinates." );
		}
		foreach ( var index in indices ) if ( index >= positions.Length ) throw new GltfException( "Generated tangent index exceeds its source vertex count." );
		var source = GltfGeometry.ExpandIndices( indices, mode );
		// Mikk may create subgroup member arrays for every face around a welded
		// position. Position incidence bounds the more restrictive P/N/UV weld.
		var incidence = new Dictionary<Vector3, int>();
		long squareSum = 0;
		foreach ( var vertex in source )
		{
			var position = positions[vertex]; incidence.TryGetValue( position, out var count );
			squareSum = checked(squareSum + 2L * count + 1); incidence[position] = count + 1;
			if ( squareSum > (maxWorkspaceBytes - uvBytes - corners * 512) / 4 ) throw new GltfException( "Generated tangent subgroup workspace exceeds its admission budget." );
		}
		Vector3[] flatNormals = null;
		if ( normals is null )
		{
			flatNormals = new Vector3[source.Length / 3];
			for ( var face = 0; face < flatNormals.Length; face++ )
			{
				var p = positions[source[face * 3]];
				var value = Vector3.Cross( positions[source[face * 3 + 1]] - p, positions[source[face * 3 + 2]] - p );
				var length = value.LengthSquared();
				if ( !Finite( value ) || !float.IsFinite( length ) ) throw new GltfException( "Flat normal calculation produced non-finite values." );
				flatNormals[face] = length > 0 ? Vector3.Normalize( value ) : Vector3.Zero;
			}
		}
		var tangents = new Vector4[source.Length];
		var written = new bool[source.Length];
		Vector3 Position( int face, int vertex ) => positions[source[face * 3 + vertex]];
		var context = new SMikkTSpaceContext
		{
			m_getNumFaces = () => source.Length / 3,
			m_getNumVerticesOfFace = _ => 3,
			m_setTSpace = null, // Optional extended callback; the basic callback below supplies tangent and sign.
			m_getPosition = ( face, vertex ) => ToMikk( Position( face, vertex ) ),
			m_getNormal = ( face, vertex ) =>
			{
				var value = normals is null ? flatNormals[face] : normals[source[face * 3 + vertex]];
				return ToMikk( value );
			},
			m_getTexCoord = ( face, vertex ) =>
			{
				var uv = transformedUvs[source[face * 3 + vertex]];
				return new SVec2( uv.X, uv.Y );
			},
			m_setTSpaceBasic = ( tangent, sign, face, vertex ) =>
			{
				var corner = face * 3 + vertex;
				// MikkTSpace receives glTF's upper-left-origin texture coordinates.
				// Convert its handedness to the glTF tangent convention, as the
				// Khronos sample renderer does; authored tangents bypass this generator.
				tangents[corner] = new Vector4( tangent.x, tangent.y, tangent.z, -sign ); written[corner] = true;
			}
		};
		if ( !genTangSpaceDefault( context ) ) throw new GltfException( "MikkTSpace could not generate tangent frames." );
		for ( var i = 0; i < tangents.Length; i++ )
			if ( !written[i] || !Finite( new Vector3( tangents[i].X, tangents[i].Y, tangents[i].Z ) ) || !float.IsFinite( tangents[i].W ) )
				throw new GltfException( "MikkTSpace generated an incomplete or non-finite tangent frame." );
		return new( source, tangents );
	}

	private static SVec3 ToMikk( Vector3 value ) => new( value.X, value.Y, value.Z );
	private static bool Finite( Vector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
}
