using System;
using Sandbox;

namespace Editor;

/// <summary>Reads the serialized inertia scale of an existing physics body.</summary>
public static class ModelPhysicsInertiaReadback
{
	public enum Status { Success, WrongThread, InvalidPhysics, InvalidBody, InvalidData }
	public static Status Read( PhysicsGroupDescription physics, int bodyIndex, out float serializedScale, out float effectiveScale )
	{
		serializedScale = default; effectiveScale = default;
		if ( !ThreadSafe.IsMainThread ) return Status.WrongThread;
		if ( physics == null || !physics.IsValid ) return Status.InvalidPhysics;
		if ( bodyIndex < 0 || bodyIndex >= physics.Parts.Count || physics.Parts.Count > 256 ) return Status.InvalidBody;
		var body = physics.Parts[bodyIndex];
		float value = body.native.m_flInertiaScale;
		if ( !float.IsFinite( value ) || value < 0 || value > 1000000 ) return Status.InvalidData;
		serializedScale = value; effectiveScale = body.InertiaScale;
		return Status.Success;
	}
}
