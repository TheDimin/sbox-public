using static Sandbox.Component;

namespace Sandbox;

/// <summary>
/// Renders a model in the world
/// </summary>
[Title( "Model Renderer" )]
[Category( "Rendering" )]
[Icon( "free_breakfast" )]
[Alias( "ModelComponentMate", "ModelComponent" )]
public partial class ModelRenderer : Renderer, ExecuteInEditor, ITintable, IMaterialSetter, IHasBounds, IHasModel
{
	ulong _bodyGroups = ulong.MaxValue;

	[Property]
	public Model Model
	{
		get;
		set
		{
			if ( field == value ) return;
			var previous = field;
			var previousBodyGroups = _bodyGroups;
			if ( value?.GltfResource is not null || previous?.GltfResource is not null )
			{
				// Prepare without disposing or publishing the previous presentation.
				PrepareGltfChange( value, value?.GltfResource );
				field = value;
				try
				{
					if ( !GameObject.Flags.Contains( GameObjectFlags.Deserializing ) )
					{
						_bodyGroups = ulong.MaxValue;
						if ( field is not null && field.GltfResource is null && field.native.GetNumMeshGroups() > 0 )
							_bodyGroups = field.native.GetDefaultMeshGroupMask();
					}
					ApplyGltfReload(); ValidateGltfReload(); PublishGltfReload();
				}
				catch ( Exception error )
				{
					field = previous; _bodyGroups = previousBodyGroups;
					Sandbox.Gltf.DirectGpu.GltfGpuCleanup.Rollback( error, [RollbackGltfReload] );
					throw;
				}
				// The assignment is committed; later cleanup/notification failures
				// cannot safely restore a possibly disposed previous instance.
				try { FinishGltfReload(); }
				catch ( Exception error ) { Log.Warning( error, "Could not clean up the previous model renderer after assignment." ); }
				try { OnModelChanged(); }
				catch ( Exception error ) { Log.Warning( error, "Model assignment succeeded, but a change notification failed." ); }
				return;
			}
			field = value;
			try
			{
				if ( !GameObject.Flags.Contains( GameObjectFlags.Deserializing ) )
				{
					_bodyGroups = ulong.MaxValue;

					if ( field is not null && field.GltfResource is null && field.native.GetNumMeshGroups() > 0 )
					{
						_bodyGroups = field.native.GetDefaultMeshGroupMask();
					}
				}
				UpdateObject();
				OnModelChanged();
			}
			catch { throw; }
		}
	}

	Color _tint = "#FFFFFF";

	[Property]
	public Color Tint
	{
		get => _tint;
		set
		{
			if ( _tint == value ) return;
			_tint = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.ColorTint = Tint;
			}
		}
	}

	[Property]
	public bool CreateAttachments
	{
		get => _createAttachments;
		set
		{
			if ( _createAttachments == value ) return;
			_createAttachments = value;

			UpdateObject();
		}
	}

	[Property, Model.BodyGroupMask, ShowIf( nameof( HasBodyGroups ), true )]
	[DefaultValue( ulong.MaxValue )]
	public ulong BodyGroups
	{
		get => _bodyGroups;
		set
		{
			if ( _bodyGroups == value ) return;
			_bodyGroups = value;

			Update();
		}
	}

	public bool HasBodyGroups => Model?.Parts.All.Sum( x => x.Choices.Count ) > 1;

	[Property, Model.MaterialGroup, ShowIf( nameof( HasMaterialGroups ), true )]
	[DefaultValue( default )]
	public string MaterialGroup
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			Update();
		}
	}

	public bool HasMaterialGroups => Model?.MaterialGroupCount > 0 && MaterialOverride is null;

	[Title( "Cast Shadows" ), Property, Category( "Lighting" )]
	public ShadowRenderType RenderType
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			Update();
		}
	} = ShadowRenderType.On;

	private int? _lodOverride;

	/// <summary>
	/// Force a level of detail. Unset uses automatic LOD.
	/// </summary>
	[Property, Title( "LOD Override" )]
	public int? LodOverride
	{
		get => _lodOverride;
		set
		{
			if ( _lodOverride == value ) return;
			_lodOverride = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.LodOverride = _lodOverride ?? -1;
			}
		}
	}

	/// <summary>
	/// Set body group value by name
	/// </summary>
	public void SetBodyGroup( string name, int value )
	{
		if ( Model is null || Model.native.IsNull )
			return;

		int part = Model.native.GetBodyPartForName( name );
		if ( part < 0 ) return;

		SetBodyGroup( part, value );
	}

	/// <summary>
	/// Set body group value by name and choice
	/// </summary>
	public void SetBodyGroup( string name, string choice )
	{
		if ( Model is null || Model.native.IsNull ) return;

		var partIndex = Model.native.GetBodyPartForName( name );
		if ( partIndex < 0 ) return;

		var parts = Model.Parts;
		if ( partIndex >= parts.Count ) return;

		var choiceIndex = parts.All[partIndex].GetChoiceIndex( choice );
		if ( choiceIndex < 0 ) return;

		SetBodyGroup( partIndex, choiceIndex );
	}

	/// <summary>
	/// Set body group value by index
	/// </summary>
	public void SetBodyGroup( int part, int value )
	{
		if ( Model is null || Model.native.IsNull )
			return;

		ulong mask = Model.native.GetBodyPartMask( part );
		ulong modelMask = Model.native.GetBodyPartMeshMask( part, value );

		var groups = BodyGroups;
		groups &= ~mask; // remove the whole mask
		groups |= modelMask; // apply the chosen part flag

		BodyGroups = groups;
	}

	/// <summary>
	/// Get body group value by name
	/// </summary>
	public int GetBodyGroup( string name )
	{
		if ( Model is null || Model.native.IsNull )
			throw new ArgumentException( "Invalid Model" );

		return GetBodyGroup( Model.native.GetBodyPartForName( name ) );
	}

	/// <summary>
	/// Get body group value by index
	/// </summary>
	public int GetBodyGroup( int part )
	{
		if ( Model is null || Model.native.IsNull )
			throw new ArgumentException( "Invalid Model" );

		if ( part < 0 || part >= Model.native.GetNumBodyParts() )
			throw new ArgumentOutOfRangeException( nameof( part ) );

		return Model.native.FindMeshIndexForMask( part, BodyGroups );
	}

	internal SceneObject _sceneObject;
	public SceneObject SceneObject => _sceneObject;

	Color ITintable.Color { get => Tint; set => Tint = value; }

	internal void OnModelReloaded()
	{
		if ( !Active ) return;

		UpdateObject();
		OnModelChanged();
	}

	internal Action ModelChanged;

	internal void OnModelChanged()
	{
		ModelChanged?.Invoke();
	}

	protected virtual void UpdateObject()
	{
		if ( UpdateGltfObject() ) return;
		BuildAttachmentHierarchy();

		if ( !_sceneObject.IsValid() )
			return;

		var model = Model ?? Model.Load( "models/dev/box.vmdl" );

		_sceneObject.ColorTint = Tint;
		_sceneObject.Model = model;
		_sceneObject.MeshGroupMask = BodyGroups;
		_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		_sceneObject.Flags.IsStatic = GameObject.IsStatic && !_hasActiveDeformations;
		_sceneObject.RenderingEnabled = gltfReloadPlan is { } plan && ReferenceEquals( plan.CandidateObject, _sceneObject ) ? false : model.HasRenderMeshes();

		if ( _lodOverride.HasValue )
			_sceneObject.LodOverride = _lodOverride.Value;

		RenderOptions.Apply( _sceneObject );

		if ( RenderType == ShadowRenderType.ShadowsOnly )
			_sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.ExcludeGameLayer, true );

		if ( HasMaterialGroups )
		{
			_sceneObject.SetMaterialOverride( null );
			_sceneObject.SetMaterialGroup( MaterialGroup );
		}
		else
		{
			_sceneObject.SetMaterialOverride( MaterialOverride );
		}

	}

	protected override void OnEnabled()
	{
		Assert.True( !_sceneObject.IsValid(), "_sceneObject should be null - disable wasn't called" );
		Assert.NotNull( Scene, "Scene should not be null" );

		var model = Model ?? Model.Load( "models/dev/box.vmdl" );

		try
		{
			_sceneObject = CreateSceneObject( model );
			OnSceneObjectCreated( _sceneObject );
			CommitGltfCreationVariant();
			Transform.OnTransformChanged += OnTransformChanged;
		}
		catch when ( gltfInstance is not null )
		{
			RollbackGltfCreationVariant();
			try { DisposeGltfInstance(); }
			finally { _sceneObject?.Delete(); _sceneObject = null; }
			throw;
		}
	}

	internal override void OnSceneObjectCreated( SceneObject o )
	{
		base.OnSceneObjectCreated( o );

		if ( o.IsValid() )
		{
			o.Transform = gltfInstance is not null ? global::Transform.Zero : WorldTransform;
			o.Component = this;
			o.Tags.SetFrom( GameObject.Tags );
		}

		ApplyMaterialOverrides();
		UpdateObject();
		OnTransformChanged();
		gltfInstance?.SyncSortObjects();
	}


	internal override void OnDisabledInternal()
	{
		try
		{
			ClearAttachmentHierarchy();
		}
		finally
		{
			Transform.OnTransformChanged -= OnTransformChanged;

			BackupRenderAttributes( _sceneObject?.Attributes );
			try { DisposeGltfInstance(); }
			finally
			{
				_sceneObject?.Delete();
				_sceneObject = null;
				ResetDeformations();
			}
		}

		base.OnDisabledInternal();
	}

	private void OnTransformChanged()
	{
		if ( gltfInstance is not null ) { UpdateGltfPlacement(); return; }
		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Transform = Transform.InterpolatedWorld;
		}
	}

	/// <summary>
	/// Tags have been updated - lets update our scene object tags
	/// </summary>
	internal override void OnTagsUpdatedInternal()
	{
		base.OnTagsUpdatedInternal();

		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.Tags.SetFrom( GameObject.Tags );
	}

	public enum ShadowRenderType
	{
		// Render the model with shadows
		[Icon( "wb_shade" )]
		On,
		// Render the model without shadows
		[Icon( "wb_twilight" )]
		Off,
		// Render ONLY the models shadows
		[Icon( "hide_source" )]
		[Title( "Shadows Only" )]
		ShadowsOnly,
	}

	void Update()
	{
		UpdateObject();
		ApplyMaterialOverrides();
	}

	protected override void OnRenderOptionsChanged()
	{
		if ( _sceneObject.IsValid() )
		{
			RenderOptions.Apply( _sceneObject );
		}
	}

	public void SetMaterial( Material material, int triangle = -1 )
	{
		MaterialOverride = material;
	}

	public Material GetMaterial( int triangle = -1 )
	{
		return MaterialOverride;
	}

	/// <summary>
	/// Copy everything from another renderer
	/// </summary>
	public override void CopyFrom( Renderer other )
	{
		base.CopyFrom( other );

		if ( other is ModelRenderer mr )
		{
			Model = mr.Model;
			Tint = mr.Tint;
			BodyGroups = mr.BodyGroups;
			MaterialGroup = mr.MaterialGroup;

			taggedMaterialOverrides = mr.taggedMaterialOverrides is not null ? new( mr.taggedMaterialOverrides ) : null;
			ApplyMaterialOverrides();
		}
	}

	void IHasModel.OnModelReloaded()
	{
		OnModelReloaded();
	}
}
