# Inventory ERP

Sistema de inventario para múltiples bodegas. Backend modular con destino .NET 10, SDK .NET 11 y PostgreSQL.

## Documentación

- [Producto](docs/producto.md): requisitos y reglas de negocio.
- [Arquitectura](docs/arquitectura.md): estructura y decisiones técnicas.
- [Estado y backlog](docs/backlog.md): alcance, pendientes y evidencia de validación.

## Ejecución local con PostgreSQL

Requisitos: SDK indicado en `global.json`, runtime ASP.NET Core 10 y PostgreSQL 16 o superior. Para los scripts se necesitan Bash y `psql`. El usuario PostgreSQL debe poder crear bases (`CREATEDB`); las pruebas crean y eliminan únicamente una base temporal con nombre `inventario_erp_test_<uuid>`.

```bash
cp .env.example .env # Completar la contraseña; si ya existe .env, conservarlo.
bash scripts/init-db.sh
source .env
dotnet run --project src/Inventory.API --launch-profile http
```

La configuración local usa `localhost:5432`, el usuario `ozkar` y la base `inventario_erp`. Las credenciales se guardan en `.env`, excluido de Git. En este entorno ya está configurado con las credenciales proporcionadas. `init-db.sh` crea la base si no existe y aplica migraciones y datos iniciales sin levantar el servidor HTTP. Es idempotente y no elimina datos existentes. El arranque de la API también migra y siembra; una falla detiene el inicio y devuelve un código de error.

La API se publica en `http://localhost:5092` y Swagger en `/swagger`. Administrador inicial de desarrollo: `admin@inventory.local` / `Admin#12345`, con acceso a todas las bodegas.

La cadena `ConnectionStrings__Postgres` configura tanto la API como `dotnet ef`. La factoría de diseño también admite `POSTGRES_CONNECTION` por compatibilidad. Para migrar manualmente con la herramienta EF instalada:

```bash
source .env
dotnet ef database update --project src/Inventory.Persistence --startup-project src/Inventory.Persistence
```

Como alternativa a PostgreSQL instalado localmente, `docker compose up -d postgres` provisiona PostgreSQL 16 en el puerto **5433** con las variables `PGUSER`, `PGPASSWORD` y `PGDATABASE` de `.env`. Cambiar `PGPORT` y los puertos de ambas cadenas en `.env` a 5433 antes de ejecutar los scripts. Las variables de inicialización del contenedor solo se aplican a volúmenes nuevos. Redis y RabbitMQ están disponibles en Compose para etapas posteriores.

## Validación reproducible

```bash
bash scripts/validate.sh
```

El script restaura dependencias, compila Release y ejecuta todas las pruebas. `TEST_POSTGRES_CONNECTION` debe señalar el servidor PostgreSQL y un rol con `CREATEDB`; se carga desde `.env` si existe. La suite **falla** si PostgreSQL o esa variable no están disponibles.

Cada ejecución crea una base vacía, aplica las migraciones reales y verifica el catálogo de permisos, roles y administrador, incluida la idempotencia del seeder. Ejecuta la API con `WebApplicationFactory`, JWT, autorización e interceptor reales; cubre login, catálogos, producto, dos bodegas, entrada de 100, salida de 30 y transferencia de 20. Verifica existencias finales de 50 y 20, movimientos persistidos y ambas caras del kardex desde solicitudes y contextos independientes. Al terminar elimina la base temporal; `inventario_erp` no recibe datos de prueba. Si el proceso se interrumpe abruptamente puede quedar una base temporal para limpieza manual.

Los resultados TRX quedan en `TestResults/`. GitHub Actions ejecuta el mismo script en cada push y pull request, con PostgreSQL 16 como servicio efímero y credenciales exclusivas de CI; publica los TRX como artefacto.

Para ejecutar únicamente las pruebas existentes sin PostgreSQL:

```bash
dotnet test tests/Inventory.Domain.Tests
dotnet test tests/Inventory.Authorization.Tests
dotnet test tests/Inventory.Application.Tests
```

## Contrato de movimientos e integridad

Los cuatro POST de `/api/v1/inventory` requieren `idempotencyKey`, un UUID no vacío generado por el cliente para cada operación. Ejemplo de entrada:

```json
{
  "idempotencyKey": "abf9c832-554d-4461-8515-a40a2048ecbb",
  "warehouseId": "9c90e0c8-bcaf-4e3a-b191-3a179560e3e9",
  "lines": [{ "productId": "f85dcbdf-b38e-4ca1-bacd-a28041db5044", "quantity": 10, "unitCost": 2 }]
}
```

Conservar esa clave y el contenido del comando al reintentar. La clave se reserva por usuario entre los cuatro tipos de movimiento y permanece mientras exista el movimiento. Una repetición confirmada devuelve `200` con el movimiento original, sin cambiar stock ni añadir auditorías. Reutilizar la clave con otro contenido o tipo devuelve `409`. Los movimientos históricos anteriores a esta migración no tienen clave.

Un conflicto de escritura simultánea devuelve `409 ProblemDetails` y revierte toda la operación. El cliente puede reintentar la solicitud completa con la misma clave y contenido, usando una espera entre intentos. Esto también aplica a duplicados que llegan simultáneamente: uno confirma y el otro puede recibir `409`; su siguiente intento devuelve el resultado confirmado. Un reintento se valida contra el saldo actual y puede recibir `422`, por ejemplo si ya no hay stock suficiente. Las solicitudes fallidas no consumen su clave.

Cada producto puede aparecer una sola vez en un movimiento; los duplicados y las claves ausentes devuelven `400`. Cantidades y costos deben caber en `numeric(18,4)` (hasta 14 dígitos enteros y 4 decimales); se rechaza el exceso de precisión para evitar redondeos que desajusten el kardex. Los ceros decimales finales no cambian la huella de idempotencia: `10` y `10.0` son equivalentes. Los ajustes conservan el contrato de cantidad absoluta positiva y justificación obligatoria. Una transferencia exige acceso a ambas bodegas. Todas las solicitudes, incluidos los reintentos, comprueban los permisos y alcances actuales. Las consultas sin bodega devuelven únicamente datos autorizados; consultar explícitamente una bodega fuera de alcance devuelve `403`.

La migración `InventoryIntegrity` incorpora versiones de stock, la unicidad de claves por usuario y una secuencia para reconstruir el kardex independientemente del reloj de las solicitudes. Conserva el orden histórico anterior y limpia campos de contraseñas, hashes y tokens previamente guardados en auditoría. Esta limpieza de datos sensibles es irreversible; la auditoría nueva usa una lista explícita de propiedades permitidas.

La suite PostgreSQL incluye aislamiento por bodega, Allow/Deny, limpieza de auditoría, actualización de una base existente, movimientos simultáneos, reintentos y errores en ajustes y transferencias. Las pruebas coordinan dos solicitudes antes de guardar para comprobar conflictos reales, tanto con stock existente como al crearlo. El límite de solicitudes se desactiva solo en el servidor de pruebas para permitir ejecutar la suite; autenticación, autorización, PostgreSQL e interceptor siguen activos.
