using System.Text.Json.Serialization;
using static Sandbox.Component;
using static Sandbox.ModelRenderer;

namespace Sandbox;

/// <summary>
/// An editable polygon mesh with collision
/// </summary>
[Hide, Expose]
public sealed partial class MeshComponent : Collider, ExecuteInEditor, ITintable, IMaterialSetter
{
	/// <summary>A derived rendering mesh and its native LOD switch distance.</summary>
	[Expose]
	public sealed class LodLevel
	{
		[Property, Hide] public PolygonMesh Mesh { get; set; }
		[Property] public float SwitchDistance { get; set; }
		/// <summary>Optional model-space bounds for vertex-deformed geometry.</summary>
		[Property, Hide] public BBox? RenderBounds { get; set; }
	}

	/// <summary>Derived meshes, ordered from LOD1 to LOD7.</summary>
	[Property] public List<LodLevel> Lods { get; set; } = new();

	/// <summary>Fingerprint of the editable source used to build the derived LODs.</summary>
	[Property, Hide] public string LodSourceFingerprint { get; set; }

	/// <summary>Fingerprint of the current editable mesh. Use when publishing derived LODs.</summary>
	public string GetLodSourceFingerprint()
	{
		if ( Mesh is null ) return null;
		var bytes = System.Text.Encoding.UTF8.GetBytes( Json.Serialize( Mesh ) );
		return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes ) );
	}

	[Expose]
	public enum CollisionType
	{
		None,
		Mesh,
		Hull,
		Sphere = 4,
		Capsule
	}

	[Property, Hide]
	public PolygonMesh Mesh
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			RebuildMesh();
		}
	}

	[Property, Order( 1 )]
	public CollisionType Collision
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			UpdateMesh();
		}
	} = CollisionType.Mesh;

	[Property, Title( "Tint" ), Order( 2 )]
	public Color Color
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			_sceneObject?.ColorTint = Color;
		}
	} = Color.White;

	[Property, Order( 3 )]
	public float SmoothingAngle
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;
			Mesh?.SetSmoothingAngle( field );
		}
	}

	/// <summary>
	/// Scene object tag for Hide In Game meshes in the editor, excluded from envmap and indirect light volume bakes.
	/// </summary>
	internal const string HideInGameTag = "hide_in_game";

	[Property, Order( 4 )]
	public bool HideInGame
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( Scene.IsEditor )
			{
				UpdateSceneObjectTags();
				return;
			}

			if ( HideInGame )
			{
				DeleteSceneObject();
			}
			else if ( !_sceneObject.IsValid() && Model is not null )
			{
				_sceneObject = new SceneObject( Scene.SceneWorld, Model, WorldTransform );
				UpdateSceneObject();
			}
		}
	}

	[Title( "Cast Shadows" ), Property, Category( "Lighting" )]
	public ShadowRenderType RenderType
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;
			if ( _sceneObject.IsValid() )
			{
				_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
				_sceneObject.Flags.ExcludeGameLayer = RenderType == ShadowRenderType.ShadowsOnly;
			}
		}
	} = ShadowRenderType.On;

	/// <summary>Draw order for overlapping overlay surfaces.</summary>
	[Property, Category( "Rendering" )]
	public int RenderOrder
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( _sceneObject.IsValid() )
				_sceneObject.Attributes.Set( "RenderOrder", value );
		}
	}

	/// <summary>Hide this surface when rendering in low quality mode.</summary>
	[Property, Category( "Rendering" )]
	public bool DisabledInLowQuality
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( _sceneObject.IsValid() )
				_sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.DisabledInLowQuality, value );
		}
	}

	[JsonIgnore, Hide]
	public Model Model { get; private set; }

	public override bool IsConcave => Collision == CollisionType.Mesh;

	bool Hidden => !Scene.IsEditor && HideInGame;

	SceneObject _sceneObject;
	PolygonMesh _builtMesh;
	CollisionType _builtCollision;
	List<(PolygonMesh Mesh, float Distance, BBox? Bounds)> _builtLods = new();
	string _builtLodFingerprint;
	bool _builtLodsActive;

	bool LodSettingsChanged()
	{
		if ( _builtLodFingerprint != LodSourceFingerprint || _builtLods.Count != (Lods?.Count ?? 0) ) return true;
		for ( var i = 0; i < _builtLods.Count; i++ )
		{
			var lod = Lods[i];
			if ( _builtLods[i].Mesh != lod?.Mesh || !_builtLods[i].Distance.Equals( lod?.SwitchDistance ?? float.NaN ) || !Nullable.Equals( _builtLods[i].Bounds, lod?.RenderBounds ) || (_builtLodsActive && (lod?.Mesh?.IsDirty == true || lod?.Mesh?.IsVertexDataDirty == true)) ) return true;
		}
		return false;
	}

	void RememberLodSettings()
	{
		_builtLodFingerprint = LodSourceFingerprint;
		_builtLods = Lods?.Select( x => (x?.Mesh, x?.SwitchDistance ?? float.NaN, x?.RenderBounds) ).ToList() ?? new();
	}

	bool CanBuildLods()
	{
		if ( Mesh is null || Lods is null || Lods.Count is < 1 or > 7 || string.IsNullOrEmpty( LodSourceFingerprint ) ) return false;
		if ( !string.Equals( LodSourceFingerprint, GetLodSourceFingerprint(), StringComparison.Ordinal ) ) return false;
		var previous = 0f;
		foreach ( var lod in Lods )
		{
			if ( lod?.Mesh is null || lod.Mesh == Mesh || !float.IsFinite( lod.SwitchDistance ) || lod.SwitchDistance <= previous ) return false;
			if ( lod.RenderBounds is { } bounds && (!float.IsFinite( bounds.Mins.x ) || !float.IsFinite( bounds.Mins.y ) || !float.IsFinite( bounds.Mins.z ) || !float.IsFinite( bounds.Maxs.x ) || !float.IsFinite( bounds.Maxs.y ) || !float.IsFinite( bounds.Maxs.z ) || bounds.Mins.x > bounds.Maxs.x || bounds.Mins.y > bounds.Maxs.y || bounds.Mins.z > bounds.Maxs.z) ) return false;
			previous = lod.SwitchDistance;
		}
		return true;
	}

	public void SetMaterial( Material material, int triangle )
	{
		if ( Mesh is null ) return;

		var face = Mesh.TriangleToFace( triangle );
		if ( !face.IsValid ) return;

		Mesh.SetFaceMaterial( face, material );
	}

	public Material GetMaterial( int triangle )
	{
		if ( Mesh is null ) return default;

		var face = Mesh.TriangleToFace( triangle );
		return Mesh.GetFaceMaterial( face );
	}

	internal override void OnEnabledInternal()
	{
		GameObject.Tags.Add( "world" );

		// Mesh needs to build before collider.
		RebuildRenderMesh();

		base.OnEnabledInternal();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		DeleteSceneObject();
	}

	void DeleteSceneObject()
	{
		DeleteEditorRenderData();

		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.RenderingEnabled = false;
		_sceneObject.Delete();
		_sceneObject = null;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		UpdateMesh();
	}

	public void RebuildMesh()
	{
		if ( Scene?.IsEditor != true ) return;

		UpdateMesh();
	}

	void UpdateMesh()
	{
		if ( !Active || !PhysicsBody.IsValid() ) return;

		if ( Scene.IsEditor && (_builtMesh != Mesh || Mesh?.IsDirty == true || LodSettingsChanged() || (Mesh?.IsVertexDataDirty == true && Lods?.Count > 0)) )
		{
			RebuildRenderMesh();
		}
		else if ( _builtMesh is not null && _builtCollision != Collision )
		{
			SetModel( CreateCurrentModel( _builtMesh ) );
		}
		else if ( Scene.IsEditor && Mesh?.IsVertexDataDirty == true )
		{
			Mesh.UpdateVertexData();
		}
	}

	protected override void OnTagsChanged()
	{
		base.OnTagsChanged();

		UpdateSceneObjectTags();
	}

	internal override void TransformChanged( GameTransform root )
	{
		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Transform = WorldTransform;
		}
		if ( Scene.IsEditor )
		{
			UpdateEditorRenderState();
			EditorRenderDataChanged?.Invoke();
		}

		if ( Mesh is not null && Scene.IsEditor )
		{
			// Compute face texture parameters on transform change but don't rebuild mesh now.
			var wasDirty = Mesh.IsDirty;
			Mesh.Transform = WorldTransform;
			if ( !wasDirty )
				Mesh.IsDirty = false;
		}

		base.TransformChanged( root );
	}

	internal override void ApplyShapeSurfaces()
	{
		if ( Shapes.Count == 0 ) return;

		// PolygonMesh emits one shape per physics part, in model order.
		var index = 0;
		foreach ( var body in Model.Physics.Parts )
		{
			foreach ( var part in body.Parts )
			{
				var shape = Shapes[index++];
				shape.Surface = Surface ?? part.Surface;
				if ( part is PhysicsGroupDescription.BodyPart.MeshPart mesh && shape._shape is PhysicsShape3d physics )
					physics.SetSurfaces( mesh.Surfaces, Surface );
			}
		}
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		if ( Collision == CollisionType.None )
			yield break;

		if ( Model is null || Model.Physics is null )
			yield break;

		foreach ( var part in Model.Physics.Parts )
		{
			Assert.NotNull( part, "Physics part was null" );

			var bx = local.ToWorld( part.Transform );

			if ( Collision == CollisionType.Mesh )
			{
				foreach ( var mesh in part.Meshes )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( mesh.Surface, mesh.Surfaces ) )
					{
						shape = targetBody.AddShape( mesh, bx, false, true );
					}
					Assert.NotNull( shape, "Mesh shape was null" );

					yield return shape;
				}
			}
			else if ( Collision == CollisionType.Sphere )
			{
				foreach ( var sphere in part.Spheres )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( sphere.Surface ) )
						shape = targetBody.AddSphereShape( bx.PointToWorld( sphere.Sphere.Center ), sphere.Sphere.Radius * MathF.Abs( bx.UniformScale ) );
					yield return shape;
				}
			}
			else if ( Collision == CollisionType.Capsule )
			{
				foreach ( var capsule in part.Capsules )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( capsule.Surface ) )
						shape = targetBody.AddCapsuleShape( bx.PointToWorld( capsule.Capsule.CenterA ), bx.PointToWorld( capsule.Capsule.CenterB ), capsule.Capsule.Radius * MathF.Abs( bx.UniformScale ) );
					yield return shape;
				}
			}
			else if ( Collision == CollisionType.Hull )
			{
				foreach ( var hull in part.Hulls )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( hull.Surface ) )
					{
						shape = targetBody.AddShape( hull, bx );
					}
					Assert.NotNull( shape, "Hull shape was null" );
					yield return shape;
				}
			}
		}
	}

	void RebuildRenderMesh()
	{
		if ( !Active ) return;

		if ( Mesh is not null )
		{
			Mesh.Transform = WorldTransform;
			Mesh.SetSmoothingAngle( SmoothingAngle );
		}

		var model = Model;
		if ( _builtMesh != Mesh || Mesh?.IsDirty == true || model is null || LodSettingsChanged() || (Mesh?.IsVertexDataDirty == true && Lods?.Count > 0) )
		{
			if ( Mesh is not null )
			{
				if ( Mesh.IsDirty ) Mesh.RebuildGeometry();
				else if ( Mesh.IsVertexDataDirty ) Mesh.UpdateVertexData();
			}
			model = CreateCurrentModel( Mesh );
		}
		else
		{
			if ( Mesh?.IsVertexDataDirty == true )
				Mesh.UpdateVertexData();
			if ( _builtCollision != Collision )
				model = CreateCurrentModel( Mesh );
		}

		_builtMesh = Mesh;
		RememberLodSettings();
		SetModel( model );
	}

	Model CreateCurrentModel( PolygonMesh source )
	{
		if ( source is null ) { _builtLodsActive = false; return null; }
		_builtLodsActive = source == Mesh && CanBuildLods();
		if ( !_builtLodsActive ) return source.CreateModel( Collision );
		return source.CreateModel( Collision, Lods.Select( x => x.Mesh ).ToArray(), Lods.Select( x => x.SwitchDistance ).ToArray(), Lods.Select( x => x.RenderBounds ).ToArray() );
	}

	void SetModel( Model model )
	{
		_builtCollision = Collision;

		if ( Model != model )
		{
			Model = model;
			RebuildImmediately();
		}

		if ( Model is null || Model.MeshCount == 0 )
		{
			DeleteSceneObject();
			return;
		}

		if ( Hidden ) return;

		if ( !_sceneObject.IsValid() )
		{
			_sceneObject = new SceneObject( Scene.SceneWorld, Model, WorldTransform );
		}
		else
		{
			_sceneObject.Model = Model;
			_sceneObject.Transform = WorldTransform;

			// We manually set the model, sceneobject needs to update based on any new materials in it
			_sceneObject.UpdateFlagsBasedOnMaterial();
		}

		UpdateSceneObject();
	}

	void UpdateSceneObject()
	{
		if ( !_sceneObject.IsValid() ) return;

		if ( Scene.IsEditor )
		{
			UpdateEditorRenderData();
		}

		_sceneObject.Component = this;
		UpdateSceneObjectTags();
		_sceneObject.ColorTint = Color;
		if ( RenderOrder != 0 ) _sceneObject.Attributes.Set( "RenderOrder", RenderOrder );
		if ( DisabledInLowQuality ) _sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.DisabledInLowQuality, true );
		_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		_sceneObject.Flags.ExcludeGameLayer = RenderType == ShadowRenderType.ShadowsOnly;
		_sceneObject.Flags.IsStatic = GameObject.IsStatic;
	}

	/// <summary>
	/// Hide In Game meshes still render in the editor, tag them so editor bakes (envmaps, indirect light volumes) can leave them out.
	/// </summary>
	void UpdateSceneObjectTags()
	{
		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.Tags.SetFrom( GameObject.Tags );

		if ( HideInGame )
			_sceneObject.Tags.Add( HideInGameTag );

		if ( Scene.IsEditor )
		{
			_sceneObject.Tags.Add( "editor_mesh" );
			UpdateEditorRenderState();
		}
	}
}
