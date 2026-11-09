using System.Text.Json;
using Sandbox.Gltf.DirectGpu.CarConceptValidation;
var rows=CarConceptTests.Run(args[0]);
Console.WriteLine(JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
return rows.All(x=>x.Passed)?0:1;
