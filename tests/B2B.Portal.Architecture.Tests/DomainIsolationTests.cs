using NetArchTest.Rules;
using Xunit;

namespace B2B.Portal.Architecture.Tests;

/// <summary>
/// Erzwingt die Architekturregel aus Blueprint Abschnitt 3 / Anhang A:
/// "Domain referenziert keine Azure-, Graph- oder UI-Pakete." und
/// "Domain darf Infrastructure/Graph nicht referenzieren" (MVP-Dokument, Abschnitt 8).
/// </summary>
public class DomainIsolationTests
{
    private static readonly System.Reflection.Assembly DomainAssembly =
        typeof(B2B.Portal.Domain.Entities.GuestAccount).Assembly;

    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(B2B.Portal.Application.Ports.IGuestDirectory).Assembly;

    [Fact]
    public void Domain_Should_Not_Reference_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("B2B.Portal.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_Should_Not_Reference_MicrosoftGraph()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.Graph")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_Should_Not_Reference_Azure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny("Azure", "Microsoft.Azure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_Should_Not_Reference_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("B2B.Portal.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_Should_Not_Reference_MicrosoftGraph_Directly()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.Graph")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Verletzende Typen: " + string.Join(", ", result.FailingTypeNames ?? []);
}
