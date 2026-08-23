namespace Kanstraction.OnSite.Application.Common.Changes;

public enum OnSiteChangeScope
{
    ProjectCatalog,
    MaterialCategories,
    Materials,
    StagePresets,
    BuildingTypes,
    ProjectWork,
    AllData
}

public enum OnSiteChangeKind
{
    Created,
    Updated,
    Deleted,
    Replaced
}

public sealed record OnSiteChange(
    OnSiteChangeScope Scope,
    OnSiteChangeKind Kind,
    int? ProjectId = null,
    int? BuildingId = null,
    int? StageId = null,
    int? SubStageId = null,
    int? EntityId = null);
