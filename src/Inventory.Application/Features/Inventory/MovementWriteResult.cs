using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

/// <summary>Application outcome; the transport exposes Movement only and selects the HTTP status.</summary>
public sealed record MovementWriteResult(MovementDto Movement, bool IsReplay);
