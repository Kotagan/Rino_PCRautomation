using System.Text.Json;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoSidecarEnvelope
{
	public int ProtocolVersion { get; init; }

	public required string MessageKind { get; init; }

	public required string MessageType { get; init; }

	public string? RequestId { get; init; }

	public string? EventId { get; init; }

	public long? Sequence { get; init; }

	public string? RunId { get; init; }

	public string? NodeId { get; init; }

	public JsonElement? Payload { get; init; }

	public JsonElement? Result { get; init; }

	public JsonElement? Error { get; init; }
}
