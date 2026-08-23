using System.Windows;

namespace Kanstraction.Shell;

public sealed record DesktopSection(
    string Id,
    string Title,
    string Icon,
    FrameworkElement Content,
    bool ShowProjectExplorer,
    Func<Task>? ActivateAsync = null);

public interface IDesktopModule
{
    string Id { get; }
    IReadOnlyList<DesktopSection> Sections { get; }
}
