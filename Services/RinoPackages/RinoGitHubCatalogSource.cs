using System;
using System.Text.RegularExpressions;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoGitHubCatalogSource
{
	private static readonly Regex GitHubNamePattern = new Regex("^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100.0));

	public string Owner { get; }

	public string Repository { get; }

	public RinoGitHubCatalogSource(string owner, string repository)
	{
		if (!GitHubNamePattern.IsMatch(owner) || !GitHubNamePattern.IsMatch(repository))
		{
			throw new ArgumentException("GitHub owner and repository names are invalid.");
		}
		Owner = owner;
		Repository = repository;
	}
}
