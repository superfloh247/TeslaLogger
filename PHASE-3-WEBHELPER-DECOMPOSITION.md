# Phase 3: WebHelper.cs Decomposition Strategy
**Date:** March 23, 2026  
**Status:** IN PROGRESS  
**Target:** Split 5,856-line WebHelper into 4 focused services

---

## Analysis: WebHelper.cs Current State

### Line Count Distribution
- **Total Lines:** 5,856
- **Public Methods:** 30+
- **Internal Methods:** 50+
- **Static Methods:** 15+
- **Properties:** 20+

### Current Responsibilities (VIOLATES SRP)

#### 1. **Tesla API Communication** (~1,500 lines)
- HTTP requests to Tesla API endpoints
- Request/response serialization (JSON)
- Rate limiting and retry logic
- User-Agent headers, timeouts
- **Methods:** `GetCommand()`, `PostCommand()`, `Wakeup()`, `GetAllVehicles()`

#### 2. **Token Management** (~800 lines)
- OAuth token acquisition and refresh
- Token caching and expiration tracking
- Tesla authentication endpoint handling
- Token validation and retry logic
- Cookie-based session management
- **Fields:** `tesla_token`, `nextTeslaTokenFromRefreshToken`, `tokenCookieContainer`
- **Methods:** `CheckRefreshToken()`, `LoginRetry()`, `Tesla_token` property

#### 3. **Geolocation Services** (~1,200 lines)
- Reverse geocoding (address lookup from coordinates)
- Multiple provider support: MapQuest, Nominatim, OpenTopoData
- Rate limiting per service (MapQuestCount, NominatimCount)
- Elevation data retrieval
- Cache management for geocoding results
- **Methods:** `ReverseGecocodingAsync()`, `GetOutsideTempAsync()`, `UpdateAllEmptyAddresses()`

#### 4. **Location/Address Management** (~1,000 lines)
- POI (Point of Interest) address updates
- Database persistence of locations
- Address caching and invalidation
- Charging site information
- **Methods:** `UpdateAllPosAddresses()`, `UpdateAllEmptyAddresses()`, `UpdateAllPOIAddresses()`

#### 5. **Streaming & Telemetry** (~400 lines)
- WebSocket stream handling
- Telemetry data reception
- Stream lifecycle management
- **Methods:** `StartStreamThread()`, `StopStreaming()`

#### 6. **Integration Services** (~500 lines)
- ABRP (A Better Route Planner)
- SuC Bingo (charging network tracking)
- Exceptionless error reporting
- **Methods:** `SendDataToAbetterrouteplannerAsync()`, `SuperchargeBingoCheckin()`

#### 7. **Vehicle State & Efficiency** (~250 lines)
- Shift state tracking
- Charging state reset
- Efficiency updates
- **Fields:** `lastShift_State`, `lastChargingState`
- **Methods:** `ResetLastChargingState()`, `UpdateEfficiency()`

---

## Decomposition Plan: 4 New Services

### Service 1: **ITeslaAPIClient** ↔ **TeslaAPIClient.cs**
**Purpose:** Handle all Tesla API HTTP communication  
**Scope:** 500 lines  
**Responsibilities:**
- HTTP request execution with retry logic
- JSON serialization/deserialization
- API endpoint routing
- Timeout and rate limiting management
- Response handling and error codes

**Key Methods:**
```csharp
public async Task<string> GetCommand(string command, int timeoutSeconds = 30)
public async Task<string> PostCommand(string cmd, string data, bool _json = false)
public async Task<string> Wakeup()
public void GetAllVehicles(out string? resultContent, out JArray vehicles, bool throwExceptionOnUnauthorized, bool doNotCache = false)
public async IAsyncEnumerable<JObject> GetChargingHistoryStreamAsync()
public async Task<string> GetNearbyChargingSitesOwnerAPIAsync()
public async Task<string> GetChargingHistoryV2(string vin, int pageNumber = 1)
```

**Dependencies:**
- ITokenManager (for OAuth tokens)
- HttpClient
- ILogger

**Design Pattern:** Repository Pattern (abstracts API details)

---

### Service 2: **ITokenManager** ↔ **TokenManager.cs**
**Purpose:** Manage Tesla OAuth token lifecycle  
**Scope:** 350 lines  
**Responsibilities:**
- Token acquisition from auth.tesla.com
- Token refresh using refresh tokens
- Token caching and expiration tracking
- Cookie-based session persistence
- Token validation before API calls

**Key Methods:**
```csharp
public async Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
public void RefreshToken()
public bool IsTokenValid()
public void CheckRefreshToken()
public bool LoginRetry(HttpResponseMessage response)
public string GetApiAddress(Car car) // FleetAPI vs standard vs China
```

**Public Properties:**
```csharp
public string TeslaToken { get; internal set; }
public string TeslaId { get; internal set; }
public string TeslaVehicleId { get; internal set; }
public string TeslaStreamingToken { get; internal set; }
public DateTime NextTokenRefresh { get; internal set; }
```

**Dependencies:**
- HttpClient
- Car entity
- ILogger

**Design Pattern:** Singleton (one token manager per application, could be per-user in multi-tenant)

---

### Service 3: **IGeolocationService** ↔ **GeolocationService.cs**
**Purpose:** Reverse geocoding and location-based lookups  
**Scope:** 600 lines  
**Responsibilities:**
- Reverse geocoding (lat/lon → address) via MapQuest, Nominatim, OpenTopoData
- Provider selection and failover
- Rate limiting per provider
- Elevation data retrieval from OpenTopoData
- Caching of geocoding results (1-week TTL)
- Exceptionless error reporting

**Key Methods:**
```csharp
public static async Task<string> ReverseGecocodingAsync(
    Car car, 
    double latitude, 
    double longitude, 
    bool forceGeocoding = false, 
    bool insertGeocodecache = true)
public async Task<double?> GetOutsideTempAsync(CancellationToken cancellationToken = default)
public string GetRegion() // Determines API endpoint by timezone
```

**Rate Limiting:**
```csharp
private static int MapQuestCount { get; set; }  // Track daily usage
private static int NominatimCount { get; set; } // Track daily usage
```

**Dependencies:**
- HttpClient
- IDbQueryBuilder (for caching)
- Car entity
- ILogger

**Design Pattern:** Strategy Pattern (multiple geocoding providers)

---

### Service 4: **ILocationCacheService** ↔ **LocationCacheService.cs**
**Purpose:** Persist and manage location/address data  
**Scope:** 450 lines  
**Responsibilities:**
- Update POI addresses in database
- Manage location cache tables (positions, addresses)
- Batch update operations
- Address validation and formatting
- Charging site information management

**Key Methods:**
```csharp
public void UpdateAllPosAddresses(int count = 50)
public void UpdateAllEmptyAddresses()
public int UpdateAllPOIAddresses(int count, string bucket)
public async Task UpdateLastChargingAddressAsync()
public void ResetLastChargingState()
private void UpdatePOIAdress(int id, string address, double altitude)
private void UpdatePosAddressName(int id, string addressname)
```

**Dependencies:**
- IDbQueryBuilder (for database operations)
- IGeolocationService (for address lookups)
- ILogger

**Design Pattern:** Repository Pattern (database abstraction)

---

## Migration Strategy

### Step 1: Create Interfaces (160 lines)
- `TeslaLogger/Data/Abstractions/ITeslaAPIClient.cs`
- `TeslaLogger/Data/Abstractions/ITokenManager.cs`
- `TeslaLogger/Data/Abstractions/IGeolocationService.cs`
- `TeslaLogger/Data/Abstractions/ILocationCacheService.cs`

### Step 2: Create Implementations (1,900 lines)
- `TeslaLogger/Services/TeslaAPI/TeslaAPIClient.cs`
- `TeslaLogger/Services/Authentication/TokenManager.cs`
- `TeslaLogger/Services/Geolocation/GeolocationService.cs`
- `TeslaLogger/Services/Location/LocationCacheService.cs`

### Step 3: Refactor WebHelper
- Keep WebHelper as a facade/coordinator class
- Inject 4 new services via constructor DI
- Migrate field-to-property conversions for Car state
- Maintain backward compatibility with existing public API

### Step 4: Update Program.cs
- Register 4 new services in IServiceCollection
- Configure appropriate lifetimes (Singleton vs Scoped)
- Wire up dependencies for each service

### Step 5: Verify Compilation
- Build TeslaLoggerNET8.sln
- Ensure 0 errors, capture warning count
- Verify DLL generation

---

## Key Design Decisions

### Why 4 Services?
1. **SRP Compliance:** Each service has single, well-defined responsibility
2. **Testability:** Each service can be unit-tested independently with mocks
3. **Reusability:** TeslaAPIClient can be shared; TokenManager is per-car
4. **ARM32 Optimization:** Smaller classes → better JIT compilation, lower memory
5. **Maintainability:** Clear interfaces, easier to understand and extend

### Singleton vs Scoped vs Transient?
- **TokenManager:** Tied to Car (identity); should be Scoped or per-Car instance
- **TeslaAPIClient:** Uses TokenManager; Scoped makes sense
- **GeolocationService:** Can be Singleton (stateless, just calls APIs)
- **LocationCacheService:** Uses database; Scoped (per request/operation)

### Backward Compatibility Approach
```csharp
// WebHelper becomes thin facade
public class WebHelper : IDisposable
{
    private readonly ITeslaAPIClient _apiClient;
    private readonly ITokenManager _tokenManager;
    private readonly IGeolocationService _geoService;
    private readonly ILocationCacheService _cacheService;
    
    // Keep public methods that delegate to services
    public async Task<string> GetCommand(string command, int timeout = 30)
        => await _apiClient.GetCommand(command, timeout);
    
    // Preserve existing internal fields for compatibility
    internal string Tesla_id => _tokenManager.TeslaId;
    internal string Tesla_vehicle_id => _tokenManager.TeslaVehicleId;
}
```

---

## Success Criteria

✅ **Interfaces Created:** All 4 interfaces compiled without errors  
✅ **Implementations Created:** All 4 services compiled without errors  
✅ **Zero Compilation Errors:** TeslaLoggerNET8.sln builds successfully  
✅ **Backward Compatibility:** Existing WebHelper public API unchanged  
✅ **Test Coverage:** Unit tests for critical methods (token refresh, geocoding)  
✅ **Documentation:** XML docs on all public members  

---

## Next Steps

1. **Create 4 interfaces** (160 lines) - validate SRP design
2. **Create 4 implementations** (1,900 lines) - extract from WebHelper
3. **Compile & fix errors** - iterate on design until 0 errors
4. **Update Program.cs** - register services in DI container
5. **Update WebHelper facade** - wire current code to use services
6. **Document Phase 3** - completion report with metrics

---

## Timeline

- **Interface Creation:** 30 minutes
- **Implementation Extraction:** 2 hours
- **Compilation & Error Fixes:** 1 hour
- **Program.cs Integration:** 30 minutes
- **Testing & Verification:** 1 hour
- **Documentation:** 30 minutes

**Total Estimated Effort:** 5.5 hours

---

## Risks & Mitigation

| Risk | Mitigation |
|------|-----------|
| **Circular Dependencies** | Design services with single direction of dependency (e.g., TeslaAPIClient → TokenManager, not vice versa) |
| **Breaking Existing Code** | Keep WebHelper public API intact; use facade pattern for delegation |
| **Token Refresh Race Conditions** | Use SemaphoreSlim for thread-safe token refresh in TokenManager |
| **Geocoding Provider Failures** | Implement failover between MapQuest→Nominatim→OpenTopoData with appropriate error handling |
| **ARM32 Memory Pressure** | Ensure LocationCacheService batches large operations; use streaming queries |

---

**Status:** Ready to begin implementation  
**Next Action:** Create interfaces (Step 1)
