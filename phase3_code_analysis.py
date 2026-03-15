#!/usr/bin/env python3
"""
Phase 3: Code Structure Analysis
Analyzes method patterns and identifies refactoring opportunities
"""

import re
from pathlib import Path
from collections import defaultdict

def analyze_file(filepath):
    """Analyze code structure of a C# file"""
    with open(filepath, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()
    
    stats = {
        'filename': filepath.name,
        'lines': len(content.split('\n')),
        'methods': defaultdict(list),
        'properties': 0,
        'fields': 0,
        'is_partial': 'partial class' in content,
        'namespaces': [],
    }
    
    # Count methods by type
    for match in re.finditer(r'(public|private|internal|protected)\s+(async\s+)?(static\s+)?(?:void|bool|int|string|double|Task|object|[A-Za-z_]\w*)\s+([A-Za-z_]\w*)\s*\(', content):
        access = match.group(1)
        is_async = bool(match.group(2))
        is_static = bool(match.group(3))
        name = match.group(4)
        stats['methods'][(access, 'async' if is_async else 'sync', 'static' if is_static else 'instance')].append(name)
    
    # Count properties
    stats['properties'] = len(re.findall(r'\{\s*get;\s*set;\s*\}', content))
    
    # Count fields
    stats['fields'] = len(re.findall(r'(public|private|internal|protected)\s+[^;]*\s+_?\w+\s*;', content))
    
    return stats

# Analyze top files
base_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
files_to_check = [
    'DBHelper.cs',
    'DBHelper.Connection.cs',
    'WebHelper.cs',
    'WebServer.cs',
    'Car.cs',
    'Tools.cs',
    'TelemetryParser.cs',
]

print("\n" + "="*80)
print("PHASE 3: CODE STRUCTURE ANALYSIS FOR REFACTORING")
print("="*80 + "\n")

results = []
for filename in files_to_check:
    filepath = base_path / filename
    if filepath.exists():
        stats = analyze_file(filepath)
        results.append(stats)

# Display results
print(f"{'File':<30} {'Lines':<8} {'Partial':<10} {'Methods':<10} {'Props':<8} {'Refactoring Candidate':<20}")
print("-" * 90)

for r in sorted(results, key=lambda x: -x['lines']):
    is_partial = "✓ Yes" if r['is_partial'] else "✗ No"
    total_methods = sum(len(v) for v in r['methods'].values())
    candidate = "HIGH" if r['lines'] > 3000 else "MEDIUM" if r['lines'] > 2000 else "LOW"
    
    print(f"{r['filename']:<30} {r['lines']:<8} {is_partial:<10} {total_methods:<10} {r['properties']:<8} {candidate:<20}")

print("\n" + "="*80)
print("REFACTORING RECOMMENDATIONS")
print("="*80 + "\n")

print("""
Based on code analysis:

1. **HIGH PRIORITY (>3000 lines)**:
   - DBHelper.cs (7546 lines) - ALREADY PARTIAL (DBHelper.Connection.cs exists)
     * Consider: DBHelper.Charging.cs, DBHelper.Analysis.cs, DBHelper.Costs.cs
   
   - WebHelper.cs (5473 lines) - NOT PARTIAL
     * Consider: WebHelper.Auth.cs (authentication), WebHelper.Streaming.cs (telemetry)
   
   - WebServer.cs (3662 lines) - NOT PARTIAL
     * Consider: WebServer.Admin.cs (admin endpoints), WebServer.API.cs (data endpoints)

2. **MEDIUM PRIORITY (2000-3000 lines)**:
   - UpdateTeslalogger.cs, Tools.cs, Car.cs
     * Can be refactored with helper/utility classes

3. **REFACTORING STRATEGY**:
   - Start with WebServer.cs (smaller, manageable)
   - Create WebServer.Admin.cs for admin panel endpoints
   - Create WebServer.API.cs for data endpoints
   - Ensure backwards compatibility through public methods
   - Keep as partial classes for gradual migration
   - Follow existing pattern (DBHelper -> DBHelper.Connection.cs)

4. **CODE QUALITY IMPROVEMENTS**:
   - Extract common patterns to utility classes
   - Use dependency injection where applicable
   - Create service interfaces for testability
   - Consider async/await for I/O operations (Raspberry Pi constraint!)

5. **PROPERTY MODERNIZATION**:
   - Most properties already modernized
   - Focus on auto-properties where backing fields exist
   - Consider init-only properties for immutable data
""")

print("="*80)
print("EXECUTION ORDER FOR PHASE 3")
print("="*80 + "\n")

print("""
Phase 3A: Analysis & Property Modernization
  - Inventory all properties and backing fields
  - Modernize simple properties to auto-properties
  - Add documentation for complex properties
  - Estimated: 50-100 conversions

Phase 3B: Strategic File Splitting (WebServer.cs)
  - Extract admin panel endpoints → WebServer.Admin.cs
  - Extract data/chart endpoints → WebServer.API.cs
  - Extract utility methods → WebServer.Utils.cs
  - Keep main WebServer.cs as coordinator
  - Maintain public interface compatibility
  - Estimated: 1000+ lines relocated

Phase 3C: WebHelper.cs Splitting (future iteration)
  - Extract authentication logic → WebHelper.Auth.cs
  - Extract telemetry streaming → WebHelper.Streaming.cs
  - Extract data parsing → WebHelper.Parsing.cs

Phase 3D: DBHelper Continuation (future iteration)
  - Extract charging state logic → DBHelper.Charging.cs
  - Extract analysis methods → DBHelper.Analysis.cs
  - Extract cost calculations → DBHelper.Costs.cs

Next Steps:
1. Proceed with Phase 3A (property modernization - quick wins)
2. Execute Phase 3B (WebServer.cs splitting - manageable scope)
3. Document refactoring patterns for future phases
4. Maintain test compatibility and build verification
""")
