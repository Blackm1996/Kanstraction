using Kanstraction.OnSite.Application.Projects.Queries.ListProjects;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.Services;
using Kanstraction.OnSite.Application.Projects.Commands.CreateProject;
using Kanstraction.OnSite.Application.Projects.Commands.DeleteProject;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kanstraction.ViewModels;

public sealed class ProjectsExplorerViewModel : INotifyPropertyChanged
{
    private readonly IApplicationDispatcher _dispatcher;
    private readonly IOnSiteChangeFeed _changeFeed;
    private readonly IUserInteractionService? _interaction;
    private IReadOnlyList<ProjectListItemDto> _allProjects = [];
    private ProjectListItemDto? _selectedProject;
    private string _searchText = string.Empty;
    private bool _isBusy;
    private long _projectCatalogRevision;
    private long _allDataRevision;

    public ProjectsExplorerViewModel(IApplicationDispatcher dispatcher)
        : this(dispatcher, EmptyChangeFeed.Instance, null)
    {
    }

    public ProjectsExplorerViewModel(
        IApplicationDispatcher dispatcher,
        IOnSiteChangeFeed changeFeed)
        : this(dispatcher, changeFeed, null)
    {
    }

    public ProjectsExplorerViewModel(
        IApplicationDispatcher dispatcher,
        IOnSiteChangeFeed changeFeed,
        IUserInteractionService? interaction)
    {
        _dispatcher = dispatcher;
        _changeFeed = changeFeed;
        _interaction = interaction;
    }

    public ObservableCollection<ProjectListItemDto> Projects { get; } = [];

    public ProjectListItemDto? SelectedProject
    {
        get => _selectedProject;
        set => SetField(ref _selectedProject, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public async Task RefreshAsync(
        int? preferredProjectId = null,
        bool selectFirst = true,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var previousId = preferredProjectId ?? SelectedProject?.Id;
            _allProjects = await _dispatcher.Send(
                new ListProjectsQuery(),
                cancellationToken);
            ApplyFilter();
            SelectedProject = previousId.HasValue
                ? Projects.FirstOrDefault(project => project.Id == previousId)
                : null;
            if (SelectedProject == null && selectFirst)
                SelectedProject = Projects.FirstOrDefault();
            CaptureRevisions();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (_projectCatalogRevision ==
                _changeFeed.GetRevision(OnSiteChangeScope.ProjectCatalog) &&
            _allDataRevision ==
                _changeFeed.GetRevision(OnSiteChangeScope.AllData))
        {
            return;
        }

        await RefreshAsync(
            SelectedProject?.Id,
            selectFirst: true,
            cancellationToken);
    }

    public async Task<ProjectListItemDto?> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var interaction = RequireInteraction();
        var name = interaction.PromptText(
            ResourceHelper.GetString(
                "MainWindow_NewProjectDialogTitle",
                "New Project"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var result = await _dispatcher.Send(
            new CreateProjectCommand(name.Trim(), DateTime.Today),
            cancellationToken);
        if (result.IsFailure || result.Value == null)
        {
            interaction.Error(
                result.Error?.Message ??
                ResourceHelper.GetString(
                    "Common_UnknownError",
                    "The project could not be created."),
                ResourceHelper.GetString("Common_ErrorTitle", "Error"));
            return null;
        }

        await RefreshAsync(result.Value.Id, cancellationToken: cancellationToken);
        return Projects.FirstOrDefault(project => project.Id == result.Value.Id);
    }

    public async Task<bool> DeleteSelectedAsync(
        CancellationToken cancellationToken = default)
    {
        var interaction = RequireInteraction();
        var selectedProject = SelectedProject;
        if (selectedProject == null)
        {
            interaction.Information(
                ResourceHelper.GetString(
                    "MainWindow_RemoveProjectSelectFirst",
                    "Select a project to remove."),
                ResourceHelper.GetString(
                    "MainWindow_RemoveProjectTitle",
                    "Remove Project"));
            return false;
        }

        var confirmation = string.Format(
            CultureInfo.CurrentCulture,
            ResourceHelper.GetString(
                "MainWindow_RemoveProjectConfirmFormat",
                "Remove project \"{0}\"? This will delete all related buildings and data."),
            selectedProject.Name);
        if (!interaction.Confirm(
                confirmation,
                ResourceHelper.GetString(
                    "MainWindow_RemoveProjectTitle",
                    "Remove Project"),
                isWarning: true))
        {
            return false;
        }

        var result = await _dispatcher.Send(
            new DeleteProjectCommand(selectedProject.Id),
            cancellationToken);
        await RefreshAsync(cancellationToken: cancellationToken);
        return result.IsSuccess;
    }

    public void Clear()
    {
        _allProjects = [];
        Projects.Clear();
        SelectedProject = null;
    }

    private void CaptureRevisions()
    {
        _projectCatalogRevision = _changeFeed.GetRevision(
            OnSiteChangeScope.ProjectCatalog);
        _allDataRevision = _changeFeed.GetRevision(
            OnSiteChangeScope.AllData);
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allProjects
            : _allProjects
                .Where(project => project.Name.Contains(
                    SearchText.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        Projects.Clear();
        foreach (var project in filtered)
            Projects.Add(project);

        if (SelectedProject != null &&
            Projects.All(project => project.Id != SelectedProject.Id))
        {
            SelectedProject = Projects.FirstOrDefault();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private IUserInteractionService RequireInteraction() =>
        _interaction ?? throw new InvalidOperationException(
            "Project interactions are unavailable in this ViewModel instance.");

    private sealed class EmptyChangeFeed : IOnSiteChangeFeed
    {
        public static EmptyChangeFeed Instance { get; } = new();

        public long GetRevision(OnSiteChangeScope scope) => 0;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
