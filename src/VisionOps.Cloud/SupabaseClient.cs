using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Supabase;
using Supabase.Postgrest.Models;
using System.Text.Json;

namespace VisionOps.Cloud;

/// <summary>
/// Managed Supabase client with connection resilience and retry logic
/// </summary>
public class SupabaseClient : IDisposable
{
    private readonly ILogger<SupabaseClient> _logger;
    private readonly SupabaseConfiguration _configuration;
    private readonly IAsyncPolicy _retryPolicy;
    private readonly IAsyncPolicy<bool> _circuitBreakerPolicy;
    private Client? _client;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SupabaseClient(SupabaseConfiguration configuration, ILogger<SupabaseClient> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Configure retry policy with exponential backoff
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(exception,
                        "Retry {RetryCount} after {TimeSpan}s delay", retryCount, timeSpan.TotalSeconds);
                });

        // Configure circuit breaker
        _circuitBreakerPolicy = Policy<bool>
            .HandleResult(success => !success)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: (result, timespan) =>
                {
                    _logger.LogError("Circuit breaker opened for {Duration} minutes", timespan.TotalMinutes);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                });
    }

    /// <summary>
    /// Initialize Supabase client connection
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized && _client != null)
                return true;

            _logger.LogInformation("Initializing Supabase client");

            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false, // We don't need realtime for this use case
            };

            _client = new Client(_configuration.Url, _configuration.AnonKey, options);
            await _client.InitializeAsync();

            _isInitialized = true;
            _logger.LogInformation("Supabase client initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Supabase client");
            _isInitialized = false;
            return false;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Ensure client is connected and ready
    /// </summary>
    private async Task<bool> EnsureConnectedAsync()
    {
        if (!_isInitialized || _client == null)
        {
            return await InitializeAsync();
        }
        return true;
    }

    /// <summary>
    /// Insert batch of records with retry logic
    /// </summary>
    public async Task<bool> InsertBatchAsync<T>(string tableName, List<T> records) where T : BaseModel, new()
    {
        if (!records.Any()) return true;

        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                if (!await EnsureConnectedAsync())
                    throw new InvalidOperationException("Supabase client not connected");

                _logger.LogDebug("Inserting {Count} records into {Table}", records.Count, tableName);

                var table = _client!.From<T>();
                await table.Insert(records);

                _logger.LogInformation("Successfully inserted {Count} records into {Table}",
                    records.Count, tableName);
                return true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert batch into {Table}", tableName);
            return false;
        }
    }

    /// <summary>
    /// Upsert batch of records (insert or update)
    /// </summary>
    public async Task<bool> UpsertBatchAsync<T>(string tableName, List<T> records) where T : BaseModel, new()
    {
        if (!records.Any()) return true;

        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                if (!await EnsureConnectedAsync())
                    throw new InvalidOperationException("Supabase client not connected");

                _logger.LogDebug("Upserting {Count} records into {Table}", records.Count, tableName);

                var table = _client!.From<T>();
                await table.Upsert(records);

                _logger.LogInformation("Successfully upserted {Count} records into {Table}",
                    records.Count, tableName);
                return true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert batch into {Table}", tableName);
            return false;
        }
    }

    /// <summary>
    /// Query records with filters
    /// </summary>
    public async Task<List<T>?> QueryAsync<T>(
        string tableName,
        Dictionary<string, object>? filters = null,
        int limit = 100) where T : BaseModel, new()
    {
        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                if (!await EnsureConnectedAsync())
                    throw new InvalidOperationException("Supabase client not connected");

                var query = _client!.From<T>();

                // Apply filters if provided
                if (filters != null)
                {
                    foreach (var filter in filters)
                    {
                        query = query.Filter(filter.Key, Supabase.Postgrest.Constants.Operator.Equals, filter.Value);
                    }
                }

                query = query.Limit(limit);
                var response = await query.Get();

                return response.Models;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query {Table}", tableName);
            return null;
        }
    }

    /// <summary>
    /// Delete records matching criteria
    /// </summary>
    public async Task<bool> DeleteWhereAsync<T>(
        string tableName,
        Dictionary<string, object> filters) where T : BaseModel, new()
    {
        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                if (!await EnsureConnectedAsync())
                    throw new InvalidOperationException("Supabase client not connected");

                var query = _client!.From<T>();

                foreach (var filter in filters)
                {
                    query = query.Filter(filter.Key, Supabase.Postgrest.Constants.Operator.Equals, filter.Value);
                }

                await query.Delete();

                _logger.LogInformation("Deleted records from {Table} with filters: {Filters}",
                    tableName, JsonSerializer.Serialize(filters));
                return true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete from {Table}", tableName);
            return false;
        }
    }

    /// <summary>
    /// Test connection to Supabase
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
            {
                if (!await EnsureConnectedAsync())
                    return false;

                // Try a simple query to test connection
                var testQuery = _client!.From<SupabaseHealthCheck>();
                await testQuery.Limit(1).Get();

                _logger.LogDebug("Supabase connection test successful");
                return true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supabase connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Get connection status
    /// </summary>
    public ConnectionStatus GetStatus()
    {
        return new ConnectionStatus
        {
            IsConnected = _isInitialized && _client != null,
            CircuitBreakerState = GetCircuitBreakerState(),
            LastError = null // Could track last error if needed
        };
    }

    private string GetCircuitBreakerState()
    {
        // Polly doesn't expose circuit state directly, so we track it
        return "Closed"; // Would need custom implementation to track actual state
    }

    public void Dispose()
    {
        _initLock?.Dispose();
        _client = null;
        _isInitialized = false;
    }
}

/// <summary>
/// Supabase configuration
/// </summary>
public class SupabaseConfiguration
{
    public string Url { get; set; } = string.Empty;
    public string AnonKey { get; set; } = string.Empty;
    public string? ServiceKey { get; set; }
    public bool EnableSync { get; set; } = true;
    public int SyncIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Connection status information
/// </summary>
public class ConnectionStatus
{
    public bool IsConnected { get; set; }
    public string CircuitBreakerState { get; set; } = "Unknown";
    public string? LastError { get; set; }
    public DateTime? LastSuccessfulSync { get; set; }
}

/// <summary>
/// Dummy class for health check queries
/// </summary>
[Supabase.Postgrest.Attributes.Table("configurations")]
public class SupabaseHealthCheck : BaseModel
{
    [Supabase.Postgrest.Attributes.PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;
}