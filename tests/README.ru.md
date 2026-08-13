# Тесты

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Русский](README.ru.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Português (Brasil)](README.pt-BR.md)

Модульные тесты для **PicoBench** с использованием фреймворка тестирования [TUnit](https://github.com/thomhurst/TUnit).

**Всего: 518 тестов**

## Запуск

```bash
dotnet run --project tests/PicoBench.Tests/PicoBench.Tests.csproj -c Debug
```

## Категории тестов

### Formatters/ (249 теста)

Тесты для четырёх форматтеров вывода на базе `IFormatter`, `SummaryFormatter` и их поддерживающей инфраструктуры.

| Файл | Тесты | Описание |
|------|-------|-------------|
| `ConsoleFormatterTests.cs` | 41 | Генерация таблиц с рисованием рамок, выравнивание, кодировка |
| `MarkdownFormatterTests.cs` | 28 | Рендеринг Markdown таблиц GitHub |
| `HtmlFormatterTests.cs` | 36 | Генерация HTML отчетов со стилями |
| `CsvFormatterTests.cs` | 31 | Экспорт CSV с правильным экранированием |
| `SummaryFormatterTests.cs` | 25 | Текст сводки побед/поражений |
| `FormatterBaseTests.cs` | 37 | Поведение базового класса Template Method |
| `FormatterOptionsTests.cs` | 42 | Значения по умолчанию опций, предустановки, разрешение путей |
| `CrossPlatformTests.cs` | 9 | Согласованность конца строк и кодировки |

### Formatters/Integration/ (6 тестов)

| Файл | Тесты | Описание |
|------|-------|-------------|
| `FormatterIntegrationTests.cs` | 6 | Сквозное форматирование полных объектов `BenchmarkSuite` |

### Attributes/ (18 тестов)

| Файл | Тесты | Описание |
|------|-------|-------------|
| `AttributeTests.cs` | 18 | Все семь атрибутов: значения по умолчанию, установка свойств, цели `AttributeUsage`, хранение значений `[Params]` |

### BenchmarkRunnerTests.cs (11 тестов)

| Файл | Тесты | Описание |
|------|-------|-------------|
| `BenchmarkRunnerTests.cs` | 11 | `BenchmarkRunner.Run<T>()` с экземпляром без параметров / предварительно настроенным, проверки null, распространение конфигурации |

### Generators/ (90 тестов)

| Файл | Тесты | Описание |
|------|-------|-------------|
| `EmitterTests.cs` | 38 | Генерация кода генератором исходного кода: структура класса, итерация параметров, хуки настройки/очистки, сравнения базовых вариантов, квалификация `global::` |
| `ModelsTests.cs` | 30 | Равенство `BenchmarkClassModel`, `BenchmarkMethodModel`, `ParamsPropertyModel`, хэш-коды, граничные случаи |
| `BenchmarkGeneratorDiagnosticsTests.cs` | 22 | Сквозные диагностики генератора для неверных сигнатур, дублирующихся baseline, неверных `[Params]` и генерации enum-параметров |

### Покрытие основного runtime

| Файл | Тесты | Описание |
|------|-------|-------------|
| `BenchmarkTests.cs` | 56 | Императивный API, scoped-выполнение, сохранение образцов, сравнения и поведение автокалибровки |
| `StatisticsCalculatorTests.cs` | 12 | Статистические вычисления, включая стандартную ошибку, циклы CPU и граничные случаи |
| `ModelsTests.cs` | 38 | Проверка модели результатов, метаданные счетчика CPU и вспомогательные свойства вариативности |

Тесты форматтеров теперь также покрывают ориентированный на точность вывод: стандартную ошибку, относительное стандартное отклонение и пояснения по счетчику CPU в Console, Markdown, HTML и CSV.

### TestData/

Фабричные классы для построения согласованных тестовых фикстур:

| Файл | Назначение |
|------|---------|
| `BenchmarkResultFactory.cs` | Создает экземпляры `BenchmarkResult` с разумными значениями по умолчанию |
| `BenchmarkSuiteFactory.cs` | Создает `BenchmarkSuite` с результатами и сравнениями |
| `ComparisonResultFactory.cs` | Создает пары `ComparisonResult` |
| `GcInfoFactory.cs` | Создает записи `GcInfo` |
| `StatisticsFactory.cs` | Создает `Statistics` с реалистичными распределениями |

### Utilities/

| Файл | Назначение |
|------|---------|
| `FileSystemHelper.cs` | Управление временными директориями для тестов с выводом в файлы |
| `TestContextLogger.cs` | Помощник ведения журнала для контекста тестов TUnit |
