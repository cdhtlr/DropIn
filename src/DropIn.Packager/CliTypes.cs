namespace DropIn.Packager;

/// <summary>Forwards progress reports directly to a supplied callback.</summary>
sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    /// <summary>Forwards a progress value to the callback provided at construction.</summary>
    public void Report(T value) => report(value);
}

/// <summary>Represents a package input and its optional source and version overrides.</summary>
sealed record PackageRequest(string Input, string? Source = null, string? Version = null);
