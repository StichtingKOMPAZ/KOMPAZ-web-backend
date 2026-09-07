using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kompaz.Application.FunctionalTests;

/// <summary>
/// Mirrors the serializer the API is configured with, so tests read and write the same wire format a client would.
/// </summary>
internal static class JsonOptions
{
	public static JsonSerializerOptions Web { get; } = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter() },
	};
}
