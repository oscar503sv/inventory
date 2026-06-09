namespace Shared.Authorization;

/// <summary>
/// The catalog of business-oriented permission codes (PRD: Permissions). Permissions MUST be
/// business-oriented; controllers reference these constants via <c>[RequirePermission(...)]</c>.
/// </summary>
public static class Permissions
{
    public static class Products
    {
        public const string Read = "products.read";
        public const string Create = "products.create";
        public const string Update = "products.update";
        public const string Delete = "products.delete";
    }

    public static class Inventory
    {
        public const string Read = "inventory.read";
        public const string EntryCreate = "inventory.entry.create";
        public const string ExitCreate = "inventory.exit.create";
        public const string AdjustCreate = "inventory.adjust.create";
        public const string AdjustApprove = "inventory.adjust.approve";
        public const string TransferCreate = "inventory.transfer.create";
        public const string TransferApprove = "inventory.transfer.approve";
    }

    public static class Suppliers
    {
        public const string Read = "suppliers.read";
        public const string Create = "suppliers.create";
    }

    public static class PurchaseOrders
    {
        public const string Create = "purchase_orders.create";
        public const string Approve = "purchase_orders.approve";
    }

    public static class Reports
    {
        public const string Read = "reports.read";
        public const string Export = "reports.export";
    }

    public static class Users
    {
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string Manage = "roles.manage";
    }

    public static class PermissionsAdmin
    {
        public const string Manage = "permissions.manage";
    }

    public static class Audit
    {
        public const string Read = "audit.read";
    }

    /// <summary>Every permission code with its human-readable description, used to seed the catalog.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [Products.Read] = "Read products",
        [Products.Create] = "Create products",
        [Products.Update] = "Update products",
        [Products.Delete] = "Delete products",
        [Inventory.Read] = "Read inventory and stock",
        [Inventory.EntryCreate] = "Create inventory entries",
        [Inventory.ExitCreate] = "Create inventory exits",
        [Inventory.AdjustCreate] = "Create inventory adjustments",
        [Inventory.AdjustApprove] = "Approve inventory adjustments",
        [Inventory.TransferCreate] = "Create inventory transfers",
        [Inventory.TransferApprove] = "Approve inventory transfers",
        [Suppliers.Read] = "Read suppliers",
        [Suppliers.Create] = "Create suppliers",
        [PurchaseOrders.Create] = "Create purchase orders",
        [PurchaseOrders.Approve] = "Approve purchase orders",
        [Reports.Read] = "Read reports",
        [Reports.Export] = "Export reports",
        [Users.Manage] = "Manage users",
        [Roles.Manage] = "Manage roles",
        [PermissionsAdmin.Manage] = "Manage permissions",
        [Audit.Read] = "Read audit logs"
    };
}
