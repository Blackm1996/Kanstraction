using Kanstraction.Application.Abstractions;

namespace Kanstraction.Application.Localization;

/// <summary>
/// Provides a centralized localization gateway for non-UI layers.
/// </summary>
public static class StringLocalizer
{
    private static IStringLocalizer _provider = new PassthroughStringLocalizer();

    public static void Configure(IStringLocalizer provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static string GetString(string key, string fallback)
    {
        return _provider.GetString(key, fallback);
    }

    private sealed class PassthroughStringLocalizer : IStringLocalizer
    {
        public string GetString(string key, string fallback) => fallback;
    }
}
