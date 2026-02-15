using Kanstraction.Domain.Entities;
using MediatR;

namespace Kanstraction.Application.Stages.Commands.ChangeStageStatus;

public sealed record ChangeStageStatusCommand(int StageId, WorkStatus NewStatus) : IRequest;
