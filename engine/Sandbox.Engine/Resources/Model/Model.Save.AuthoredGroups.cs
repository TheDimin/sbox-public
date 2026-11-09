namespace Sandbox;

partial class Model
{
	// Native runtime builders retain these fields, but the prebuilt serializer omits
	// their raw arrays and offers no raw mesh-group name getter. Freeze authoring
	// values at Create so later builder edits cannot change a model being saved.
	internal string[] AuthoringMeshGroupNames { get; set; }
	internal ModelSaveMaterialGroup[] AuthoringMaterialGroups { get; set; }
}
