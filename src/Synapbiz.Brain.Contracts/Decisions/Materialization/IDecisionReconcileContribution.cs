namespace Nuvora.Decisions.Contracts.Materialization;

/// <summary>Module hook the reconcile job runs for the current tenant before it re-materializes active versions. Implementations must be idempotent.</summary>
public interface IDecisionReconcileContribution
{
    /// <summary>The kind this contribution serves; it labels a failure in the log and the reconcile telemetry.</summary>
    string KindKey { get; }

    /// <summary>Runs the contribution for the tenant of the ambient context, as of <paramref name="now"/>.</summary>
    Task ContributeAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
