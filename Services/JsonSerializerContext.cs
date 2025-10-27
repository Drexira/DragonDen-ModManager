using System.Text.Json.Serialization;

namespace DragonDen.ModManager.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Config))]

[JsonSerializable(typeof(DragonDen.ModManager.Services.ModsDbRegistry.Model))]
[JsonSerializable(typeof(DragonDen.ModManager.Services.ModsDbRegistry.Entry))]
public partial class AppJsonContext : JsonSerializerContext { }