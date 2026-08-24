using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoSidecarClient : IAsyncDisposable
{
	private readonly RinoSidecarLaunchOptions _launchOptions;

	private readonly string _desktopVersion;

	private readonly TimeSpan _startupTimeout;

	private readonly TimeSpan _requestTimeout;

	private readonly TimeSpan _shutdownTimeout;

	private readonly SemaphoreSlim _exchangeLock = new SemaphoreSlim(1, 1);

	private Process? _process;

	private Task? _standardErrorDrain;

	private int _maximumFrameBytes = 1048576;

	private RinoSidecarHandshakeResult? _handshakeResult;

	private bool _disposed;

	public bool IsBundledRuntimePresent
	{
		get
		{
			try
			{
				string fullPath = Path.GetFullPath(_launchOptions.ExecutablePath);
				return File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0;
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
			{
				return false;
			}
		}
	}

	public RinoSidecarClient(RinoSidecarLaunchOptions launchOptions)
		: this(launchOptions, TimeSpan.FromSeconds(10.0), TimeSpan.FromSeconds(30.0), TimeSpan.FromSeconds(5.0))
	{
	}

	internal RinoSidecarClient(RinoSidecarLaunchOptions launchOptions, TimeSpan startupTimeout, TimeSpan requestTimeout, TimeSpan shutdownTimeout)
	{
		_launchOptions = launchOptions;
		_startupTimeout = startupTimeout;
		_requestTimeout = requestTimeout;
		_shutdownTimeout = shutdownTimeout;
		Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
		_desktopVersion = $"{Math.Max(version.Major, 0)}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
	}

	public async Task<RinoSidecarHandshakeResult> EnsureStartedAsync(CancellationToken cancellationToken)
	{
		await _exchangeLock.WaitAsync(cancellationToken);
		try
		{
			ThrowIfDisposed();
			return await EnsureStartedCoreAsync(cancellationToken);
		}
		finally
		{
			_exchangeLock.Release();
		}
	}

	public async Task<RinoGraphPreflightResult> ValidateGraphAsync(JsonElement document, CancellationToken cancellationToken)
	{
		await _exchangeLock.WaitAsync(cancellationToken);
		try
		{
			ThrowIfDisposed();
			await EnsureStartedCoreAsync(cancellationToken);
			JsonElement jsonElement = await ExchangeAsync("graph.validate", new { document }, _requestTimeout, cancellationToken);
			JsonElement executable = default(JsonElement);
			bool flag = jsonElement.ValueKind != JsonValueKind.Object || !jsonElement.TryGetProperty("executable", out executable);
			if (!flag)
			{
				JsonValueKind valueKind = executable.ValueKind;
				bool flag2 = valueKind - 5 <= JsonValueKind.Object;
				flag = !flag2;
			}
			if (flag || !jsonElement.TryGetProperty("report", out var value) || value.ValueKind != JsonValueKind.Object || jsonElement.EnumerateObject().Any(delegate(JsonProperty property)
			{
				string name = property.Name;
				bool flag3 = ((name == "executable" || name == "report") ? true : false);
				return !flag3;
			}))
			{
				await StopProcessAsync();
				throw new RinoSidecarProtocolException("sidecar.graphValidateResult", "The Rino runtime returned an invalid graph validation result.");
			}
			return new RinoGraphPreflightResult(executable.GetBoolean(), value.Clone());
		}
		finally
		{
			_exchangeLock.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _exchangeLock.WaitAsync();
		try
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			Process process = _process;
			if (process != null && !process.HasExited)
			{
				try
				{
					await ExchangeAsync("system.shutdown", new { }, _shutdownTimeout, CancellationToken.None);
				}
				catch (Exception ex) when (((ex is RinoSidecarProtocolException || ex is RinoSidecarResponseException || ex is IOException || ex is OperationCanceledException) ? 1 : 0) != 0)
				{
				}
			}
			await StopProcessAsync();
		}
		finally
		{
			_exchangeLock.Release();
			_exchangeLock.Dispose();
		}
	}

	private async Task<RinoSidecarHandshakeResult> EnsureStartedCoreAsync(CancellationToken cancellationToken)
	{
		Process process = _process;
		if (process != null && !process.HasExited)
		{
			return _handshakeResult ?? throw new RinoSidecarProtocolException("sidecar.handshake", "The Rino runtime process is missing its handshake state.");
		}
		await StopProcessAsync();
		StartProcess();
		try
		{
			_handshakeResult = ValidateHandshake(await ExchangeAsync("system.handshake", new
			{
				desktopVersion = _desktopVersion,
				protocolVersionRange = new
				{
					minimum = 1,
					maximum = 1
				},
				maximumFrameBytes = 16777216
			}, _startupTimeout, cancellationToken));
			return _handshakeResult;
		}
		catch
		{
			await StopProcessAsync();
			throw;
		}
	}

	private void StartProcess()
	{
		string fullPath = Path.GetFullPath(_launchOptions.ExecutablePath);
		if (!File.Exists(fullPath) || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoSidecarProtocolException("sidecar.missing", "The bundled Rino runtime executable is unavailable.");
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = fullPath,
			WorkingDirectory = Path.GetDirectoryName(fullPath),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string argument in _launchOptions.Arguments)
		{
			processStartInfo.ArgumentList.Add(argument);
		}
		foreach (string argument2 in _launchOptions.Arguments)
		{
			if (!Path.IsPathFullyQualified(argument2))
			{
				continue;
			}
			string text = ((Path.GetExtension(argument2).Length == 0) ? argument2 : Path.GetDirectoryName(argument2));
			if (!string.IsNullOrWhiteSpace(text))
			{
				Directory.CreateDirectory(text);
				if ((File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0)
				{
					throw new RinoSidecarProtocolException("sidecar.storagePath", "The Rino runtime cannot use a reparse-point data directory.");
				}
			}
		}
		Process process = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = true
		};
		try
		{
			if (!process.Start())
			{
				throw new RinoSidecarProtocolException("sidecar.start", "The bundled Rino runtime process did not start.");
			}
			_process = process;
			_maximumFrameBytes = 1048576;
			_handshakeResult = null;
			_standardErrorDrain = DrainStandardErrorAsync(process.StandardError.BaseStream);
		}
		catch (RinoSidecarProtocolException)
		{
			process.Dispose();
			throw;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
		{
			process.Dispose();
			throw new RinoSidecarProtocolException("sidecar.start", "The bundled Rino runtime process could not be started.", ex2);
		}
	}

	private async Task<JsonElement> ExchangeAsync(string messageType, object payload, TimeSpan timeout, CancellationToken cancellationToken)
	{
		Process process = _process;
		if (process == null || process.HasExited)
		{
			throw new RinoSidecarProtocolException("sidecar.unavailable", "The Rino runtime is not running.");
		}
		string requestId = Guid.NewGuid().ToString("D");
		byte[] array = RinoSidecarProtocol.EncodeRequest(messageType, requestId, payload, _maximumFrameBytes);
		using CancellationTokenSource timeoutCancellation = new CancellationTokenSource(timeout);
		using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
		try
		{
			await process.StandardInput.BaseStream.WriteAsync(array, linkedCancellation.Token);
			await process.StandardInput.BaseStream.FlushAsync(linkedCancellation.Token);
			for (int eventCount = 0; eventCount <= 1024; eventCount++)
			{
				RinoSidecarEnvelope rinoSidecarEnvelope = (await RinoSidecarProtocol.ReadEnvelopeAsync(process.StandardOutput.BaseStream, _maximumFrameBytes, linkedCancellation.Token)) ?? throw new RinoSidecarProtocolException("sidecar.exited", "The Rino runtime output closed before a response arrived.");
				if (!(rinoSidecarEnvelope.MessageKind == "event"))
				{
					if (rinoSidecarEnvelope.MessageKind != "response")
					{
						throw new RinoSidecarProtocolException("sidecar.envelope", "The Rino runtime emitted a request on its output channel.");
					}
					if (!rinoSidecarEnvelope.RequestId.Equals(requestId, StringComparison.Ordinal) || !rinoSidecarEnvelope.MessageType.Equals(messageType, StringComparison.Ordinal))
					{
						throw new RinoSidecarProtocolException("sidecar.correlation", "The Rino runtime returned an uncorrelated response.");
					}
					JsonElement? error = rinoSidecarEnvelope.Error;
					if (error.HasValue)
					{
						JsonElement valueOrDefault = error.GetValueOrDefault();
						throw ParseStructuredError(valueOrDefault);
					}
					return rinoSidecarEnvelope.Result.Value.Clone();
				}
			}
			throw new RinoSidecarProtocolException("sidecar.eventFlood", "The Rino runtime emitted too many events before its response.");
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			await StopProcessAsync();
			throw new RinoSidecarProtocolException("sidecar.timeout", "The Rino runtime request timed out.");
		}
		catch (OperationCanceledException)
		{
			await StopProcessAsync();
			throw;
		}
		catch (RinoSidecarProtocolException)
		{
			await StopProcessAsync();
			throw;
		}
		catch (Exception ex4) when (((ex4 is IOException || ex4 is ObjectDisposedException) ? 1 : 0) != 0)
		{
			await StopProcessAsync();
			throw new RinoSidecarProtocolException("sidecar.transport", "The Rino runtime transport failed.", ex4);
		}
	}

	private RinoSidecarHandshakeResult ValidateHandshake(JsonElement result)
	{
		string result2 = default(string);
		JsonElement value;
		int value2;
		JsonElement value3;
		int value4 = default(int);
		bool flag = result.ValueKind != JsonValueKind.Object || !TryGetRequiredString(result, "runtimeVersion", out result2) || !result.TryGetProperty("protocolVersion", out value) || !value.TryGetInt32(out value2) || value2 != 1 || !result.TryGetProperty("maximumFrameBytes", out value3) || !value3.TryGetInt32(out value4);
		if (!flag)
		{
			bool flag2 = ((value4 < 4096 || value4 > 16777216) ? true : false);
			flag = flag2;
		}
		string result3 = default(string);
		bool flag3 = flag || !TryGetRequiredString(result, "runtimeMode", out result3);
		if (!flag3)
		{
			bool flag2 = ((result3 == "source" || result3 == "frozen") ? true : false);
			flag3 = !flag2;
		}
		if (flag3 || !result.TryGetProperty("graphSchemaVersionRange", out var value5) || value5.ValueKind != JsonValueKind.Object || !value5.TryGetProperty("minimum", out var value6) || !value6.TryGetInt32(out var value7) || !value5.TryGetProperty("maximum", out var value8) || !value8.TryGetInt32(out var value9) || value7 > 1 || value9 < 1)
		{
			throw new RinoSidecarProtocolException("sidecar.handshake", "The Rino runtime returned an incompatible handshake.");
		}
		_maximumFrameBytes = value4;
		return new RinoSidecarHandshakeResult(result2, result3, value4, value7, value9);
	}

	private static RinoSidecarResponseException ParseStructuredError(JsonElement error)
	{
		if (error.ValueKind != JsonValueKind.Object || !TryGetRequiredString(error, "code", out string result) || !TryGetRequiredString(error, "messageKey", out string result2))
		{
			throw new RinoSidecarProtocolException("sidecar.errorEnvelope", "The Rino runtime returned an invalid structured error.");
		}
		return new RinoSidecarResponseException(result, result2);
	}

	private static bool TryGetRequiredString(JsonElement value, string propertyName, out string result)
	{
		if (value.TryGetProperty(propertyName, out var value2) && value2.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value2.GetString()))
		{
			result = value2.GetString();
			return true;
		}
		result = string.Empty;
		return false;
	}

	private async Task StopProcessAsync()
	{
		Process process = _process;
		_process = null;
		_handshakeResult = null;
		if (process == null)
		{
			return;
		}
		try
		{
			process.StandardInput.Close();
		}
		catch (InvalidOperationException)
		{
		}
		try
		{
			if (process.HasExited)
			{
				return;
			}
			using CancellationTokenSource cleanupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2.0));
			try
			{
				await process.WaitForExitAsync(cleanupCancellation.Token);
			}
			catch (OperationCanceledException)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync();
			}
		}
		finally
		{
			Task standardErrorDrain = _standardErrorDrain;
			if (standardErrorDrain != null)
			{
				try
				{
					await standardErrorDrain.WaitAsync(TimeSpan.FromSeconds(1.0));
				}
				catch (Exception ex3) when (((ex3 is TimeoutException || ex3 is IOException) ? 1 : 0) != 0)
				{
				}
			}
			_standardErrorDrain = null;
			process.Dispose();
		}
	}

	private static async Task DrainStandardErrorAsync(Stream stream)
	{
		byte[] buffer = new byte[4096];
		try
		{
			while (await stream.ReadAsync(buffer) > 0)
			{
				Array.Clear(buffer);
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is ObjectDisposedException) ? 1 : 0) != 0)
		{
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
