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
    public void MockGroups_ExposeOnlyStandardGroupShape()
    {
        var store = new MockEntraDirectoryStore();

        var group = store.ListGroups().Single(g => g.ObjectId == "mock-m365-collab");
        var properties = typeof(MockEntraGroup).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Equal(["Unified"], group.GroupTypes);
        Assert.True(group.MailEnabled);
        Assert.False(group.SecurityEnabled);
        Assert.DoesNotContain("WorkloadName", properties);
    }

    [Fact]
    public void MockStore_CanMaintainUsersGroupsAndMemberships()
    {
        var store = new MockEntraDirectoryStore();

        var user = store.UpsertUser(new(
            ObjectId: string.Empty,
            UserPrincipalName: string.Empty,
            Mail: "new.user@contoso.example",
            DisplayName: "New User",
            GivenName: "New",
            Surname: "User",
            CompanyName: "Contoso Consulting",
            Department: "Delivery",
            JobTitle: "Consultant",
            Sponsor: "sponsor@platform.example",
            AccountEnabled: "true",
            UserType: "Guest",
            PortalRoles: ["User"]));
        var group = store.UpsertGroup(new(
            ObjectId: string.Empty,
            DisplayName: "SG-EDITOR-TEST",
            MailNickname: string.Empty,
            Description: "Editor test group",
            GroupTypes: [],
            MailEnabled: false,
            SecurityEnabled: true,
            ResourceProvisioningOptions: []));

        store.AddMember(group.ObjectId, user.ObjectId);
        Assert.Contains(store.ListMemberships(user.ObjectId), m => m.GroupId == group.ObjectId);

        Assert.True(store.DeleteGroup(group.ObjectId));
        Assert.DoesNotContain(store.ListMemberships(user.ObjectId), m => m.GroupId == group.ObjectId);
        Assert.True(store.DeleteUser(user.ObjectId));
        Assert.DoesNotContain(store.ListUsers(), u => u.ObjectId == user.ObjectId);
    }

    [Fact]
    public async Task MockConnector_CreatesGroupAndAssignsMember()
    {
        var store = new MockEntraDirectoryStore();
        var connector = new MockResourceConnector("SecurityGroup", store);

        var groupId = await connector.CreateResourceAsync(
            "dir-a",
            "SG-NEW-WORKLOAD-READER",
            new Dictionary<string, string> { ["ScenarioId"] = Guid.NewGuid().ToString() },
            CancellationToken.None);
        await connector.GrantAccessAsync("dir-a", "mock-obj-anna", groupId, CancellationToken.None);

        var memberships = store.ListMemberships("mock-obj-anna");

        Assert.Contains(memberships, m => m.GroupId == groupId && m.GroupName == "SG-NEW-WORKLOAD-READER");
    }
}
