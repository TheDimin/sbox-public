using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Literal checks independently decoded from the pinned JSON and binary, not from the producer evaluator.</summary>
public static class AnimatedColorsCubeCoreTests
{
    public static int RunSource(string directory)
    {
        var json = File.ReadAllBytes(Path.Combine(directory, "AnimatedColorsCube.gltf"));
        var bin = File.ReadAllBytes(Path.Combine(directory, "AnimatedColorsCube.bin"));
        Require(Convert.ToHexStringLower(SHA256.HashData(json)) == "5639d1aa3489a4ebfeda2045876b671b94e0bb046c66e069c959b62939e8abca", "unexpected source JSON");
        Require(Convert.ToHexStringLower(SHA256.HashData(bin)) == "c5980c793c830b16b60b8eac80765b26dd2f868aa13b48ee76fbab8c42dd5b88", "unexpected source binary");
        using var doc = GltfReader.Read(json, _ => bin, supportedExtensions: new HashSet<string>(StringComparer.Ordinal));
        var restMaterial = doc.ReadMaterial(0).BaseColorFactor;
        Require(Vector4.Distance(restMaterial, new Vector4(.8f, .02f, .02f, 1)) < 2e-6f, "authored fallback material changed");
        Check(doc, 0, -3, 0, 1);
        Check(doc, .25f, -1.5f, .3826834559f, .9238795638f);
        Check(doc, 1.25f, 1.5f, .9238795638f, .3826833665f);
        Check(doc, 1.5f, 3, -1, 4.3711388e-8f);
        Check(doc, 2.25f, 0, .7071068287f, .7071067095f);
        Check(doc, 2.75f, -3, 0, 1);
        // Midpoint between actual adjacent 60 Hz keys, not a keyed frame.
        var q0 = Quaternion.Normalize(new Quaternion(0, .3826834559f, 0, .9238795638f));
        var q1 = Quaternion.Normalize(new Quaternion(0, .4186069667f, 0, .9081674814f));
        var q = Quaternion.Normalize(new Quaternion(0, q0.Y + q1.Y, 0, q0.W + q1.W));
        Check(doc, .2583333333f, (-1.5f - 1.3502219915f) / 2, q.Y, q.W);
        var early = doc.EvaluatePose(0, .25f).GetWorldTransform(0);
        var late = doc.EvaluatePose(0, 1.25f).GetWorldTransform(0);
        Require(Math.Abs(early.M41 - late.M41) > 2.9f && Math.Abs(early.M11 - late.M11) > 1f, "wrong-time negative control failed");
        Require(Vector4.Distance(doc.ReadMaterial(0).BaseColorFactor, restMaterial) < 2e-6f, "pose evaluation mutated shared document material");
        var required = Encoding.UTF8.GetString(json).Replace("\"extensionsUsed\"", "\"extensionsRequired\": [\"KHR_animation_pointer\"], \"extensionsUsed\"");
        var rejected = false;
        try { using var unsupported = GltfReader.Read(Encoding.UTF8.GetBytes(required), _ => bin, supportedExtensions: new HashSet<string>(StringComparer.Ordinal)); }
        catch (GltfException) { rejected = true; }
        Require(rejected, "unsupported required extension was silently accepted");
        return 13;
    }

    private static void Check(GltfDocument doc, float time, float x, float qy, float qw)
    {
        var m = doc.EvaluatePose(0, time).GetWorldTransform(0);
        var expected = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(0, qy, 0, qw))) * Matrix4x4.CreateTranslation(x, 3, 0);
        Require(Math.Abs(m.M41 - expected.M41) < 2e-5f && Math.Abs(m.M42 - expected.M42) < 2e-5f && Math.Abs(m.M43 - expected.M43) < 2e-5f &&
            Math.Abs(m.M11 - expected.M11) < 2e-5f && Math.Abs(m.M13 - expected.M13) < 2e-5f && Math.Abs(m.M31 - expected.M31) < 2e-5f && Math.Abs(m.M33 - expected.M33) < 2e-5f,
            $"wrong node pose at {time}: {m}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
