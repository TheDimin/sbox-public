using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.UI;

namespace Editor.Inspectors;

[Inspector( typeof( GameObject ) )]
[Inspector( typeof( PrefabScene ) )]
public class GameObjectInspector : InspectorWidget
{
	ComponentListWidget componentList;

	public GameObjectInspector( SerializedObject so ) : base( so )
	{
		SerializedObject.OnPropertyStartEdit += PropertyStartEdit;
		SerializedObject.OnPropertyChanged += PropertyChanged;
		SerializedObject.OnPropertyFinishEdit += PropertyFinishEdit;

		Layout = Layout.Column();
		RebuildUI();
	}

	private void RebuildUI()
	{
		Layout.Clear( true );

		var h = new GameObjectHeader( this, SerializedObject );
		Layout.Add( h );

		BuildComponentList();
	}

	IDisposable undoScope;

	void PropertyStartEdit( SerializedProperty property )
	{
		var propertyDisplayName = property.Parent.ParentProperty is null
			? property.Name
			: $"{property.Parent.ParentProperty.Name}.{property.Name}";
		var undoName = $"Edit {propertyDisplayName} on {SerializedObject.GetProperty( nameof( GameObject.Name ) ).GetValue<string>()}";

		var gameObjects = SerializedObject.Targets.OfType<GameObject>();

		var session = SceneEditorSession.Resolve( gameObjects.FirstOrDefault() );
		using var scene = session.Scene.Push();
		undoScope = session.UndoScope( undoName ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties ).Push();

		property.DispatchPreEdited();
	}

	void PropertyChanged( SerializedProperty property )
	{
		property.DispatchEdited();
	}

	void PropertyFinishEdit( SerializedProperty property )
	{
		property.DispatchEdited();

		undoScope?.Dispose();
		undoScope = null;
	}

	void BuildComponentList()
	{
		var scroller = Layout.Add( new ScrollArea( this ) );
		scroller.Canvas = new Widget( scroller );
		scroller.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		scroller.Canvas.Layout = Layout.Column();

		//
		// GameObject Transform component
		//
		if ( SerializedObject.GetProperty( nameof( GameObject.Transform ) ).TryGetAsObject( out var transform ) )
		{
			var transformWidget = new ComponentSheet( Guid.Empty, transform );
			transformWidget.Header.Title = "Transform";
			transformWidget.Header.Color = InspectorHeader.HeaderColor;
			transformWidget.Header.IsDraggable = false;
			scroller.Canvas.Layout.Add( transformWidget );
		}

		// A node of a glTF preview shows its glTF property groups, in place of components.
		if ( TryBuildGltfNode( scroller ) ) return;

		componentList = new ComponentListWidget( SerializedObject );

		scroller.Canvas.Layout.Add( componentList );

		// Add component button
		var row = scroller.Canvas.Layout.AddRow();
		row.AddStretchCell();
		row.Margin = 16;
		var button = row.Add( new Button.Primary( "Add Component", "add" ) );
		button.MinimumWidth = 300;
		button.Clicked = () => AddComponentDialog( button );
		row.AddStretchCell();


		scroller.Canvas.Layout.AddStretchCell( 1 );
	}

	bool TryBuildGltfNode( ScrollArea scroller )
	{
		var targets = SerializedObject.Targets.OfType<GameObject>().ToList();
		if ( targets.Count != 1 || SceneEditorSession.Resolve( targets[0] ) is not GltfEditorSession session || session.GetElement( targets[0] ) is not { } node )
			return false;

		var layout = scroller.Canvas.Layout;
		if ( node.MeshIndex is { } meshIndex )
		{
			var meshes = node.Document.GetElements( GltfElementKind.Mesh );
			if ( meshIndex >= 0 && meshIndex < meshes.Count )
			{
				var mesh = meshes[meshIndex];
				var row = layout.AddRow();
				row.Margin = 8;
				row.Spacing = 6;
				row.Add( new Label( "Mesh" ), 1 );
				row.Add( new Button( mesh.Name, "view_in_ar" ) { Clicked = () => { session.Selection.Clear(); session.Selection.Add( mesh ); } }, 2 );
			}
		}

		if ( session.JointOf( targets[0] ) is { } joint )
		{
			var column = layout.AddColumn();
			column.Margin = 8;
			column.Spacing = 4;
			var row = column.AddRow();
			row.Spacing = 6;
			row.Add( new Label( "Joint" ), 1 );
			row.Add( new Button( $"{joint.Joint} of {joint.Skin.Name}", "accessibility" ) { Clicked = () => { session.Selection.Clear(); session.Selection.Add( joint.Skin ); } }, 2 );
			column.Add( new GltfHeatLegend { FixedHeight = 24 } );
			column.Add( new Label( "Weights in the viewport. Black: no influence. Blue to red: weight 0 to 1. The skinned meshes show in their rest pose." ) { WordWrap = true, Color = Theme.TextControl.WithAlpha( 0.6f ) } );
		}

		// The transform above is the scene one. It writes to the glTF node, so the glTF transform group would repeat it.
		layout.Add( new GltfFacetList( session, node, type => type != typeof( Editor.GltfFacetTypes.NodeTransformFacet ) && (type != typeof( Editor.GltfFacetTypes.NodeMorphWeightsFacet ) || node.HasMorphTargets) ) );
		layout.AddStretchCell( 1 );
		return true;
	}

	/// <summary>
	/// Pop up a window to add a component to this entity
	/// </summary>
	public void AddComponentDialog( Button source )
	{
		var s = new ComponentTypeSelector( this );
		s.OnSelect += ( t ) => AddComponent( t );
		s.OpenAt( source.ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -4 ) );
		s.FixedWidth = source.Width;
	}

	private void AddComponent( TypeDescription componentType )
	{
		var gameObjects = SerializedObject.Targets.OfType<GameObject>();

		var session = SceneEditorSession.Resolve( gameObjects.FirstOrDefault() );
		using var scene = session.Scene.Push();
		using ( session.UndoScope( "Add Component(s)" ).WithComponentCreations().Push() )
		{
			foreach ( var go in gameObjects )
			{
				go.Components.Create( componentType );
			}
		}
	}

	private void PasteComponent()
	{
		foreach ( var go in SerializedObject.Targets.OfType<GameObject>() )
		{
			go.PasteComponent();
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		if ( SceneEditor.HasComponentInClipboard() )
		{
			var menu = new ContextMenu( this );
			menu.AddOption( "Paste Component As New", action: PasteComponent );
			menu.OpenAtCursor( false );
			e.Accepted = true;
			return;
		}

		base.OnContextMenu( e );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		componentList?.Frame();
	}
}
