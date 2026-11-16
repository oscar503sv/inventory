# Kardex: plan antes y después de OZK-66

Escenario de `KardexQueryPlanTests`: historial de 500 entradas de una unidad para un producto y una bodega, ordenadas por `LedgerSequence`; primera página de 200 filas. La prueba crea una base PostgreSQL temporal, prepara estadísticas con `ANALYZE`, ejecuta la consulta por la API y después obtiene `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` de la consulta de saldos con sus parámetros originales.

| Medida de una ejecución | Consulta anterior de `ad26264` | Funciones de ventana |
| --- | ---: | ---: |
| Filas devueltas | 200 | 200 |
| Tiempo de ejecución PostgreSQL (ms) | 26,868 | 1,906 |
| Tiempo de planificación (ms) | 1,742 | 1,272 |
| Bloques compartidos encontrados en caché (nodo raíz) | 5056 | 24 |
| Máximo `Actual Loops` de un nodo | 200 | 1 |
| Subplanes correlacionados | 4 | 0 |
| Nodos `WindowAgg` | 0 | 3 |

Son observaciones de una ejecución sobre datos sintéticos, sin umbral de tiempo en las pruebas. No miden percentiles, la latencia HTTP ni la consulta separada del total. Los tiempos dependen de carga, caché, estadísticas y hardware; el cambio verificable del plan es la eliminación de los subplanes correlacionados.

La lectura nueva delimita primero el historial del producto y bodega. Una ventana identifica segmentos que empiezan en cada ajuste, otra acumula el saldo dentro de cada segmento y `LAG` calcula el cambio relativo de un ajuste. Los filtros de fecha y la paginación se aplican después de estas ventanas para conservar el saldo de apertura, incluso con ajustes excluidos por fecha o relojes regresivos. Véase [funciones de ventana de PostgreSQL](https://www.postgresql.org/docs/18/functions-window.html).

Para obtener el plan actual, configurar `TEST_POSTGRES_CONNECTION` como en el README y ejecutar desde la raíz:

```bash
KARDEX_PLAN_OUTPUT=/tmp/kardex-plan.json dotnet test tests/Inventory.API.IntegrationTests/Inventory.API.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~KardexQueryPlanTests
```

Para repetir la referencia anterior, usar un checkout aislado de `ad26264` y copiar únicamente `KardexQueryPlanTests.cs` de esta versión a su proyecto de integración. Ejecutar el mismo comando con otra ruta de salida. El archivo de salida contiene el plan completo de la base temporal; no incluye credenciales de conexión.
