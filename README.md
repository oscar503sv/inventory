# Inventory ERP

Sistema de gestión de inventario para empresas con múltiples bodegas: productos, existencias, entradas, salidas, transferencias, ajustes y kardex. Backend en .NET 10 y PostgreSQL, con arquitectura de monolito modular.

## Documentación

Toda la documentación de producto y planificación se mantiene en tres archivos:

- [Producto](docs/producto.md): requisitos funcionales, reglas del negocio y objetivos no funcionales; consolida el PRD v2.0.
- [Arquitectura](docs/arquitectura.md): estructura, tecnologías y decisiones ADR-001 a ADR-005.
- [Estado y backlog](docs/backlog.md): funciones implementadas, evidencia histórica, pendientes y entregas con criterios de finalización.

El backend de fase 1 está mayormente implementado y tiene pendientes de validación y corrección. El frontend y las fases posteriores siguen pendientes; el estado detallado se mantiene en el backlog.

## Ejecución local

Requisitos: SDK .NET 10 compatible con `global.json` (10.0.300, `latestFeature`), Docker con Compose y herramienta `dotnet ef` para ejecutar manualmente las migraciones.

Desde la raíz del repositorio:

```bash
docker compose up -d
dotnet ef database update --project src/Inventory.Persistence --startup-project src/Inventory.API
dotnet run --project src/Inventory.API --launch-profile http
```

El perfil `http` configura Development y publica la API en `http://localhost:5092`; Swagger está en `http://localhost:5092/swagger`. La API también intenta aplicar migraciones y datos iniciales al arrancar.

Para usar un puerto explícito sin el perfil de lanzamiento:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/Inventory.API --no-launch-profile
```

Administrador inicial de desarrollo: `admin@inventory.local` / `Admin#12345`, con acceso a todas las bodegas.

| Servicio | Puerto local |
|---|---|
| PostgreSQL | 5433 → 5432 del contenedor |
| Redis | 6379 |
| RabbitMQ | 5672; administración en 15672 |

El puerto 5433 evita el conflicto con PostgreSQL local que motivó la configuración original. Redis y RabbitMQ están provisionados para etapas posteriores.

## Compilación y pruebas

```bash
dotnet build InventoryErp.slnx
dotnet test InventoryErp.slnx
```

La validación pendiente con PostgreSQL real y los resultados históricos están en [Estado y backlog](docs/backlog.md).
