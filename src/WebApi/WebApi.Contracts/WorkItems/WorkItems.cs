using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using soft.square.net.WebApi.Contracts.Messaging;

namespace soft.square.net.WebApi.Contracts.WorkItems;

/// <summary>
/// Points at a work item a module owns: <c>{Module}:{Type}:{Id}</c>, e.g. <c>content:content:42</c> or
/// <c>newsroom:post-project:7</c>. Every module keeps its own work items (with its own states); this reference and
/// <see cref="WorkItemSnapshot"/> are the shared shape other modules ("My work", live updates) read through events.
/// </summary>
/// <param name="Module">The owning module's id (lowercase, as in its manifest).</param>
/// <param name="Type">The kind of work item within the module (lowercase, a-z, 0-9, '-').</param>
/// <param name="Id">The item's id in the module (any text without line breaks).</param>
public sealed record WorkItemRef(string Module, string Type, string Id)
{
    public const char Separator = ':';

    /// <summary><c>{Module}:{Type}:{Id}</c>; also the subject of the work-item events.</summary>
    public override string ToString() => $"{Module}{Separator}{Type}{Separator}{Id}";

    /// <summary>Parses <see cref="ToString"/>'s format (the id may itself contain ':').</summary>
    public static WorkItemRef Parse(string text) =>
        TryParse(text, out var value) ? value : throw new FormatException($"'{text}' is not a work item reference ('module:type:id').");

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out WorkItemRef? value)
    {
        value = null;
        var parts = text?.Split(Separator, 3);
        if (parts is not { Length: 3 } || !WorkItemEvents.IsName(parts[0]) || !WorkItemEvents.IsName(parts[1]) || parts[2].Length == 0)
            return false;
        value = new WorkItemRef(parts[0], parts[1], parts[2]);
        return true;
    }
}

/// <summary>Who a work item is assigned to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkItemAssigneeKind>))]
public enum WorkItemAssigneeKind
{
    /// <summary>One person (the token subject).</summary>
    [JsonStringEnumMemberName("user")] User,
    /// <summary>Whoever works a shift (Shifts module): the shift's members see it until one of them takes it.</summary>
    [JsonStringEnumMemberName("shift")] Shift,
    /// <summary>Anyone holding a role (in the item's unit, when it has one).</summary>
    [JsonStringEnumMemberName("role")] Role,
    /// <summary>An organization unit as a whole (Organization module).</summary>
    [JsonStringEnumMemberName("unit")] Unit
}

/// <summary>The assignee of a work item: a user, a shift, a role or a unit, by id. Serialized as <c>{"kind":"user","id":"…"}</c>.</summary>
public sealed record WorkItemAssignee(WorkItemAssigneeKind Kind, string Id)
{
    public static WorkItemAssignee User(string userId) => new(WorkItemAssigneeKind.User, userId);

    public static WorkItemAssignee Shift(string shiftId) => new(WorkItemAssigneeKind.Shift, shiftId);

    public static WorkItemAssignee Role(string role) => new(WorkItemAssigneeKind.Role, role);

    public static WorkItemAssignee Unit(string unitId) => new(WorkItemAssigneeKind.Unit, unitId);

    /// <summary><c>user:42</c>, <c>shift:7</c>, <c>role:editor</c>, <c>unit:desk-1</c> (the same form as the live topics and scopes).</summary>
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}:{Id}";
}

/// <summary>
/// What other modules know of a work item: carried by the work-item events, enough to list it in "My work" and route
/// live updates without calling the owning module.
/// </summary>
/// <param name="Ref">The item.</param>
/// <param name="Title">A short, human-readable title.</param>
/// <param name="State">The owning module's state name (e.g. <c>InProgress</c>); only the owner gives it a meaning.</param>
/// <param name="Assignee">Who it is assigned to now; null when unassigned.</param>
/// <param name="DueAt">When it is due, if it has a deadline.</param>
/// <param name="UnitId">The organization unit the item belongs to, if any.</param>
public sealed record WorkItemSnapshot(
    WorkItemRef Ref,
    string Title,
    string State,
    WorkItemAssignee? Assignee,
    DateTimeOffset? DueAt,
    string? UnitId);

/// <summary>Data of <c>{module}.work-item-assigned.v1</c>: the item after the change and the assignee before it.</summary>
public sealed record WorkItemAssigned(WorkItemSnapshot Item, WorkItemAssignee? PreviousAssignee);

/// <summary>Data of <c>{module}.work-item-state-changed.v1</c>: the item after the change and the state before it.</summary>
public sealed record WorkItemStateChanged(WorkItemSnapshot Item, string PreviousState);

/// <summary>
/// The work-item integration event types. Each module publishes its own (<c>content.work-item-assigned.v1</c>);
/// a consumer of every module's events subscribes to <see cref="AnyAssigned"/> and <see cref="AnyStateChanged"/>.
/// </summary>
public static class WorkItemEvents
{
    public const string AssignedName = "work-item-assigned";
    public const string StateChangedName = "work-item-state-changed";
    public const string Version = "v1";

    /// <summary>
    /// Subscription pattern for every module's assignments: <c>#.work-item-assigned.v1</c> (<c>#</c>, not <c>*</c>,
    /// because a module id may contain dots).
    /// </summary>
    public const string AnyAssigned = "#." + AssignedName + "." + Version;

    /// <summary>Subscription pattern for every module's state changes: <c>#.work-item-state-changed.v1</c>.</summary>
    public const string AnyStateChanged = "#." + StateChangedName + "." + Version;

    /// <summary><c>{module}.work-item-assigned.v1</c>.</summary>
    public static string Assigned(string module) => TypeOf(module, AssignedName);

    /// <summary><c>{module}.work-item-state-changed.v1</c>.</summary>
    public static string StateChanged(string module) => TypeOf(module, StateChangedName);

    public static bool IsAssigned(string type) => ModuleOf(type, AssignedName) is not null;

    public static bool IsStateChanged(string type) => ModuleOf(type, StateChangedName) is not null;

    /// <summary>The publishing module of a work-item event type, or null when <paramref name="type"/> is not one.</summary>
    public static string? ModuleOf(string type) => ModuleOf(type, AssignedName) ?? ModuleOf(type, StateChangedName);

    /// <summary>
    /// Adds <c>{module}.work-item-assigned.v1</c> to the module's outbox (sent when its SaveChanges commits); the subject is
    /// the item's reference. <paramref name="unitPath"/> defaults to nothing: pass the unit path when the item has a unit.
    /// </summary>
    public static IntegrationEnvelope PublishWorkItemAssigned(this IIntegrationEvents events, WorkItemSnapshot item,
        WorkItemAssignee? previousAssignee, string? unitPath = null, string? orgId = null) =>
        events.Publish(Assigned(item.Ref.Module), new WorkItemAssigned(item, previousAssignee), item.Ref.ToString(), unitPath, orgId);

    /// <summary>Adds <c>{module}.work-item-state-changed.v1</c> to the module's outbox; see <see cref="PublishWorkItemAssigned"/>.</summary>
    public static IntegrationEnvelope PublishWorkItemStateChanged(this IIntegrationEvents events, WorkItemSnapshot item,
        string previousState, string? unitPath = null, string? orgId = null) =>
        events.Publish(StateChanged(item.Ref.Module), new WorkItemStateChanged(item, previousState), item.Ref.ToString(), unitPath, orgId);

    /// <summary>A module id or a work-item type: lowercase words of a-z, 0-9, '-' and '_', separated by dots.</summary>
    internal static bool IsName(string text) => IntegrationTypes.IsValidType(text);

    private static string? ModuleOf(string type, string name)
    {
        var suffix = $".{name}.{Version}";
        return type.Length > suffix.Length && type.EndsWith(suffix, StringComparison.Ordinal) && IntegrationTypes.IsValidType(type)
            ? type[..^suffix.Length]
            : null;
    }

    private static string TypeOf(string module, string name)
    {
        if (!IsName(module))
            throw new ArgumentException($"Invalid module id '{module}': lowercase words separated by dots.", nameof(module));
        return $"{module}.{name}.{Version}";
    }
}
