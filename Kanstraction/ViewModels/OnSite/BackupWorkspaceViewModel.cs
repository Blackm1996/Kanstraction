using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.Services;
using Kanstraction.ViewModels.Commands;
using System.Globalization;
using System.Windows.Threading;

namespace Kanstraction.ViewModels.OnSite;

public sealed class BackupWorkspaceViewModel : ObservableObject
{
    private readonly IBackupService _backupService;
    private readonly IUserInteractionService _interaction;
    private Func<Task>? _beforeRestoreAsync;
    private Func<Task>? _afterRestoreAsync;
    private DispatcherTimer? _automaticBackupTimer;
    private bool _isBusy;

    public BackupWorkspaceViewModel(
        IBackupService backupService,
        IUserInteractionService interaction)
    {
        _backupService = backupService;
        _interaction = interaction;
        CreateManualBackupCommand = new AsyncCommand(
            (_, _) => CreateManualBackupAsync(),
            _ => !IsBusy);
        RestoreBackupCommand = new AsyncCommand(
            (_, _) => RestoreBackupAsync(),
            _ => !IsBusy);
    }

    public AsyncCommand CreateManualBackupCommand { get; }
    public AsyncCommand RestoreBackupCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            CreateManualBackupCommand.NotifyCanExecuteChanged();
            RestoreBackupCommand.NotifyCanExecuteChanged();
        }
    }

    public void ConfigureRestoreLifecycle(
        Func<Task> beforeRestoreAsync,
        Func<Task> afterRestoreAsync)
    {
        _beforeRestoreAsync = beforeRestoreAsync;
        _afterRestoreAsync = afterRestoreAsync;
    }

    public void StartAutomaticBackups()
    {
        if (_automaticBackupTimer == null)
        {
            _automaticBackupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(1)
            };
            _automaticBackupTimer.Tick += AutomaticBackupTimerTick;
        }

        _automaticBackupTimer.Start();
    }

    public void SuspendAutomaticBackups() =>
        _automaticBackupTimer?.Stop();

    public void StopAutomaticBackups()
    {
        if (_automaticBackupTimer == null)
        {
            return;
        }

        _automaticBackupTimer.Stop();
        _automaticBackupTimer.Tick -= AutomaticBackupTimerTick;
        _automaticBackupTimer = null;
    }

    private async void AutomaticBackupTimerTick(object? sender, EventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            await _backupService.CreateHourlyBackupAsync();
        }
        catch
        {
            // Automatic backups are best-effort and must not interrupt the user.
        }
    }

    private async Task CreateManualBackupAsync()
    {
        var path = _interaction.SelectSavePath(
            ResourceHelper.GetString(
                "BackupHubView_SaveDialogTitle",
                "Save backup"),
            ResourceHelper.GetString(
                "BackupHubView_SaveDialogFilter",
                "Kanstraction Backup (*.db)|*.db|All files (*.*)|*.*"),
            $"KanstractionBackup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        if (path == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _backupService.CreateManualBackupAsync(path);
            _interaction.Information(
                string.Format(
                    CultureInfo.CurrentCulture,
                    ResourceHelper.GetString(
                        "BackupHubView_SaveSuccessFormat",
                        "Backup saved to:\n{0}"),
                    path),
                ResourceHelper.GetString("Common_SuccessTitle", "Success"));
        }
        catch (Exception exception)
        {
            _interaction.Error(
                string.Format(
                    CultureInfo.CurrentCulture,
                    ResourceHelper.GetString(
                        "BackupHubView_SaveFailureFormat",
                        "Backup failed:\n{0}"),
                    exception.Message),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreBackupAsync()
    {
        var path = _interaction.SelectOpenPath(
            ResourceHelper.GetString(
                "BackupHubView_RestoreDialogTitle",
                "Open backup"),
            ResourceHelper.GetString(
                "BackupHubView_RestoreDialogFilter",
                "Kanstraction Backup (*.db)|*.db|All files (*.*)|*.*"));
        if (path == null ||
            !_interaction.Confirm(
                ResourceHelper.GetString(
                    "BackupHubView_RestoreConfirmMessage",
                    "Restore from this backup? Current data will be replaced."),
                ResourceHelper.GetString(
                    "BackupHubView_RestoreConfirmTitle",
                    "Restore"),
                isWarning: true))
        {
            return;
        }

        IsBusy = true;
        string? failureMessage = null;
        var restoreAttempted = false;
        var restoreSucceeded = false;
        try
        {
            if (_beforeRestoreAsync != null)
            {
                await _beforeRestoreAsync();
            }

            restoreAttempted = true;
            await _backupService.RestoreBackupAsync(path);
            restoreSucceeded = true;
        }
        catch (Exception exception)
        {
            var key = restoreAttempted
                ? "BackupHubView_RestoreFailureFormat"
                : "BackupHubView_RestorePrepareFailedFormat";
            failureMessage = string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString(
                    key,
                    restoreAttempted
                        ? "Restore failed:\n{0}"
                        : "Failed to prepare for restore:\n{0}"),
                exception.Message);
        }
        finally
        {
            if (_afterRestoreAsync != null)
            {
                try
                {
                    await _afterRestoreAsync();
                }
                catch (Exception exception)
                {
                    failureMessage ??= string.Format(
                        CultureInfo.CurrentCulture,
                        ResourceHelper.GetString(
                            "BackupHubView_RestoreReloadFailedFormat",
                            "Failed to reload after restore:\n{0}"),
                        exception.Message);
                    restoreSucceeded = false;
                }
            }

            if (failureMessage != null)
            {
                _interaction.Error(
                    failureMessage,
                    ResourceHelper.GetString("Common_ErrorTitle", "Error"));
            }
            else if (restoreSucceeded)
            {
                _interaction.Information(
                    ResourceHelper.GetString(
                        "BackupHubView_RestoreSuccess",
                        "Backup restored successfully."),
                    ResourceHelper.GetString("Common_SuccessTitle", "Success"));
            }

            IsBusy = false;
        }
    }
}
