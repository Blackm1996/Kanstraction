using Kanstraction.OnSite.Application.Payments;

namespace Kanstraction.OnSite.Application.Abstractions;

public interface IPaymentReportRenderer
{
    Task RenderAsync(
        PaymentReportData report,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
