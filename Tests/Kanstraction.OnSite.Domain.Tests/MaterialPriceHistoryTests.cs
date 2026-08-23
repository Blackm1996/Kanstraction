using Kanstraction.OnSite.Domain.Entities;
using Xunit;

namespace Kanstraction.OnSite.Domain.Tests;

public sealed class MaterialPriceHistoryTests
{
    [Fact]
    public void Changing_price_on_new_effective_date_closes_previous_period()
    {
        var material = Material.Create(
            "Cement",
            "bag",
            10m,
            new DateTime(2026, 1, 1),
            true,
            1);

        material.Update(
            "Cement",
            "bag",
            12m,
            new DateTime(2026, 2, 1),
            true,
            1);

        var periods = material.PriceHistory.OrderBy(period => period.StartDate).ToList();
        Assert.Equal(2, periods.Count);
        Assert.Equal(new DateTime(2026, 1, 31), periods[0].EndDate);
        Assert.Null(periods[1].EndDate);
        Assert.Equal(12m, periods[1].PricePerUnit);
    }

    [Fact]
    public void Changing_price_without_changing_date_updates_open_period()
    {
        var date = new DateTime(2026, 1, 1);
        var material = Material.Create("Cement", "bag", 10m, date, true, 1);

        material.Update("Cement", "bag", 11m, date, true, 1);

        var period = Assert.Single(material.PriceHistory);
        Assert.Equal(11m, period.PricePerUnit);
        Assert.Null(period.EndDate);
    }
}
