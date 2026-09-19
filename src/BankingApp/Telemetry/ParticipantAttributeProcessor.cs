using System.Diagnostics;
using OpenTelemetry;

namespace BankingApp.Telemetry;

/// <summary>
/// Stamps every emitted span with the participant's identity and the Langfuse
/// session/user attributes that make the OTLP payload land as a Langfuse
/// Session. Applied in OnStart on EVERY span: Langfuse only groups traces into
/// a session when `langfuse.session.id` is present, and it must be propagated
/// to each span in the trace to be filterable/aggregatable. Guarantees that
/// Langfuse telemetry for a participant-fork can be correlated back to its
/// owner even when the trace leaves the app (gateway, eval, leaderboard).
/// </summary>
public sealed class ParticipantAttributeProcessor : BaseProcessor<Activity>
{
    private readonly FdeOptions _fde;

    public ParticipantAttributeProcessor(FdeOptions fde)
    {
        _fde = fde;
    }

    public override void OnStart(Activity activity)
    {
        // Langfuse trace-level attributes. SessionScope is set by the chat
        // handler before the root activity starts, so the root AND agent child
        // spans carry the same session id (AsyncLocal flows through the async
        // context) and Langfuse groups the whole trace into one session.
        activity.SetTag("langfuse.session.id", SessionScope.CurrentSessionId ?? _fde.SessionId);
        activity.SetTag("langfuse.user.id", SessionScope.CurrentUserId ?? _fde.ParticipantId);
        if (activity.Parent is null)
        {
            activity.SetTag("langfuse.trace.name", "bankingapp.chat");
        }

        activity.SetTag("fde.participant_id", _fde.ParticipantId);
        activity.SetTag("fde.pod_id", _fde.PodId);
        activity.SetTag("fde.event_id", _fde.EventId);
        activity.SetTag("fde.workload_type", _fde.WorkloadType);
    }
}

/// <summary>
/// Per-request Langfuse session context. The chat handler sets this before it
/// starts the trace activity; AsyncLocal flows it to agent child spans so the
/// whole trace lands under the same Langfuse session.
/// </summary>
public static class SessionScope
{
    private static readonly AsyncLocal<string?> CurrentSessionIdValue = new();
    private static readonly AsyncLocal<string?> CurrentUserIdValue = new();

    public static string? CurrentSessionId => CurrentSessionIdValue.Value;

    public static string? CurrentUserId => CurrentUserIdValue.Value;

    public static void Set(string sessionId, string userId)
    {
        CurrentSessionIdValue.Value = sessionId;
        CurrentUserIdValue.Value = userId;
    }
}

/// <summary>Central activity source for the consolidated app.</summary>
public static class BankingActivitySources
{
    public const string Name = "BankingApp";

    public static readonly ActivitySource Source = new(Name);
}