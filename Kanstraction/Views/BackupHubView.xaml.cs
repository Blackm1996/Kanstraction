using Kanstraction.ViewModels.OnSite;
using System.Windows.Controls;

namespace Kanstraction.Views;

public partial class BackupHubView : UserControl
{
    private readonly BackupWorkspaceViewModel _viewModel;

    public BackupHubView(BackupWorkspaceViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public void ConfigureRestoreLifecycle(
        Func<Task> beforeRestoreAsync,
        Func<Task> afterRestoreAsync) =>
        _viewModel.ConfigureRestoreLifecycle(
            beforeRestoreAsync,
            afterRestoreAsync);

    public void StartAutomaticBackups() =>
        _viewModel.StartAutomaticBackups();

    public void SuspendAutomaticBackups() =>
        _viewModel.SuspendAutomaticBackups();

    public void StopAutomaticBackups() =>
        _viewModel.StopAutomaticBackups();
}
