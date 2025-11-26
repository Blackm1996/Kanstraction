using Kanstraction.Application.Abstractions;
using Microsoft.Win32;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Kanstraction.Views;

public partial class BackupHubView : UserControl
{
    private IBackupService? _backupService;
    private Func<Task>? _onBeforeRestoreAsync;
    private Func<Task>? _onAfterRestoreAsync;

    public BackupHubView()
    {
        InitializeComponent();
    }

    public void Initialize(IBackupService backupService, Func<Task> onBeforeRestoreAsync, Func<Task> onAfterRestoreAsync)
    {
        _backupService = backupService;
        _onBeforeRestoreAsync = onBeforeRestoreAsync;
        _onAfterRestoreAsync = onAfterRestoreAsync;
    }

    private async void ManualBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_backupService == null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = ResourceHelper.GetString("BackupHubView_SaveDialogTitle", "Save backup"),
            Filter = ResourceHelper.GetString("BackupHubView_SaveDialogFilter", "Kanstraction Backup (*.db)|*.db|All files (*.*)|*.*"),
            FileName = $"KanstractionBackup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _backupService.CreateManualBackupAsync(dialog.FileName);

                MessageBox.Show(
                    string.Format(CultureInfo.CurrentCulture,
                        ResourceHelper.GetString("BackupHubView_SaveSuccessFormat", "Backup saved to:\n{0}"), dialog.FileName),
                    ResourceHelper.GetString("Common_SuccessTitle", "Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(CultureInfo.CurrentCulture,
                        ResourceHelper.GetString("BackupHubView_SaveFailureFormat", "Backup failed:\n{0}"), ex.Message),
                    ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_backupService == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = ResourceHelper.GetString("BackupHubView_RestoreDialogTitle", "Open backup"),
            Filter = ResourceHelper.GetString("BackupHubView_RestoreDialogFilter", "Kanstraction Backup (*.db)|*.db|All files (*.*)|*.*"),
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var confirm = MessageBox.Show(
            ResourceHelper.GetString("BackupHubView_RestoreConfirmMessage", "Restore from this backup? Current data will be replaced."),
            ResourceHelper.GetString("BackupHubView_RestoreConfirmTitle", "Restore"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        string? failureMessage = null;
        var restoreAttempted = false;
        var restoreSucceeded = false;

        try
        {
            if (_onBeforeRestoreAsync != null)
                await _onBeforeRestoreAsync();

            restoreAttempted = true;

            await _backupService.RestoreBackupAsync(dialog.FileName);
            restoreSucceeded = true;
        }
        catch (Exception ex)
        {
            var key = restoreAttempted
                ? "BackupHubView_RestoreFailureFormat"
                : "BackupHubView_RestorePrepareFailedFormat";

            failureMessage = string.Format(
                CultureInfo.CurrentCulture,
                ResourceHelper.GetString(key, restoreAttempted ? "Restore failed:\n{0}" : "Failed to prepare for restore:\n{0}"),
                ex.Message);
        }
        finally
        {
            if (_onAfterRestoreAsync != null)
            {
                try
                {
                    await _onAfterRestoreAsync();
                }
                catch (Exception finalEx)
                {
                    failureMessage ??= string.Format(
                        CultureInfo.CurrentCulture,
                        ResourceHelper.GetString("BackupHubView_RestoreReloadFailedFormat", "Failed to reload after restore:\n{0}"),
                        finalEx.Message);
                    restoreSucceeded = false;
                }
            }

            if (failureMessage != null)
            {
                MessageBox.Show(
                    failureMessage,
                    ResourceHelper.GetString("Common_ErrorTitle", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else if (restoreSucceeded)
            {
                MessageBox.Show(
                    ResourceHelper.GetString("BackupHubView_RestoreSuccess", "Backup restored successfully."),
                    ResourceHelper.GetString("Common_SuccessTitle", "Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
