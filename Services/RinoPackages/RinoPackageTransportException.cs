using System;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageTransportException : Exception
{
	public string Code { get; }

	public DateTimeOffset? RetryAt { get; }

	public RinoPackageTransportException(string code, string message, DateTimeOffset? retryAt = null)
		: base(message)
	{
		Code = code;
		RetryAt = retryAt;
	}

	public RinoPackageTransportException(string code, string message, Exception innerException, DateTimeOffset? retryAt = null)
		: base(message, innerException)
	{
		Code = code;
		RetryAt = retryAt;
	}
}
