# Task tool-02: Fix CA1416 Warnings in StaticMapProvider.cs

**File**: StaticMapProvider.cs  
**Warnings Found**: 1 CA1416 (Image.Save)  
**Method Affected**: `SaveImage(Bitmap image, string filename)`  
**Location**: Line 94  
**Status**: Ready for execution

## Warning Details

```
StaticMapProvider.cs(94,21): warning CA1416: Diese Aufrufsite ist auf allen Plattformen erreichbar. 
"Image.Save(string)" nur unterstützt für: windows, 6.1 und höhere Versionen.
```

## Root Cause

The `SaveImage` static method uses `System.Drawing.Bitmap.Save()` which is Windows-only:

```csharp
public static void SaveImage(Bitmap image, string filename)
{
    if (image != null)
    {
        try
        {
            image.Save(filename);  // ← Windows-only (System.Drawing on Windows)
            Logfile.Log("Create File: " + filename);
        }
        catch (Exception ex)
        {
            ex.ToExceptionless().FirstCarUserID().Submit();
            Tools.DebugLog("Exception", ex);
        }
    }
}
```

## Fix Strategy

Mark the entire `SaveImage(Bitmap, string)` method with `[SupportedOSPlatform("windows")]` since:
1. Method uses System.Drawing.Bitmap which is Windows-on specifically
2. Image.Save() requires Windows GDI+ subsystem
3. Method cannot function on Linux/ARM without major refactoring

## Implementation

Add `[SupportedOSPlatform("windows")]` attribute above the method:

```csharp
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static void SaveImage(Bitmap image, string filename)
{
    // existing code
}
```

## Verification Steps

1. Build project: `dotnet build TeslaLoggerNET8.sln`
2. Check for CA1416 in output - should be gone
3. Verify 0 new warnings introduced
4. Git commit: `git add StaticMapProvider.cs && git commit -m "Phase 1: Fix CA1416 in StaticMapProvider.cs - mark SaveImage as Windows-only"`

## Files Modified
- StaticMapProvider.cs - 1 method decorated with `[SupportedOSPlatform("windows")]`

---

**Status**: ✅ Ready to execute
