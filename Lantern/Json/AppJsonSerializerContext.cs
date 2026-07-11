using System.Text.Json.Serialization;
using Lantern.MikroTik;

namespace Lantern.Json;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MikroTikLeaseResponse[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
