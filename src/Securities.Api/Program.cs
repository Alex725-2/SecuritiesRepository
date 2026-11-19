using Microsoft.AspNetCore.DataProtection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Securities.Application;
using Securities.Domain;
using Securities.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
// JWT не использует cookie-ключи. Служебные ключи ASP.NET изолированы в temp,
// чтобы запуск не зависел от системного профиля и Windows DPAPI.
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "securities-demo-protection")));
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var connection = DatabaseConnection.Resolve(builder.Configuration["ConnectionStrings:Database"], builder.Configuration["Database:Password"]) ?? throw new InvalidOperationException("Задайте ConnectionStrings__Database.");
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Задайте Jwt__Key (минимум 32 байта).");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Ключ JWT слишком короткий.");
builder.Services.AddDbContext<AccountingDb>(o => o.UseNpgsql(connection));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new DecimalJsonConverter()));
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = "securities-demo", ValidateAudience = true, ValidAudience = "securities-demo", ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(10) });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Read", p => p.RequireRole("Admin", "Operator", "Auditor"));
    o.AddPolicy("Write", p => p.RequireRole("Admin", "Operator"));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(ApiDocumentation.Configure);
var app = builder.Build();

// Централизоанный MiddleWare для перехвата ошибок
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        if (ctx.RequestAborted.IsCancellationRequested)
            return;
        var status = ex switch
        {
            BusinessException e => e.Status,
            BadHttpRequestException => 400,
            DbUpdateConcurrencyException => 409,
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" or "23503" or "23514" } } => 409,
            Npgsql.PostgresException { SqlState: "23505" or "23503" or "23514" } => 409,
            Npgsql.NpgsqlException { IsTransient: true } => 503,
            DbUpdateException { InnerException: Npgsql.NpgsqlException { IsTransient: true } } => 503,
            OverflowException => 422,
            _ => 500
        };
        if (status is 500 or 503)
            app.Logger.LogError(ex, "Ошибка запроса.");
        // тут особо круто, 500 и 503 - ошибки сервера, могут например содержать таблицы или другую важную информацию, такое клиенту
        // показывать нельзя
        await Results.Problem(statusCode: status, title: status is 500 or 503 ? "Внутренняя ошибка сервера." : ex is DbUpdateException ? "Конфликт ограничений данных." : ex.Message).ExecuteAsync(ctx);
    }
});
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Demo:Swagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Environment.IsDevelopment())
    app.MapPost("/api/dev/token", (DevLogin login) =>
    {
        if (login.Role is not ("Admin" or "Operator" or "Auditor"))
            return Results.BadRequest(new { error = "Неизвестная роль." });
        var token = new JwtSecurityToken("securities-demo", "securities-demo", [new Claim(ClaimTypes.Name, "Демонстрационный пользователь"), new Claim(ClaimTypes.Role, login.Role)], expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256));
        return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token) });
    })
    .WithSummary("Получить демонстрационный JWT")
    .WithDescription("Доступно только в Development. Токен действует один час.")
    .Produces<TokenResponse>();
app.MapGet("/api/health", async (AccountingDb db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "Healthy" }) : Results.StatusCode(503))
    .WithSummary("Проверить доступность базы данных")
    .WithDescription("Проверяет подключение API к PostgreSQL.")
    .Produces<HealthResponse>();
var read = app.MapGroup("/api").RequireAuthorization("Read");
read.MapGet("/management-companies", async (AccountingDb db, CancellationToken ct) => await db.Set<ManagementCompany>().AsNoTracking().ToListAsync(ct))
    .WithSummary("Получить управляющие компании")
    .WithDescription("Возвращает справочник управляющих компаний фондов.");
read.MapGet("/funds", async (AccountingDb db, CancellationToken ct) => await db.Funds.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct))
    .WithSummary("Получить фонды")
    .WithDescription("Возвращает фонды в порядке их кодов.");
read.MapGet("/funds/{id:guid}", async (Guid id, AccountingDb db, PortfolioService service, CancellationToken ct) =>
{
    var fund = await db.Funds.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    if (fund is null)
        return Results.NotFound();
    var units = await service.UnitsOnDate(id, DateOnly.FromDateTime(DateTime.UtcNow), ct);
    return Results.Ok(new FundDetailsResponse(fund.Id, fund.ManagementCompanyId, fund.Code,
        fund.Name, fund.FundType, fund.Status, units));
})
    .WithSummary("Получить карточку фонда")
    .WithDescription("Возвращает реквизиты фонда и количество паёв, действующее на текущую дату UTC.")
    .Produces<FundDetailsResponse>();
read.MapGet("/funds/{id:guid}/positions", async (Guid id, AccountingDb db, CancellationToken ct) => await (
    from p in db.Positions.AsNoTracking()join a in db.Accounts on p.CustodyAccountId equals a.Id
    join s in db.Securities on p.SecurityId equals s.Id
    where a.FundId == id
    select new { p.CustodyAccountId, a.AccountNumber, p.SecurityId, s.Ticker, s.Name, p.Quantity, p.Version }).ToListAsync(ct))
    .WithSummary("Получить позиции фонда")
    .WithDescription("Возвращает текущие остатки по парам счёт хранения и ценная бумага. Позиции изменяются при проведении операций.")
    .Produces<List<PositionResponse>>();
read.MapGet("/funds/{id:guid}/operations", async (Guid id, int? offset, int? limit, AccountingDb db, CancellationToken ct) => await db.Operations.AsNoTracking().Where(x => x.FundId == id).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(Math.Max(0, offset ?? 0)).Take(Math.Clamp(limit ?? 100, 1, 200)).ToListAsync(ct))
    .WithSummary("Получить журнал операций фонда")
    .WithDescription("Возвращает операции от новых к старым, при совпадении времени сортирует по идентификатору. Поддерживает пагинацию.");
read.MapGet("/securities", async (AccountingDb db, CancellationToken ct) => await db.Securities.AsNoTracking().ToListAsync(ct))
    .WithSummary("Получить ценные бумаги")
    .WithDescription("Возвращает справочник, включая неактивные бумаги.");
read.MapGet("/custodians", async (AccountingDb db, CancellationToken ct) => await db.Set<Custodian>().AsNoTracking().ToListAsync(ct))
    .WithSummary("Получить депозитарии")
    .WithDescription("Возвращает справочник организаций, в которых открыты счета хранения.");
read.MapGet("/accounts", async (AccountingDb db, CancellationToken ct) => await db.Accounts.AsNoTracking().ToListAsync(ct))
    .WithSummary("Получить счета хранения")
    .WithDescription("Возвращает счета всех фондов с указанием фонда, депозитария и статуса.");
read.MapGet("/operations/{id:guid}", async (Guid id, AccountingDb db, CancellationToken ct) => await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)is { } operation ? Results.Ok(new { Operation = operation, Entries = await db.Entries.AsNoTracking().Where(x => x.OperationId == id).ToListAsync(ct) }) : Results.NotFound())
    .WithSummary("Получить операцию и её проводки")
    .WithDescription("Возвращает запись журнала и проводки со знаковым изменением количества по каждому затронутому счёту.")
    .Produces<OperationDetailsResponse>();
var write = app.MapGroup("/api").RequireAuthorization("Write");
write.MapPost("/operations/credit", (MovementRequest r, AccountingService s, CancellationToken ct) => s.Execute("Credit", r, ct))
    .WithSummary("Зачислить бумаги")
    .WithDescription("Credit увеличивает остаток на выбранном счёте одной проводкой. Укажите внешний источник и документ-основание.");
write.MapPost("/operations/debit", (MovementRequest r, AccountingService s, CancellationToken ct) => s.Execute("Debit", r, ct))
    .WithSummary("Списать бумаги")
    .WithDescription("Debit уменьшает остаток на выбранном счёте одной проводкой. Укажите внешнего получателя и документ-основание. При недостаточном остатке возвращается 422.");
write.MapPost("/operations/transfer", (MovementRequest r, AccountingService s, CancellationToken ct) => s.Execute("Transfer", r, ct))
    .WithSummary("Перевести бумаги между счетами")
    .WithDescription("Transfer списывает бумаги с одного счёта и зачисляет на другой счёт того же фонда. Две проводки имеют равные по модулю количества и противоположные знаки.");
write.MapPost("/operations/{id:guid}/reverse", (Guid id, ReverseRequest r, AccountingService s, CancellationToken ct) => s.Reverse(id, r, ct))
    .WithSummary("Скорректировать операцию")
    .WithDescription("Reverse создаёт проводки с обратными знаками и сохраняет исходную операцию. Исходную операцию можно скорректировать один раз. Требуются причина и достаточный остаток для обратного движения.");
write.MapPost("/reconciliations", (ReconcileRequest r, PortfolioService s, CancellationToken ct) => s.Reconcile(r, ct))
    .WithSummary("Выполнить сверку фонда")
    .WithDescription("Сравнивает полный контрольный набор с текущими остатками. Пропущенная пара считается нулевой, дубликаты и отрицательные количества отклоняются. Результат сохраняется без изменения позиций.");
read.MapGet("/reconciliations/{id:guid}", async (Guid id, AccountingDb db, CancellationToken ct) => await db.Set<ReconciliationRun>().Include(x => x.Items).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)is { } run ? Results.Ok(run) : Results.NotFound())
    .WithSummary("Получить результат сверки")
    .WithDescription("Возвращает сохранённую сверку с контрольными и внутренними остатками.")
    .Produces<ReconciliationRun>();
read.MapGet("/funds/{id:guid}/reconciliations", async (Guid id, AccountingDb db, CancellationToken ct) => await db.Set<ReconciliationRun>().Include(x => x.Items).AsNoTracking().Where(x => x.FundId == id).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct))
    .WithSummary("Получить сверки фонда")
    .WithDescription("Возвращает до 20 последних сверок фонда вместе со строками сравнения.");
write.MapPost("/valuations", (ValuationRequest r, PortfolioService s, CancellationToken ct) => s.Value(r, ct))
    .WithSummary("Рассчитать стоимость фонда и пая")
    .WithDescription("Восстанавливает позиции по журналу на конец даты UTC. Использует котировки на эту дату в единой валюте и последние доступные на дату данные о паях. Сохраняет оценку по паре фонд и дата.")
    .Produces<ValuationResponse>();
read.MapGet("/funds/{id:guid}/valuations", async (Guid id, AccountingDb db, CancellationToken ct) => await db.Set<FundValuation>().AsNoTracking().Where(x => x.FundId == id).OrderByDescending(x => x.ValuationDate).Take(100).ToListAsync(ct))
    .WithSummary("Получить оценки фонда")
    .WithDescription("Возвращает до 100 сохранённых оценок, начиная с самой поздней даты.");
write.MapPut("/prices", async (SecurityPrice r, AccountingDb db, CancellationToken ct) =>
{
    if (r.Price < 0 || string.IsNullOrWhiteSpace(r.Source) || (string.IsNullOrWhiteSpace(r.Currency) || !System.Text.RegularExpressions.Regex.IsMatch(r.Currency, "^[A-Z]{3}$")))
        throw new BusinessException("Некорректная котировка.");
    var existing = await db.Set<SecurityPrice>().FindAsync([r.SecurityId, r.PriceDate], ct);
    if (existing is null)
        db.Add(r);
    else
        db.Entry(existing).CurrentValues.SetValues(r);
    await db.SaveChangesAsync(ct);
    return Results.Ok(r);
})
    .WithSummary("Сохранить котировку")
    .WithDescription("Добавляет или заменяет котировку бумаги на указанную дату. Цена неотрицательна, источник обязателен, валюта задаётся тремя заглавными латинскими буквами.")
    .Produces<SecurityPrice>();
write.MapPut("/fund-units", async (FundUnits r, AccountingDb db, CancellationToken ct) =>
{
    AccountingRules.Positive(r.UnitsOutstanding);
    var existing = await db.Set<FundUnits>().FindAsync([r.FundId, r.EffectiveDate], ct);
    if (existing is null)
        db.Add(r);
    else
        existing.UnitsOutstanding = r.UnitsOutstanding;
    await db.SaveChangesAsync(ct);
    return Results.Ok(r);
})
    .WithSummary("Сохранить количество паёв")
    .WithDescription("Добавляет или заменяет положительное количество паёв фонда на дату начала действия.")
    .Produces<FundUnits>();
if (args.Contains("--schema"))
{
    await using var scope = app.Services.CreateAsyncScope();
    Console.WriteLine(scope.ServiceProvider.GetRequiredService<AccountingDb>().Database.GenerateCreateScript() + DatabaseSetup.IntegritySql);
    return;
}

if (args.Contains("--init"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AccountingDb>();
    await DatabaseSetup.Create(db);
    await DemoSeed.Run(db);
    return;
}

await app.RunAsync();
/// <summary>Запрос тестовой роли; доступен только в Development.</summary>
/// <param name="Role">Роль токена: Admin, Operator или Auditor.</param>
public record DevLogin(string Role);
