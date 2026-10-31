using System;
using System.IO;
using System.Text.Json;
using Unbound.ApiTools;

var repo=args[0];var runtime=args[1];var editor=args[2];var output=args[3];
var modules=Path.Combine(editor,"Data/Managed/UnityEngine");
var source=Path.Combine(repo,"managed/UnityEngine");
var assembly=Path.Combine(runtime,"UnityEngine.CoreModule.dll");
var platform=Directory.GetFiles(Path.Combine(editor,"Data/PlaybackEngines"),"UnityEditor.*.dll",SearchOption.AllDirectories);
var api=ApiChecker.Check(modules,assembly,platform,source);
File.WriteAllText(Path.Combine(output,"api.json"),JsonSerializer.Serialize(api,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{api.Errors.Count} errors in {api.CheckedTypes} hand-written types checked against Unity");
foreach(var error in api.Errors)Console.WriteLine($"{error.Category}: {error.Type}: {error.Message}");
var stubs=Path.Combine(output,"generated");StubGenerator.Generate(modules,assembly,stubs,source,platform);
var file="UnityEngine.PhysicsModule.g.cs";
bool same=File.ReadAllText(Path.Combine(stubs,file))==File.ReadAllText(Path.Combine(source,"Generated/6000.0",file));
Console.WriteLine("PhysicsModule generator consistency: "+same);
return api.Errors.Count==0 && same ? 0:1;
