using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Gltf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.MetalRoughSpheresValidation;

[TestClass]
public sealed class NeutralOcclusionLeaseTests
{
    [TestMethod]
    public async Task SharedNeutralOcclusionSurvivesPeerReleaseAndFailedAcquire()
    {
        await MainThread.Wait();
        using var doc = GltfReader.Read(Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"}}"));
        var prepared = GltfStockMaterialAdapter.Prepare(doc, null);
        var template = Material.FromShader("shaders/gltf_stock_sampler.shader");
        Assert.IsTrue(template is { IsValid: true, IsError: false });
        var first = GltfStockMaterialAdapter.Acquire(prepared, template, linearVertexColor: true);
        GltfMaterialLease<Material> second = null;
        try
        {
            second = GltfStockMaterialAdapter.Acquire(prepared, template, linearVertexColor: true);
            Assert.AreSame(first.Value, second.Value, "Equal material keys should share one native material.");
            var ao = second.Value.GetTexture("g_tAmbientOcclusion");
            Assert.IsTrue(ao is { IsValid: true, IsError: false } && ao.Width == 1 && ao.Height == 1);
            var pixel = ao.GetPixels().Single();
            Assert.AreEqual((byte)255, pixel.r, "Missing occlusion must be neutral.");
            first.Dispose(); first = null;
            Assert.IsTrue(second.Value.IsValid && ao.IsValid, "Peer disposal invalidated the retained lease.");
            Assert.ThrowsException<ArgumentNullException>(() => GltfStockMaterialAdapter.Acquire(prepared, null, linearVertexColor: true));
            Assert.IsTrue(second.Value.IsValid && ao.IsValid, "A failed acquisition invalidated a live peer.");
            using var third = GltfStockMaterialAdapter.Acquire(prepared, template, linearVertexColor: true);
            Assert.AreSame(second.Value, third.Value, "Failed acquisition broke the cache entry.");
        }
        finally { first?.Dispose(); second?.Dispose(); }
        using var afterRelease = GltfStockMaterialAdapter.Acquire(prepared, template, linearVertexColor: true);
        Assert.IsTrue(afterRelease.Value.GetTexture("g_tAmbientOcclusion") is { IsValid: true, IsError: false }, "Reacquisition after final release failed.");
    }
}
