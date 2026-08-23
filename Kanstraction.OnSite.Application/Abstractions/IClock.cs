namespace Kanstraction.OnSite.Application.Abstractions;

public interface IClock
{
    DateTime Today { get; }
    DateTime Now { get; }
}
