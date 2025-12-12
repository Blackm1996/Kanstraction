using Kanstraction.Application.Abstractions;

namespace Kanstraction.Localization;

public sealed class ResourceDictionaryStringLocalizer : IStringLocalizer
{
    public string GetString(string key, string fallback)
    {
        return ResourceHelper.GetString(key, fallback);
    }
}
