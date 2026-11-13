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
dotnet ef database update --project src/Inventory.Persistence --startup-project src/Inventory.API
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

La validación del punto 1 no cierra los pendientes de autorización, auditoría o concurrencia del punto 2 del backlog.
