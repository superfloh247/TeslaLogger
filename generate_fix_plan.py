#!/usr/bin/env python3
"""
Comprehensive Warning Analysis and Fix Plan
"""

import subprocess
import re
from collections import defaultdict

# Run clean build and capture output
result = subprocess.run(['bash', '-c', 
    'cd /Users/lindner/VSCode/TeslaLogger && dotnet clean TeslaLoggerNET8.sln >/dev/null 2>&1 && dotnet build TeslaLoggerNET8.sln 2>&1'],
    capture_output=True, text=True)
output = result.stdout + result.stderr

# Parse warnings
warnings_by_code = defaultdict(list)
warnings_by_file = defaultdict(list)

for line in output.split('\n'):
    if 'warning CS' in line or 'warning CA' in line or 'warning SYSLIB' in line:
        # Extract code
        code_match = re.search(r'warning ((?:CS|CA|SYSLIB)\d+)', line)
        if code_match:
            code = code_match.group(1)
            
            # Extract file
            file_match = re.search(r'/([^/]+\.cs)\(', line)
            file = file_match.group(1) if file_match else 'Unknown'
            
            # Extract line number
            line_num_match = re.search(r'\.cs\((\d+)', line)
            line_num = line_num_match.group(1) if line_num_match else '?'
            
            # Extract message (simplified)
            msg_match = re.search(r':\s+(.+?)\s+\[', line)
            msg = msg_match.group(1)[:80] if msg_match else 'Unknown'
            
            warnings_by_code[code].append({
                'file': file,
                'line': line_num,
                'msg': msg,
                'full_line': line
            })
            warnings_by_file[file].append(code)

print("=" * 90)
print(" COMPREHENSIVE WARNING ANALYSIS & FIX PLAN")
print("=" * 90)

total_warnings = sum(len(v) for v in warnings_by_code.values())
total_files = len(warnings_by_file)

print(f"\nSUMMARY:")
print(f"  Total Warnings: {total_warnings}")
print(f"  Files Affected: {total_files}")
print(f"  Warning Codes: {len(warnings_by_code)}\n")

# Categorize warnings by severity and type
null_safety = ['CS8625', 'CS8618', 'CS8600', 'CS8767', 'CS8604', 'CS0165']
unused = ['CS0169', 'CS0649', 'CS0414', 'CS0067', 'CS0168']
platform_specific = ['CA1416']
obsolete = ['CS0618', 'SYSLIB0014']
not_found = ['CS0103', 'CS1061', 'CS0246']
other = ['CA1031', 'CS0162']

categories = {
    'Null Safety Issues (CS86xx)': (null_safety, '🔴 HIGH - Type safety errors'),
    'Unused Code (CS016x, CS064x, CS041x, CS006x)': (unused, '🟡 MEDIUM - Code cleanup'),
    'Platform-Specific APIs (CA1416)': (platform_specific, '🟠 MEDIUM-HIGH - Compatibility'),
    'Obsolete APIs (CS0618, SYSLIB)': (obsolete, '🟡 MEDIUM - Use modern replacements'),
    'Symbol Not Found (CS010x, CS106x, CS024x)': (not_found, '🔴 HIGH - May indicate errors'),
    'Other': (other, '🟡 MEDIUM - Design/pattern issues'),
}

print("=" * 90)
print("WARNINGS BY CATEGORY & SEVERITY\n")

total_by_cat = {}
for cat_name, (codes, severity) in categories.items():
    count = sum(len(warnings_by_code.get(c, [])) for c in codes if c in warnings_by_code)
    if count > 0:
        total_by_cat[cat_name] = (count, severity, codes)
        print(f"{severity} - {cat_name}")
        print(f"  Total: {count} warnings\n")

# Show Top 15 warning codes
print("\n" + "=" * 90)
print("TOP WARNING CODES BY FREQUENCY\n")
print(f"{'Code':<10} {'Count':<8} {'Sample Files':<50}")
print("-" * 70)

sorted_codes = sorted(warnings_by_code.items(), key=lambda x: len(x[1]), reverse=True)

code_descriptions = {
    'CS8625': 'NULL literal to non-nullable type conversion',
    'CS8604': 'Possible NULL reference argument',
    'CS8600': 'NULL literal/value to non-nullable type',
    'CS8618': 'Non-nullable field missing initialization',
    'CS0169': 'Field declared but never used',
    'CS0649': 'Field never assigned (default value)',
    'CS0618': 'Obsolete member used',
    'CA1416': 'Platform-specific API call',
    'SYSLIB0014': 'Type is obsolete',
    'CS067': 'Event never used',
    'CS0414': 'Field assigned but never used',
    'CS0168': 'Variable declared but never used',
    'CS0162': 'Unreachable code',
    'CS0103': 'Name not found in context',
    'CS1061': 'Type has no method definition',
}

for code, items in sorted_codes[:20]:
    count = len(items)
    files_set = set(item['file'] for item in items)
    sample_files = ', '.join(list(files_set)[:3])
    if len(files_set) > 3:
        sample_files += f', +{len(files_set)-3} more'
    
    desc = code_descriptions.get(code, '')
    print(f"{code:<10} {count:<8} {sample_files:<50}")
    if desc:
        print(f"    → {desc}")

# Show affected files
print("\n" + "=" * 90)
print("FILES WITH MOST WARNINGS\n")
print(f"{'File':<50} {'Count':<8}")
print("-" * 60)

sorted_files = sorted(warnings_by_file.items(), key=lambda x: len(x[1]), reverse=True)
for filename, codes in sorted_files[:15]:
    count = len(codes)
    unique_codes = len(set(codes))
    print(f"{filename:<50} {count:<8} ({unique_codes} different codes)")

# Generate fix plan
print("\n" + "=" * 90)
print("RECOMMENDED FIX PLAN\n")

print("PHASE 1: CRITICAL NULL SAFETY ISSUES (Highest Priority)")
print("-" * 70)
null_count = sum(len(warnings_by_code.get(c, [])) for c in null_safety)
print(f"Warnings to fix: {null_count}")
print("Impact: Type safety, potential runtime errors")
print("Action items:")
print("  ✓ Add #nullable annotations where needed")
print("  ✓ Change constructor initializations to use null-coalescing (??)")
print("  ✓ Add null checks or assertions")
print("  ✓ Add ! pragmas for verified non-null values")
print("Effort: ~30-40 hours (many scattered across files)")
print("Impact on build: None (warnings only)")
print()

print("PHASE 2: UNUSED CODE CLEANUP (Medium Priority)")
print("-" * 70)
unused_count = sum(len(warnings_by_code.get(c, [])) for c in unused)
print(f"Warnings to fix: {unused_count}")
print("Impact: Code clarity, maintainability")
print("Action items:")
print("  ✓ Remove unused private fields")
print("  ✓ Remove unused parameters (if safe)")
print("  ✓ Remove unused events")
print("  ✓ Comment out unused local variables (if intent unclear)")
print("Effort: ~15-20 hours")
print("Impact on build: None (cleanup only)")
print()

print("PHASE 3: OBSOLETE API REPLACEMENTS (Medium Priority)")
print("-" * 70)
obs_count = sum(len(warnings_by_code.get(c, [])) for c in obsolete)
print(f"Warnings to fix: {obs_count}")
print("Sample obsolete types:")
print("  - WebClient → HttpClient")
print("  - WebRequest/HttpWebRequest → HttpClient")
print("  - System.Security APIs → newer alternatives")
print("Effort: ~20-30 hours (required refactoring)")
print("Impact: Prepares for future .NET versions")
print()

print("PHASE 4: PLATFORM-SPECIFIC API GUARDS (Lower Priority)")
print("-" * 70)
plat_count = sum(len(warnings_by_code.get(c, [])) for c in platform_specific)
print(f"Warnings to fix: {plat_count}")
print("Impact: Cross-platform compatibility")
print("Action items:")
print("  ✓ Add platform guards: [SupportedOSPlatform(...)]")
print("  ✓ Add runtime platform checks")
print("Effort: ~5-10 hours")
print("Impact: Enables Linux deployment (relevant for Raspberry Pi)")
print()

print("=" * 90)
print("PRIORITY RECOMMENDATION FOR RASPBERRY PI TARGET")
print("=" * 90)
print("""
Given Raspberry Pi 3B target with ARM32/Linux deployment:

1. MUST DO (Required for Linux deployment):
   - Fix platform-specific API warnings (CA1416) - needed for ARM Linux
   - Resolve critical null safety issues that could cause runtime errors
   Estimated: 10 hours

2. SHOULD DO (Improves stability):
   - Add null safety annotations systematically
   - Fix unused field warnings (they may indicate abandoned code)
   Estimated: 20 hours

3. CAN DEFER (Nice to have):
   - Replace obsolete APIs (works now, but future-proof needed)
   - Full code cleanup (doesn't affect functionality)
   Estimated: 30+ hours

Total for MVP: ~30-35 hours
Total for comprehensive: ~65-90 hours

Recommendation: Start with Phase 4 (platform guards), then Phase 1 (critical safety)
""")

print("\n" + "=" * 90)
print("END OF ANALYSIS")
print("=" * 90)
