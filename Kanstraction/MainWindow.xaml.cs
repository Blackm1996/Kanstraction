using Kanstraction.OnSite.Application.Abstractions;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;
using Kanstraction.Views;
using Kanstraction.Services;
using Kanstraction.ViewModels;
using Kanstraction.Shell;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kanstraction;

public partial class MainWindow : Window
{
    private readonly IOnSiteChangePublisher _changePublisher;

    // Explorer sizing/toggle
    private double _explorerLastWidth = 220;
    private bool _explorerCollapsed;

    // Views
    private OperationsView _opsView = null!;
    private AdminHubView _adminView = null!;
    private BackupHubView _backupView = null!;

    private readonly ProjectsExplorerViewModel _projects;

    private bool _isRestoring;

    private DesktopSection _activeSection = null!;

    public ObservableCollection<DesktopSection> Sections { get; } = [];

    public MainWindow(
        IOnSiteChangePublisher changePublisher,
        ProjectsExplorerViewModel projects,
        IEnumerable<IDesktopModule> modules)
    {
        _changePublisher = changePublisher;
        _projects = projects;
        InitializeComponent();
        DataContext = _projects;

        BuildViews(modules);
        ActivateSection(Sections.First(section => section.Content is OperationsView));

        _backupView.StartAutomaticBackups();

        Loaded += OnLoaded;
    }


    private void BuildViews(IEnumerable<IDesktopModule> modules)
    {
        foreach (var section in modules.SelectMany(module => module.Sections))
            Sections.Add(section);

        _opsView = Sections.Select(section => section.Content)
            .OfType<OperationsView>()
            .Single();
        _adminView = Sections.Select(section => section.Content)
            .OfType<AdminHubView>()
            .Single();
        _backupView = Sections.Select(section => section.Content)
            .OfType<BackupHubView>()
            .Single();
        _backupView.ConfigureRestoreLifecycle(
            PrepareForRestoreAsync,
            FinalizeRestoreAsync);
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isRestoring)
            return;

        await _projects.RefreshAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _backupView.StopAutomaticBackups();
    }

    // ============ Explorer toggle ============
    private void ToggleExplorer()
    {
        if (!_activeSection.ShowProjectExplorer)
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

    private void ActivateSection(DesktopSection section)
    {
        _activeSection = section;
        MainContentHost.Content = section.Content;

        if (section.ShowProjectExplorer)
        {
            if (_explorerCollapsed)
            {
                ExplorerCol.Width = new GridLength(
                    _explorerLastWidth > 0 ? _explorerLastWidth : 220);
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
        else
        {
            CollapseExplorer();
        }
    }

    private async void SectionRail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DesktopSection section)
            return;

        ActivateSection(section);
        if (section.ActivateAsync != null)
            await section.ActivateAsync();
    }

    // ============ Projects search & selection ============
    private async void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoring || !_activeSection.ShowProjectExplorer)
            return;

        var p = ProjectsList.SelectedItem as ProjectListItemDto;
        if (p == null)
            return;

        await _opsView.ShowProject(p);
    }

    // ============ Top bar action stubs ============
    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_isRestoring)
            return;

        var project = await _projects.CreateAsync();
        if (project != null)
        {
            ProjectsList.SelectedItem = project;
            ActivateSection(Sections.First(section =>
                section.Content is OperationsView));
            await _opsView.ShowProject(project);
        }
    }

    private async void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_isRestoring)
            return;

        if (!await _projects.DeleteSelectedAsync())
            return;

        if (ProjectsList.SelectedItem is ProjectListItemDto project)
        {
            ActivateSection(Sections.First(section =>
                section.Content is OperationsView));
            await _opsView.ShowProject(project);
        }
        else
        {
            _opsView.ClearProject();
        }
    }

    private async Task RefreshProjectsList(bool ignoreRestoringGuard = false)
    {
        if (ProjectsList == null || (_isRestoring && !ignoreRestoringGuard)) return;

        var previouslySelectedId = _projects.SelectedProject?.Id;
        await _projects.RefreshAsync(
            previouslySelectedId,
            selectFirst: _activeSection.ShowProjectExplorer);

        if (!_activeSection.ShowProjectExplorer)
        {
            _projects.SelectedProject = null;
            return;
        }

        if (_projects.SelectedProject == null)
            _projects.SelectedProject = _projects.Projects.FirstOrDefault();
    }

    private Task PrepareForRestoreAsync()
    {
        _isRestoring = true;
        _backupView.SuspendAutomaticBackups();


        _projects.Clear();

        return Task.CompletedTask;
    }

    private async Task FinalizeRestoreAsync()
    {
        try
        {
            _opsView.ClearProject();
            await _changePublisher.PublishAsync(new OnSiteChange(
                OnSiteChangeScope.AllData,
                OnSiteChangeKind.Replaced));
            ActivateSection(_activeSection);
            await RefreshProjectsList(ignoreRestoringGuard: true);
            await _adminView.RefreshAfterDatabaseRestoreAsync();
        }
        finally
        {
            _backupView.StartAutomaticBackups();
            _isRestoring = false;
        }
    }
}
