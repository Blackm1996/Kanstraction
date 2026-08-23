using Kanstraction.Shell;
using Kanstraction.ViewModels;
using Kanstraction.Views;

namespace Kanstraction.Desktop;

public sealed class OnSiteDesktopModule : IDesktopModule
{
    public OnSiteDesktopModule(
        OperationsView operations,
        AdminHubView administration,
        BackupHubView backups,
        ProjectsExplorerViewModel projects)
    {
        Sections =
        [
            new DesktopSection(
                "onsite.operations",
                ResourceHelper.GetString("MainWindow_Projects", "Projects"),
                "📁",
                operations,
                ShowProjectExplorer: true,
                async () =>
                {
                    await projects.ActivateAsync();
                    await operations.ActivateAsync();
                }),
            new DesktopSection(
                "onsite.administration",
                ResourceHelper.GetString("MainWindow_Admin", "Administration"),
                "⚙️",
                administration,
                ShowProjectExplorer: false,
                administration.ActivateAsync),
            new DesktopSection(
                "onsite.backups",
                ResourceHelper.GetString("MainWindow_Backups", "Backups"),
                "💾",
                backups,
                ShowProjectExplorer: false)
        ];
    }

    public string Id => "onsite";

    public IReadOnlyList<DesktopSection> Sections { get; }
}
