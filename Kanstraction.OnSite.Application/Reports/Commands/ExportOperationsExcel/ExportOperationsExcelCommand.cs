using Kanstraction.OnSite.Application.Common;
using Kanstraction.OnSite.Application.Common.Changes;
using Kanstraction.OnSite.Application.Reports.Models;

namespace Kanstraction.OnSite.Application.Reports.Commands.ExportOperationsExcel;

public sealed record ExportOperationsExcelCommand(
    OperationsExcelReport Report,
    string DestinationPath) : INonMutatingRequest<Result>;
