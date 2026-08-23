using Kanstraction.OnSite.Application.Abstractions;

namespace Kanstraction.OnSite.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime Today => DateTime.Today;
    public DateTime Now => DateTime.Now;
}
