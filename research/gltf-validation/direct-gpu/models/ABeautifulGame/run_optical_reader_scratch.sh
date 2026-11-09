#!/bin/bash
set -euo pipefail
root=/home/damian/projects/sbox-public
owned="$root/research/gltf-validation/direct-gpu/models/ABeautifulGame"
printf '%s  %s\n' \
  '9dd9e11742601bb57d1fe03aec205ecdfd0653c16296e3a863c6b731b2ef9155' "$owned/optical-reader-isolated-proposal.patch" \
  '54847a554b479ae46b2a94359aaa15a8b36a1d70f1390354ec29ad4b4c1f1662' "$owned/OpticalReaderTests.cs" \
  'dc6a24daeefb03db427f09422205906b89047abea663c259b389e5fc721799f7' "$owned/optical-reader-harness/OpticalReaderHarness.csproj" \
  '5a3c8eee32edac96068d31822dc5dbaece207becd2b0de4304dd87c64c6bcb53' "$owned/optical-reader-harness/Program.cs" | sha256sum --check
scratch=$(mktemp -d /tmp/gltf-optical-reader-XXXXXX)
mkdir -p "$scratch/engine/Sandbox.Gltf" "$scratch/harness"
cp -a "$root/engine/Sandbox.Gltf/." "$scratch/engine/Sandbox.Gltf/"
sha256sum "$scratch/engine/Sandbox.Gltf/GltfContracts.cs" "$scratch/engine/Sandbox.Gltf/GltfDocument.Materials.cs" "$scratch/engine/Sandbox.Gltf/GltfDocument.MaterialImages.cs"
rm -rf "$scratch/engine/Sandbox.Gltf/bin" "$scratch/engine/Sandbox.Gltf/obj"
cp "$owned/OpticalReaderTests.cs" "$scratch/OpticalReaderTests.cs"
cp "$owned/optical-reader-harness/"* "$scratch/harness/"
(cd "$scratch" && git -c apply.ignoreWhitespace=change apply "$owned/optical-reader-isolated-proposal.patch")
export DOTNET_ROOT=/home/damian/.dotnet
/home/damian/.dotnet/dotnet run --project "$scratch/harness/OpticalReaderHarness.csproj" --no-launch-profile
printf 'scratch=%s\n' "$scratch"
