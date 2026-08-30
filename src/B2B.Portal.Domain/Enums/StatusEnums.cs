namespace B2B.Portal.Domain.Enums;

/// <summary>Lifecycle-Status eines GuestAccount (Blueprint 7.1 / 14.3).</summary>
public enum GuestAccountState
{
    Discovered,
    Invited,
    Active,
    Inactive,
    Blocked,
    OrphanCandidate,
    PendingRemoval,
    Disabled,
    Deleted
}

/// <summary>Status eines GuestWorkloadAssignment / einer Membership.</summary>
public enum AssignmentStatus
{
    Requested,
    Approved,
    Active,
    PendingReview,
    Expired,
    Revoked,
    Rejected,
    Removed
}

/// <summary>Compliance-/Findings-Status.</summary>
public enum ComplianceStatus
{
    Compliant,
    Warning,
    NonCompliant,
    Unknown,
    Exempted
}

/// <summary>Status eines Job / DirectoryOperation.</summary>
public enum JobStatus
{
    Pending,
    Running,
    Success,
    Retry,
    Failed,
    DeadLetter,
    Cancelled
}

/// <summary>Klassifizierung eines entdeckten ResourceAccess (Blueprint 12.2).</summary>
public enum AccessClassification
{
    Classified,
    Unclassified
}

/// <summary>Review-Entscheidung eines ReviewItem (Blueprint 13.2).</summary>
public enum ReviewDecision
{
    Pending,
    Keep,
    Remove,
    Escalated
}

/// <summary>Review-/Lifecycle-Provider (Capability Resolver, Blueprint 13.4).</summary>
public enum GovernanceProvider
{
    Auto,
    Internal,
    EntraNative
}

/// <summary>Status einer ExternalOrganization.</summary>
public enum OrganizationStatus
{
    Active,
    Blocked
}
