using B2B.Portal.Infrastructure.Directory;
using Xunit;

namespace B2B.Portal.Integration.Tests;

public class MockEntraDirectoryTests
{
    [Fact]
    public async Task MockDirectory_ContainsUsersGroupsAndMemberships()
    {
        var store = new MockEntraDirectoryStore();
        var directory = new MockGuestDirectory(store);

        var users = await directory.ListGuestsAsync("dir-a", CancellationToken.None);
        var memberships = await directory.ListMembershipsAsync("dir-a", "mock-obj-peter", CancellationToken.None);

        Assert.True(users.Count >= 3);
        Assert.Contains(users, u => u.EntraObjectId == "mock-obj-peter" && u.Mail == "peter@fabrikam.example");
        Assert.Contains(memberships, m => m.GroupName == "SG-DEMO-READER");
        Assert.Contains(memberships, m => m.GroupName == "SG-DEMO-CONTRIBUTOR");
    }

    [Fact]
    public async Task MockConnector_CreatesGroupAndAssignsMember()
    {
        var store = new MockEntraDirectoryStore();
        var connector = new MockResourceConnector("SecurityGroup", store);

        var groupId = await connector.CreateResourceAsync(
            "dir-a",
            "SG-NEW-WORKLOAD-READER",
            new Dictionary<string, string> { ["WorkloadName"] = "New Workload" },
            CancellationToken.None);
        await connector.GrantAccessAsync("dir-a", "mock-obj-anna", groupId, CancellationToken.None);

        var memberships = store.ListMemberships("mock-obj-anna");

        Assert.Contains(memberships, m => m.GroupId == groupId && m.GroupName == "SG-NEW-WORKLOAD-READER");
    }
}
