namespace Estimates.Domain;

/// <summary>
/// Lifecycle of an estimate. Milestone 1 supports Draft -> Approved only.
/// New states (for example Sent or Rejected) can be added later; clients must
/// therefore handle unknown enum values gracefully.
/// </summary>
public enum EstimateStatus
{
    Draft = 0,
    Approved = 1,
}
