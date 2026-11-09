using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

/// <summary>Runs only changed feature probes without repeating the separate six-form model capture gate.</summary>
[TestClass]
public sealed class UnlitFocusedFeatureExecution
{
 public static List<object> Measurements => UnlitTestGraphCandidateGate.Measurements;
 public static void Configure(string root,string config)=>UnlitTestGraphCandidateGate.Configure(root,config);
 [TestMethod] public async Task SourceMaterialFeatures()=>await new UnlitTestGraphCandidateGate().SourceAdapterTextureAlphaSidednessAndOverrides();
}
