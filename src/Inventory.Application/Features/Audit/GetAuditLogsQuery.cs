using System.Text.Json;
using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
using Inventory.Domain.Audit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Audit;
using Shared.Contracts.Common;

namespace Inventory.Application.Features.Audit;

public sealed record GetAuditLogsQuery(
    Guid? UserId = null, string? Entity = null, string? Action = null,
    DateTime? FromUtc = null, DateTime? ToUtc = null,
    int Page = 1, int PageSize = QueryPaging.DefaultPageSize)
    : IRequest<PagedResult<AuditLogDto>>, IPagedQuery, IDateRangeQuery;

public sealed class GetAuditLogsQueryValidator : PagedQueryValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        this.AddDateRangeRules();
        RuleFor(q => q.UserId).NotEqual(Guid.Empty);
        RuleFor(q => q.Entity).MaximumLength(128);
        RuleFor(q => q.Action).Must(a => a is null or "Created" or "Updated" or "Deleted")
            .WithMessage("Action must be Created, Updated or Deleted.");
    }
}

public sealed class GetAuditLogsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>>
{
    public async Task<PagedResult<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (request.UserId is { } user) query = query.Where(a => a.UserId == user);
        if (!string.IsNullOrWhiteSpace(request.Entity)) query = query.Where(a => a.Entity == request.Entity.Trim());
        if (request.Action is { } action) query = query.Where(a => a.Action == action);
        if (request.FromUtc is { } from) query = query.Where(a => a.TimestampUtc >= from);
        if (request.ToUtc is { } to) query = query.Where(a => a.TimestampUtc <= to);
        var page = await query.OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id)
            .ToPageAsync(request.Page, request.PageSize, ct);
        return new PagedResult<AuditLogDto>(page.Items.Select(ToDto).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    private static AuditLogDto ToDto(AuditLog log) => new(
        log.Id, log.UserId, log.TimestampUtc, log.Entity, log.Action,
        Parse(log.OldValues), Parse(log.NewValues), log.IpAddress, log.CorrelationId);

    private static JsonElement? Parse(string? json) => json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);
}
