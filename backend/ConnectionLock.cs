using Microsoft.EntityFrameworkCore;
namespace Pointage;
// Session advisory lock: covers HTTP calls and committed journal entries across API replicas.
public sealed class ConnectionLock(AppDb db) : IAsyncDisposable
{
    private static readonly SemaphoreSlim DemoLock = new(1, 1);
    private bool held;
    public async Task Acquire(CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) { held = await DemoLock.WaitAsync(0, ct); }
        else
        {
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'Pointage.Connection', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
                held = (int)(await command.ExecuteScalarAsync(ct))! >= 0;
            }
            finally { if (!held) await db.Database.CloseConnectionAsync(); }
        }
        if (!held) throw new AppError("busy", "Une opération est déjà en cours sur cette connexion.", 409);
    }
    public async ValueTask DisposeAsync()
    {
        if (!held) return;
        if (!db.Database.IsSqlServer()) DemoLock.Release();
        else
        {
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'Pointage.Connection', @LockOwner='Session';";
                await command.ExecuteNonQueryAsync();
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        held = false;
    }
}
