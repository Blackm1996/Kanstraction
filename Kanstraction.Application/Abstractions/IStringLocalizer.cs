namespace Kanstraction.Application.Abstractions;

/// <summary>
/// Provides localized strings for infrastructure and application services without coupling to WPF resources.
/// </summary>
public interface IStringLocalizer
{
    string GetString(string key, string fallback);
}
