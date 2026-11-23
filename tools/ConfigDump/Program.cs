// Writes the source-of-truth configuration defaults, split exactly as
// StandaloneConfigStorage.Save splits them, and reports drift against the
// shipped DEFAULT-*.json reference files. Never touches a real ModConfig.
using Newtonsoft.Json.Linq;
using SeventhNightZombies;

string outDir = args.Length > 0 ? args[0] : "config-dump";
string repo = args.Length > 1 ? args[1] : ".";
Directory.CreateDirectory(outDir);
var config = new Config();
config.Validate();
var main = new JObject(); var advanced = new JObject(); var retired = new JObject();
foreach (var p in JObject.FromObject(config).Properties())
{
    if (StandaloneConfigStorage.IsRetired(p.Name)) { retired[p.Name] = p.Value; continue; }
    (StandaloneConfigStorage.MainOwns(p.Name) ? main : advanced)[p.Name] = p.Value;
}
File.WriteAllText(Path.Combine(outDir, StandaloneConfigStorage.MainFile), main.ToString());
File.WriteAllText(Path.Combine(outDir, StandaloneConfigStorage.AdvancedFile), advanced.ToString());
File.WriteAllText(Path.Combine(outDir, "retired-keys.json"), retired.ToString());

int drift = 0;
var unvalidated = JObject.FromObject(new Config());
var validated = JObject.FromObject(config);
foreach (var p in unvalidated.Properties())
    if (!JToken.DeepEquals(p.Value, validated[p.Name]))
    { Console.WriteLine($"Validate() changes declared default {p.Name}: {p.Value} -> {validated[p.Name]}"); drift++; }
foreach (var (generated, file) in new[] { (main, StandaloneConfigStorage.MainFile), (advanced, StandaloneConfigStorage.AdvancedFile) })
{
    var shippedPath = Path.Combine(repo, "DEFAULT-" + file);
    if (!File.Exists(shippedPath)) continue;
    var shipped = JObject.Parse(File.ReadAllText(shippedPath));
    foreach (var key in generated.Properties().Select(p => p.Name).Union(shipped.Properties().Select(p => p.Name)).OrderBy(k => k))
    {
        var a = generated[key]; var b = shipped[key];
        if (a == null || b == null || !JToken.DeepEquals(a, b))
        { Console.WriteLine($"DEFAULT-{file} drift {key}: source={a?.ToString(Newtonsoft.Json.Formatting.None) ?? "<absent>"} shipped={b?.ToString(Newtonsoft.Json.Formatting.None) ?? "<absent>"}"); drift++; }
    }
}
Console.WriteLine($"main={main.Count} advanced={advanced.Count} retired={retired.Count} drift={drift}");
