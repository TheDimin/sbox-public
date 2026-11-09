using System.Text.Json;
using NMatrix = System.Numerics.Matrix4x4;
using NQuaternion = System.Numerics.Quaternion;

namespace Sandbox.Gltf.Cameras;

/// <summary>Transactional, camera-only rest-pose import. No document lease survives import.</summary>
internal static class GltfCameraImport
{
	private sealed record Node( int Index, int Parent, string Name, Transform Transform, GltfCamera Camera );
	private const float InchesPerMeter = 1f / .0254f;

	internal static GameObject Create( GltfDocument document, int sceneIndex, GameObject parent, Vector2 viewport )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !float.IsFinite( viewport.x ) || !float.IsFinite( viewport.y ) || viewport.x <= 0 || viewport.y <= 0 )
			throw new ArgumentOutOfRangeException( nameof( viewport ), "Camera presentation size must be finite and positive." );
		var nodes = ReadNodes( document, sceneIndex ); // Preflight before changing the scene.
		using var scope = parent.Scene.Push();
		var root = new GameObject( parent, false, "glTF Cameras" ) { LocalTransform = Transform.Zero };
		try
		{
			var objects = new Dictionary<int, GameObject>();
			foreach ( var node in nodes )
			{
				var owner = node.Parent < 0 ? root : objects[node.Parent];
				var gameObject = new GameObject( owner, true, node.Name );
				gameObject.WorldTransform = root.WorldTransform.ToWorld( node.Transform );
				objects.Add( node.Index, gameObject );
				if ( node.Camera is null ) continue;
				// glTF looks along local -Z with +Y up. The converted node's
				// engine axes are +X = glTF +Z, +Y = +X, +Z = +Y.
				var lens = new GameObject( gameObject, true, "Camera" )
				{
					LocalTransform = new Transform( Vector3.Zero, Rotation.FromYaw( 180 ), Vector3.One )
				};
				var camera = lens.Components.Create<CameraComponent>();
				Configure( camera, node.Camera, viewport );
			}
			root.Enabled = true;
			return root;
		}
		catch
		{
			root.Destroy();
			throw;
		}
	}

	private static List<Node> ReadNodes( GltfDocument document, int selected )
	{
		var root = document.Root;
		var scene = root.GetProperty( "scenes" )[selected];
		var selectedNodes = new List<(int Index, int Parent, JsonElement Value)>();
		var pending = new Stack<(int Index, int Parent)>();
		var seen = new HashSet<int>();
		if ( scene.TryGetProperty( "nodes", out var roots ) )
			foreach ( var node in roots.EnumerateArray().Reverse() ) pending.Push( (node.GetInt32(), -1) );
		while ( pending.TryPop( out var item ) )
		{
			if ( !seen.Add( item.Index ) ) throw new GltfException( "Camera scene contains duplicate node references." );
			var node = root.GetProperty( "nodes" )[item.Index];
			selectedNodes.Add( (item.Index, item.Parent, node) );
			if ( node.TryGetProperty( "children", out var children ) )
				foreach ( var child in children.EnumerateArray().Reverse() ) pending.Push( (child.GetInt32(), item.Index) );
		}
		// Unrelated geometry nodes do not participate in a camera-only import.
		var needed = selectedNodes.Where( x => x.Value.TryGetProperty( "camera", out _ ) ).Select( x => x.Index ).ToHashSet();
		foreach ( var node in selectedNodes.AsEnumerable().Reverse() ) if ( needed.Contains( node.Index ) && node.Parent >= 0 ) needed.Add( node.Parent );
		var result = new List<Node>();
		foreach ( var item in selectedNodes.Where( x => needed.Contains( x.Index ) ) )
		{
			var node = item.Value;
			var matrix = document.GetWorldTransform( item.Index );
			var determinant = (double)matrix.M11 * ((double)matrix.M22 * matrix.M33 - (double)matrix.M23 * matrix.M32)
				- (double)matrix.M12 * ((double)matrix.M21 * matrix.M33 - (double)matrix.M23 * matrix.M31)
				+ (double)matrix.M13 * ((double)matrix.M21 * matrix.M32 - (double)matrix.M22 * matrix.M31);
			if ( determinant < 0 )
				throw new GltfException( "Reflected camera hierarchy world transforms need an explicit scale-free view convention; this import does not support them. The glTF resource remains valid." );
			if ( !NMatrix.Decompose( matrix, out _, out var rotation, out var position ) )
				throw new GltfException( "The camera hierarchy contains a world transform whose scale cannot be separated from rotation." );
			var enginePosition = new Vector3( position.Z, position.X, position.Y ) * InchesPerMeter;
			if ( !float.IsFinite( enginePosition.x ) || !float.IsFinite( enginePosition.y ) || !float.IsFinite( enginePosition.z ) )
				throw new GltfException( "Camera hierarchy position exceeds the finite engine coordinate domain." );
			var transform = new Transform( enginePosition, new NQuaternion( rotation.Z, rotation.X, rotation.Y, rotation.W ), Vector3.One );
			var camera = node.TryGetProperty( "camera", out var reference ) ? document.ReadCamera( reference.GetInt32() ) : null;
			if ( camera is not null ) Validate( camera );
			result.Add( new( item.Index, item.Parent, node.TryGetProperty( "name", out var name ) ? name.GetString() : $"Node {item.Index}", transform, camera ) );
		}
		return result;
	}

	private static void Validate( GltfCamera camera )
	{
		foreach ( var value in new[] { camera.ZNear, camera.ZFar ?? 1, camera.XMagnitude, camera.YMagnitude } )
			if ( !float.IsFinite( value * InchesPerMeter ) ) throw new GltfException( "Camera parameter exceeds the finite engine coordinate domain." );
		if ( camera.Type == "orthographic" && !float.IsFinite( 2 * MathF.Abs( camera.YMagnitude ) * InchesPerMeter ) )
			throw new GltfException( "Camera height exceeds the finite engine coordinate domain." );
	}

	private static void Configure( CameraComponent camera, GltfCamera source, Vector2 viewport )
	{
		camera.ZNear = source.ZNear * InchesPerMeter;
		camera.ZFar = source.ZFar.HasValue ? source.ZFar.Value * InchesPerMeter : float.PositiveInfinity;
		camera.CustomSize = viewport;
		camera.Orthographic = source.Type == "orthographic";
		camera.FovAxis = CameraComponent.Axis.Vertical;
		var tangent = camera.Orthographic ? 0 : Math.Tan( (double)source.YFov * .5 );
		if ( camera.Orthographic )
		{
			camera.OrthographicHeight = 2 * MathF.Abs( source.YMagnitude ) * InchesPerMeter;
			camera.FlipX = source.XMagnitude < 0;
			camera.FlipY = source.YMagnitude < 0;
		}
		else
		{
			// The prescribed projection depends on tan(yfov/2), including its
			// sign. Use the equivalent positive principal angle for the native
			// frustum, then compose the two image reflections when necessary.
			var degrees = (float)(2 * Math.Atan( Math.Abs( tangent ) ) * (180 / Math.PI));
			// Keep float rounding on the positive side of the tangent pole.
			camera.FieldOfView = Math.Min( degrees, float.BitDecrement( 180f ) );
			camera.FlipX = camera.FlipY = tangent < 0;
		}
		var authoredAspect = camera.Orthographic ? MathF.Abs( source.XMagnitude / source.YMagnitude ) : source.AspectRatio;
		if ( authoredAspect is float aspect )
		{
			var targetAspect = viewport.x / viewport.y;
			if ( aspect < targetAspect )
			{
				var width = aspect / targetAspect;
				camera.Viewport = new Vector4( (1 - width) / 2, 0, width, 1 );
			}
			else
			{
				var height = targetAspect / aspect;
				camera.Viewport = new Vector4( 0, (1 - height) / 2, 1, height );
			}
		}
		if ( !camera.Orthographic && (!source.ZFar.HasValue || source.YFov >= MathF.PI) )
		{
			// Preserve the prescribed cotangent even when a principal angle
			// rounds close to pi. Native infinity also needs the supported
			// reverse-Z override; no finite far-plane sentinel is introduced.
			camera.InitializeRendering( false );
			var cotangent = (float)(1 / Math.Abs( tangent ));
			var aspect = camera.ScreenRect.Width / camera.ScreenRect.Height;
			var depthScale = source.ZFar.HasValue ? camera.ZNear / (camera.ZFar - camera.ZNear) : 0;
			var depthOffset = source.ZFar.HasValue ? camera.ZNear * (camera.ZFar / (camera.ZFar - camera.ZNear)) : camera.ZNear;
			camera.CustomProjectionMatrix = new NMatrix(
				cotangent / aspect, 0, 0, 0,
				0, cotangent, 0, 0,
				0, 0, depthScale, depthOffset,
				0, 0, -1, 0 );
		}
	}
}
