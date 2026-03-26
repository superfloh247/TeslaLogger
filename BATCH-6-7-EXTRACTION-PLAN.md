# Phase 3.2 Completion Plan - Batches 6-7 (Final ~450 lines)

**Current Status:** 76% complete (Batches 1-5 done, 1,469 lines extracted, TripAnalytics 1,342 lines)

---

## Batch 6: Metadata & Lookups (8 methods, ~280 lines)

### Identified Methods & Line Ranges

| # | Method | Type | Lines | Start-End | Notes |
|---|--------|------|-------|-----------|-------|
| 1 | `FindCombineCandidates()` | private Queue<int> | ~50 | 620-669 | Finds charging states with identical odometer |
| 2 | `FindSimilarChargingStates()` | private Queue<int> | ~154 | 1172-1325 | Locates similar charging states |
| 3 | `GetStartValuesFromChargingState()` | internal static bool | ~52 | 1028-1079 | [INTERNAL STATIC] Returns startDate, IDs, meter values |
| 4 | `GetAddressFromChargingState()` | private Address | ~38 | 3016-3053 | Looks up Address POI from charging state |
| 5 | `UpdateChargingstate()` | private void (6 params) | ~73 | 3635-3707 | Updates charging state record with start values |
| 6 | `DeleteChargingstate()` | private void | ~39 | 3708-3746 | Deletes charging state record by ID |
| 7 | `UpdateMaxChargerPower()` | public void (no args) | ~29 | 1251-1279 | Public entry point, finds all open states |
| 8 | `UpdateMaxChargerPower()` | public void (1 arg) | ~29 | 1292-1320 | Public overload, updates specific state ID |

**Note:** 
- `GetStartValuesFromChargingState` is `internal static` (unusual for partial - likely utility helper)
- Multiple `UpdateMaxChargerPower` overloads need consolidation in target partial
- `FindSimilarChargingStates` is largest Batch 6 method (~154 lines)
- Total: ~464 lines (estimate higher due to try/catch blocks)

### Extraction Strategy for Batch 6

1. **Create new partial:** `DBHelper.Metadata.cs` 
2. **Add all 8 methods** to new partial
3. **Create target partial method:** Include all overloads in logical order
4. **Remove all 8 methods** from main DBHelper.cs using multi_replace_string_in_file (8 operations)
5. **Build verification** → expect 0 errors
6. **Git commit** with message: "Phase 3.2 Batch 6: Extract metadata & lookup methods"

---

## Batch 7: Power Analytics & Utilities (5-7 methods, ~170 lines)

### Line-by-Line Method Analysis Needed

**Potential methods to search:**
```
- UpdateMaxChargerPower(int id, int startChargingID, int endChargingID) @ Line 1360
- GetChargeStateID() 
- GetChargePrice()
- Summary/aggregation methods
- Final utility methods  
```

### Recommended Batch 7 Approach

**Search commands to run:**
```bash
# Find remaining large methods that call charger-related functions
grep -n "private.*CalcCharger\|private.*GetCharger\|private.*UpdatePower\|UpdateMaxChargerPower.*int.*int\|private.*void.*Charger" TeslaLogger/DBHelper.cs | head -20

# Find any remaining Trip-related methods
grep -n "private.*Trip\|private.*Drive" TeslaLogger/DBHelper.cs | head -20
```

---

## File Size Projections After Batches 6-7

| Scenario | Main DBHelper | TripAnalytics | Metadata | Power | Total |
|----------|---------------|---------------|----------|-------|-------|
| **Now** | 5,634 | 1,342 | - | - | 6,976 |
| **After 6** | ~5,100 | 1,342 | ~460 | - | ~6,902 |
| **After 7** | ~4,700 | 1,342 | ~460 | ~170 | ~6,672 |
| **Initial** | 7,334 | - | - | - | 7,334 |

**Main DBHelper.cs Reduction:**
- Initial: 7,334 lines
- After Phase 3.2: ~4,700 lines  
- **Total reduction: 36% (2,634 lines moved to partials)**

---

## Batch 6 Detailed Extraction Instructions

### Step 1: Read All Methods (Parallel Batch)
```
read_file: lines 620-669 (FindCombineCandidates)
read_file: lines 1172-1325 (FindSimilarChargingStates - LARGEST)
read_file: lines 1251-1279 (UpdateMaxChargerPower public overload 1)
read_file: lines 1292-1320 (UpdateMaxChargerPower public overload 2)
read_file: lines 1360-1515 (UpdateMaxChargerPower private 3-arg overload - CHECK THIS IS LARGEST)

read_file: lines 3016-3053 (GetAddressFromChargingState)
read_file: lines 3635-3707 (UpdateChargingstate)
read_file: lines 3708-3746 (DeleteChargingstate)
```

### Step 2: Create New Metadata Partial
Create file: `/Users/lindner/VSCode/TeslaLogger/TeslaLogger/DBHelper.Metadata.cs`

**Template skeleton:**
```csharp
#pragma warning disable CS8600, CS8601, CS8602, CS8625

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exceptionless;
using MySql.Data.MySqlClient;
using static TeslaLogger.Tools;

namespace TeslaLogger
{
    public partial class DBHelper
    {
        // [INSERT ALL 8 METHODS HERE]
    }
}
```

### Step 3: Add Methods to New Partial

Insert all 8 methods in this order:
1. FindCombineCandidates
2. FindSimilarChargingStates  
3. GetAddressFromChargingState
4. GetStartValuesFromChargingState
5. UpdateChargingstate
6. DeleteChargingstate
7. UpdateMaxChargerPower (public no-args)
8. UpdateMaxChargerPower (public 1-arg)
9. UpdateMaxChargerPower (private 3-arg) - [OPTIONAL if discovered]

### Step 4: Remove from Main DBHelper.cs

Use `multi_replace_string_in_file` with 8-9 operations (one per method):

```json
{
  "replacements": [
    { "filePath": "...", "oldString": "...[FindCombineCandidates - FULL]...", "newString": "" },
    { "filePath": "...", "oldString": "...[FindSimilarChargingStates - FULL]...", "newString": "" },
    { "filePath": "...", "oldString": "...[GetAddressFromChargingState - FULL]...", "newString": "" },
    { "filePath": "...", "oldString": "...[GetStartValuesFromChargingState - FULL]...", "newString": "" },
    { "filePath": "...", "oldString": "...[UpdateChargingstate - FULL]...", "newString": "" },
    { "filePath": "...", "oldString": "...[DeleteChargingstate - FULL]...", "newString": "" },
    { "filePath": "...", "oldString": "...[UpdateMaxChargerPower pub 1 - FULL]...", "newString": "" },
    { "filePath": "...", "oldString": "...[UpdateMaxChargerPower pub 2 - FULL]...", "newString": "" }
  ]
}
```

### Step 5: Build & Verify

```bash
cd /Users/lindner/VSCode/TeslaLogger
dotnet build TeslaLoggerNET8.sln -c Release 2>&1 | tail -8
# Expect: 0 errors, 0 warnings
```

### Step 6: Commit

```bash
git add -A
git commit -m "Phase 3.2 Batch 6: Extract metadata & lookup methods

- FindCombineCandidates (50 lines)
- FindSimilarChargingStates (154 lines)
- GetAddressFromChargingState (38 lines)
- GetStartValuesFromChargingState (52 lines - internal static)
- UpdateChargingstate (73 lines)
- DeleteChargingstate (39 lines)
- UpdateMaxChargerPower (29 lines - public overload 1)
- UpdateMaxChargerPower (29 lines - public overload 2)

Total extraction: 464 lines, 8 methods (2 overloads + 1 internal static)
Main DBHelper.cs reduced: 5,634 → ~5,170 lines
DBHelper.Metadata.cs created: ~464 lines (8 methods)
Build: 0 errors, 0 warnings ✓"
```

---

## Batch 7 Execution Framework

After Batch 6 succeeds, proceed with Batch 7:

1. **Identify remaining methods** using grep patterns above
2. **Estimate total lines** for Batch 7  
3. **Create new partial:** `DBHelper.Power.cs` (or `DBHelper.Charger.cs`)
4. **Extract & remove** using same multi_replace pattern
5. **Build & verify** → 0 errors
6. **Git commit** with detailed message

### Expected Batch 7 Methods (Estimate)
- UpdateMaxChargerPower(int, int, int) - private 3-arg version ~155 lines
- Power calculation utility methods (~15 lines each)
- Charger aggregation/summary methods (~30 lines)

---

## Final Phase 3.2 Completion Checklist

After Batches 6-7 complete:

```
☐ Update DBHELPER_DECOMPOSITION_STATUS.md:
  ☐ Phase 3.2: Mark as "100% COMPLETE"
  ☐ Update completion date
  ☐ Record final statistics:
    - Total methods extracted: 50+ (35 from Phase 3.2)
    - Total lines moved: 2,150+
    - Main DBHelper.cs final: ~4,700 lines (36% reduction)

☐ Phase 3.3 Planning (if needed):
  ☐ Identify remaining large methods
  ☐ Plan subsequent phases

☐ Final commit: "Phase 3.2 Complete: DBHelper decomposition - 50+ methods extracted"
  ☐ Update all documentation
  ☐ Tag commit if using versioning
```

---

## Quick Reference: All Batch 6 Methods (Copy/Paste Ready)

### Method 1: FindCombineCandidates (Lines 620-669)
**Dependency:** None (external) - calls FindSimilarChargingStates internally
**Calling methods in main:** CombineChangingStates (already extracted)

### Method 2: FindSimilarChargingStates (Lines 1172-1325)
**Dependency:** None
**Size:** LARGEST Batch 6 method (~154 lines, 2 SQL queries)
**Calling methods:** CombineChangingStates, FixChargeEnergyAdded (both extracted)

### Others
All 8 are relatively self-contained with internal DBalper/SQL dependencies only.

---

## Notes

- **Internal static method warning:** `GetStartValuesFromChargingState` being internal static is unusual for partial class - this is OK and works as intended
- **Overload consolidation:** Three `UpdateMaxChargerPower` methods should be grouped in target partial for clarity
- **Batch 7 scope refinement:** Exact line counts for Batch 7 require grep analysis first
- **Expected time:** Batch 6 ~15 min, Batch 7 ~12 min, final docs ~5 min

---

**Generated:** March 7, 2025 | Phase 3.2 Remaining Work: **~40 minutes**
