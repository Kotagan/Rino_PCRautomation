using System;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageValidationException : Exception
{
	public string Code { get; }

	public RinoPackageValidationException(string code, string message)
		: base(message)
	{
		Code = code;
	}

	public RinoPackageValidationException(string code, string message, Exception innerException)
		: base(message, innerException)
	{
		Code = code;
	}
}
