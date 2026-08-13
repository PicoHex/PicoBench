# Pruebas

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

Pruebas unitarias para **PicoBench** usando el framework de pruebas [TUnit](https://github.com/thomhurst/TUnit).

**Total: 518 pruebas**

## Ejecución

```bash
dotnet run --project tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
```

## Categorías de Pruebas

### Formatters/ (249 pruebas)

Pruebas para los cuatro formateadores de salida basados en `IFormatter`, `SummaryFormatter` y su infraestructura de soporte.

| Archivo | Pruebas | Descripción |
|------|-------|-------------|
| `ConsoleFormatterTests.cs` | 41 | Generación de tablas con dibujo de cajas, alineación, codificación |
| `MarkdownFormatterTests.cs` | 28 | Renderizado de tablas Markdown de GitHub |
| `HtmlFormatterTests.cs` | 36 | Generación de informes HTML con estilos |
| `CsvFormatterTests.cs` | 31 | Exportación CSV con escape adecuado |
| `SummaryFormatterTests.cs` | 25 | Texto de resumen de ganancias/pérdidas |
| `FormatterBaseTests.cs` | 37 | Comportamiento de clase base Template Method |
| `FormatterOptionsTests.cs` | 42 | Valores predeterminados de opciones, preajustes, resolución de rutas |
| `CrossPlatformTests.cs` | 9 | Consistencia de fin de línea y codificación |

### Formatters/Integration/ (6 pruebas)

| Archivo | Pruebas | Descripción |
|------|-------|-------------|
| `FormatterIntegrationTests.cs` | 6 | Formateo de extremo a extremo de objetos `BenchmarkSuite` completos |

### Attributes/ (18 pruebas)

| Archivo | Pruebas | Descripción |
|------|-------|-------------|
| `AttributeTests.cs` | 18 | Los siete atributos: valores predeterminados, configuración de propiedades, objetivos `AttributeUsage`, almacenamiento de valores `[Params]` |

### BenchmarkRunnerTests.cs (11 pruebas)

| Archivo | Pruebas | Descripción |
|------|-------|-------------|
| `BenchmarkRunnerTests.cs` | 11 | `BenchmarkRunner.Run<T>()` con instancia sin parámetros / preconfigurada, comprobaciones de nulos, propagación de configuración |

### Generators/ (90 pruebas)

| Archivo | Pruebas | Descripción |
|------|-------|-------------|
| `EmitterTests.cs` | 38 | Emisión de código del generador de código fuente: estructura de clase, iteración de parámetros, ganchos de configuración/limpieza, comparaciones de línea base, calificación `global::` |
| `ModelsTests.cs` | 30 | Igualdad de `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel`, códigos hash, casos límite |
| `BenchmarkGeneratorDiagnosticsTests.cs` | 22 | Diagnósticos end-to-end del generador para firmas inválidas, baselines duplicadas, `[Params]` inválidos y emisión de parámetros enum |

### Cobertura del runtime principal

| Archivo | Pruebas | Descripción |
|------|-------|-------------|
| `BenchmarkTests.cs` | 56 | API imperativa, ejecución scoped, muestras retenidas, comparaciones y comportamiento de auto-calibración |
| `StatisticsCalculatorTests.cs` | 12 | Cálculo estadístico incluyendo error estándar, ciclos de CPU y casos límite |
| `ModelsTests.cs` | 38 | Validación del modelo de resultados, metadatos del contador de CPU y ayudas de varianza |

Las pruebas de formateadores ahora también cubren salidas orientadas a la precisión como error estándar, desviación estándar relativa y notas del contador de CPU en Console, Markdown, HTML y CSV.

### TestData/

Clases de fábrica para construir fixtures de prueba consistentes:

| Archivo | Propósito |
|------|---------|
| `BenchmarkResultFactory.cs` | Crea instancias de `BenchmarkResult` con valores predeterminados sensatos |
| `BenchmarkSuiteFactory.cs` | Crea `BenchmarkSuite` con resultados y comparaciones |
| `ComparisonResultFactory.cs` | Crea pares de `ComparisonResult` |
| `GcInfoFactory.cs` | Crea registros de `GcInfo` |
| `StatisticsFactory.cs` | Crea `Statistics` con distribuciones realistas |

### Utilities/

| Archivo | Propósito |
|------|---------|
| `FileSystemHelper.cs` | Gestión de directorios temporales para pruebas de salida de archivos |
| `TestContextLogger.cs` | Ayudante de registro para el contexto de prueba TUnit |
