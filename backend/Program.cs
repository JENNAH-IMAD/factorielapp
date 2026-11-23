using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Pointage;

if (args.Contains("--hash-password"))
{
    var password = Console.ReadLine() ?? "";
    if (password.Length < 14) throw new InvalidOperationException("Utiliser au moins 14 caractères.");
    Console.WriteLine(new PasswordHasher<string>().HashPassword("admin", password)); return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
var importOnly = builder.Configuration.GetValue<bool>("ImportFactorialProduction");
var saveFactorialKey = builder.Configuration.GetValue<bool>("SaveFactorialProductionKey");
if (importOnly || saveFactorialKey)
{
    var keyInput = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(keyInput)) builder.Configuration["Factorial:ApiKey"] = keyInput;
}
var demo = builder.Configuration.GetValue<bool>("Demo");
if (demo && !builder.Environment.IsDevelopment()) throw new InvalidOperationException("Le mode simulé exige Development.");
builder.Services.AddDbContext<AppDb>(o => { if (demo) o.UseInMemoryDatabase("pointage-demo"); else o.UseSqlServer(builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("Database non configurée.")); });
// Browser cookies are shared between ports on the same host. Also isolate ticket encryption
// so a demo cookie copied into the real cookie name cannot authenticate to the real instance.
builder.Services.AddDataProtection().SetApplicationName(demo ? "Pointage.Simulated" : "Pointage.Real");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.Cookie.Name = demo ? "pointage.demo.session" : "pointage.real.session";
    o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(4); o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireRole("Admin")));
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
builder.Services.AddSingleton<ServerCredentialProvider>();
builder.Services.AddSingleton<ICredentialProvider>(sp => sp.GetRequiredService<ServerCredentialProvider>());
builder.Services.AddSingleton<DemoHandler>();
var http = builder.Services.AddHttpClient<FactorialClient>(c => c.Timeout = TimeSpan.FromSeconds(45)).RemoveAllLoggers();
if (demo) http.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<DemoHandler>()).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
else http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<SyncService>(); builder.Services.AddScoped<OutboundService>(); builder.Services.AddHostedService<SyncWorker>();
var app = builder.Build();
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    // All mutating requests require a non-simple header. No CORS is enabled; SameSite strict cookie is additional protection.
    if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE" && context.Request.Headers["X-Pointage-Request"] != "1") { context.Response.StatusCode = 400; return; }
    try { await next(); }
    catch (AppError e) { context.Response.StatusCode = e.Status; await context.Response.WriteAsJsonAsync(new { code = e.Code, message = e.Message }); }
    catch { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { code = "internal", message = "L’opération a échoué. Vérifier la configuration et l’historique." }); }
});
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/api/session", (HttpContext ctx) => new { authenticated = ctx.User.Identity?.IsAuthenticated == true, demo, name = ctx.User.Identity?.Name });
app.MapPost("/api/login", async (LoginRequest input, HttpContext ctx, IConfiguration config) => {
    var valid = demo;
    if (!demo)
    {
        var hash = config["Admin:PasswordHash"];
        valid = !string.IsNullOrEmpty(hash) && input.Username == config["Admin:Username"] && new PasswordHasher<string>().VerifyHashedPassword("admin", hash, input.Password) != PasswordVerificationResult.Failed;
    }
    if (!valid) return Results.Json(new { message = "Identifiants incorrects ou administrateur non configuré." }, statusCode: 401);
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, demo ? "Administrateur démo" : input.Username), new Claim(ClaimTypes.Role, "Admin")], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { ok = true });
}).RequireRateLimiting("login");
app.MapPost("/api/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); });
var api = app.MapGroup("/api").RequireAuthorization("Admin");
api.MapGet("/connection", async (AppDb db, ServerCredentialProvider credentials) => {
    var connection = await db.Connections.SingleAsync();
    return new { connection, version = FactorialClient.Version, demo,
        apiKeyConfigured = !string.IsNullOrWhiteSpace(credentials.Resolve(new() { Environment = connection.Environment, AuthMode = "ApiKey" })),
        companyTokenConfigured = !string.IsNullOrWhiteSpace(credentials.Resolve(new() { Environment = connection.Environment, AuthMode = "CompanyToken" })) };
});
api.MapPut("/connection", async (ConnectionInput input, AppDb db, CancellationToken ct) => {
    if (!demo && input.AllowWrites)
        throw new AppError("read_only", "Le connecteur Factorial réel autorise uniquement la lecture.", 403);
    if (!new[] { "Demo", "Production" }.Contains(input.Environment) || !new[] { "ApiKey", "CompanyToken" }.Contains(input.AuthMode) || input.SyncMinutes is < 0 or > 1440 || (input.SyncMinutes > 0 && input.SyncMinutes < 5) || input.LookbackDays is < 1 or > 366) throw new AppError("configuration", "Configuration invalide (fréquence : 0 ou 5 à 1440 minutes).");
    if (string.IsNullOrWhiteSpace(input.TimeZone) || NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(input.TimeZone) == null) throw new AppError("timezone", "Fuseau IANA inconnu.");
    await using var gate = new ConnectionLock(db); await gate.Acquire(ct);
    var c = await db.Connections.SingleAsync(ct);
    if (c.CompanyId != null && c.Environment != input.Environment) throw new AppError("environment_bound", "Une base liée ne peut pas changer d’environnement. Utiliser une base distincte.", 409);
    if (c.AuthMode != input.AuthMode) c.Status = "Non testée";
    c.Environment = input.Environment; c.AuthMode = input.AuthMode; c.AllowWrites = input.AllowWrites; c.SyncMinutes = input.SyncMinutes; c.LookbackDays = input.LookbackDays; c.TimeZone = input.TimeZone;
    await db.SaveChangesAsync(ct); return Results.Ok(c);
});
api.MapPost("/connection/test", async (SyncService sync, CancellationToken ct) => await sync.Run("Connection", null, ct));
api.MapPost("/sync/employees", async (SyncService sync, CancellationToken ct) => await sync.Run("Employees", null, ct));
api.MapPost("/sync/attendance", async (SyncRequest input, SyncService sync, CancellationToken ct) => {
    if (input.EmployeeIds == null || input.EmployeeIds.Length > 100) throw new AppError("employees", "Sélectionner au maximum 100 employés.");
    return await sync.Run("Attendance", input, ct);
});
api.MapGet("/employees", async (AppDb db) => new { factorial = await db.Employees.OrderBy(x => x.Name).ToListAsync(), local = await db.LocalEmployees.OrderBy(x => x.Name).ToListAsync(), mappings = await db.Mappings.ToListAsync() });
api.MapPost("/employees/local", async (LocalEmployeeInput input, AppDb db, CancellationToken ct) => {
    if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.PersonnelNumber) || input.Name.Length > 150 || input.PersonnelNumber.Length > 100) throw new AppError("employee", "Nom et matricule sont obligatoires et doivent rester courts.");
    await using var gate = new ConnectionLock(db); await gate.Acquire(ct);
    if (await db.LocalEmployees.AnyAsync(x => x.PersonnelNumber == input.PersonnelNumber.Trim(), ct)) throw new AppError("duplicate", "Ce matricule existe déjà.", 409);
    var row = new LocalEmployee { Name = input.Name.Trim(), PersonnelNumber = input.PersonnelNumber.Trim() }; db.LocalEmployees.Add(row); await db.SaveChangesAsync(ct); return Results.Ok(row);
});
api.MapPut("/mappings/{localId:guid}", async (Guid localId, MappingInput input, AppDb db, CancellationToken ct) => {
    await using var gate = new ConnectionLock(db); await gate.Acquire(ct);
    if (!await db.LocalEmployees.AnyAsync(x => x.Id == localId, ct)) throw new AppError("employee", "Employé local introuvable.", 404);
    var old = await db.Mappings.FindAsync([localId], ct);
    if (input.FactorialEmployeeId == null) { if (old != null) db.Mappings.Remove(old); }
    else {
        if (!await db.Employees.AnyAsync(x => x.Id == input.FactorialEmployeeId && !x.Missing, ct)) throw new AppError("employee", "Employé Factorial introuvable.", 404);
        if (await db.Mappings.AnyAsync(x => x.FactorialEmployeeId == input.FactorialEmployeeId && x.LocalEmployeeId != localId, ct)) throw new AppError("duplicate", "Cet employé Factorial est déjà associé.", 409);
        if (old == null) { old = new EmployeeFactorialMapping { LocalEmployeeId = localId }; db.Mappings.Add(old); }
        old.FactorialEmployeeId = input.FactorialEmployeeId; old.Method = "Manual";
    }
    await db.SaveChangesAsync(ct); return Results.Ok();
});
api.MapGet("/attendance", async (string start, string end, string? employee, string? status, int? page, AppDb db) => {
    TimeRules.ValidatePeriod(start, end); int number = page ?? 1;
    if (number is < 1 or > 100000) throw new AppError("page", "Page invalide.");
    var rows = db.Attendance.Where(x => x.ReferenceDate.CompareTo(start) >= 0 && x.ReferenceDate.CompareTo(end) <= 0);
    if (!string.IsNullOrEmpty(employee)) rows = rows.Where(x => x.EmployeeId == employee);
    if (!string.IsNullOrEmpty(status)) rows = rows.Where(x => x.Status == status);
    return Results.Ok(new { total = await rows.CountAsync(), page = number, pageSize = 25, data = await rows.OrderByDescending(x => x.ReferenceDate).ThenBy(x => x.EmployeeId).ThenBy(x => x.RawClockIn).ThenBy(x => x.Id).Skip((number - 1) * 25).Take(25).ToListAsync() });
});
api.MapGet("/attendance/journal", async (string? start, string? end, string? employee, int? page, bool? allDates, AppDb db, CancellationToken ct) =>
    await AttendanceJournal.Read(db, start, end, employee, page ?? 1, ct, allDates == true));
api.MapGet("/attendance/local-clock/{employeeId}", async (string employeeId, AppDb db, CancellationToken ct) => {
    var current = await db.Attendance.AsNoTracking().SingleOrDefaultAsync(row => row.EmployeeId == employeeId
        && row.Origin == "Local" && row.Status == "LocalOnly" && row.ClockOut == null, ct);
    return Results.Ok(new { state = current == null ? "idle" : current.Workable == true ? "working" : "paused", since = current?.ClockIn });
});
api.MapPost("/attendance/local-clock", async (LocalClockInput input, AppDb db, CancellationToken ct) => {
    await new LocalClock(db).Record(input, DateTimeOffset.UtcNow, ct);
    return Results.NoContent();
});
api.MapPost("/attendance/manual", async (ManualPointageInput input, AppDb db, CancellationToken ct) => {
    var entry = await new LocalClock(db).RecordManual(input, ct);
    return Results.Ok(new { entry.Id, entry.ReferenceDate });
});
api.MapPut("/attendance/manual/{id:guid}", async (Guid id, ManualPointageInput input, AppDb db, CancellationToken ct) => {
    var entry = await new LocalClock(db).RecordManual(input, ct, id);
    return Results.Ok(new { entry.Id, entry.ReferenceDate });
});
api.MapDelete("/attendance/manual/{id:guid}", async (Guid id, AppDb db, CancellationToken ct) => {
    await new LocalClock(db).DeleteManual(id, ct);
    return Results.NoContent();
});
api.MapPost("/attendance/local", async (LocalAttendanceInput input, AppDb db, CancellationToken ct) => {
    var start = TimeRules.RequireTimestamp(input.Start); var end = TimeRules.RequireTimestamp(input.End);
    if (end <= start || end - start > TimeSpan.FromDays(2)) throw new AppError("period", "La plage doit durer entre 0 et 48 heures.");
    if (!await db.Mappings.AnyAsync(x => x.FactorialEmployeeId == input.EmployeeId, ct)) throw new AppError("employee", "Employé non associé.");
    var row = new AttendanceRecord { EmployeeId = input.EmployeeId, Date = start.ToString("yyyy-MM-dd"), ReferenceDate = start.ToString("yyyy-MM-dd"), ClockIn = start.ToUniversalTime(), ClockOut = end.ToUniversalTime(), RawClockIn = input.Start, RawClockOut = input.End, Workable = true, CalculatedMinutes = (end - start).TotalMinutes, Origin = "Local", Status = "Pending", TimeInterpretation = "Horodatage local avec décalage explicite" };
    var c = await db.Connections.SingleAsync(ct);
    row.Date = row.ReferenceDate = TimeRules.BusinessDate(start, c.TimeZone);
    db.Attendance.Add(row); await db.SaveChangesAsync(ct); return Results.Ok(row);
});
api.MapPost("/attendance/{id:guid}/accept-remote", async (Guid id, AppDb db, CancellationToken ct) => {
    await using var gate = new ConnectionLock(db); await gate.Acquire(ct);
    var row = await db.Attendance.FindAsync([id], ct) ?? throw new AppError("record", "Pointage introuvable.", 404);
    if (row.Status != "Conflict" || row.RemoteSnapshot == null) throw new AppError("conflict", "Aucun conflit distant à résoudre.");
    var c = await db.Connections.SingleAsync(ct); TimeRules.Apply(row, System.Text.Json.JsonSerializer.Deserialize<ShiftDto>(row.RemoteSnapshot, FactorialJson.Options)!, c.TimeZone);
    await db.SaveChangesAsync(ct); return Results.Ok();
});
api.MapGet("/sync/history", async (AppDb db) => new { runs = await db.Runs.OrderByDescending(x => x.StartedAt).Take(100).ToListAsync(), errors = await db.Errors.Where(e => db.Runs.OrderByDescending(x => x.StartedAt).Take(100).Select(x => x.Id).Contains(e.RunId)).ToListAsync() });
api.MapGet("/outbound", async (AppDb db) => await db.Outbox.OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.EmployeeId, x.Action, x.Status, x.FactorialId, x.ErrorCode, x.CreatedAt }).ToListAsync());
api.MapPost("/outbound", async (SendRequest input, OutboundService service, CancellationToken ct) => {
    if (!demo) throw new AppError("read_only", "Le connecteur Factorial réel autorise uniquement la lecture.", 403);
    return await service.Send(input, ct);
});
api.MapPost("/outbound/{id:guid}/reconcile", async (Guid id, OutboundService service, CancellationToken ct) => await service.Reconcile(id, ct));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapFallbackToFile("index.html");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    if (demo) await db.Database.EnsureCreatedAsync(); else await db.Database.MigrateAsync();
    if (!await db.Connections.AnyAsync()) { db.Connections.Add(new() { Environment = demo || builder.Configuration["Factorial:Environment"] == "Demo" ? "Demo" : "Production" }); await db.SaveChangesAsync(); }
    if (!demo)
        await db.Connections.Where(connection => connection.AllowWrites).ExecuteUpdateAsync(update => update.SetProperty(connection => connection.AllowWrites, false));
    if (saveFactorialKey)
    {
        var connection = await db.Connections.AsNoTracking().SingleAsync();
        var key = builder.Configuration["Factorial:ApiKey"];
        if (!OperatingSystem.IsWindows() || demo || connection.Environment != "Production" || connection.AuthMode != "ApiKey" || string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("Clé de production et base Factorial réelle requises."); Environment.ExitCode = 1; return;
        }
        try
        {
            await scope.ServiceProvider.GetRequiredService<SyncService>().Verify(connection, default);
            LocalFactorialKey.Save(scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), key);
            Console.WriteLine("Clé Factorial vérifiée en lecture seule et enregistrée dans le stockage protégé de Windows.");
        }
        catch (AppError error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
        return;
    }
    if (importOnly)
    {
        var connection = await db.Connections.SingleAsync();
        if (demo || connection.Environment != "Production" || connection.AuthMode != "ApiKey")
            throw new InvalidOperationException("L’import requiert une base distincte liée à Factorial Production par clé API.");
        var sync = scope.ServiceProvider.GetRequiredService<SyncService>();
        var employees = await sync.Run("Employees", null, default);
        Console.WriteLine($"Employés : {employees.Status}, {employees.Count}");
        if (employees.Status != "Succeeded")
        {
            Console.WriteLine(await db.Errors.Where(error => error.RunId == employees.Id).Select(error => error.Message).FirstAsync());
            Environment.ExitCode = 1;
            return;
        }
        var today = TimeRules.Today(connection.TimeZone);
        var attendance = await sync.Run("Attendance", new(today.AddDays(-90).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), []), default);
        Console.WriteLine($"Pointages : {attendance.Status}, {attendance.Count}");
        if (attendance.Status != "Succeeded") Environment.ExitCode = 1;
        return;
    }
    if (demo && !await db.LocalEmployees.AnyAsync())
    {
        db.LocalEmployees.AddRange(new LocalEmployee { Name = "Salma Bennani", PersonnelNumber = "MA-001" }, new LocalEmployee { Name = "Youssef El Amrani", PersonnelNumber = "MA-002" }, new LocalEmployee { Name = "Lina Alaoui", PersonnelNumber = "MA-003" });
        await db.SaveChangesAsync();
        var sync = scope.ServiceProvider.GetRequiredService<SyncService>();
        await sync.Run("Employees", null, default);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await sync.Run("Attendance", new(today.AddDays(-30).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), []), default);
    }
}
await app.RunAsync();
public record LoginRequest(string Username, string Password);
public record ConnectionInput(string Environment, string AuthMode, bool AllowWrites, int SyncMinutes, int LookbackDays, string TimeZone);
public record LocalEmployeeInput(string Name, string PersonnelNumber);
public record MappingInput(string? FactorialEmployeeId);
public record LocalAttendanceInput(string EmployeeId, string Start, string End);
public partial class Program { }
