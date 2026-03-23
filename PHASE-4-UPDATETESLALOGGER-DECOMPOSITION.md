# Phase 4: UpdateTeslalogger.cs Decomposition Strategy
**Date:** March 23, 2026  
**Status:** PLANNING  
**Target:** Split 3,085-line UpdateTeslalogger into focused services

---

## Analysis: UpdateTeslalogger.cs Current State

### Line Count Distribution
- **Total Lines:** 3,085
- **Public Methods:** 15+
- **Internal Methods:** 10+
- **Private Methods:** 30+
- **Static Fields:** 8+

### Current Responsibilities (VIOLATES SRP)

#### 1. **Database Schema Management** (~1,200 lines)
- Schema version checking and migration
- 20+ individual table schema checks
- View creation and updates
- Table constraint validation
- UTF-8mb4 compatibility handling
- Database growth monitoring
- **Methods:** `CheckDBSchema_*()` (cars, charging, drivestate, positions, etc.), `CheckDBViews()`, `CheckDBCharset()`, `AssertAlterDB()`

#### 2. **Version Management & Updates** (~900 lines)
- Application version checking
- Update download and installation
- NET8 installation verification
- .NET framework management
- Version comparison logic
- HTTP download handling
- **Methods:** `CheckForNewVersion()`, `DownloadUpdateAndInstallAsync()`, `UpdateNeeded()`, `CheckNET8Installed()`, `GetLastVersionCheck()`

#### 3. **Grafana Integration** (~600 lines)
- Dashboard configuration
- Datasource management
- Plugin configuration
- Dashboard JSON templating
- Language file updates for Grafana
- Grafana version updates
- **Methods:** `UpdateGrafanaAsync()`, `UpdateDatasourceUID()`, `AllowUnsignedPlugins()`, `CopyLanguageFileToTimelinePanel()`, `UpdateDefaultCar()`, replacement tag methods

#### 4. **System Configuration & File Management** (~300 lines)
- Apache configuration updates
- PHP.ini modifications
- Language file management
- SSL certificate updates
- Crontab backup setup
- File permission management
- **Methods:** `UpdateApacheConfig()`, `UpdatePHPini()`, `CertUpdate()`, `CheckBackupCrontab()`, `Chmod()`, `FileCheckerAsync()`

#### 5. **State Management & Initialization** (~85 lines)
- Version check caching with SemaphoreSlim
- Comforting message thread lifecycle
- Update progress tracking
- Application lifecycle hooks
- **Fields:** `lastTeslaLoggerVersionCheck`, `lastTeslaLoggerVersionCheckObj`, `ComfortingMessages`, `done`, `DownloadUpdateAndInstallStarted`

---

## Decomposition Plan: 2 Primary Services + 1 Support Class

### Service 1: **IDbSchemaMigrator** ↔ **DbSchemaMigrator.cs**
**Purpose:** Manage database schema versioning and migrations  
**Scope:** 600 lines  
**Responsibilities:**
- Validate/create essential database tables
- Check individual table schemas and constraints
- Create/update database views
- Handle charset upgrades (utf-8mb4)
- Monitor database growth
- Track schema versions

**Key Methods:**
```csharp
public async Task ValidateAllSchemasAsync()
public void CheckTableSchema<T>(string tableName, ...)
public void CheckDatabaseViews()
public Task<long> GetLargestTableMBAsync()
public void ValidateCharset()
public bool SchemaVersionMatches(string versionKey, int expectedVersion)
```

**Database Tables Handled:**
- areaa, can, candata, cars, car_version
- charging, chargingstate, drivestate
- httpcodes, mothership, mothershipcommands
- pos, shiftstate, state, superchargers, superchargerstate
- Battery, Alerts, Cruisestate, TPMS

**Dependencies:**
- IDbQueryBuilder
- Logfile
- KVS (key-value store for versioning)

**Design Pattern:** Strategy Pattern (different schema checks per table), Repository Pattern (encapsulates schema logic)

---

### Service 2: **IApplicationUpdateManager** ↔ **ApplicationUpdateManager.cs**
**Purpose:** Manage application updates and installation  
**Scope:** 500 lines  
**Responsibilities:**
- Check for new versions online
- Download updates
- Install/apply updates
- Verify .NET 8 installation
- Check SSL certificates
- Manage system services
- Progress reporting

**Key Methods:**
```csharp
public async Task CheckForNewVersionAsync()
public async Task<bool> UpdateNeededAsync(string currentVersion, string onlineVersion)
public async Task DownloadAndInstallUpdateAsync(string downloadUrl, CancellationToken cancellationToken)
public bool IsNET8Installed()
public void UpdateCertificates()
public void RestartApplication()
public event EventHandler<UpdateProgressEventArgs>? ProgressChanged;
```

**Dependencies:**
- HttpClient
- ILogger
- Logfile
- ProcessStartInfo for system calls

**Design Pattern:** Observer Pattern (events for progress), Async/Task-based

---

### Service 3: **IGrafanaDashboardConfigurer** ↔ **GrafanaDashboardConfigurer.cs**
**Purpose:** Manage Grafana dashboard configuration and updates  
**Scope:** 450 lines  
**Responsibilities:**
- Update Grafana configuration files
- Manage datasource UIDs
- Configure plugins
- Update dashboards with language-specific content
- Handle dashboard JSON templating
- Manage Grafana version updates

**Key Methods:**
```csharp
public async Task UpdateGrafanaAsync()
public string UpdateDatasourceUID(string dashboardJson, string newUID)
public string AllowUnsignedPlugins(string configPath, bool overwrite)
public void CopyLanguageFileToTimelinePanel(string language)
public void CopySettingsToTimelinePanel()
public string UpdateDefaultCar(string dashboardJson, string carName, string carId, string carLabel)
public void UpdateGrafanaVersion()
```

**Tag Replacement Methods:**
- `ReplaceValuesTags()` - Replace {{value_xxx}} tags
- `ReplaceAliasTags()` - Replace {{alias_xxx}} tags
- `ReplaceLanguageTags()` - Replace {{lang_xxx}} tags
- `ReplaceTitleTag()` - Replace {{title}} tags
- `ReplaceNameTag()` - Replace {{name}} tags

**Dependencies:**
- Newtonsoft.Json (for JSON parsing)
- ILogger
- File system access
- Logfile

**Design Pattern:** Template Method (tag replacement pipeline), Strategy Pattern (different replacement strategies per tag type)

---

### Support Class: **SystemConfigurationManager.cs**
**Purpose:** Handle system-level configuration and file management  
**Scope:** 250 lines  
**Responsibilities:**
- Update Apache configuration
- Modify PHP.ini settings
- Manage SSL certificates
- Setup cron jobs
- Modify file permissions
- Create default configuration files

**Key Methods:**
```csharp
public string UpdateApacheConfig(string configFilePath, bool writeChanges = true)
public void UpdatePHPini()
public void CertUpdate()
public void CheckBackupCrontab()
public void CreateEmptyWeatherIniFile()
public void Chmod(string filename, int modeValue, bool logging = true)
```

**Dependencies:**
- File system access
- Process execution for shell commands
- ILogger
- Logfile

**Design Pattern:** Utility Class (static methods for system operations)

---

## Migration Strategy

### Step 1: Create Interfaces (180 lines)
- `IDbSchemaMigrator.cs`
- `IApplicationUpdateManager.cs`
- `IGrafanaDashboardConfigurer.cs`

### Step 2: Create Implementations (1,200 lines)
- `DbSchemaMigrator.cs`
- `ApplicationUpdateManager.cs`
- `GrafanaDashboardConfigurer.cs`
- `SystemConfigurationManager.cs` (no interface)

### Step 3: Refactor UpdateTeslalogger
- Keep `UpdateTeslalogger` as coordinator/orchestrator
- Inject 3 services via DI
- Call service methods from `Start()` method
- Maintain backward compatibility with public API

### Step 4: Update Program.cs
- Register services in IServiceCollection
- Configure appropriate lifetimes (Singleton vs Scoped)
- Wire up logging

### Step 5: Verify Compilation
- Build TeslaLoggerNET8.sln
- Ensure 0 errors, capture warning count
- Verify DLL generation

---

## Key Design Decisions

### Why 3 Services + 1 Utility Class?

1. **DbSchemaMigrator:** Schema management is cohesive, reusable, testable
2. **ApplicationUpdateManager:** Update lifecycle is complex, needs event reporting, distinct from schema
3. **GrafanaDashboardConfigurer:** Grafana is external system, separate integration concern, template-based
4. **SystemConfigurationManager:** File/system ops are utilities, can stay static (single responsibility)

### Singleton vs Scoped?

- **IDbSchemaMigrator:** Singleton (stateless schema validation, reusable across requests)
- **IApplicationUpdateManager:** Singleton (version check is global state, expensive HTTP calls)
- **IGrafanaDashboardConfigurer:** Singleton (stateless JSON/file operations)
- **SystemConfigurationManager:** Static utility (no DI needed, file ops are idempotent)

### Event Reporting Pattern

`ApplicationUpdateManager` will use .NET events for progress:

```csharp
public class UpdateProgressEventArgs : EventArgs
{
    public string Stage { get; set; }  // "Checking", "Downloading", "Installing"
    public int PercentComplete { get; set; }  // 0-100
    public string Message { get; set; }
}

public event EventHandler<UpdateProgressEventArgs>? ProgressChanged;
```

This allows UI/logging to track long-running updates without tight coupling.

---

## Success Criteria

✅ **Interfaces Created:** All 3 interfaces compiled without errors  
✅ **Implementations Created:** All 4 services compiled without errors  
✅ **Zero Compilation Errors:** TeslaLoggerNET8.sln builds successfully  
✅ **Backward Compatibility:** Existing UpdateTeslalogger public API unchanged  
✅ **Database Schema Validation:** All 20+ table schemas validated correctly  
✅ **Grafana Integration:** Dashboard JSON templating works correctly  
✅ **Test Coverage:** Unit tests for critical methods (version comparison, tag replacement)  

---

## Next Steps

1. **Create 3 interfaces** (180 lines) - validate SRP design
2. **Create 4 implementations** (1,200 lines) - extract from UpdateTeslalogger
3. **Compile & fix errors** - iterate on design until 0 errors
4. **Update Program.cs** - register services in DI container
5. **Update UpdateTeslalogger facade** - wire current code to use services
6. **Document Phase 4** - completion report with metrics

---

## Timeline

- **Interface Creation:** 40 minutes
- **Implementation Extraction:** 2 hours
- **Compilation & Error Fixes:** 1 hour
- **Program.cs Integration:** 30 minutes
- **UpdateTeslalogger Facade:** 30 minutes
- **Testing & Verification:** 1 hour
- **Documentation:** 30 minutes

**Total Estimated Effort:** 6 hours

---

## Risks & Mitigation

| Risk | Mitigation |
|------|-----------|
| **Schema check complexity** | Create helper methods for common patterns (column exists, add column if not exists) |
| **Grafana JSON parsing failures** | Comprehensive error handling, JSON validation before modification |
| **Update download interruptions** | Implement retry logic with exponential backoff |
| **System file permission errors** | Run chmod/file ops with try-catch, log failures, continue gracefully |
| **Database growth monitoring** | Cache results, limit query frequency to once per startup |

---

**Status:** Ready to begin Phase 4 implementation  
**Next Action:** Create interfaces (Step 1)
