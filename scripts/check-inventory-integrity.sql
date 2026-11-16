-- Read-only preflight for InventoryRelationalConstraints. Every invalid_rows value must be zero.
SELECT 'stocks' AS inventory_table, COUNT(*) AS invalid_rows
FROM stocks s
LEFT JOIN products p ON p."Id" = s."ProductId"
LEFT JOIN warehouses w ON w."Id" = s."WarehouseId"
WHERE p."Id" IS NULL OR w."Id" IS NULL
    OR s."Quantity" < 0 OR s."ReservedQuantity" < 0 OR s."ReservedQuantity" > s."Quantity"
UNION ALL
SELECT 'inventory_movements', COUNT(*)
FROM inventory_movements m
LEFT JOIN warehouses w ON w."Id" = m."WarehouseId"
LEFT JOIN warehouses d ON d."Id" = m."DestinationWarehouseId"
WHERE w."Id" IS NULL OR (m."DestinationWarehouseId" IS NOT NULL AND d."Id" IS NULL)
UNION ALL
SELECT 'inventory_movement_lines', COUNT(*)
FROM inventory_movement_lines l
LEFT JOIN products p ON p."Id" = l."ProductId"
WHERE p."Id" IS NULL OR l."Quantity" <= 0 OR l."UnitCost" < 0;
