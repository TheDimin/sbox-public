namespace Sandbox;

public partial class SceneObject
{
	internal interface SceneObjectCallbacks : IValid
	{
		void OnBeforeObjectRender();
		void OnAfterObjectRender();
	}

	private SceneObjectCallbacks callbackTarget;
	internal SceneObjectCallbacks CallbackTarget
	{
		get => callbackTarget;
		set
		{
			if ( ReferenceEquals( callbackTarget, value ) ) return;
			callbackTarget = value;
			DependentStateChanged?.Invoke( Rendering.SceneObjectChange.Settings );
		}
	}

	internal void ExecuteBefore()
	{
		if ( !CallbackTarget.IsValid() ) return;
		CallbackTarget.OnBeforeObjectRender();
	}

	internal void ExecuteAfter()
	{
		if ( !CallbackTarget.IsValid() ) return;

		CallbackTarget.OnAfterObjectRender();
	}
}
