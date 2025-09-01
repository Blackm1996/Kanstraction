using Kanstraction.Data;
using Kanstraction.Entities;
using Kanstraction.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kanstraction;

public partial class MainWindow : Window
{
    private readonly AppDbContext _db = new AppDbContext();

    // Explorer sizing/toggle
    private double _explorerLastWidth = 220;
    private bool _explorerCollapsed = false;

    // Views
    private OperationsView _opsView = new OperationsView();
    private AdminHubView _adminView = new AdminHubView();

    // Projects cache
    private List<Project> _allProjects = new();

    public MainWindow()
    {
        InitializeComponent();

        // Prepare views
        _opsView.SetDb(_db);
        _adminView.SetDb(_db);

        // Default to Operations view
        MainContentHost.Content = _opsView;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Load projects
        _allProjects = await _db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        ProjectsList.ItemsSource = _allProjects;

        // Auto-select first project (if any)
        if (ProjectsList.Items.Count > 0)
            ProjectsList.SelectedIndex = 0;
    }

    // ============ Explorer toggle ============
    private void ToggleExplorer()
    {
        // Only meaningful in Operations mode
        if (MainContentHost.Content == _adminView) return;

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

    // ============ Left rail buttons ============
    private void ProjectsRail_Click(object sender, RoutedEventArgs e)
    {
        // Switch to operations view, restore explorer
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

        // If nothing is selected, select first
        if (ProjectsList.Items.Count > 0 && ProjectsList.SelectedIndex < 0)
            ProjectsList.SelectedIndex = 0;
    }

    private void AdminRail_Click(object sender, RoutedEventArgs e)
    {
        // Switch to admin view, hide explorer
        MainContentHost.Content = _adminView;
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

    // ============ Projects search & selection ============
    private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
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

        // Auto-select first result (if any)
        if (ProjectsList.Items.Count > 0)
            ProjectsList.SelectedIndex = 0;
    }

    private async void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainContentHost.Content != _opsView) return; // ignore when in Admin

        var p = ProjectsList.SelectedItem as Project;
        if (p == null) return;

        await _opsView.ShowProject(p);
    }

    // ============ Top bar action stubs ============
    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null) return;

        var dlg = new PromptTextDialog("New Project") { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var p = new Project
        {
            Name = dlg.Value!,
            StartDate = System.DateTime.Today
        };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync();

        // reload list and select the new project
        _allProjects = await _db.Projects.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        await RefreshProjectsList();
        var row = _allProjects.FirstOrDefault(x => x.Id == p.Id);
        if (row != null)
        {
            ProjectsList.SelectedItem = row;
            // Show in operations view (if not already)
            ProjectsRail_Click(null, null);
            // Optionally call into OperationsView to show project
            _opsView?.ShowProject(row);
        }
    }

    private void Reporting_Click(object sender, RoutedEventArgs e)
    {
        if (MainContentHost.Content != _opsView)
            MessageBox.Show("Switch to Projects to run reports tied to a project selection.", "Reporting");
        else
            MessageBox.Show("TODO: Open reporting wizard / monthly summary.", "Reporting");
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
                File.WriteAllText(dlg.FileName, "This is a placeholder export.\n");
                MessageBox.Show($"Exported to:\n{dlg.FileName}", "Export",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed:\n" + ex.Message, "Export",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshProjectsList()
    {
        if (_db == null || ProjectsList == null) return;

        _allProjects = await _db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();

        ProjectsList.ItemsSource = _allProjects;

        // keep selection or select first
        if (_allProjects.Count > 0)
        {
            if (ProjectsList.SelectedItem == null)
                ProjectsList.SelectedIndex = 0;
        }
    }
}
