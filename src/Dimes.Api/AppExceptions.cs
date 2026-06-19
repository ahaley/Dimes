namespace Dimes.Api;

/// <summary>A referenced entity does not exist → 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The actor is not permitted to perform the action → 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>The request is invalid in context → 400.</summary>
public sealed class BadRequestException(string message) : Exception(message);
