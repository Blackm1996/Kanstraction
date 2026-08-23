namespace Kanstraction.OnSite.Application.Stages.Commands.Common;

public sealed record WorkMutationResult(int BuildingId, int StageId, int? SubStageId = null);
