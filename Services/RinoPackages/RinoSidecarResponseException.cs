using System;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoSidecarResponseException : Exception
{
	public string Code { get; }

	public string MessageKey { get; }

	public RinoSidecarResponseException(string code, string messageKey)
		: base("The Rino runtime rejected the request with a structured error.")
	{
		Code = code;
		MessageKey = messageKey;
	}
}
