#!/bin/bash
set -euo pipefail
root=/home/damian/projects/sbox-public
owned="$root/research/gltf-validation/direct-gpu/models/CarConcept"
scratch=$(mktemp -d /tmp/car-concept-candidate-XXXXXX)
mkdir -p "$scratch/engine/Sandbox.Gltf" "$scratch/harness"
cp -a "$root/engine/Sandbox.Gltf/." "$scratch/engine/Sandbox.Gltf/"
rm -rf "$scratch/engine/Sandbox.Gltf/bin" "$scratch/engine/Sandbox.Gltf/obj"
(cd "$scratch" && git apply --include=engine/Sandbox.Gltf/GltfDocument.Materials.cs "$owned/uv-sign-candidate.patch")
cp "$owned/CarConceptTests.cs" "$owned/CarConceptTireTests.cs" "$scratch/"
cat > "$scratch/harness/Harness.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="../engine/Sandbox.Gltf/Sandbox.Gltf.csproj"/><Compile Include="../CarConceptTests.cs" Link="CarConceptTests.cs"/><Compile Include="../CarConceptTireTests.cs" Link="CarConceptTireTests.cs"/></ItemGroup></Project>
PROJECT
cat > "$scratch/harness/Program.cs" <<'PROGRAM'
using System.Text.Json;
using Sandbox.Gltf.DirectGpu.CarConceptValidation;
var checks=CarConceptTests.Run(args[0]); var tires=CarConceptTireTests.Run(args[0]);
Console.WriteLine(JsonSerializer.Serialize(new { checks, tires },new JsonSerializerOptions{WriteIndented=true}));
return checks.All(x=>x.Passed) && tires.All(x=>x.Passed) ? 0 : 1;
PROGRAM
export DOTNET_ROOT=/home/damian/.dotnet
/home/damian/.dotnet/dotnet run --project "$scratch/harness/Harness.csproj" -- "$root" | tee "$scratch/result.json"
printf 'scratch=%s\n' "$scratch"
