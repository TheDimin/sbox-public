namespace Editor;

/// <summary>
/// Drag selected GameObjects across scene surfaces. Each object follows its own surface sample,
/// aligns its up axis to the hit normal, and retains its original signed depth in that surface.
/// </summary>
[Title( "Surface Place" )]
[Icon( "pan_tool" )]
[Alias( "tools.surface-place-tool" )]
[Group( "1" )]
[Order( 3 )]
public class SurfacePlacementEditorTool : EditorTool
{
	const float MinimumTraceDistance = 4096.0f;
	const float BaselineTracePadding = 1024.0f;

	sealed class PlacementState
	{
		public Transform StartTransform { get; init; }
		public float SurfaceOffset { get; set; }
		public bool HasPlacement { get; set; }
		public Vector3 HitPosition { get; set; }
		public Vector3 HitNormal { get; set; }
	}

	readonly Dictionary<GameObject, PlacementState> placements = [];
	readonly HashSet<Rigidbody> bodies = [];

	IDisposable undoScope;
	Vector3 startCenter;
	bool isDragging;
	bool isPointerGesture;
	bool pointerShiftPressed;
	GameObject pointerHitObject;
	bool hasCursorHit;
	Vector3 cursorHitPosition;
	Vector3 cursorHitNormal;

	public override void OnEnabled()
	{
		base.OnEnabled();
		AllowGameObjectSelection = false;
	}

	public override void OnDisabled()
	{
		isPointerGesture = false;
		pointerHitObject = null;
		CancelDrag();
		ClearBodies();
		base.OnDisabled();
	}

	public override void OnUpdate()
	{
		if ( isDragging && Application.IsKeyDown( KeyCode.Escape ) )
		{
			CancelDrag();
			return;
		}

		if ( !isDragging && Gizmo.HasMouseFocus && Gizmo.WasLeftMousePressed )
		{
			BeginPointerGesture();
		}

		if ( isPointerGesture && Gizmo.IsLeftMouseDown && !pointerShiftPressed && Gizmo.Pressed.CursorDelta.Length > 3.0f )
		{
			isPointerGesture = false;
			StartDrag();
		}

		if ( isDragging && Gizmo.IsLeftMouseDown ) UpdatePlacement();

		if ( isDragging ) DrawHitMarkers();

		if ( Gizmo.WasLeftMouseReleased )
		{
			if ( isDragging ) FinishDrag();
			else if ( isPointerGesture ) ApplyClickSelection();

			isPointerGesture = false;
		}
	}

	private void BeginPointerGesture()
	{
		isPointerGesture = true;
		pointerShiftPressed = Gizmo.IsShiftPressed;
		pointerHitObject = Scene.Trace.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.UsePhysicsWorld( false )
			.WithoutTags( "hidden" )
			.Run().GameObject;
	}

	private void ApplyClickSelection()
	{
		using ( Scene.Editor?.UndoScope( "Select Object" ).Push() )
		{
			if ( pointerShiftPressed )
			{
				if ( pointerHitObject.IsValid() && !Selection.Contains( pointerHitObject ) ) Selection.Add( pointerHitObject );
			}
			else if ( pointerHitObject.IsValid() )
			{
				Selection.Set( pointerHitObject );
			}
			else
			{
				Selection.Clear();
			}
		}
	}

	private void StartDrag()
	{
		var selected = Selection.OfType<GameObject>()
			.Where( go => go.IsValid() && go.GetType() != typeof( Sandbox.Scene ) )
			.ToHashSet();

		var roots = selected
			.Where( go => !selected.Any( ancestor => ancestor != go && go.IsAncestor( ancestor ) ) )
			.ToArray();

		if ( roots.Length == 0 ) return;

		ClearBodies();
		placements.Clear();
		hasCursorHit = false;
		startCenter = roots.Select( go => go.WorldPosition ).Aggregate( ( sum, position ) => sum + position ) / roots.Length;

		foreach ( var go in roots )
		{
			placements[go] = new PlacementState
			{
				StartTransform = go.WorldTransform
			};
		}

		foreach ( var entry in placements )
		{
			entry.Value.SurfaceOffset = FindSurfaceOffset( entry.Key, entry.Value.StartTransform );
		}

		undoScope = SceneEditorSession.Active.UndoScope( "Surface Place Object(s)" )
			.WithGameObjectChanges( roots, GameObjectUndoFlags.Properties )
			.Push();

		roots.DispatchPreEdited( nameof( GameObject.LocalPosition ) );
		roots.DispatchPreEdited( nameof( GameObject.LocalRotation ) );
		isDragging = true;
		UpdatePlacement();
	}

	private float FindSurfaceOffset( GameObject gameObject, Transform transform )
	{
		var up = transform.Rotation.Up.Normal;
		var projectedExtent = Vector3.Dot( gameObject.GetBounds().Extents, up.Abs() );
		var traceDistance = MathF.Max( projectedExtent + BaselineTracePadding, BaselineTracePadding );
		var result = CreateSurfaceTrace( transform.Position + up * traceDistance, transform.Position - up * traceDistance ).Run();
		if ( !result.Hit ) return 0.0f;

		var normal = result.Normal.Normal;
		return normal.LengthSquared < 0.001f
			? 0.0f
			: Vector3.Dot( transform.Position - result.HitPosition, normal );
	}

	private void UpdatePlacement()
	{
		var cursorResult = CreateSurfaceTrace( Gizmo.CurrentRay, Gizmo.RayDepth ).Run();
		if ( !cursorResult.Hit ) return;

		cursorHitPosition = cursorResult.HitPosition;
		cursorHitNormal = cursorResult.Normal.Normal;
		if ( cursorHitNormal.LengthSquared < 0.001f ) return;

		hasCursorHit = true;
		foreach ( var entry in placements )
		{
			var state = entry.Value;
			var startOffset = state.StartTransform.Position - startCenter;
			var tangentOffset = startOffset - cursorHitNormal * Vector3.Dot( startOffset, cursorHitNormal );
			var sampleCenter = cursorHitPosition + tangentOffset;
			var traceDistance = MathF.Max( MinimumTraceDistance, startOffset.Length + BaselineTracePadding );
			var result = CreateSurfaceTrace(
				sampleCenter + cursorHitNormal * traceDistance,
				sampleCenter - cursorHitNormal * traceDistance ).Run();

			if ( !result.Hit ) continue;

			var normal = result.Normal.Normal;
			if ( normal.LengthSquared < 0.001f ) continue;

			var alignedRotation = Rotation.FromToRotation( state.StartTransform.Rotation.Up, normal ) * state.StartTransform.Rotation;
			var transform = state.StartTransform with
			{
				Position = result.HitPosition + normal * state.SurfaceOffset,
				Rotation = alignedRotation
			};

			MoveObject( entry.Key, transform );
			state.HasPlacement = true;
			state.HitPosition = result.HitPosition;
			state.HitNormal = normal;
		}
	}

	private SceneTrace CreateSurfaceTrace( Ray ray, float distance )
	{
		var trace = Scene.Trace.Ray( ray, distance )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.UsePhysicsWorld( false )
			.WithoutTags( "hidden" );

		return IgnoreSelection( trace );
	}

	private SceneTrace CreateSurfaceTrace( Vector3 from, Vector3 to )
	{
		var trace = Scene.Trace.Ray( from, to )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.UsePhysicsWorld( false )
			.WithoutTags( "hidden" );

		return IgnoreSelection( trace );
	}

	private SceneTrace IgnoreSelection( SceneTrace trace )
	{
		foreach ( var go in placements.Keys ) trace = trace.IgnoreGameObjectHierarchy( go );
		return trace;
	}

	private void DrawHitMarkers()
	{
		using var fixedScale = Gizmo.GizmoControls.PushFixedScale();

		if ( hasCursorHit )
		{
			Gizmo.Draw.Color = Gizmo.Colors.Green;
			Gizmo.Draw.SolidSphere( cursorHitPosition, 3.0f, 12, 8 );
			Gizmo.Draw.Line( cursorHitPosition, cursorHitPosition + cursorHitNormal * 24.0f );
		}

		Gizmo.Draw.Color = Gizmo.Colors.Blue;
		foreach ( var state in placements.Values.Where( state => state.HasPlacement ) )
		{
			Gizmo.Draw.Line( state.HitPosition, state.HitPosition + state.HitNormal * 16.0f );
		}
	}

	private void FinishDrag()
	{
		isDragging = false;
		placements.Clear();
		undoScope?.Dispose();
		undoScope = null;
		ClearBodies();
	}

	private void CancelDrag()
	{
		if ( !isDragging ) return;

		foreach ( var entry in placements ) MoveObject( entry.Key, entry.Value.StartTransform );
		FinishDrag();
	}

	private void MoveObject( GameObject gameObject, Transform transform )
	{
		if ( !gameObject.IsValid() ) return;

		if ( !Scene.IsEditor )
		{
			var body = gameObject.GetComponent<Rigidbody>();
			if ( body.IsValid() && body.MotionEnabled )
			{
				bodies.Add( body );
				body.SetTargetTransform( transform );
				return;
			}
		}

		gameObject.BreakProceduralBone();
		gameObject.WorldTransform = transform;
		gameObject.DispatchEdited( nameof( GameObject.LocalPosition ) );
		gameObject.DispatchEdited( nameof( GameObject.LocalRotation ) );
	}

	private void ClearBodies()
	{
		foreach ( var body in bodies )
		{
			if ( body.IsValid() ) body.SetTargetTransform( null );
		}

		bodies.Clear();
	}

	[Shortcut( "tools.surface-place-tool", "q", typeof( SceneViewWidget ) )]
	public static void ActivateSubTool()
	{
		if ( !(EditorToolManager.CurrentModeName == nameof( ObjectEditorTool ) || EditorToolManager.CurrentModeName == "object") ) return;
		EditorToolManager.SetSubTool( nameof( SurfacePlacementEditorTool ) );
	}
}
