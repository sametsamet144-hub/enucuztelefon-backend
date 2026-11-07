using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace EnUcuzTelefon.ArchitectureTests;

public class CleanArchitectureTests
{
    private const string DomainNamespace = "EnUcuzTelefon.Domain";
    private const string ApplicationNamespace = "EnUcuzTelefon.Application";
    private const string InfrastructureNamespace = "EnUcuzTelefon.Infrastructure";
    private const string ApiNamespace = "EnUcuzTelefon.Api";
    private const string WorkerNamespace = "EnUcuzTelefon.Worker";
    private const string SchedulerNamespace = "EnUcuzTelefon.Scheduler";

    [Fact]
    public void Domain_ShouldNotHaveDependencyOn_OtherProjects()
    {
        var otherProjects = new[]
        {
            ApplicationNamespace,
            InfrastructureNamespace,
            ApiNamespace,
            WorkerNamespace,
            SchedulerNamespace
        };

        var result = Types.InAssembly(typeof(Domain.Common.BaseEntity).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(otherProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_ShouldNotHaveDependencyOn_InfrastructureOrApiOrWorkers()
    {
        var forbiddenProjects = new[]
        {
            InfrastructureNamespace,
            ApiNamespace,
            WorkerNamespace,
            SchedulerNamespace
        };

        var result = Types.InAssembly(typeof(Application.Common.IApplicationMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
