namespace DropIn.Common;

/// <summary>Outcome and user-facing message produced by an installation operation.</summary>
/// <param name="Success">Whether the requested installation completed successfully.</param>
/// <param name="Message">Description of the outcome.</param>
/// <param name="InstallDirectory">Installed package directory, when one is known.</param>
/// <param name="Portable">Whether the files were extracted or copied by DropIn (portable/zip) rather than installed by a setup program.</param>
public sealed record InstallResult(bool Success, string Message, string? InstallDirectory = null, bool Portable = false);
