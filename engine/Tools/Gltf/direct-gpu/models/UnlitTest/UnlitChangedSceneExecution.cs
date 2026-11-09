using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

[TestClass]
public sealed class UnlitChangedSceneExecution
{
 [DataTestMethod][DataRow(false)][DataRow(true)]
 public void MixedSceneHasIndependentSourceSlots(bool compiled)=>new UnlitPublicSelectionTests().MixedReachabilityPreservesIndependentLitAndUnlitSourceSlots(compiled);
}
