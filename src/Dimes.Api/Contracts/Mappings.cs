using System.Text.Json;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Providers;

namespace Dimes.Api.Contracts;

/// <summary>Entity → DTO projections. Kept in one place so controllers and services stay thin.</summary>
public static class Mappings
{
    public static NotificationChannelDto ToDto(this NotificationChannelConfig c) =>
        new(c.Id, c.ProjectId, c.Type, c.Name, c.Target, c.SecretRef,
            ParseEvents(c.EventsJson), c.Enabled, c.LastDeliveryAt, c.LastDeliveryOk, c.LastDeliveryError);

    /// <summary>Parse a channel's stored event set (a JSON array of names) into typed events, dropping any
    /// unknown/legacy names rather than throwing.</summary>
    public static IReadOnlyList<NotificationEventType> ParseEvents(string? eventsJson)
    {
        if (string.IsNullOrWhiteSpace(eventsJson))
        {
            return [];
        }
        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(eventsJson) ?? [];
            return names
                .Select(n => Enum.TryParse<NotificationEventType>(n, ignoreCase: true, out var e) ? (NotificationEventType?)e : null)
                .Where(e => e is not null)
                .Select(e => e!.Value)
                .Distinct()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Serialize a typed event set to the stored JSON array of names, de-duplicated.</summary>
    public static string SerializeEvents(IReadOnlyList<NotificationEventType> events) =>
        JsonSerializer.Serialize(events.Distinct().Select(e => e.ToString()).ToList());

    /// <summary>Carries the creator's display name when the <see cref="Project.CreatedByActor"/> navigation
    /// is loaded — callers that surface provenance must Include (or explicitly load) it.</summary>
    public static ProjectDto ToDto(this Project p, MemberRole? myRole = null) =>
        new(p.Id, p.Name, p.Description, p.CreatedAt, p.IsArchived, p.ArchivedAt, p.SourceControlEnabled, p.HumanOnly, p.Key, myRole,
            p.CreatedByActorId, p.CreatedByActor?.DisplayName);

    public static MemberDto ToMemberDto(this Membership m) =>
        new(m.ActorId, m.ProjectId, m.Actor.DisplayName, m.Actor.Type, m.Actor.Email, m.Role, m.Actor.LlmProviderConfigId);

    public static LlmProviderConfigDto ToDto(this LlmProviderConfig c) =>
        new(c.Id, c.ProjectId, c.Type, c.Name, c.BaseUrl, c.Model, c.ApiKeySecretRef, c.Enabled,
            c.SettingsJson is null ? null : LlmProviderSettings.Parse(c.SettingsJson).ToDto());

    public static LlmProviderSettingsDto ToDto(this LlmProviderSettings s) =>
        new(s.GcpProject, s.GcpLocation, s.UseApplicationDefaultCredentials);

    /// <summary>A null settings block means "none supplied" and stores as a null column.</summary>
    public static string? ToSettingsJson(this LlmProviderSettingsDto? dto) =>
        dto is null
            ? null
            : new LlmProviderSettings
            {
                GcpProject = string.IsNullOrWhiteSpace(dto.GcpProject) ? null : dto.GcpProject.Trim(),
                GcpLocation = string.IsNullOrWhiteSpace(dto.GcpLocation) ? null : dto.GcpLocation.Trim(),
                UseApplicationDefaultCredentials = dto.UseApplicationDefaultCredentials,
            }.ToJson();

    public static ObservationSourceDto ToDto(this ObservationSource s) =>
        new(s.Id, s.ProjectId, s.Type, s.Name, s.Enabled);

    public static ObservationDto ToDto(this Observation o) => new(
        o.Id, o.ProjectId, o.SourceId, o.Kind, o.Status, o.Payload, o.ContextMetadata,
        o.Fingerprint, o.OccurrenceCount, o.FirstSeen, o.LastSeen, o.ChangeRequestId, o.TargetActorId);

    public static AssistMessageDto ToDto(this AssistMessage m) =>
        new(m.Id, m.ConversationId, m.AuthorActorId, m.Sender, m.Body, m.CreatedAt);

    // Requires Requester, Assistant, and Messages to be loaded.
    public static AssistConversationDto ToDto(this AssistConversation c) => new(
        c.Id, c.ProjectId,
        c.RequesterActorId, c.Requester.DisplayName,
        c.AssistantActorId, c.Assistant.DisplayName,
        c.Status, c.Title, c.Draft, c.ChangeRequestId, c.CreatedAt, c.UpdatedAt,
        c.Messages.OrderBy(m => m.CreatedAt).Select(m => m.ToDto()).ToList());

    /// <summary>Maps a change to its DTO. <paramref name="projectKey"/> is the owning project's key, used
    /// to build the human-readable display id "KEY-NUMBER" (null until both are backfilled/assigned).
    /// <paramref name="report"/> is this change's most recent work-order item, when the caller resolved one.
    /// Write paths leave it null and so omit the field: that's safe because the SPA only ever invalidates
    /// on a mutation response, never seeding the cache from it. The read paths (list, detail) pass it.</summary>
    public static ChangeRequestDto ToDto(this ChangeRequest c, string? projectKey, WorkOrderItem? report = null) => new(
        c.Id, c.ProjectId, c.Title, c.Description, c.Kind, c.Status, c.Priority,
        c.CreatedByActorId, c.AssigneeActorId, c.DuplicateOfId, c.CreatedAt, c.UpdatedAt, c.SortOrder,
        c.Number, projectKey != null && c.Number is int n ? $"{projectKey}-{n}" : null, c.CompletedAt,
        c.ParentChangeRequestId,
        report?.Status, report?.ReportedAt);

    public static CommentDto ToDto(this Comment c) =>
        new(c.Id, c.ChangeRequestId, c.AuthorActorId, c.Body, c.Kind, c.CreatedAt);

    public static ScmLinkDto ToDto(this ScmLink l) =>
        new(l.Id, l.ChangeRequestId, l.Provider, l.Url, l.ContextSnapshot);

    public static AuditEventDto ToDto(this AuditEvent e) => new(
        e.Id, e.EntityType, e.EntityId, e.ActorId, e.FromStatus, e.ToStatus, e.Action, e.Reason, e.Timestamp);
}
