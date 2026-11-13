# Estado y backlog — Inventory ERP

Fecha de planificación: 2026-11-12.

Objetivo: cerrar y validar la fase 1 del backend, construir una interfaz operativa y avanzar después con compras y reportes.

Este documento reúne el estado histórico, los pendientes y la planificación acordada. Todas las tareas del plan están pendientes; su inclusión no implica implementación ni validación. Los requisitos se mantienen en [producto.md](producto.md) y las decisiones técnicas en [arquitectura.md](arquitectura.md).

Consolidación documental: 2026-11-12. El contenido de `docs/project.md` se integró aquí; las instrucciones de ejecución están en el [README](../README.md).

## Estado de partida

El estado histórico del 2026-06-08 reporta la base y gran parte del backend de fase 1 implementadas: 41 pruebas aprobadas (Domain 27, Authorization 7, Application 7), compilación sin advertencias ni errores y pruebas de aplicación con EF en memoria. Es evidencia documental anterior; no se volvieron a ejecutar esas comprobaciones al consolidar estos archivos.

La revisión de código del 2026-11-12 identificó pendientes de autorización, auditoría y concurrencia detallados en el paso 2. Por ello, las funciones descritas a continuación no equivalen a una fase cerrada ni a una validación de producción. La atomicidad debe verificarse contra PostgreSQL real.

### Funciones documentadas en la fase 1


#### Identity & Auth
- ✅ JWT login + refresh-token rotation (hashed refresh tokens).
- ✅ RBAC + per-user Allow/Deny overrides + warehouse scopes — engine
  `EffectivePermissions = Roles ∪ Allow − Deny` with warehouse-scope validation (ADR-003).
- ✅ `[RequirePermission(...)]` policy provider/handler over business-oriented permission codes.
- ✅ Seeder: full permission catalog, 5 default roles, initial admin user.

#### Catalog
- ✅ Products: create, update, soft-delete (deactivate), get-by-id, paged/searchable list.
- ✅ Categories, Brands, Units of Measure: create + list.

#### Warehouses
- ✅ Create, update, list (multi-warehouse first — ADR-002).

#### Inventory (core)
- ✅ Movement-based stock (ADR-001): Entry, Exit, Transfer, Adjustment — stock is never edited directly.
- ✅ Per-warehouse `Stock` with `AvailableQuantity = Quantity − ReservedQuantity`; negative stock rejected.
- ✅ Adjustments require justification; transfers are atomic across two warehouses.
- ✅ Kardex (stock ledger) with running balance; stock query.
- ✅ `StockBelowMinimum` domain event raised when stock drops under the product minimum.

#### Audit
- ✅ Immutable `AuditLog` written by a `SaveChanges` interceptor (user, timestamp, entity, action,
  old/new values as `jsonb`, IP, correlation id) for critical entities.

#### Cross-cutting
- ✅ CorrelationId middleware (every request), RFC 7807 ProblemDetails error handling.
- ✅ JWT bearer auth, fixed-window rate limiting, Swagger with bearer button, `/health`.
- ✅ Serilog structured logging, OpenTelemetry tracing, Prometheus metrics at `/metrics`.
- ✅ In-process domain-event dispatch after commit (the seam for the future RabbitMQ publisher).

#### API surface (`/api/v1`)
| Area | Endpoints |
|---|---|
| Auth | `POST /auth/login`, `POST /auth/refresh` |
| Products | `GET/POST /products`, `GET/PUT/DELETE /products/{id}` |
| Catalog | `GET/POST /categories`, `/brands`, `/units-of-measure` |
| Warehouses | `GET/POST /warehouses`, `PUT /warehouses/{id}` |
| Inventory | `POST /inventory/{entries,exits,transfers,adjustments}`, `GET /inventory/{kardex,stock}` |
| Users/Roles | `GET/POST /users`, `GET /roles` |


### Verificación histórica


- ✅ `dotnet build InventoryErp.slnx` — clean.
- ✅ `dotnet test InventoryErp.slnx` — 41 tests green, including an in-memory end-to-end inventory
  flow (entry/exit/transfer → stock + kardex running balance).
- ✅ API boots and serves `/health` (`Healthy`), `/swagger`, returns `401` on protected routes, emits
  the `X-Correlation-Id` header, and degrades to `500 ProblemDetails` (no crash) when the DB is down.
- ⏳ **Pending:** full end-to-end run against a real PostgreSQL (login → product → entry → kardex).
  La nota original indicaba que Docker seguía en instalación y requería reinicio; el entorno debe comprobarse de nuevo.


### Límites actuales

- Usuarios: creación y listado; administración completa de roles, excepciones y alcances pendiente.
- Auditoría: escritura mediante interceptor; falta consulta y corrección del registro de datos sensibles.
- Reservas: `ReservedQuantity` y operaciones Reserve/Release en el dominio, sin casos de uso ni endpoints.
- Sesiones: sin logout ni endpoint de revocación, ni blacklist JWT en Redis.
- `/health` comprueba que el proceso está activo; falta verificar disponibilidad de dependencias.
- Redis y RabbitMQ están provisionados en Docker, pero no integrados funcionalmente. El despachador de eventos registra mensajes en el proceso.
- `Shared.Events` es un espacio reservado para la fase 3.
- Fases 2–4 y frontend sin iniciar según el estado documentado.

## Fases del producto

| Fase | Alcance previsto |
|---|---|
| 1 | Autenticación, usuarios, roles, permisos, alcance por bodega, productos, bodegas, movimientos, kardex y auditoría. |
| 2 | Proveedores, órdenes de compra, Redis, dashboard y reportes. |
| 3 | RabbitMQ, notificaciones, procesos en segundo plano y auditoría avanzada. |
| 4 | Multiempresa, multitenencia, lotes, vencimientos, números de serie, códigos de barras y aplicación móvil. |
| Frontend | Interfaz React sobre la API estabilizada; su entrega precede a la fase 2 en el plan acordado. |

## Orden de ejecución

| Entrega | Prioridad | Alcance | Dependencia |
|---|---|---|---|
| 1 | P0 | Validación con PostgreSQL y correcciones de autorización e integridad | Punto de partida |
| 2 | P1 | Usuarios, permisos, sesiones, auditoría y consultas operativas | Entrega 1 |
| 3 | P1 | Frontend del MVP | Entrega 2 y contratos de API estabilizados |
| 4 | P2 | Compras, reportes y caché | MVP validado |

P0 identifica la base necesaria para continuar; P1 completa el producto operativo; P2 amplía sus capacidades. El orden no representa una estimación de duración.

## 1. Establecer una validación reproducible con PostgreSQL

- [ ] Levantar el entorno local y verificar la configuración de conexión.
- [ ] Aplicar migraciones en una base aislada y verificar los datos iniciales.
- [ ] Ejecutar la compilación y las pruebas existentes; registrar los resultados actuales.
- [ ] Incorporar pruebas de integración mediante la API y una base PostgreSQL aislada.
- [ ] Cubrir el flujo login → producto → entrada → salida → transferencia → kardex.
- [ ] Verificar persistencia, saldos por bodega y correspondencia entre movimientos y kardex.
- [ ] Automatizar compilación y pruebas en integración continua.

**Criterio de finalización:** el flujo completo funciona contra PostgreSQL real y se verifica automáticamente en un entorno reproducible, sin depender de datos de desarrollo.

## 2. Corregir autorización e integridad del inventario

La revisión inicial identificó consultas de stock y kardex sin comprobación del alcance por bodega, serialización de propiedades sensibles como `PasswordHash` en auditoría y ausencia de un control de concurrencia configurado para el stock. Estos puntos deben contar con pruebas de regresión.

- [ ] Restringir las consultas de stock y kardex a las bodegas autorizadas para el usuario.
- [ ] Aplicar el alcance del usuario a los listados de bodegas y a las consultas sin filtro de bodega.
- [ ] Probar acceso autorizado y denegado, incluidos usuarios con acceso global y excepciones Allow/Deny.
- [ ] Excluir contraseñas, hashes y tokens de los datos de auditoría.
- [ ] Incorporar control de concurrencia para impedir que movimientos simultáneos sobrescriban saldos.
- [ ] Definir el comportamiento ante conflictos de concurrencia y verificarlo con PostgreSQL.
- [ ] Definir e implementar idempotencia para evitar movimientos duplicados al reintentar solicitudes.
- [ ] Definir y probar el tratamiento de productos repetidos dentro de un movimiento.
- [ ] Probar ajustes y transferencias con errores, verificando que no persistan cambios parciales.
- [ ] Comprobar que stock y kardex permanezcan consistentes después de operaciones simultáneas y reintentos.

**Criterio de finalización:** las pruebas demuestran aislamiento entre bodegas, ausencia de datos sensibles en auditoría, saldos consistentes bajo concurrencia, reintentos sin duplicación y transferencias atómicas.

**Hito de la primera entrega:** completar los pasos 1 y 2 antes de ampliar la administración y construir la interfaz.

## 3. Completar usuarios, permisos y sesiones

- [ ] Implementar edición, activación y desactivación de usuarios.
- [ ] Completar las operaciones de dominio y API para asignar y retirar roles.
- [ ] Permitir agregar, modificar y retirar excepciones de permisos Allow/Deny.
- [ ] Permitir otorgar y retirar acceso a bodegas, incluido el acceso global.
- [ ] Validar los identificadores de roles, permisos y bodegas recibidos.
- [ ] Agregar consulta del usuario actual y sus permisos efectivos para la interfaz.
- [ ] Implementar cierre de sesión y revocación de refresh tokens.
- [ ] Definir el alcance de la revocación y el comportamiento de los tokens de acceso después del cierre de sesión.
- [ ] Probar rotación, revocación, desactivación de usuarios y aplicación de cambios de permisos.

**Criterio de finalización:** un administrador puede gestionar accesos desde la API; los cambios y la revocación de sesiones se aplican conforme a una política explícita y comprobada mediante pruebas.

## 4. Completar auditoría y consultas operativas

- [ ] Crear una consulta paginada de auditoría protegida por `audit.read`.
- [ ] Incorporar filtros útiles para investigar operaciones: usuario, entidad, acción y fechas.
- [ ] Registrar cambios en roles, permisos y acceso a bodegas.
- [ ] Incorporar listado y detalle de movimientos con sus controles de autorización.
- [ ] Paginar las consultas voluminosas y definir un orden estable.
- [ ] Conservar el saldo correcto del kardex al filtrar por fechas y paginar resultados.
- [ ] Agregar una comprobación de disponibilidad de PostgreSQL, diferenciada de la comprobación de que el proceso está activo.
- [ ] Preparar configuración por ambiente y documentar los requisitos para ejecutar la API.

**Criterio de finalización:** se puede rastrear quién realizó cada operación, consultar movimientos y auditoría de forma controlada, y detectar si la API está preparada para atender solicitudes.

## 5. Construir el frontend del MVP

Stack previsto: React, TypeScript y Vite, siguiendo las herramientas de interfaz contempladas en la documentación del proyecto.

- [ ] Crear la base del frontend y su integración con la API.
- [ ] Implementar login, manejo de sesión y cierre de sesión.
- [ ] Incorporar navegación según permisos efectivos y selección de bodega.
- [ ] Crear pantallas de productos y catálogos auxiliares.
- [ ] Crear pantallas de administración de bodegas.
- [ ] Implementar formularios de entradas, salidas, transferencias y ajustes.
- [ ] Incorporar consultas de existencias, listado y detalle de movimientos, y kardex.
- [ ] Incorporar administración de usuarios, roles asignados, permisos excepcionales y acceso a bodegas.
- [ ] Crear la consulta de auditoría.
- [ ] Resolver estados de carga, resultados vacíos, validaciones y errores de la API.
- [ ] Probar los flujos completos desde el navegador con distintos permisos y alcances por bodega.

**Criterio de finalización:** un usuario puede realizar las operaciones diarias de inventario desde la interfaz, respetando los permisos aplicados por el backend.

## 6. Implementar la fase 2

- [ ] Implementar gestión de proveedores.
- [ ] Implementar órdenes de compra y sus estados: borrador, pendiente, aprobada, recibida y cancelada.
- [ ] Definir y validar las transiciones permitidas y los permisos de aprobación y recepción.
- [ ] Implementar recepción de compras que genere entradas de inventario.
- [ ] Garantizar que un reintento de recepción no duplique las entradas ni las existencias.
- [ ] Implementar dashboard y reportes de inventario y compras.
- [ ] Medir las consultas candidatas a caché.
- [ ] Incorporar Redis en las consultas donde las mediciones justifiquen su uso.
- [ ] Definir y probar invalidación y expiración de caché, manteniendo PostgreSQL como fuente de verdad.
- [ ] Probar el ciclo de compra completo y su reflejo en existencias, kardex y reportes.

**Criterio de finalización:** una compra recorre su ciclo completo y actualiza las existencias una sola vez; los reportes reflejan los datos persistidos y la caché no introduce resultados incorrectos.

## Trabajo posterior

- **Reservas:** definir primero el flujo de negocio que requiere reservar y liberar existencias; después implementar sus casos de uso y endpoints.
- **Fase 3:** RabbitMQ, notificaciones, procesos en segundo plano y auditoría avanzada.
- **Fase 4:** multiempresa, multitenencia, lotes, vencimientos, números de serie, códigos de barras y aplicación móvil.

## Seguimiento

- Marcar una tarea como completada únicamente cuando exista evidencia de su implementación y validación.
- Registrar los resultados de cada entrega y actualizar la sección «Estado de partida» de este documento con el estado comprobado y su fecha.
- Revisar dependencias y pendientes al cerrar cada entrega antes de iniciar la siguiente.
