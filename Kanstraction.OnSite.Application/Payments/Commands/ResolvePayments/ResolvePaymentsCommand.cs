using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;

namespace Kanstraction.OnSite.Application.Payments.Commands.ResolvePayments;

public sealed record ResolvePaymentsCommand(
    int ProjectId,
    string ProjectName,
    string DestinationPath) : IStateChangingRequest<Result<PaymentResolutionResult>>
{
    public IReadOnlyList<OnSiteChange> GetChanges(Result<PaymentResolutionResult> response)
        => [new(
            OnSiteChangeScope.ProjectWork,
            OnSiteChangeKind.Updated,
            ProjectId: ProjectId,
            EntityId: ProjectId)];
}

public sealed record PaymentResolutionResult(int ResolvedSubStageCount, decimal GrandTotal);
