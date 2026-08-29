using B2B.Portal.Api.Auth;
using Xunit;

namespace B2B.Portal.Integration.Tests;

public class PortalAuthorizationFactsTests
{
    [Fact]
    public void WorkloadOwner_CanManageOnlyOwnedWorkload()
    {
        var user = new PortalUserContext(
            "owner@platform.example",
            new HashSet<string>([PortalRoles.WorkloadOwner], StringComparer.OrdinalIgnoreCase),
            new HashSet<Guid>());

        Assert.True(user.CanManageWorkload("owner@platform.example"));
        Assert.False(user.CanManageWorkload("other@platform.example"));
    }

    [Fact]
    public void ScenarioManager_CanActInsideConfiguredWorkloadScope()
    {
        var workloadId = Guid.NewGuid();
        var user = new PortalUserContext(
            "scenario-manager@platform.example",
            new HashSet<string>([PortalRoles.ScenarioManager], StringComparer.OrdinalIgnoreCase),
            new HashSet<Guid>([workloadId]));

        Assert.True(user.CanManageScenario(workloadId, "owner@platform.example", []));
        Assert.False(user.CanManageScenario(Guid.NewGuid(), "owner@platform.example", []));
    }
}
