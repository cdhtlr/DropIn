namespace DropIn.Common;

/// <summary>Resolved executable and optional environment settings for a package-manager process.</summary>
/// <param name="FileName">Path to the package-manager executable.</param>
/// <param name="WorkingDirectory">Optional working directory for the process.</param>
/// <param name="ChocolateyInstall">Optional Chocolatey installation root to expose to the process.</param>
public sealed record PackageManagerCommand(string FileName, string? WorkingDirectory = null, string? ChocolateyInstall = null);
