using Kanstraction.Shared.Localization;

namespace Kanstraction.Presentation.Localization;

public sealed class ResourcePaymentReportLocalizer : IPaymentReportLocalizer
{
    public string Title => ResourceHelper.GetString("PaymentReportRenderer_Title", "Payment Resolution Report");

    public string FooterPage => ResourceHelper.GetString("PaymentReportRenderer_FooterPage", "Page ");

    public string FooterOf => ResourceHelper.GetString("PaymentReportRenderer_FooterOf", " of ");
}
