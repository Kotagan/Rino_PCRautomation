using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal static class RinoSidecarProtocol
{
	public const int ProtocolVersion = 1;

	public const int DefaultMaximumFrameBytes = 1048576;

	public const int MaximumSupportedFrameBytes = 16777216;

	public const int MaximumHeaderBytes = 128;

	private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

	private const string HeaderPrefix = "Content-Length: ";

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = false,
		ReadCommentHandling = JsonCommentHandling.Disallow,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	public static byte[] EncodeRequest(string messageType, string requestId, object payload, int maximumFrameBytes)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(messageType, "messageType");
		ArgumentException.ThrowIfNullOrWhiteSpace(requestId, "requestId");
		ArgumentNullException.ThrowIfNull(payload, "payload");
		if (!Guid.TryParseExact(requestId, "D", out var _) || !IsValidMessageType(messageType))
		{
			throw new ArgumentException("The Rino request envelope identifiers are invalid.");
		}
		return EncodeBody(JsonSerializer.SerializeToUtf8Bytes(new
		{
			protocolVersion = 1,
			messageKind = "request",
			messageType = messageType,
			requestId = requestId,
			payload = payload
		}, SerializerOptions), maximumFrameBytes);
	}

	internal static byte[] EncodeEnvelopeForTesting(object envelope, int maximumFrameBytes)
	{
		return EncodeBody(JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions), maximumFrameBytes);
	}

	public static async Task<RinoSidecarEnvelope?> ReadEnvelopeAsync(Stream stream, int maximumFrameBytes, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream, "stream");
		if ((maximumFrameBytes < 4096 || maximumFrameBytes > 16777216) ? true : false)
		{
			throw new ArgumentOutOfRangeException("maximumFrameBytes");
		}
		byte[] header = new byte[128 + HeaderTerminator.Length];
		int headerLength = 0;
		while (true)
		{
			if (await stream.ReadAsync(header.AsMemory(headerLength, 1), cancellationToken) == 0)
			{
				if (headerLength == 0)
				{
					return null;
				}
				throw new RinoSidecarProtocolException("sidecar.truncatedFrame", "The Rino runtime output ended during a frame header.");
			}
			headerLength++;
			if (headerLength >= HeaderTerminator.Length && header.AsSpan(headerLength - HeaderTerminator.Length, HeaderTerminator.Length).SequenceEqual(HeaderTerminator))
			{
				break;
			}
			if (headerLength > 128)
			{
				throw new RinoSidecarProtocolException("sidecar.headerTooLarge", "The Rino runtime frame header exceeded its byte limit.");
			}
		}
		int count = headerLength - HeaderTerminator.Length;
		string @string;
		try
		{
			@string = Encoding.ASCII.GetString(header, 0, count);
		}
		catch (DecoderFallbackException innerException)
		{
			throw new RinoSidecarProtocolException("sidecar.malformedHeader", "The Rino runtime frame header is malformed.", innerException);
		}
		if (!@string.StartsWith("Content-Length: ", StringComparison.Ordinal) || !int.TryParse(@string.AsSpan("Content-Length: ".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var bodyLength) || bodyLength < 0)
		{
			throw new RinoSidecarProtocolException("sidecar.malformedHeader", "The Rino runtime frame header is malformed.");
		}
		if (bodyLength > maximumFrameBytes)
		{
			throw new RinoSidecarProtocolException("sidecar.frameTooLarge", "The Rino runtime frame exceeded the negotiated byte limit.");
		}
		byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Max(bodyLength, 1));
		try
		{
			Memory<byte> body = rented.AsMemory(0, bodyLength);
			int num2;
			for (int totalRead = 0; totalRead < bodyLength; totalRead += num2)
			{
				int num = totalRead;
				num2 = await stream.ReadAsync(body.Slice(num, body.Length - num), cancellationToken);
				if (num2 == 0)
				{
					throw new RinoSidecarProtocolException("sidecar.truncatedFrame", "The Rino runtime output ended during a frame body.");
				}
			}
			try
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(body, new JsonDocumentOptions
				{
					AllowTrailingCommas = false,
					CommentHandling = JsonCommentHandling.Disallow,
					MaxDepth = 64
				});
				RinoPackageManifestReader.EnsureNoDuplicateProperties(jsonDocument.RootElement);
				RinoSidecarEnvelope? obj = jsonDocument.RootElement.Deserialize<RinoSidecarEnvelope>(SerializerOptions) ?? throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino runtime returned an empty protocol envelope.");
				ValidateEnvelope(obj);
				return obj;
			}
			catch (RinoPackageValidationException innerException2)
			{
				throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino runtime returned a protocol envelope with duplicate fields.", innerException2);
			}
			catch (JsonException innerException3)
			{
				throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino runtime returned invalid strict JSON.", innerException3);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rented, clearArray: true);
		}
	}

	private static byte[] EncodeBody(byte[] body, int maximumFrameBytes)
	{
		if ((maximumFrameBytes < 4096 || maximumFrameBytes > 16777216) ? true : false)
		{
			throw new ArgumentOutOfRangeException("maximumFrameBytes");
		}
		if (body.Length > maximumFrameBytes)
		{
			throw new RinoSidecarProtocolException("sidecar.frameTooLarge", "The Rino request exceeded the negotiated byte limit.");
		}
		byte[] bytes = Encoding.ASCII.GetBytes($"{"Content-Length: "}{body.Length}\r\n\r\n");
		byte[] array = GC.AllocateUninitializedArray<byte>(bytes.Length + body.Length);
		bytes.CopyTo(array, 0);
		body.CopyTo(array, bytes.Length);
		return array;
	}

	private static void ValidateEnvelope(RinoSidecarEnvelope envelope)
	{
		bool flag = envelope.ProtocolVersion != 1 || !IsValidMessageType(envelope.MessageType);
		if (!flag)
		{
			bool flag2;
			switch (envelope.MessageKind)
			{
			case "request":
			case "response":
			case "event":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = !flag2;
		}
		if (flag)
		{
			throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino runtime returned an unsupported protocol envelope.");
		}
		Guid result;
		if (envelope.MessageKind == "request")
		{
			if (!Guid.TryParseExact(envelope.RequestId, "D", out result) || !envelope.Payload.HasValue || envelope.Result.HasValue || envelope.Error.HasValue || envelope.EventId != null || envelope.Sequence.HasValue)
			{
				throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino peer returned an invalid request envelope.");
			}
			return;
		}
		if (envelope.MessageKind == "response")
		{
			if (Guid.TryParseExact(envelope.RequestId, "D", out result) && envelope.Result.HasValue != envelope.Error.HasValue && !envelope.Payload.HasValue && envelope.EventId == null && !envelope.Sequence.HasValue)
			{
				return;
			}
			throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino runtime returned an invalid response envelope.");
		}
		flag = !Guid.TryParseExact(envelope.EventId, "D", out result);
		if (!flag)
		{
			long? sequence = envelope.Sequence;
			bool flag2 = ((!sequence.HasValue || sequence.GetValueOrDefault() < 0) ? true : false);
			flag = flag2;
		}
		if (!flag && envelope.Payload.HasValue && envelope.RequestId == null && !envelope.Result.HasValue && !envelope.Error.HasValue)
		{
			return;
		}
		throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino runtime returned an invalid event envelope.");
	}

	private static bool IsValidMessageType(string value)
	{
		if (value.Length > 128 || !value.Contains('.', StringComparison.Ordinal))
		{
			return false;
		}
		return value.Split('.').All((string segment) => segment.Length > 0 && char.IsAsciiLetterLower(segment[0]) && segment.AsSpan(1).ToArray().All(char.IsAsciiLetterOrDigit));
	}
}
