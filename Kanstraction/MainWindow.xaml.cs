using Kanstraction.Application.Abstractions;
using Kanstraction.Infrastructure.Data;
using Kanstraction.Domain.Entities;
using Kanstraction.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Kanstraction;

public partial class MainWindow : Window
{
    private AppDbContext? _db;

    // Explorer sizing/toggle
    private double _explorerLastWidth = 220;
    private bool _explorerCollapsed;

    // Views
    private OperationsView _opsView = null!;
    private AdminHubView _adminView = null!;
    private BackupHubView _backupView = null!;

    // Projects cache
    private List<Project> _allProjects = new();

    private DispatcherTimer? _hourlyBackupTimer;
    private bool _isRestoring;

    private ActiveView _activeView = ActiveView.Operations;

    private enum ActiveView
    {
        Operations,
        Admin,
        Backup
    }

    public MainWindow()
    {
        InitializeComponent();

        InitializeDataLayer();
        BuildViews();
        ActivateOperationsView();

        _hourlyBackupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(1)
        };
        _hourlyBackupTimer.Tick += HourlyBackupTimer_Tick;
        _hourlyBackupTimer.Start();

        Loaded += OnLoaded;
    }

    private void InitializeDataLayer()
    {
        _db?.Dispose();
        _db = new AppDbContext();
    }

    private void BuildViews()
    {
        _opsView = new OperationsView();
        if (_db != null)
            _opsView.SetDb(_db);

        _adminView = new AdminHubView();
        if (_db != null)
            _adminView.SetDb(_db);

        _backupView = new BackupHubView();
        if (App.BackupService != null)
            _backupView.Initialize(App.BackupService, PrepareForRestoreAsync, FinalizeRestoreAsync);
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_db == null || _isRestoring)
            return;

        _allProjects = await _db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        ProjectsList.ItemsSource = _allProjects;

        if (ProjectsList.Items.Count > 0)
            ProjectsList.SelectedIndex = 0;
    }

    private async void HourlyBackupTimer_Tick(object? sender, EventArgs e)
    {
        if (_isRestoring || App.BackupService == null)
            return;

        try
        {
            await App.BackupService.CreateHourlyBackupAsync();
        }
        catch
        {
            // Ignore automatic backup failures to avoid disrupting the user
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_hourlyBackupTimer != null)
        {
            _hourlyBackupTimer.Stop();
            _hourlyBackupTimer.Tick -= HourlyBackupTimer_Tick;
            _hourlyBackupTimer = null;
        }

        _db?.Dispose();
        _db = null;
    }

    // ============ Explorer toggle ============
    private void ToggleExplorer()
    {
        if (_activeView != ActiveView.Operations)
            return;

        if (_explorerCollapsed)
        {
            ExplorerCol.Width = new GridLength(_explorerLastWidth > 0 ? _explorerLastWidth : 220);
            _explorerCollapsed = false;
        }
        else
        {
            _explorerLastWidth = ExplorerCol.ActualWidth > 0 ? ExplorerCol.ActualWidth : 220;
            ExplorerCol.Width = new GridLength(0);
            _explorerCollapsed = true;
        }
    }

    private void CollapseExplorer()
    {
        if (!_explorerCollapsed)
        {
            _explorerLastWidth = ExplorerCol.ActualWidth > 0 ? ExplorerCol.ActualWidth : 220;
            ExplorerCol.Width = new GridLength(0);
            _explorerCollapsed = true;
        }

        SplitterCol.Width = new GridLength(0);
        ExplorerSplitter.Visibility = Visibility.Collapsed;
        ExplorerSplitter.IsEnabled = false;
        ExplorerSplitter.IsHitTestVisible = false;
        ExplorerSplitter.Cursor = Cursors.Arrow;
    }

    private void ActivateOperationsView()
    {
        _activeView = ActiveView.Operations;
        MainContentHost.Content = _opsView;

        if (_explorerCollapsed)
        {
            ExplorerCol.Width = new GridLength(_explorerLastWidth > 0 ? _explorerLastWidth : 220);
            _explorerCollapsed = false;
        }

        SplitterCol.Width = new GridLength(6);
        ExplorerSplitter.Visibility = Visibility.Visible;
        ExplorerSplitter.IsEnabled = true;
        ExplorerSplitter.IsHitTestVisible = true;
        ExplorerSplitter.Cursor = Cursors.SizeWE;

        if (ProjectsList.Items.Count > 0 && ProjectsList.SelectedIndex < 0)
            ProjectsList.SelectedIndex = 0;
    }

    private void ActivateAdminView()
    {
        _activeView = ActiveView.Admin;
        MainContentHost.Content = _adminView;
        CollapseExplorer();
    }

    private void ActivateBackupView()
    {
        _activeView = ActiveView.Backup;
        MainContentHost.Content = _backupView;
        CollapseExplorer();
    }

    private void ShowActiveView()
    {
        switch (_activeView)
        {
            case ActiveView.Operations:
                ActivateOperationsView();
                break;
            case ActiveView.Admin:
                ActivateAdminView();
                break;
            case ActiveView.Backup:
                ActivateBackupView();
                break;
        }
    }

    // ============ Left rail buttons ============
    private void ProjectsRail_Click(object sender, RoutedEventArgs e)
    {
        ActivateOperationsView();
    }

    private void AdminRail_Click(object sender, RoutedEventArgs e)
    {
        ActivateAdminView();
    }

    private void BackupsRail_Click(object sender, RoutedEventArgs e)
    {
        ActivateBackupView();
    }

    // ============ Projects search & selection ============
    private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRestoring)
            return;

        var q = ProjectSearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            ProjectsList.ItemsSource = _allProjects;
        }
        else
        {
            ProjectsList.ItemsSource = _allProjects
                .Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (ProjectsList.Items.Count > 0)
            ProjectsList.SelectedIndex = 0;
    }

    private async void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_db == null || _isRestoring || _activeView != ActiveView.Operations)
            return;

        var p = ProjectsList.SelectedItem as Project;
        if (p == null)
            return;

        await _opsView.ShowProject(p);
    }

    // ============ Top bar action stubs ============
    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _isRestoring)
            return;

        var dlg = new PromptTextDialog(ResourceHelper.GetString("MainWindow_NewProjectDialogTitle", "New Project"))
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true) return;

        var p = new Project
        {
            Name = dlg.Value!,
            StartDate = DateTime.Today
        };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync();

        _allProjects = await _db.Projects.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        await RefreshProjectsList();
        var row = _allProjects.FirstOrDefault(x => x.Id == p.Id);
        if (row != null)
        {
            ProjectsList.SelectedItem = row;
            ActivateOperationsView();
            await _opsView.ShowProject(row);
        }
    }

    private void Reporting_Click(object sender, RoutedEventArgs e)
    {
        if (_activeView != ActiveView.Operations)
            MessageBox.Show("Passez à Projets pour exécuter des rapports liés à une sélection de projet.", "Rapports");
        else
            MessageBox.Show("TODO : ouvrir l'assistant de rapports / résumé mensuel.", "Rapports");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export (placeholder)",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "export.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(
                    dlg.FileName,
                    ResourceHelper.GetString("MainWindow_ExportPlaceholderContent", "This is a placeholder export.\n"));
                MessageBox.Show(
                    string.Format(ResourceHelper.GetString("MainWindow_ExportedToFormat", "Exported to:\n{0}"), dlg.FileName),
                    ResourceHelper.GetString("MainWindow_ExportTitle", "Export"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(ResourceHelper.GetString("MainWindow_ExportFailedFormat", "Export failed:\n{0}"), ex.Message),
                ResourceHelper.GetString("MainWindow_ExportTitle", "Export"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RefreshProjectsList(bool ignoreRestoringGuard = false)
    {
        if (_db == null || ProjectsList == null || (_isRestoring && !ignoreRestoringGuard)) return;

        var previouslySelectedId = (ProjectsList.SelectedItem as Project)?.Id;

        _allProjects = await _db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();

        ProjectsList.ItemsSource = _allProjects;

        if (_activeView != ActiveView.Operations)
        {
            ProjectsList.SelectedItem = null;
            ProjectsList.SelectedIndex = -1;
            return;
        }

        if (_allProjects.Count == 0)
        {
            ProjectsList.SelectedItem = null;
            ProjectsList.SelectedIndex = -1;
            return;
        }

        if (previouslySelectedId.HasValue)
        {
            var match = _allProjects.FirstOrDefault(p => p.Id == previouslySelectedId.Value);
            if (match != null)
            {
                ProjectsList.SelectedItem = match;
                return;
            }
        }

        if (ProjectsList.SelectedItem == null)
            ProjectsList.SelectedIndex = 0;
    }

    private Task PrepareForRestoreAsync()
    {
        _isRestoring = true;
        _hourlyBackupTimer?.Stop();

        _db?.Dispose();
        _db = null;

        _allProjects.Clear();
        ProjectsList.ItemsSource = null;
        ProjectsList.SelectedItem = null;
        ProjectsList.SelectedIndex = -1;

        return Task.CompletedTask;
    }

    private async Task FinalizeRestoreAsync()
    {
        try
        {
            InitializeDataLayer();
            BuildViews();
            ShowActiveView();
            await RefreshProjectsList(ignoreRestoringGuard: true);
        }
        finally
        {
            _hourlyBackupTimer?.Start();
            _isRestoring = false;
        }
    }
}
