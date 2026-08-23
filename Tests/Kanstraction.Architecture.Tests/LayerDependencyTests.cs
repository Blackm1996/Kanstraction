using System.Reflection;
using Kanstraction.OnSite.Application.Common.Changes;
using Xunit;

namespace Kanstraction.Architecture.Tests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_HasNoFrameworkOrOuterLayerDependencies()
    {
        var references = ReferencesOf(typeof(Kanstraction.OnSite.Domain.Entities.Building).Assembly);

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(references, name => name.StartsWith("PresentationFramework"));
        Assert.DoesNotContain(references, name => name.StartsWith("MediatR"));
        Assert.DoesNotContain(references, name => name.Contains("Application"));
        Assert.DoesNotContain(references, name => name.Contains("Infrastructure"));
    }

    [Fact]
    public void Application_HasNoInfrastructureOrPresentationDependencies()
    {
        var references = ReferencesOf(typeof(Kanstraction.OnSite.Application.Common.Result).Assembly);

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(references, name => name.StartsWith("PresentationFramework"));
        Assert.DoesNotContain(references, name => name.Contains("Infrastructure"));
        Assert.DoesNotContain(references, name => name.Contains("Presentation.Wpf"));
    }

    [Fact]
    public void Infrastructure_DependsOnOnSiteInnerLayers()
    {
        var references = ReferencesOf(typeof(Kanstraction.OnSite.Infrastructure.Data.OnSiteDbContext).Assembly);

        Assert.Contains("Kanstraction.OnSite.Domain", references);
        Assert.Contains("Kanstraction.OnSite.Application", references);
    }

    [Fact]
    public void Presentation_DependsOnlyOnApplication()
    {
        var references = ReferencesOf(typeof(global::Kanstraction.MainWindow).Assembly);

        Assert.Contains("Kanstraction.OnSite.Application", references);
        Assert.DoesNotContain(references, name => name.Contains("OnSite.Domain"));
        Assert.DoesNotContain(references, name => name.Contains("OnSite.Infrastructure"));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(references, name => name.StartsWith("ClosedXML"));
    }

    [Fact]
    public void EveryApplicationCommandDeclaresWhetherItChangesState()
    {
        var applicationAssembly = typeof(Kanstraction.OnSite.Application.Common.Result).Assembly;
        var commandTypes = applicationAssembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                type.Namespace?.Contains(".Commands.", StringComparison.Ordinal) == true &&
                type.GetInterfaces().Any(@interface =>
                    @interface.IsGenericType &&
                    @interface.GetGenericTypeDefinition() == typeof(MediatR.IRequest<>)))
            .ToList();

        Assert.Equal(21, commandTypes.Count);

        foreach (var commandType in commandTypes)
        {
            var classifications = commandType
                .GetInterfaces()
                .Count(@interface =>
                    @interface.IsGenericType &&
                    (@interface.GetGenericTypeDefinition() == typeof(IStateChangingRequest<>) ||
                     @interface.GetGenericTypeDefinition() == typeof(INonMutatingRequest<>)));

            Assert.True(
                classifications == 1,
                $"{commandType.FullName} must declare exactly one change classification.");
        }
    }

    private static HashSet<string> ReferencesOf(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
}
