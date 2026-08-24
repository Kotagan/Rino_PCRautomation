using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageCompatibilityPolicy
{
	private sealed class RinoSemanticVersion : IComparable<RinoSemanticVersion>
	{
		private string Major { get; }

		private string Minor { get; }

		private string Patch { get; }

		private IReadOnlyList<string> Prerelease { get; }

		private RinoSemanticVersion(string major, string minor, string patch, IReadOnlyList<string> prerelease)
		{
			Major = major;
			Minor = minor;
			Patch = patch;
			Prerelease = prerelease;
		}

		public static RinoSemanticVersion Parse(string value, string parameterName)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
			if (value.Length > 128)
			{
				throw new ArgumentException("Semantic version is too long.", parameterName);
			}
			int num = value.IndexOf('+');
			string text = ((num >= 0) ? value.Substring(0, num) : value);
			object obj;
			if (num < 0)
			{
				obj = null;
			}
			else
			{
				string text2 = value;
				int num2 = num + 1;
				obj = text2.Substring(num2, text2.Length - num2);
			}
			string text3 = (string)obj;
			int num3 = text.IndexOf('-');
			string obj2 = ((num3 >= 0) ? text.Substring(0, num3) : text);
			object obj3;
			if (num3 < 0)
			{
				obj3 = null;
			}
			else
			{
				string text2 = text;
				int num2 = num3 + 1;
				obj3 = text2.Substring(num2, text2.Length - num2);
			}
			string text4 = (string)obj3;
			string[] array = obj2.Split('.');
			if (array.Length != 3 || array.Any((string identifier) => !IsNumericIdentifier(identifier, allowLeadingZeroes: false)) || (text3 != null && !IsIdentifierList(text3)) || (text4 != null && !IsIdentifierList(text4)))
			{
				throw new ArgumentException("Semantic version is invalid.", parameterName);
			}
			return new RinoSemanticVersion(array[0], array[1], array[2], text4?.Split('.') ?? Array.Empty<string>());
		}

		public int CompareTo(RinoSemanticVersion? other)
		{
			if (other == null)
			{
				return 1;
			}
			int num = CompareNumericIdentifier(Major, other.Major);
			if (num != 0)
			{
				return num;
			}
			num = CompareNumericIdentifier(Minor, other.Minor);
			if (num != 0)
			{
				return num;
			}
			num = CompareNumericIdentifier(Patch, other.Patch);
			if (num != 0)
			{
				return num;
			}
			if (Prerelease.Count == 0 || other.Prerelease.Count == 0)
			{
				if (Prerelease.Count != other.Prerelease.Count)
				{
					if (Prerelease.Count != 0)
					{
						return -1;
					}
					return 1;
				}
				return 0;
			}
			for (int i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
			{
				int num2 = ComparePrereleaseIdentifier(Prerelease[i], other.Prerelease[i]);
				if (num2 != 0)
				{
					return num2;
				}
			}
			return Prerelease.Count.CompareTo(other.Prerelease.Count);
		}

		private static int ComparePrereleaseIdentifier(string left, string right)
		{
			bool flag = left.All(char.IsAsciiDigit);
			bool flag2 = right.All(char.IsAsciiDigit);
			if (flag && flag2)
			{
				return CompareNumericIdentifier(left, right);
			}
			if (flag != flag2)
			{
				if (!flag)
				{
					return 1;
				}
				return -1;
			}
			return string.CompareOrdinal(left, right);
		}

		private static int CompareNumericIdentifier(string left, string right)
		{
			string text = left.TrimStart('0');
			string text2 = right.TrimStart('0');
			text = ((text.Length == 0) ? "0" : text);
			text2 = ((text2.Length == 0) ? "0" : text2);
			int num = text.Length.CompareTo(text2.Length);
			if (num == 0)
			{
				return string.CompareOrdinal(text, text2);
			}
			return num;
		}

		private static bool IsIdentifierList(string value)
		{
			string[] array = value.Split('.');
			if (array.Length != 0)
			{
				return array.All((string identifier) => identifier.Length > 0 && identifier.All((char character) => char.IsAsciiLetterOrDigit(character) || character == '-'));
			}
			return false;
		}

		private static bool IsNumericIdentifier(string value, bool allowLeadingZeroes)
		{
			if (value.Length > 0 && value.All(char.IsAsciiDigit))
			{
				if (!allowLeadingZeroes && value.Length != 1)
				{
					return value[0] != '0';
				}
				return true;
			}
			return false;
		}
	}

	private readonly RinoSemanticVersion _clientVersion;

	private readonly int _sidecarProtocolVersion;

	private readonly HashSet<int> _projectSchemaVersions;

	public RinoPackageCompatibilityPolicy(string clientVersion, int sidecarProtocolVersion, IEnumerable<int> projectSchemaVersions)
	{
		_clientVersion = RinoSemanticVersion.Parse(clientVersion, "clientVersion");
		if (sidecarProtocolVersion <= 0)
		{
			throw new ArgumentOutOfRangeException("sidecarProtocolVersion", "Sidecar protocol version must be positive.");
		}
		ArgumentNullException.ThrowIfNull(projectSchemaVersions, "projectSchemaVersions");
		_projectSchemaVersions = projectSchemaVersions.ToHashSet();
		if (_projectSchemaVersions.Count == 0 || _projectSchemaVersions.Any((int version) => version <= 0))
		{
			throw new ArgumentException("At least one positive project schema version is required.", "projectSchemaVersions");
		}
		_sidecarProtocolVersion = sidecarProtocolVersion;
	}

	public void EnsureCompatible(RinoPackageCompatibilityV1 compatibility)
	{
		ArgumentNullException.ThrowIfNull(compatibility, "compatibility");
		RinoSemanticVersion other = RinoSemanticVersion.Parse(compatibility.MinimumClientVersion, "compatibility");
		if (_clientVersion.CompareTo(other) < 0)
		{
			throw new RinoPackageValidationException("compatibility.clientVersion", "The package requires a newer client version.");
		}
		string maximumClientVersion = compatibility.MaximumClientVersion;
		if (maximumClientVersion != null && _clientVersion.CompareTo(RinoSemanticVersion.Parse(maximumClientVersion, "compatibility")) > 0)
		{
			throw new RinoPackageValidationException("compatibility.clientVersion", "The package does not support this client version.");
		}
		if (compatibility.SidecarProtocolVersion != _sidecarProtocolVersion)
		{
			throw new RinoPackageValidationException("compatibility.sidecarProtocol", "The package requires a different Sidecar protocol version.");
		}
		if (!compatibility.ProjectSchemaVersions.Any(_projectSchemaVersions.Contains))
		{
			throw new RinoPackageValidationException("compatibility.projectSchema", "The package does not support an available project schema version.");
		}
		if (!compatibility.OperatingSystem.Equals("windows", StringComparison.Ordinal) || !compatibility.Architectures.Contains<string>("x86_64", StringComparer.Ordinal))
		{
			throw new RinoPackageValidationException("compatibility.platform", "The package does not support the current Windows x86-64 platform.");
		}
	}
}
