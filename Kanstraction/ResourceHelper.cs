using System.Windows;

namespace Kanstraction;

public static class ResourceHelper
{
    public static string GetString(string key, string fallback)
    {
        return Application.Current?.TryFindResource(key) as string ?? fallback;
    }
}
