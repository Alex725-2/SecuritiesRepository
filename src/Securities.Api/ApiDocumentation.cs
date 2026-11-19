using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Securities.Application;
using Securities.Domain;
using Swashbuckle.AspNetCore.SwaggerGen;

internal sealed class ApiDocumentation : IOperationFilter
{
    private const string FundId = "20000000-0000-0000-0000-000000000001";
    private const string CompanyId = "10000000-0000-0000-0000-000000000001";
    private const string CustodianId = "30000000-0000-0000-0000-000000000001";
    private const string RunId = "80000000-0000-0000-0000-000000000001";
    private const string SecurityId = "40000000-0000-0000-0000-000000000001";
    private const string AccountId = "50000000-0000-0000-0000-000000000001";
    private const string TargetId = "50000000-0000-0000-0000-000000000002";
    private const string OperationId = "60000000-0000-0000-0000-000000000001";
    private const string Date = "2026-11-16";
    private const string Timestamp = Date + "T12:00:00Z";

    internal static void Configure(SwaggerGenOptions options)
    {
        options.MapType<decimal>(() => new OpenApiSchema
        {
            Type = "string", Format = "decimal", Description = "Десятичное значение передаётся JSON-строкой.",
            Example = new OpenApiString("25")
        });
        foreach (var assembly in new[] { typeof(DevLogin).Assembly, typeof(MovementRequest).Assembly, typeof(Operation).Assembly })
            options.IncludeXmlComments(Path.ChangeExtension(assembly.Location, ".xml"));

        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Демонстрационный учёт фондов",
            Version = "v1",
            Description = "Admin и Operator могут читать данные и выполнять команды. Auditor имеет доступ только на чтение. " +
                "Операция хранит реквизиты движения, проводки фиксируют изменения количества, позиции показывают текущий остаток. " +
                "Операция, проводки и изменения позиций сохраняются одной транзакцией."
        });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
            Description = "Вставьте JWT в поле Authorize. Swagger отправит заголовок Authorization: Bearer <JWT>. " +
                "В Development демонстрационный токен выдаёт POST /api/dev/token."
        });
        options.OperationFilter<ApiDocumentation>();
    }

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath?.Replace(":guid}", "}", StringComparison.Ordinal);
        var policy = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>()
            .Select(metadata => metadata.Policy).FirstOrDefault();
        var write = policy == "Write";
        if (policy is "Read" or "Write")
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
            });
            operation.Description += write ? " Роли: Admin, Operator." : " Роли: Admin, Operator, Auditor.";
            operation.Responses["401"] = new() { Description = "Требуется действительный JWT." };
            operation.Responses["403"] = new() { Description = "Роль токена не даёт доступ к этому действию." };
        }

        foreach (var parameter in operation.Parameters)
        {
            parameter.Description = parameter.Name switch
            {
                "offset" => "Количество пропускаемых записей. По умолчанию 0, отрицательное значение заменяется на 0.",
                "limit" => "Размер страницы. По умолчанию 100, значение ограничивается диапазоном 1–200.",
                "id" when path?.StartsWith("api/funds/") == true => "Идентификатор фонда (UUID).",
                "id" when path == "api/securities/{id}/prices" => "Идентификатор ценной бумаги (UUID).",
                "id" when path?.StartsWith("api/operations/") == true => "Идентификатор операции (UUID).",
                "id" when path == "api/reconciliations/{id}" => "Идентификатор сохранённой сверки (UUID).",
                _ => parameter.Description
            };
        }

        if (operation.Responses.TryGetValue("200", out var success))
            success.Description = "Запрос выполнен успешно.";
        var problemSchema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        if (write)
        {
            operation.Responses["400"] = new() { Description = "Некорректное тело или реквизиты запроса." };
            Problem(operation, "409", "Конфликт ключа или ограничений данных.", problemSchema);
            Problem(operation, "422", "Нарушено правило учёта.", problemSchema,
                path == "api/operations/debit" ? new { title = "Недостаточный остаток.", status = 422 } : null);
        }
        if (path is "api/funds/{id}" or "api/operations/{id}" or "api/reconciliations/{id}")
            operation.Responses["404"] = new() { Description = "Запись не найдена." };
        if (path is "api/operations/credit" or "api/operations/debit" or "api/operations/transfer"
            or "api/operations/{id}/reverse" or "api/reconciliations" or "api/valuations")
            Problem(operation, "404", "Связанная запись не найдена.", problemSchema);
        if (path == "api/dev/token")
            operation.Responses["400"] = new() { Description = "Неизвестная роль или некорректное тело запроса." };
        if (path == "api/health")
            operation.Responses["503"] = new() { Description = "Подключиться к PostgreSQL не удалось." };

        AddExamples(operation, path);
    }

    private static void Problem(OpenApiOperation operation, string status, string description, OpenApiSchema schema, object? example = null)
    {
        operation.Responses[status] = new()
        {
            Description = description,
            Content = { ["application/problem+json"] = new() { Schema = schema, Example = example is null ? null : Example(example) } }
        };
    }

    private static IOpenApiAny Example(object value) =>
        OpenApiAnyFactory.CreateFromJson(System.Text.Json.JsonSerializer.Serialize(value));

    private static void AddExamples(OpenApiOperation operation, string? path)
    {
        var kind = path switch
        {
            "api/operations/credit" => "Credit",
            "api/operations/debit" => "Debit",
            "api/operations/transfer" => "Transfer",
            "api/operations/{id}/reverse" => "Reverse",
            _ => null
        };
        var key = kind is null ? null : $"demo-{kind.ToLowerInvariant()}-001";
        var party = kind == "Debit" ? "Вымышленный получатель" : "Вымышленный источник";
        var price = new { securityId = SecurityId, priceDate = Date, price = "100", currency = "RUB", source = "Демо-котировка" };
        var units = new { fundId = FundId, effectiveDate = Date, unitsOutstanding = "100" };
        var valuation = new { fundId = FundId, valuationDate = Date, totalAssets = "2500", totalLiabilities = "0", nav = "2500", currency = "RUB" };
        var fund = new { id = FundId, managementCompanyId = CompanyId, code = "DEMO", name = "Учебный фонд", fundType = "Open", status = "Active" };
        var reconciliation = new
        {
            id = RunId, fundId = FundId, createdAt = Timestamp,
            items = new[] { new { id = "90000000-0000-0000-0000-000000000001", reconciliationRunId = RunId,
                custodyAccountId = AccountId, securityId = SecurityId, expected = "25", actual = "25" } }
        };
        var balance = new { custodyAccountId = AccountId, securityId = SecurityId, quantity = "25" };
        const string reason = "Исправление ошибочного зачисления";
        object? request = kind switch
        {
            "Credit" or "Debit" => new { idempotencyKey = key, sourceAccountId = AccountId, securityId = SecurityId,
                quantity = "25", movementParty = party, documentReference = "ДЕМО-001", externalId = "EXT-001" },
            "Transfer" => new { idempotencyKey = key, sourceAccountId = AccountId, targetAccountId = TargetId, securityId = SecurityId, quantity = "25" },
            "Reverse" => new { idempotencyKey = key, reason },
            _ => path switch
            {
                "api/dev/token" => new { role = "Operator" },
                "api/reconciliations" when operation.RequestBody is not null => new { fundId = FundId, balances = new[] { balance } },
                "api/valuations" => new { fundId = FundId, date = Date, liabilities = "0", currency = "RUB" },
                "api/prices" => price,
                "api/fund-units" => units,
                _ => null
            }
        };
        object? response = kind is not null ? OperationExample(kind, key!, party, reason)
            : path switch
            {
                "api/health" => new { status = "Healthy" },
                "api/dev/token" => new { accessToken = "<JWT из ответа сервера>" },
                "api/management-companies" => new[] { new { id = CompanyId, name = "Демо УК", inn = "0000000000" } },
                "api/funds" => new[] { fund },
                "api/funds/{id}" => new { fund.id, fund.managementCompanyId, fund.code, fund.name, fund.fundType, fund.status, unitsOutstanding = "100" },
                "api/custodians" => new[] { new { id = CustodianId, code = "DEMO-CUSTODY", name = "Учебный депозитарий" } },
                "api/accounts" => new[] { new { id = AccountId, fundId = FundId, custodianId = CustodianId,
                    accountNumber = "DEMO-001", status = "Active" } },
                "api/securities" => new[] { new { id = SecurityId, isin = "DEMO00000001", ticker = "DEMO",
                    name = "Вымышленная акция", currency = "RUB", securityType = "Share", isActive = true } },
                "api/reconciliations" or "api/reconciliations/{id}" => reconciliation,
                "api/funds/{id}/reconciliations" => new[] { reconciliation },
                "api/funds/{id}/operations" => new[] { OperationExample("Credit", "demo-credit-001", party, reason) },
                "api/operations/{id}" => new
                {
                    operation = OperationExample("Credit", "demo-credit-001", party, reason),
                    entries = new[] { new { id = "70000000-0000-0000-0000-000000000001", operationId = OperationId,
                        custodyAccountId = AccountId, securityId = SecurityId, quantityDelta = "25", createdAt = Timestamp } }
                },
                "api/funds/{id}/positions" => new[] { new { custodyAccountId = AccountId, accountNumber = "DEMO-001",
                    securityId = SecurityId, ticker = "DEMO", name = "Демонстрационная акция", quantity = "25", version = 1 } },
                "api/valuations/settings" => new ValuationOptions(),
                "api/valuations" => new { valuation, unitPrice = "25", unitsOutstanding = "100", positions = new[]
                {
                    new { securityId = SecurityId, ticker = "DEMO", name = "Учебная бумага", quantity = "25", price = "100", priceDate = Date, positionValue = "2500" }
                } },
                "api/funds/{id}/valuations" => new[] { valuation },
                "api/securities/{id}/prices" => new[] { price },
                "api/prices" => price,
                "api/fund-units" => units,
                _ => null
            };
        if (request is not null && operation.RequestBody?.Content.TryGetValue("application/json", out var body) == true)
            body.Example = Example(request);
        if (response is not null && operation.Responses.TryGetValue("200", out var success)
            && success.Content.TryGetValue("application/json", out var media))
            media.Example = Example(response);
        if (kind is "Credit" or "Debit" or "Transfer")
        {
            operation.RequestBody!.Description = kind == "Transfer"
                ? "Обязательны ключ, два разных счёта одного фонда, бумага и положительное количество."
                : "Обязательны ключ, счёт хранения (sourceAccountId), бумага, положительное количество, сторона движения и документ. targetAccountId оставьте пустым.";
        }
    }

    private static object OperationExample(string kind, string key, string party, string reason) => new
    {
        id = kind == "Reverse" ? "60000000-0000-0000-0000-000000000002" : OperationId,
        type = kind,
        status = "Completed",
        externalId = kind is "Credit" or "Debit" ? "EXT-001" : null,
        idempotencyKey = key,
        fingerprint = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
        fundId = FundId,
        reversesOperationId = kind == "Reverse" ? OperationId : null,
        movementParty = kind == "Transfer" ? null : party,
        documentReference = kind == "Reverse" ? reason : kind == "Transfer" ? null : "ДЕМО-001",
        createdAt = Timestamp,
        completedAt = Timestamp,
        errorMessage = (string?)null
    };
}

// Эти схемы описывают существующие анонимные ответы. Обработчики продолжают возвращать те же объекты.
/// <summary>Токен для демонстрационного сеанса.</summary>
/// <param name="AccessToken">Подписанный JWT для заголовка Authorization.</param>
internal sealed record TokenResponse(string AccessToken);
/// <summary>Результат проверки подключения к PostgreSQL.</summary>
/// <param name="Status">Healthy при доступной базе данных.</param>
internal sealed record HealthResponse(string Status);
/// <summary>Текущая позиция с реквизитами счёта и бумаги.</summary>
/// <param name="CustodyAccountId">Идентификатор счёта хранения.</param>
/// <param name="AccountNumber">Номер счёта хранения в депозитарии.</param>
/// <param name="SecurityId">Идентификатор бумаги.</param>
/// <param name="Ticker">Биржевой тикер бумаги.</param>
/// <param name="Name">Наименование бумаги.</param>
/// <param name="Quantity">Текущий неотрицательный остаток. Передаётся JSON-строкой, например "25".</param>
/// <param name="Version">Версия позиции для контроля конкурентности.</param>
internal sealed record PositionResponse(Guid CustodyAccountId, string AccountNumber, Guid SecurityId, string Ticker, string Name, decimal Quantity, long Version);
/// <summary>Операция и её проводки.</summary>
/// <param name="Operation">Сохранённые реквизиты операции.</param>
/// <param name="Entries">Проводки со знаковым изменением количества по счетам.</param>
internal sealed record OperationDetailsResponse(Operation Operation, List<LedgerEntry> Entries);
/// <summary>Реквизиты фонда и действующее количество паёв. При отсутствии записи количество равно null.</summary>
internal sealed record FundDetailsResponse(Guid Id, Guid ManagementCompanyId, string Code, string Name,
    string FundType, string Status, decimal? UnitsOutstanding);
