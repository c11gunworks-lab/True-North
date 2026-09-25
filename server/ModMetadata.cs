using JetBrains.Annotations;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace c11_tn_4;

[UsedImplicitly]
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.c11.truenorth4";
    public string Name { get; init; } = "True North";
    public string Author { get; init; } = "C11";
    public List<string>? Contributors { get; init; } = [];
    public Version Version { get; init; } = new(typeof(ModMetadata).Assembly.GetName().Version!.ToString(3));
    public Range SptVersion { get; init; } = new("~4.1.6");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = [];
    public Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new Range("~3.0.6") },
        { "com.c11.spt22lr", new Range("~2.0.0") }
    };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}
