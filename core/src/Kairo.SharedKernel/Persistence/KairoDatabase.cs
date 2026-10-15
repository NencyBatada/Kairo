using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Kairo.SharedKernel.Persistence;

public sealed class KairoDatabase(string connectionString) : IDisposable, IAsyncDisposable
{
    private const string SessionContextSql = """
        EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @brand_id;
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = @all_brands;
        """;

    private readonly List<DbContext> _contexts = [];
    private readonly List<Func<CancellationToken, Task>> _afterCommit = [];
    private SqlTransaction? _transaction;
    private int _systemScopes;

    public SqlConnection Connection { get; } = new(connectionString);

    public Guid? BrandId { get; private set; }

    public bool AllBrands => _systemScopes > 0;

    public bool HasTransaction => _transaction is not null;

    public async Task UseBrandAsync(Guid brandId, CancellationToken cancellationToken)
    {
        BrandId = brandId;
        await RefreshSessionContextAsync(cancellationToken);
    }

    public async Task<IAsyncDisposable> EnterSystemScopeAsync(CancellationToken cancellationToken)
    {
        _systemScopes++;
        await RefreshSessionContextAsync(cancellationToken);
        return new SystemScope(this);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
            await ApplySessionContextAsync(Connection, cancellationToken);
        }

        _transaction = (SqlTransaction)await Connection.BeginTransactionAsync(cancellationToken);
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(_transaction, cancellationToken);
        }
    }

    public async Task AfterCommitAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            await action(cancellationToken);
            return;
        }

        _afterCommit.Add(action);
    }

    public async Task LockAsync(string resource, CancellationToken cancellationToken)
    {
        if (!await GetAppLockAsync(resource, 15000, cancellationToken))
        {
            throw new TimeoutException($"Could not lock {resource}.");
        }
    }

    public Task<bool> TryLockAsync(string resource, CancellationToken cancellationToken) =>
        GetAppLockAsync(resource, 0, cancellationToken);

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("No transaction is in progress.");
        await transaction.CommitAsync(cancellationToken);
        await EndTransactionAsync(transaction);
        var actions = _afterCommit.ToList();
        _afterCommit.Clear();
        foreach (var action in actions)
        {
            await action(cancellationToken);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        _afterCommit.Clear();
        if (_transaction is not { } transaction)
        {
            return;
        }

        if (transaction.Connection is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        await EndTransactionAsync(transaction);
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        Connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        await Connection.DisposeAsync();
    }

    internal void Enlist(DbContext context)
    {
        _contexts.Add(context);
        if (_transaction is not null)
        {
            context.Database.UseTransaction(_transaction);
        }
    }

    internal void ApplySessionContext(DbConnection connection)
    {
        using var command = CreateSessionContextCommand(connection);
        command.ExecuteNonQuery();
    }

    internal async Task ApplySessionContextAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = CreateSessionContextCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateSessionContextCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = SessionContextSql;
        command.Transaction = _transaction;

        var brand = command.CreateParameter();
        brand.ParameterName = "@brand_id";
        brand.DbType = DbType.Guid;
        brand.Value = BrandId.HasValue ? BrandId.Value : DBNull.Value;
        command.Parameters.Add(brand);

        var allBrands = command.CreateParameter();
        allBrands.ParameterName = "@all_brands";
        allBrands.DbType = DbType.Boolean;
        allBrands.Value = AllBrands;
        command.Parameters.Add(allBrands);

        return command;
    }

    private async Task RefreshSessionContextAsync(CancellationToken cancellationToken)
    {
        if (Connection.State == ConnectionState.Open)
        {
            await ApplySessionContextAsync(Connection, cancellationToken);
        }
    }

    private async Task<bool> GetAppLockAsync(string resource, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("A lock needs a transaction.");
        await using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @timeout;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = timeoutMilliseconds });
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! >= 0;
    }

    private async Task EndTransactionAsync(SqlTransaction transaction)
    {
        _transaction = null;
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(null);
        }

        await transaction.DisposeAsync();
    }

    private sealed class SystemScope(KairoDatabase database) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            database._systemScopes--;
            await database.RefreshSessionContextAsync(CancellationToken.None);
        }
    }
}
