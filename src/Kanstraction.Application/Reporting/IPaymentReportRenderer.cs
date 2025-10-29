namespace Kanstraction.Application.Reporting;

public interface IPaymentReportRenderer
{
    void Render(PaymentReportData data, string filePath);
}
