# Requisitos del producto — Inventory ERP

Consolidación: 2026-11-12. Requisitos del documento original **PRD + ADR, versión 2.0**, antes almacenado en `docs/PRD.pdf`. Las decisiones técnicas se mantienen en [arquitectura.md](arquitectura.md); el estado de implementación, las fases y el orden de entregas están en [backlog.md](backlog.md).

Este documento describe el producto objetivo. No constituye una declaración de funcionalidades implementadas ni de objetivos ya medidos.

## Propósito y objetivos

Plataforma de inventario empresarial, inicialmente orientada a pequeñas y medianas empresas y preparada para crecimiento empresarial y una futura modalidad SaaS.

Objetivos de negocio:

- Centralizar la gestión del inventario y reducir inconsistencias.
- Mantener trazabilidad completa y capacidad de auditoría.
- Mejorar la visibilidad operativa.
- Soportar múltiples bodegas y operaciones escalables.
- Ofrecer autorización flexible.

Objetivos técnicos:

- Monolito modular con Clean Architecture.
- Preparación para eventos e integraciones asíncronas.
- Alta capacidad de pruebas y observabilidad.
- Mantenibilidad y despliegue preparado para la nube.

## Autorización

Los roles proporcionan permisos por defecto. Roles de referencia: Administrator, Warehouse Manager, Warehouse Operator, Auditor y Viewer.

Los permisos deben representar acciones del negocio. El catálogo de ejemplos del PRD incluye:

```text
products.read
products.create
products.update
products.delete
inventory.read
inventory.entry.create
inventory.exit.create
inventory.adjust.create
inventory.adjust.approve
inventory.transfer.create
inventory.transfer.approve
suppliers.read
suppliers.create
purchase_orders.create
purchase_orders.approve
reports.read
reports.export
users.manage
roles.manage
permissions.manage
audit.read
```

Un usuario puede recibir excepciones `Allow` o `Deny` para cualquier permiso sin crear un rol adicional. Por ejemplo, aprobar ajustes o transferencias y exportar reportes.

`EffectivePermissions = (RolePermissions ∪ UserAllowPermissions) − UserDenyPermissions`

Además del permiso, debe verificarse el alcance por bodega: un usuario limitado a las bodegas 1 y 2 solo puede consultar u operar en ellas según sus permisos. El alcance global permite acceder a todas las bodegas. Véase ADR-003 en [arquitectura.md](arquitectura.md).

## Modelo funcional de inventario

Los nombres siguientes corresponden al modelo solicitado por el PRD; pueden diferir de nombres concretos del código. Las diferencias deben revisarse al implementar, sin asumir que todos los campos ya existen.

| Entidad | Campos previstos |
|---|---|
| Product | Id, SKU, Code, Name, Description, CategoryId, BrandId, UnitOfMeasureId, MinimumStock, IsActive. |
| Warehouse | Id, Code, Name, Address, Status. |
| Stock | ProductId, WarehouseId, Quantity, ReservedQuantity, AvailableQuantity. |
| InventoryMovement | Id, MovementNumber, MovementType, WarehouseId, Status, CreatedBy, CreatedAt. |
| InventoryMovementLine | ProductId, Quantity, UnitCost, Notes. |

`AvailableQuantity = Quantity − ReservedQuantity`.

Los catálogos incluyen productos, categorías, marcas y unidades de medida. El inventario incluye consultas de existencias, movimientos, ajustes, reservas y kardex.

### Reglas de negocio

- Las cantidades cambian exclusivamente mediante entradas, salidas, transferencias y ajustes; nunca por edición directa del stock.
- Las existencias se calculan por bodega; el total global es la suma de las bodegas.
- No se permite stock negativo.
- Cada movimiento requiere usuario y fecha/hora.
- Los movimientos históricos no pueden eliminarse.
- Los ajustes requieren justificación.
- Las transferencias deben ser transaccionales.

Los fundamentos y consecuencias de estas reglas se conservan en ADR-001 y ADR-002.

## Compras

Los proveedores contienen Name, TaxId, Email, Phone y Address.

Las órdenes de compra contemplan estos estados:

- Draft: borrador.
- Pending Approval: pendiente de aprobación.
- Approved: aprobada.
- Received: recibida.
- Cancelled: cancelada.

La recepción de una orden genera entradas de inventario. Las transiciones y permisos se concretarán en su implementación.

## Reportes

Reportes de inventario y operación, exportaciones y dashboard. Los eventos y procesos de generación asíncrona previstos se describen en ADR-004. Las consultas frecuentes son candidatas a caché conforme a ADR-005.

## Auditoría

Toda acción crítica debe auditarse. Los registros son inmutables y contienen:

- Usuario y fecha/hora.
- Entidad y acción.
- Valores anteriores y nuevos.
- Dirección IP y CorrelationId.

La corrección acordada en el backlog exige excluir contraseñas, hashes y tokens de estos registros.

## API y seguridad

La API utiliza el prefijo `/api/v1/`. Ejemplos del contrato solicitado:

| Método | Ruta |
|---|---|
| POST | `/api/v1/auth/login` |
| POST | `/api/v1/auth/refresh` |
| GET / POST | `/api/v1/products` |
| PUT / DELETE | `/api/v1/products/{id}` |
| POST | `/api/v1/inventory/entries` |
| POST | `/api/v1/inventory/exits` |
| POST | `/api/v1/inventory/transfers` |
| POST | `/api/v1/inventory/adjustments` |
| GET | `/api/v1/inventory/kardex` |

Los endpoints implementados se enumeran en [backlog.md](backlog.md).

Requisitos de seguridad: JWT para acceso protegido, HTTPS, hashing seguro de contraseñas, rotación de refresh tokens y limitación de solicitudes.

## Observabilidad y pruebas

Cada solicitud debe contar con CorrelationId. La solución contempla logs con Serilog, métricas con Prometheus, trazas con OpenTelemetry y dashboards con Grafana.

La estrategia de pruebas incluye:

- Pruebas unitarias de reglas de dominio, motor de permisos y cálculos de inventario.
- Integración con PostgreSQL, Redis y RabbitMQ conforme se incorporen.
- Autorización por roles, excepciones de permisos y alcance por bodega.
- Meta de cobertura: **80 % o más**; no es una medición actual.

## Requisitos no funcionales

| Área | Objetivo del PRD |
|---|---|
| Rendimiento | Respuesta media de API inferior a 500 ms en operaciones comunes. |
| Disponibilidad | 99,9 % de disponibilidad. |
| Seguridad | Cumplimiento de OWASP Top 10. |
| Escalabilidad | Escalado horizontal de la API. |
| Mantenibilidad | Respeto de los límites de Clean Architecture. |
| Extensibilidad | Agregar módulos sin modificar los existentes siempre que sea posible. |

Estos son objetivos pendientes de verificación; esta consolidación documental no certifica su cumplimiento.
