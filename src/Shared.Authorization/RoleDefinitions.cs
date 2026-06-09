namespace Shared.Authorization;

/// <summary>Default roles and the permissions they grant (PRD: Authorization Model). Roles provide default permissions.</summary>
public static class RoleDefinitions
{
    public const string Administrator = "Administrator";
    public const string WarehouseManager = "Warehouse Manager";
    public const string WarehouseOperator = "Warehouse Operator";
    public const string Auditor = "Auditor";
    public const string Viewer = "Viewer";

    public static IReadOnlyDictionary<string, string[]> DefaultPermissions { get; } =
        new Dictionary<string, string[]>
        {
            [Administrator] = Permissions.All.Keys.ToArray(),

            [WarehouseManager] =
            [
                Permissions.Products.Read,
                Permissions.Inventory.Read,
                Permissions.Inventory.EntryCreate,
                Permissions.Inventory.ExitCreate,
                Permissions.Inventory.AdjustCreate,
                Permissions.Inventory.AdjustApprove,
                Permissions.Inventory.TransferCreate,
                Permissions.Inventory.TransferApprove,
                Permissions.Reports.Read,
                Permissions.Reports.Export
            ],

            [WarehouseOperator] =
            [
                Permissions.Products.Read,
                Permissions.Inventory.Read,
                Permissions.Inventory.EntryCreate,
                Permissions.Inventory.ExitCreate,
                Permissions.Inventory.TransferCreate
            ],

            [Auditor] =
            [
                Permissions.Products.Read,
                Permissions.Inventory.Read,
                Permissions.Reports.Read,
                Permissions.Audit.Read
            ],

            [Viewer] =
            [
                Permissions.Products.Read,
                Permissions.Inventory.Read,
                Permissions.Reports.Read
            ]
        };
}
