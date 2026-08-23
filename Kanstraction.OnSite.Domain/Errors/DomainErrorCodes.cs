namespace Kanstraction.OnSite.Domain.Errors;

public static class DomainErrorCodes
{
    public const string BuildingNotFound = "building.not_found";
    public const string StageNotFound = "stage.not_found";
    public const string SubStageNotFound = "sub_stage.not_found";
    public const string AnotherSubStageIsOngoing = "sub_stage.another_is_ongoing";
    public const string PreviousSubStageIncomplete = "sub_stage.previous_incomplete";
    public const string PreviousStageIncomplete = "stage.previous_incomplete";
    public const string InvalidSubStageTransition = "sub_stage.invalid_transition";
    public const string LaborCostRequired = "sub_stage.labor_cost_required";
    public const string LaborCostNegative = "sub_stage.labor_cost_negative";
    public const string DuplicateBuildingCode = "building.duplicate_code";
    public const string BuildingCodeRequired = "building.code_required";
    public const string MaterialQuantityNegative = "material_usage.quantity_negative";
    public const string MaterialUsageNotFound = "material_usage.not_found";
    public const string DuplicateMaterialUsage = "material_usage.duplicate";
    public const string StageHasNoSubStages = "stage.has_no_sub_stages";
    public const string StageDoesNotBelongToBuilding = "stage.does_not_belong_to_building";
}
