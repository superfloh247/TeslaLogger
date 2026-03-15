# Task tool-01: Fix CA1416 Warnings in Tools.cs

**File**: Tools.cs  
**Warnings Found**: 1 CA1416 (File.Decrypt)  
**Method Affected**: `DownloadToFile(Uri uri, string path, int timeout, bool overwrite)`  
**Location**: Line 2202  
**Status**: Ready for execution

## Warning Details

```
Tools.cs(2220,25): warning CA1416: Diese Aufrufsite ist auf allen Plattformen erreichbar. 
"File.Decrypt(string)" nur unterstützt für: 'windows'.
```

## Root Cause

The `DownloadToFile` method calls `File.Decrypt(path)` which is Windows-only DPAPI functionality:

```csharp
internal async static Task<bool> DownloadToFile(Uri uri, string path, int timeout = 60, bool overwrite = false)
{
    // ... 
    if (File.Exists(path) && overwrite)
    {
        File.Decrypt(path);  // ← Windows-only
    }
    // ...
}
```

## Fix Strategy

Mark the entire `DownloadToFile(Uri, string, int, bool)` method with `[SupportedOSPlatform("windows")]` since:
1. The method's primary purpose includes file decryption
2. File.Decrypt is Windows-only DPAPI functionality
3. No other platform-specific code needs this method

## Implementation

Add `[SupportedOSPlatform("windows")]` attribute above the method:

```csharp
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal async static Task<bool> DownloadToFile(Uri uri, string path, int timeout = 60, bool overwrite = false)
{
    // existing code
}
```

## Verification Steps

1. Build project: `dotnet build TeslaLoggerNET8.sln`
2. Check for CA1416 in output - should be gone
3. Verify 0 new warnings introduced
4. Git commit: `git add Tools.cs && git commit -m "Phase 1: Fix CA1416 in Tools.cs - mark DownloadToFile as Windows-only"`

## Files Modified
- Tools.cs - 1 method decorated with `[SupportedOSPlatform("windows")]`

---

**Status**: ✅ Ready to execute
