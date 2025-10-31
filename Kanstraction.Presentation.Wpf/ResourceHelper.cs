namespace Kanstraction;

public static class ResourceHelper
{
    public static string GetString(string key, string fallback)
    {
        return System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
    }
}
