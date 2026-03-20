# Phase 12 Priority 3: IAsyncEnumerable<T> Streaming Patterns - Analysis

**Date**: March 20, 2026  
**Objective**: Implement async streaming for large data operations  
**Status**: 🟡 ANALYSIS PHASE

---

## IAsyncEnumerable<T> Overview

### What is IAsyncEnumerable<T>?
```csharp
// Read-only async streams - yield values asynchronously
public interface IAsyncEnumerable<out T>
{
    IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
}

// Consumed with await foreach
await foreach (var item in GetItemsAsync())
{
    ProcessItem(item);
}
```

### Benefits Over Returning List<T>

| Scenario | List<T> | IAsyncEnumerable<T> |
|----------|---------|-------------------|
| **Memory** | Load all into memory | Stream one at a time |
| **Latency** | Wait for all items | Process as soon as available |
| **Large datasets** | 500 MB+ RAM spike | Constant ~100 KB |
| **Cancellation** | All-or-nothing | Stop mid-stream |
| **Backpressure** | Consumer waits | Producer waits on consumer |

### Example Comparison

```csharp
// Traditional: Loads 100,000 drivingstates into memory
public List<DrivestateDump> GetDrivestates()
{
    var states = new List<DrivestateDump>();
    using (var connection = new MySqlConnection(_connStr))
    {
        connection.Open();
        using (var reader = connection.ExecuteReader("SELECT * FROM driverstates LIMIT 100000"))
        {
            while (reader.Read())
            {
                states.Add(ParseDrivestate(reader));
            }
        }
    }
    return states;  // ~150+ MB heap pressure
}

// Streaming: Yields one at a time
public async IAsyncEnumerable<DrivestateDump> GetDrivestatesAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    using (var connection = new MySqlConnection(_connStr))
    {
        await connection.OpenAsync(ct);
        using (var reader = await connection.ExecuteReaderAsync(ct))
        {
            while (await reader.NextResultAsync(ct))
            {
                yield return ParseDrivestate(reader);  // One at a time, low memory
            }
        }
    }
}

// Usage: Stream and process
await foreach (var state in GetDrivestatesAsync(ct))
{
    await ProcessStateAsync(state, ct);
}
```

---

## Priority 1: Database Query Streaming

### Candidate 1: DrivestateDump Query Operations
**Location**: DBHelper.cs (typical data access layer)  
**Current Pattern**: Returns `List<T>` from database queries  
**Issue**: Large result sets loaded entirely into memory

**Suggested Streaming Implementation**:
```csharp
public async IAsyncEnumerable<DrivestateDump> GetDrivestatesAsync(
    int maxCount = 100000,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    using var connection = new MySqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT * FROM driverstates ORDER BY id DESC LIMIT @limit";
    command.Parameters.AddWithValue("@limit", maxCount);
    
    using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.NextResultAsync(cancellationToken))
    {
        yield return new DrivestateDump
        {
            Id = reader.GetInt32("id"),
            Timestamp = reader.GetDateTime("datum"),
            Speed = reader.GetDouble("speed"),
            // ... other fields
        };
    }
}
```

**Benefits**:
- Memory: ~10 MB vs. ~500 MB for 100,000 records
- Latency: Start processing immediately
- Cancellation: Stop mid-stream if user cancels
- Backpressure: Database pauses if consumer slow

**Expected Files**:
- DBHelper.cs (data access)
- Potentially CurrentJSON.cs if used for state updates

---

### Candidate 2: Charging History Pagination
**Location**: WebHelper.cs (L4740 - GetChargingHistoryV2)  
**Current Pattern**: Returns single page as string, caller must manage pagination  
**Issue**: Pagination logic scattered, hard to cancel mid-operation

**Suggested Implementation**:
```csharp
public async IAsyncEnumerable<ChargingRecord> GetChargingHistoryStreamAsync(
    string? vin = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    int pageNumber = 1;
    bool hasMore = true;
    
    while (hasMore && !cancellationToken.IsCancellationRequested)
    {
        string pageJson = await GetChargingHistoryV2Async(vin, pageNumber++);
        
        if (string.IsNullOrEmpty(pageJson))
            break;
            
        var records = JsonConvert.DeserializeObject<List<ChargingRecord>>(pageJson);
        if (records == null || records.Count == 0)
            break;
            
        foreach (var record in records)
        {
            yield return record;
        }
        
        hasMore = records.Count >= 20;  // Assume page size is 20
    }
}
```

**Benefits**:
- Pagination abstracted away
- Process records as they arrive
- Easy to cancel multi-page operations
- Cleaner client code

---

## Priority 2: File Operations Streaming

### Candidate 3: CSV/Data File Line Streaming
**Location**: Potential import/export functions  
**Current Pattern**: Read entire file into memory  
**Issue**: Large file imports cause memory spike

**Suggested Pattern**:
```csharp
public async IAsyncEnumerable<string> ReadFileLinesByLineAsync(
    string filePath,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    using var stream = File.OpenRead(filePath);
    using var reader = new StreamReader(stream);
    
    string? line;
    while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
    {
        yield return line;
    }
}

// Usage: Process large CSV
await foreach (var line in ReadFileLinesByLineAsync("large-data.csv", ct))
{
    var record = ParseCsvLine(line);
    await InsertDatabaseRecordAsync(record, ct);
}
```

**Benefits**:
- Process files larger than available RAM
- Streaming database inserts
- Cancellable operation
- Constant memory footprint

---

## Priority 3: API Pagination Streaming

### Candidate 4: Komoot Tour/Route Pagination
**Location**: Komoot.cs  
**Current Pattern**: GetTourList returns multiple API calls, manual pagination  
**Issue**: Multiple API requests, pagination logic complex

**Suggested Implementation**:
```csharp
public async IAsyncEnumerable<TourData> GetToursStreamAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    int pageNumber = 1;
    bool hasMore = true;
    
    while (hasMore)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var pageJson = await GetTourPageAsync(pageNumber);
        if (string.IsNullOrEmpty(pageJson))
            break;
            
        var tours = ParseTourPage(pageJson);
        if (tours.Count == 0)
            break;
            
        foreach (var tour in tours)
        {
            yield return tour;
        }
        
        pageNumber++;
        hasMore = tours.Count >= PAGE_SIZE;
    }
}
```

**Benefits**:
- Pagination abstracted
- Process tours as they arrive
- Cancellation support
- Rate limiting friendly (process at own pace)

---

## Priority 4: License Updates Bulk Operations

### Candidate 5: UpdateGeofence Bulk Updates Stream
**Location**: Geofence.cs / WebHelper.cs  
**Current Pattern**: Update all POI addresses in bulk  
**Issue**: Large batches can timeout or consume excessive memory

**Suggested Pattern**:
```csharp
public async IAsyncEnumerable<LocationUpdate> UpdatePOIAddressesStreamAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var pois = GetAllPOIs();
    
    const int BATCH_SIZE = 50;
    for (int i = 0; i < pois.Count; i += BATCH_SIZE)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var batch = pois.Skip(i).Take(BATCH_SIZE);
        
        // Process batch with rate limiting
        foreach (var poi in batch)
        {
            var address = await ReverseGeocodeAsync(poi.Latitude, poi.Longitude, cancellationToken);
            yield return new LocationUpdate { POI = poi, Address = address };
            
            // Rate limiting between requests
            await Task.Delay(100, cancellationToken);
        }
    }
}
```

**Benefits**:
- Batch processing with rate limiting
- Cancellation support
- Backpressure handling
- Resume-friendly (state tracked in loop)

---

## Implementation Priorities

### Phase A: Database Streaming (This Session)
**Target**: Implement DrivestateDump streaming  
**Effort**: Medium  
**Impact**: High (large dataset operations)  
**Files**: DBHelper.cs, possibly one consumer

### Phase B: API Pagination (If Time)
**Target**: Chevrolet charging history pagination  
**Effort**: Low-Medium  
**Impact**: Medium (cleaner API)

### Phase C: File Operations (Optional)
**Target**: CSV import pattern  
**Effort**: Low  
**Impact**: Medium (large file support)

---

## IAsyncEnumerable Pattern Requirements

### Pattern 1: Basic Enumeration
```csharp
public async IAsyncEnumerable<T> EnumerateAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    // Must:
    // 1. Accept CancellationToken with [EnumeratorCancellation] attribute
    // 2. Use yield return for each item
    // 3. Call ThrowIfCancellationRequested() regularly
    // 4. Use ConfigureAwait(false) on all awaits
    
    while (!cancellationToken.IsCancellationRequested)
    {
        var item = await FetchItemAsync().ConfigureAwait(false);
        if (item == null) break;
        
        yield return item;
    }
}
```

### Pattern 2: Database Query Streaming
```csharp
public async IAsyncEnumerable<T> QueryAsync(
    string query,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    
    using var command = connection.CreateCommand();
    command.CommandText = query;
    
    using var reader = await command.ExecuteReaderAsync(cancellationToken)
        .ConfigureAwait(false);
    
    while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
    {
        yield return MapRow(reader);
    }
}
```

### Pattern 3: Pagination
```csharp
public async IAsyncEnumerable<T> PaginateAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    int page = 1;
    bool hasMore = true;
    
    while (hasMore)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var items = await FetchPageAsync(page).ConfigureAwait(false);
        if (items.Count == 0) break;
        
        foreach (var item in items)
        {
            yield return item;
        }
        
        hasMore = items.Count == PAGE_SIZE;
        page++;
    }
}
```

---

## Code Quality Standards

### Requirements Checklist
- ✅ Method return type: `IAsyncEnumerable<T>`
- ✅ CancellationToken parameter: `[EnumeratorCancellation]`
- ✅ All awaits: `.ConfigureAwait(false)`  
- ✅ Cancellation checks: Regular `ThrowIfCancellationRequested()`
- ✅ Resource cleanup: `using` statements
- ✅ Zero allocations unless yielding value
- ✅ Xmldoc comments: Document the async enumeration

### Testing Patterns
```csharp
[TestMethod]
public async Task EnumerateAsync_CancelsProperlyMidStream()
{
    var cts = new CancellationTokenSource();
    var items = new List<int>();
    
    try
    {
        var enum = EnumerateAsync(cts.Token);
        int count = 0;
        await foreach (var item in enum)
        {
            items.Add(item);
            if (++count == 5)
                cts.Cancel();
        }
    }
    catch (OperationCanceledException)
    {
        // Expected
    }
    
    Assert.IsTrue(items.Count <= 5);
}
```

---

## Success Criteria

- ✅ 1-2 streaming implementations complete
- ✅ Zero build errors and warnings
- ✅ Proper async enumeration patterns
- ✅ CancellationToken integration
- ✅ Documentation and examples
- ✅ Production-ready code

---

## Resources & References

### IAsyncEnumerable Documentation
- Microsoft Docs: IAsyncEnumerable<T>
- Pattern: async-streams, await foreach
- Cancellation: EnumeratorCancellation attribute (C# 8.0+)

### Related Patterns Implemented
- ✅ ValueTask<T> (Priority 2)
- ✅ ConfigureAwait(false) (Priority 1)
- 🔄 IAsyncEnumerable<T> (Priority 3)

---

## Next Steps

1. **Identify exact consumer**: Find where DrivestateDump queries are used
2. **Implement streaming version**: Create IAsyncEnumerable<DrivestateDump>
3. **Update consumer**: Refactor to use `await foreach`
4. **Test**: Verify cancellation and backpressure
5. **Optional**: Add 1-2 more streaming patterns

