using System;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoSidecarProtocolException : Exception
{
	public string Code { get; }

	public RinoSidecarProtocolException(string code, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Code = code;
	}
}
