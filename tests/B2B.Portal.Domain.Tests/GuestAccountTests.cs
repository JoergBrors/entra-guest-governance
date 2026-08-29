using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using Xunit;

namespace B2B.Portal.Domain.Tests;

/// <summary>
/// Testet die Sicherheitsinvariante "Workloads/Connectoren dürfen keine Gastidentität
/// direkt löschen" (Anhang A, Regel 3) direkt an der Entität.
/// </summary>
public class GuestAccountTests
{
    private static GuestAccount NewGuest() => new()
    {
        PlatformTenantId = "tenant-a",
        DirectoryTenantId = "dir-a",
        Mail = "guest@example.com",
        DisplayName = "Test Guest",
    };

    [Fact]
    public void TransitionTo_Disabled_WithoutGovernanceCore_Throws()
    {
        var guest = NewGuest();

        Assert.Throws<InvalidOperationException>(() => guest.TransitionTo(GuestAccountState.Disabled));
    }

    [Fact]
    public void TransitionTo_Deleted_WithoutGovernanceCore_Throws()
    {
        var guest = NewGuest();

        Assert.Throws<InvalidOperationException>(() => guest.TransitionTo(GuestAccountState.Deleted));
    }

    [Fact]
    public void TransitionTo_Disabled_ViaGovernanceCore_Succeeds()
    {
        var guest = NewGuest();

        guest.TransitionTo(GuestAccountState.Disabled, viaGovernanceCore: true);

        Assert.Equal(GuestAccountState.Disabled, guest.AccountState);
    }

    [Fact]
    public void TransitionTo_Invited_DoesNotRequireGovernanceCore()
    {
        var guest = NewGuest();

        guest.TransitionTo(GuestAccountState.Invited);

        Assert.Equal(GuestAccountState.Invited, guest.AccountState);
    }
}
