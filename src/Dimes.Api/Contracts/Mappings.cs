using Dimes.Domain.Entities;

namespace Dimes.Api.Contracts;

/// <summary>Entity → DTO projections. Kept in one place so controllers and services stay thin.</summary>
public static class Mappings
{
    public static ProjectDto ToDto(this Project p) => new(p.Id, p.Name, p.Description, p.CreatedAt);

    public static MemberDto ToMemberDto(this Membership m) =>
        new(m.ActorId, m.ProjectId, m.Actor.DisplayName, m.Actor.Type, m.Actor.Email, m.Role, m.Actor.LlmProviderConfigId);

    public static LlmProviderConfigDto ToDto(this LlmProviderConfig c) =>
        new(c.Id, c.ProjectId, c.Type, c.Name, c.BaseUrl, c.Model, c.ApiKeySecretRef, c.Enabled);

    public static ObservationSourceDto ToDto(this ObservationSource s) =>
        new(s.Id, s.ProjectId, s.Type, s.Name, s.Enabled);

    public static ObservationDto ToDto(this Observation o) => new(
        o.Id, o.ProjectId, o.SourceId, o.Kind, o.Status, o.Payload, o.ContextMetadata,
        o.Fingerprint, o.OccurrenceCount, o.FirstSeen, o.LastSeen, o.ChangeRequestId);

    public static ChangeRequestDto ToDto(this ChangeRequest c) => new(
        c.Id, c.ProjectId, c.Title, c.Description, c.Kind, c.Status, c.Priority,
        c.CreatedByActorId, c.AssigneeActorId, c.DuplicateOfId, c.CreatedAt, c.UpdatedAt);

    public static CommentDto ToDto(this Comment c) =>
        new(c.Id, c.ChangeRequestId, c.AuthorActorId, c.Body, c.Kind, c.CreatedAt);

    public static ScmLinkDto ToDto(this ScmLink l) =>
        new(l.Id, l.ChangeRequestId, l.Provider, l.Url, l.ContextSnapshot);

    public static AuditEventDto ToDto(this AuditEvent e) => new(
        e.Id, e.EntityType, e.EntityId, e.ActorId, e.FromStatus, e.ToStatus, e.Action, e.Reason, e.Timestamp);
}
