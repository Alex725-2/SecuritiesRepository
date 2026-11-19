using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Securities.Domain;
using Securities.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Securities.IntegrationTests;

/// <summary>Проверяет API котировок через HTTP.</summary>
public sealed class PriceApiTests
{
    /// <summary>Проверяет отказ в доступе и валидацию до обращения к базе.</summary>
    [Fact]
    public async Task PriceEndpointsEnforceAccessAndValidateInput()
    {
        await using var api = await LocalApi.Start("Host=127.0.0.1;Port=1;Database=unused;Username=unused");
        var quote = new Quote(Guid.NewGuid(), new DateOnly(2026, 11, 18), "1.25", "RUB", "Тест");
        var path = HistoryPath(quote.SecurityId);
        using var anonymousHistory = await api.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousHistory.StatusCode);
        using var anonymousWrite = await api.Client.PutAsJsonAsync("/api/prices", quote);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);

        await api.SignIn("Auditor");
        using var forbidden = await api.Client.PutAsJsonAsync("/api/prices", quote);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await api.SignIn("Operator");
        foreach (var invalid in new[] { quote with { Price = "-1" }, quote with { Currency = "rub" }, quote with { Source = " " } })
        {
            using var response = await api.Client.PutAsJsonAsync("/api/prices", invalid);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Некорректная котировка.", problem.GetProperty("title").GetString());
        }
    }

    /// <summary>Проверяет историю через API, включая создание и замену записи.</summary>
    [PostgresFact]
    public async Task ReadsEmptyAndSortedHistoryAndUpsertsByDate()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        var securityId = Guid.NewGuid();
        var otherSecurityId = Guid.NewGuid();
        await using (var db = new AccountingDb(new DbContextOptionsBuilder<AccountingDb>().UseNpgsql(connection).Options))
        {
            await DatabaseSetup.Create(db);
            db.Securities.AddRange(
                new Security { Id = securityId, ISIN = "TEST00000001", Ticker = "FIRST", Name = "Первая бумага" },
                new Security { Id = otherSecurityId, ISIN = "TEST00000002", Ticker = "SECOND", Name = "Вторая бумага" });
            await db.SaveChangesAsync();
        }

        await using var api = await LocalApi.Start(connection);
        var path = HistoryPath(securityId);
        await api.SignIn("Auditor");
        Assert.Empty((await api.Client.GetFromJsonAsync<Quote[]>(path))!);
        using var unknown = await api.Client.GetAsync(HistoryPath(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        await api.SignIn("Operator");
        var earlier = new Quote(securityId, new DateOnly(2026, 11, 17), "1.25", "RUB", "Ранняя");
        var later = earlier with { PriceDate = earlier.PriceDate.AddDays(1), Price = "2.5", Source = "Поздняя" };
        var replacement = later with { Price = "3.125", Source = "Исправленная" };
        foreach (var quote in new[] { later, earlier, replacement, later with { SecurityId = otherSecurityId } })
        {
            using var response = await api.Client.PutAsJsonAsync("/api/prices", quote);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(quote, await response.Content.ReadFromJsonAsync<Quote>());
        }

        await api.SignIn("Auditor");
        var history = await api.Client.GetFromJsonAsync<Quote[]>(path);
        Assert.Equal(new[] { replacement, earlier }, history);
    }

    private static string HistoryPath(Guid securityId) => $"/api/securities/{securityId}/prices";

    private sealed record Quote(Guid SecurityId, DateOnly PriceDate, string Price, string Currency, string Source);

    private sealed class LocalApi(Process process, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<LocalApi> Start(string connection)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var url = $"http://127.0.0.1:{port}";
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Securities.Api.dll"));
            start.Environment["ASPNETCORE_URLS"] = url;
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__Database"] = connection;
            start.Environment["Jwt__Key"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            var process = new Process { StartInfo = start };
            var logs = new ConcurrentQueue<string>();
            process.OutputDataReceived += (_, args) => { if (args.Data is not null) logs.Enqueue(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (args.Data is not null) logs.Enqueue(args.Data); };
            process.Start();
            var api = new LocalApi(process, new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) });
            try
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (!process.HasExited && !timeout.IsCancellationRequested)
                {
                    try
                    {
                        using var ready = await api.Client.GetAsync("/swagger/v1/swagger.json", timeout.Token);
                        if (ready.IsSuccessStatusCode) return api;
                    }
                    catch (HttpRequestException) { }
                    await Task.Delay(100, timeout.Token);
                }
                throw new InvalidOperationException("Test API did not start. " + string.Join(Environment.NewLine, logs));
            }
            catch
            {
                await api.DisposeAsync();
                throw;
            }
        }

        public async Task SignIn(string role)
        {
            Client.DefaultRequestHeaders.Authorization = null;
            using var response = await Client.PostAsJsonAsync("/api/dev/token", new { role });
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<JsonElement>();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            process.Dispose();
        }
    }
}
