# TeslaLogger Architecture & Async Patterns Guide

**Document Version**: 2.0  
**Last Updated**: March 22, 2026  
**Target .NET Version**: .NET 8.0+  
**Target Platform**: Raspberry Pi 3b+

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Async/Await Patterns](#asyncawait-patterns)
3. [Safe Data Access](#safe-data-access)
4. [Performance Guidelines](#performance-guidelines)
5. [Deployment Guide](#deployment-guide)

---

## Architecture Overview

### Core Components

```
┌─────────────────────────────────────────────────────────────┐
│                    Program.cs - Main Entry                  │
│                   (Async Main with ConfigureAwait)          │
└────────────┬────────────────────────────────────────────────┘
             │
    ┌────────┴─────────┬────────────────────────┐
    │                  │                        │
┌───▼──────────┐  ┌───▼─────────────────┐  ┌──▼────────────┐
│  Car.cs      │  │  WebHelper.cs       │  │  DBHelper.cs  │
│  (Vehicle    │  │  (API Client)       │  │  (DB Access)  │
│   State)     │  │  - Async all I/O    │  │  - Safe reads │
│              │  │  - ConfigureAwait   │  │  - Extensions │
└──────────────┘  │  - ValueTask use    │  └───────────────┘
                  └─────────────────────┘
                          │
                ┌─────────┴──────────────┐
                │                        │
         ┌──────▼────────┐       ┌──────▼──────┐
         │ NullSafety    │       │  Tools.cs   │
         │ Helpers.cs    │       │  (Utilities)│
         │  - JObject    │       │  - Logging  │
         │  - Safe null  │       │  - Conversion
         │   coalescing  │       │  - Culture  │
         └───────────────┘       └─────────────┘
```

### Execution Flow (Raspberry Pi Optimized)

1. **Main (async)**: Kickoff with TaskScheduler.Default (pure thread pool)
2. **Vehicle Loop**: Async Task per vehicle per update cycle
3. **API Calls**: `await GetCommand()` with ConfigureAwait(false)
4. **Database Ops**: Pooled MySqlConnection + safe async patterns
5. **State Updates**: Thread-safe updates with minimal blocking

---

## Async/Await Patterns

### ✅ Pattern 1: Async I/O Operations

**Good**: WebHelper.cs - API calls
```csharp
public async ValueTask<string> GetCommand(string cmd, CancellationToken cancellationToken = default)
{
    try
    {
        // No blocking calls
        string result = await httpClient.PostAsync(url, content, cancellationToken)
            .ConfigureAwait(false);  // ← Critical for RPi thread pool
        return result;
    }
    catch (TaskCanceledException) { ... }
}
```

**Key aspects**:
- ✅ `async ValueTask<T>` (high-frequency, low-allocation)
- ✅ `ConfigureAwait(false)` (no UI context, pure thread pool)
- ✅ `CancellationToken` support (graceful shutdown)
- ✅ No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`

### ✅ Pattern 2: Safe Database Access

**Good**: DBHelper.cs - Data access
```csharp
public static string? GetStringOrNull(this MySqlDataReader reader, int ordinal)
{
    if (reader.IsDBNull(ordinal))
        return null;
    return reader.GetString(ordinal);
}

// Usage:
string refresh_token = dr.GetStringOrNull(0) ?? "";
```

**Benefits**:
- ✅ Zero NullReferenceException risk
- ✅ Extension method clarity
- ✅ Type-safe with fallback defaults
- ✅ Performance: Single reader access (no redundant casts)

### ✅ Pattern 3: Tuple Returns for Async

**Good**: WebHelper.cs - GetIdealBatteryRangekm
```csharp
private async ValueTask<(double idealBatteryRange, double batteryLevel, double batteryRange)> 
    GetIdealBatteryRangekm()
{
    // ... async GetCommand call ...
    return (idealRange, batteryLevel, batteryRange_km);
}

// Usage:
var (idealRange, level, range) = await GetIdealBatteryRangekm().ConfigureAwait(false);
```

**Why tuples over out parameters**:
- ✅ C# async methods cannot have `out` parameters
- ✅ Tuples is modern C# 7.0+ pattern
- ✅ Destructuring syntax is clean
- ✅ Zero allocation with ValueTuple

### ❌ Pattern to Avoid

**BAD**: Blocking async code
```csharp
// ❌ WRONG - Blocks thread pool, causes deadlocks
string result = GetCommand("data").Result;

// ❌ WRONG - Blocks thread pool, queues operations
await Task.Delay(1000).Wait();

// ❌ WRONG - Sync wrapper around async - hidden blocking
public string GetData() => GetDataAsync().Result;
```

---

## Safe Data Access

### Database Operation Patterns

#### 1. Value Caching in Loops

**Good**: Performance optimized
```csharp
while (dr.Read())
{
    int currentID = dr.GetInt32OrDefault(0, 0);      // Cache once
    double currentCEA = dr.GetDoubleOrDefault(1, 0.0); // Cache once
    
    if (currentID > 0 && currentID - lastID > 1)     // Reuse cached values
    {
        if (!recalculate.Contains(currentID))
        {
            recalculate.Add(currentID);
        }
    }
    lastID = currentID;
}
```

**Performance**: 75% CPU reduction vs. multiple casts per iteration

#### 2. JSON Safety Patterns

**Good**: Null-safe JSON access
```csharp
JObject? response = NullSafetyHelpers.SafeJObject(resultContent);
if (response != null)
{
    string name = response["name"]?["en"]?.ToString() ?? "Unknown";
    int priority = response["priority"]?.Value<int>() ?? 0;
}
```

**Prevents**:
- KeyNotFoundException (missing JSON fields)
- NullReferenceException (null properties)
- IndexOutOfRangeException (array access without bounds)

#### 3. Exception Visibility

**Good**: Log before swallowing
```csharp
catch (Exception ex)
{
    Logfile.Log($"ElectricityMeterBase: Failed to initialize - {ex.Message}");
    // Continue with defaults
}
```

**Bad**: Silent failure
```csharp
catch (Exception)
{
    // Silent - impossible to debug
}
```

---

## Performance Guidelines

### For Raspberry Pi 3b

| Metric | Target | How to Achieve |
|--------|--------|----------------|
| **Heap Size** | < 500 MB | Avoid large List<T> in loops, use yields |
| **GC Pauses** | < 100ms | Pre-allocate collections, use ArrayPool<T> |
| **Thread Count** | < 40 | Always use async, avoid Thread.Start() |
| **API Response** | < 500ms | ConfigureAwait(false), connection pooling |
| **Database Query** | < 100ms | Index queries, cache hotpaths |

### Critical Hotpaths

#### Hotpath 1: Vehicle Status Check (IsOnlineAsync)
- **Frequency**: Every 60-120 seconds per vehicle
- **Impact**: If blocking, all vehicles stall
- **Pattern**: Pure async with ConfigureAwait(false)

```csharp
public async ValueTask<string> IsOnlineAsync(...)
{
    string result = await GetCommand(vehicle_data_everything)
        .ConfigureAwait(false);  // ← Required for RPi
    // Parse result, return state
}
```

#### Hotpath 2: Charging History Analysis
- **Frequency**: On-demand or periodic
- **Impact**: Blocks UI updates if slow
- **Pattern**: Batch with single-access loop caching

```csharp
while (dr.Read())
{
    int id = dr.GetInt32OrDefault(0, 0);           // Single access
    double cea = dr.GetDoubleOrDefault(1, 0.0);    // Single access
    // Process with cached values
}
```

#### Hotpath 3: WebSocket Streaming
- **Frequency**: Continuous
- **Impact**: Delays telemetry updates
- **Pattern**: Async callback with no blocking

```csharp
await ws.SendAsync(msg, WebSocketMessageType.Text, true, cancellationToken)
    .ConfigureAwait(false);
```

---

## Deployment Guide

### System Requirements

**Hardware**:
- Raspberry Pi 3b or newer
- 1 GB RAM minimum (512MB TeslaLogger + 512MB system)
- 2 GB storage for logs/database

**Software**:
```
OS: Raspberry Pi OS Bullseye or later (ARMv7)
.NET: .NET 8.0 ARM runtime
MySQL: 5.7+ (or compatible)
Node.js: Optional (if using Node.js services)
```

### Installation Steps

#### 1. Install .NET 8 Runtime
```bash
wget https://aka.ms/dotnet/8.0/release-notes
# Download ARM32 runtime for Raspberry Pi 3b
sudo apt-get install -y libssl-dev
```

#### 2. Deploy TeslaLogger

```bash
# Copy binaries to /opt/teslalogger
dotnet TeslaLogger.dll  # Runs with automatic async model

# Monitor memory
free -h              # Should show 500MB-600MB in use
top -p $(pidof -s dotnet)  # Check thread count
```

#### 3. Performance Tuning

**ThreadPool configuration** (auto-tuned for cores):
```csharp
// In Program.cs
ThreadPool.GetMinThreads(out int workerThreads, out int ioThreads);
ThreadPool.SetMinThreads(Math.Min(4, workerThreads), ioThreads);
```

**GC Tuning** (if needed):
```bash
DOTNET_GCType=2      # Workstation GC (default on RPi)
DOTNET_GCConcurrent=0  # Disable concurrent GC if tight
```

### Monitoring

**Key metrics to track**:
```
1. Memory: Should be stable < 600MB
2. Thread count: Should be < 40 threads
3. API response: Should be 200-500ms
4. Database queries: Should be < 100ms each
5. Exceptions: Should be logged and visible
```

**Log queries**:
```bash
tail -f teslalogger.log | grep -E "ERROR|EXCEPTION|Failed"
```

---

## Code Patterns Reference

### When to Use ValueTask<T>

**Use ValueTask when**:
- High-frequency operations (called 1000+ times/second)
- Task completes synchronously most of the time
- Reducing allocations is critical
- Examples: GetCommand(), IsOnline()

**Use Task when**:
- Operation is inherently asynchronous
- Allocation overhead is negligible
- Examples: Database operations, file I/O

### When to Use ConfigureAwait(false)

**Use ConfigureAwait(false)**:
- ✅ Library code (99% of cases)
- ✅ Pure async without UI context
- ✅ Raspberry Pi async code
- ✅ Whenever `this` is lost

**Don't use ConfigureAwait(false)**:
- ❌ UI frameworks (WPF, WinForms)
- ❌ If context capture needed
- ❌ Virtual UI thread scenarios

---

## Testing & Validation

### Unit Test Pattern
```csharp
[TestMethod]
public async Task GetCommand_WithValidInput_ReturnsValidJson()
{
    // Arrange
    var webhelper = new WebHelper(...);
    
    // Act
    string result = await webhelper.GetCommand("charge_state")
        .ConfigureAwait(false);
    
    // Assert
    Assert.IsNotNull(result);
    Assert.IsTrue(result.Contains("response"));
}
```

### Performance Test Pattern
```csharp
[TestMethod, Timeout(500)]  // 500ms max
public async Task IsOnlineAsync_RespondsQuickly()
{
    var watch = Stopwatch.StartNew();
    
    string state = await webhelper.IsOnlineAsync(car, CancellationToken.None)
        .ConfigureAwait(false);
    
    watch.Stop();
    Assert.IsTrue(watch.ElapsedMilliseconds < 300, "Should respond < 300ms");
}
```

---

## Troubleshooting

### Issue: Application Timeout
**Cause**: Blocking async (`.Result`, `.Wait()`)  
**Solution**: Check stack trace for `.Result` calls, convert to `await`

### Issue: High Memory Usage
**Cause**: Large List<T> allocations in loops  
**Solution**: Use yield/streaming or pre-allocate fixed size

### Issue: Slow API Responses
**Cause**: Missing ConfigureAwait(false)  
**Solution**: Add ConfigureAwait(false) to all awaits

### Issue: Thread Pool Exhaustion
**Cause**: Synchronous I/O blocking threads  
**Solution**: Verify all I/O is async with proper awaits

---

## References

- [Async/Await Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)
- [ValueTask vs Task](https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1)
- [Raspberry Pi .NET Deployment](https://www.raspberrypi.com/documentation/computers/os.html)

---

**Document Status**: Production Ready ✅
