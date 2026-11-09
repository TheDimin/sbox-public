namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<Mesh> meshes = [];
	private readonly List<int> lods = [];
	private readonly List<ulong> bodyGroups = [];
	private readonly List<string> meshGroups = [];
	private readonly Dictionary<(string, int), int> _groupChoiceToIndex = [];
	private readonly HashSet<string> _groupNames = new( StringComparer.Ordinal );
	private ulong _defaultMeshGroupMask;
	private bool _explicitMeshGroups;

	private ulong DefaultMeshGroupMask => _explicitMeshGroups || _groupNames.Count > 0 ? _defaultMeshGroupMask : ulong.MaxValue;

	private void ApplyMeshes( NativeEngine.ModelBuilder modelBuilder )
	{
		foreach ( var meshGroup in meshGroups )
		{
			modelBuilder.AddMeshGroup( meshGroup );
		}

		for ( var i = 0; i < meshes.Count; ++i )
		{
			var mesh = meshes[i];
			if ( mesh == null || !mesh.IsValid )
				continue;

			modelBuilder.AddRunTimeMesh( mesh.native, bodyGroups[i], lods[i] );
		}

		modelBuilder.SetDefaultMeshGroupMask( DefaultMeshGroupMask );
	}

	/// <summary>
	/// Adds a mesh that is visible at every LOD level and in every body group.
	/// </summary>
	/// <param name="mesh">The mesh to add. Null or invalid meshes are ignored.</param>
	/// <exception cref="ArgumentException">The mesh has no vertex buffer.</exception>
	public ModelBuilder AddMesh( Mesh mesh )
	{
		AddMesh( mesh, 255, ulong.MaxValue );
		return this;
	}

	/// <summary>
	/// Adds multiple meshes that are visible at every LOD level and in every body group.
	/// </summary>
	/// <param name="meshes">The meshes to add. Null or invalid meshes are ignored.</param>
	/// <exception cref="ArgumentException">A mesh has no vertex buffer.</exception>
	public ModelBuilder AddMeshes( Mesh[] meshes )
	{
		AddMeshes( meshes, 255, ulong.MaxValue );
		return this;
	}

	/// <summary>
	/// Adds a mesh to an LOD level.
	/// </summary>
	/// <param name="mesh">The mesh to add. Null or invalid meshes are ignored.</param>
	/// <param name="lod">The LOD level, from 0 to 7. Negative values use level 0.</param>
	/// <exception cref="ArgumentException">The mesh has no vertex buffer.</exception>
	public ModelBuilder AddMesh( Mesh mesh, int lod )
	{
		if ( lod < 0 )
			lod = 0;

		AddMesh( mesh, 1 << lod, ulong.MaxValue );
		return this;
	}

	/// <summary>
	/// Adds multiple meshes to an LOD level.
	/// </summary>
	/// <param name="meshes">The meshes to add. Null or invalid meshes are ignored.</param>
	/// <param name="lod">The LOD level, from 0 to 7. Negative values use level 0.</param>
	/// <exception cref="ArgumentException">A mesh has no vertex buffer.</exception>
	public ModelBuilder AddMeshes( Mesh[] meshes, int lod )
	{
		if ( lod < 0 )
			lod = 0;

		AddMeshes( meshes, 1 << lod, ulong.MaxValue );
		return this;
	}

	/// <summary>
	/// Adds a mesh to a body group choice at LOD level 0.
	/// </summary>
	/// <param name="mesh">The mesh to add. Null or invalid meshes are ignored.</param>
	/// <param name="groupName">The body group name.</param>
	/// <param name="choiceIndex">The choice index within the body group.</param>
	/// <exception cref="ArgumentException">The group name is empty, or the mesh has no vertex buffer.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The choice index is negative.</exception>
	/// <exception cref="InvalidOperationException">The model has more than 64 body group choices.</exception>
	public ModelBuilder AddMesh( Mesh mesh, string groupName, int choiceIndex )
	{
		return AddMesh( mesh, 0, groupName, choiceIndex );
	}

	/// <summary>
	/// Adds a mesh to an LOD level and body group choice.
	/// </summary>
	/// <param name="mesh">The mesh to add. Null or invalid meshes are ignored.</param>
	/// <param name="lod">The LOD level, from 0 to 7. Negative values use level 0.</param>
	/// <param name="groupName">The body group name.</param>
	/// <param name="choiceIndex">The choice index within the body group.</param>
	/// <exception cref="ArgumentException">The group name is empty, or the mesh has no vertex buffer.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The choice index is negative.</exception>
	/// <exception cref="InvalidOperationException">The model has more than 64 body group choices.</exception>
	public ModelBuilder AddMesh( Mesh mesh, int lod, string groupName, int choiceIndex )
	{
		var groupMask = 1UL << EnsureGroupChoice( groupName, choiceIndex );

		if ( lod < 0 )
			lod = 0;

		return AddMesh( mesh, 1 << lod, groupMask );
	}

	private ModelBuilder AddMesh( Mesh mesh, int lodMask, ulong bodyGroupMask )
	{
		if ( mesh == null || !mesh.IsValid )
			return this;

		if ( !mesh.HasVertexBuffer )
			throw new ArgumentException( "Mesh has invalid vertex buffer" );

		meshes.Add( mesh );
		lods.Add( lodMask );
		bodyGroups.Add( bodyGroupMask );
		return this;
	}

	private ModelBuilder AddMeshes( Mesh[] meshes, int lodMask, ulong bodyGroupMask )
	{
		if ( meshes == null || meshes.Length == 0 )
			return this;

		var numMeshes = 0;
		foreach ( var mesh in meshes )
		{
			if ( mesh == null || !mesh.IsValid )
				continue;

			if ( !mesh.HasVertexBuffer )
				throw new ArgumentException( "Mesh has invalid vertex buffer" );

			this.meshes.Add( mesh );
			numMeshes++;
		}

		if ( numMeshes == 0 )
			return this;

		lods.AddRange( Enumerable.Repeat( lodMask, numMeshes ) );
		bodyGroups.AddRange( Enumerable.Repeat( bodyGroupMask, numMeshes ) );
		return this;
	}

	/// <summary>
	/// Sets the ordered native mesh-group names and default mask for an existing model layout.
	/// Call before adding meshes. This explicit mode cannot be mixed with named bodygroup choices.
	/// </summary>
	public ModelBuilder WithExplicitMeshGroups( IReadOnlyList<string> names, ulong defaultMask )
	{
		ArgumentNullException.ThrowIfNull( names );
		if ( _explicitMeshGroups || meshGroups.Count != 0 || meshes.Count != 0 ) throw new InvalidOperationException( "Mesh groups must be configured once before adding meshes." );
		if ( names.Count > 64 ) throw new ArgumentOutOfRangeException( nameof( names ) );
		var unique = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var name in names )
			if ( string.IsNullOrWhiteSpace( name ) || name.Length > 1024 || !unique.Add( name ) ) throw new ArgumentException( "Mesh-group names must be nonempty, bounded and unique.", nameof( names ) );
		var allowed = names.Count == 64 ? ulong.MaxValue : names.Count == 0 ? 0UL : (1UL << names.Count) - 1;
		if ( names.Count == 0 ? defaultMask != 0 && defaultMask != ulong.MaxValue : (defaultMask & ~allowed) != 0 ) throw new ArgumentOutOfRangeException( nameof( defaultMask ) );
		meshGroups.AddRange( names ); _defaultMeshGroupMask = defaultMask; _explicitMeshGroups = true;
		return this;
	}

	/// <summary>Adds a mesh with an explicit native group mask and LOD.</summary>
	public ModelBuilder AddMeshWithGroupMask( Mesh mesh, int lod, ulong groupMask )
	{
		if ( !_explicitMeshGroups ) throw new InvalidOperationException( "Configure explicit mesh groups first." );
		if ( lod is < 0 or >= 8 ) throw new ArgumentOutOfRangeException( nameof( lod ) );
		var allowed = meshGroups.Count == 64 ? ulong.MaxValue : meshGroups.Count == 0 ? 0UL : (1UL << meshGroups.Count) - 1;
		if ( groupMask != ulong.MaxValue && (groupMask & ~allowed) != 0 ) throw new ArgumentOutOfRangeException( nameof( groupMask ) );
		return AddMesh( mesh, 1 << lod, groupMask );
	}

	private int EnsureGroupChoice( string name, int choice )
	{
		if ( _explicitMeshGroups ) throw new InvalidOperationException( "Explicit mesh groups cannot be mixed with named bodygroup choices." );
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new ArgumentException( null, nameof( name ) );

		ArgumentOutOfRangeException.ThrowIfNegative( choice );

		var key = (name, choice);
		if ( _groupChoiceToIndex.TryGetValue( key, out var index ) )
			return index;

		if ( meshGroups.Count >= 64 )
			throw new InvalidOperationException( "Total bodygroup choices exceed 64 bits." );

		index = meshGroups.Count;
		meshGroups.Add( $"{name}_@{choice}" );
		_groupChoiceToIndex[key] = index;

		if ( _groupNames.Add( name ) )
			_defaultMeshGroupMask |= 1UL << index;

		return index;
	}
}
