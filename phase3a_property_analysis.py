#!/usr/bin/env python3
"""
Phase 3A Property Analysis
Identifies properties that can be modernized to auto-properties.
"""

import re
from pathlib import Path
from collections import defaultdict

def analyze_properties(directory):
    """Find property patterns in C# files."""
    csharp_files = list(Path(directory).rglob('*.cs'))
    findings = defaultdict(list)
    
    for file_path in csharp_files:
        if 'obj' in str(file_path) or 'bin' in str(file_path):
            continue
            
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                lines = f.readlines()
            
            filename = file_path.name
            
            # Find property patterns
            property_pattern = re.compile(r'{\s*get\s*;\s*(?:set|private set)\s*;\s*}')
            auto_count = 0
            
            for line in lines:
                if property_pattern.search(line):
                    auto_count += 1
            
            if auto_count > 0:
                findings[filename] = {
                    'path': str(file_path.relative_to(directory)),
                    'properties': auto_count
                }
        except Exception:
            pass
    
    return findings

def main():
    dirpath = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
    
    print("=== PHASE 3A: PROPERTY MODERNIZATION ANALYSIS ===\n")
    
    findings = analyze_properties(dirpath)
    sorted_findings = sorted(findings.items(), 
                           key=lambda x: x[1]['properties'], 
                           reverse=True)
    
    print(f"{'File':<45} {'Properties':<12}")
    print("-" * 57)
    
    total = sum(v['properties'] for v in findings.values())
    for filename, info in sorted_findings[:15]:
        print(f"{filename:<45} {info['properties']:<12}")
    
    print("-" * 57)
    print(f"{'TOTAL OPPORTUNITIES':<45} {total:<12}\n")
    
    # Now find specific patterns: backing fields + getter/setter
    print("=== BACKING FIELD PATTERNS TO MODERNIZE ===\n")
    
    backing_field_pattern = re.compile(r'private\s+(?:readonly\s+)?(\w+)\s+_(\w+);')
    property_access_pattern = re.compile(r'public\s+(?:virtual\s+)?(\w+)\s+(\w+)\s*{\s*get\s*{\s*return\s+_\2')
    
    total_backing = 0
    files_with_backing = []
    
    for file_path in dirpath.rglob('*.cs'):
        if 'obj' in str(file_path) or 'bin' in str(file_path):
            continue
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()
            
            backing_matches = len(re.findall(backing_field_pattern, content))
            if backing_matches > 0:
                files_with_backing.append((file_path.name, backing_matches))
                total_backing += backing_matches
        except Exception:
            pass
    
    files_with_backing.sort(key=lambda x: x[1], reverse=True)
    
    for filename, count in files_with_backing[:10]:
        print(f"{filename:<45} {count:<12} backing fields")
    
    print(f"\nEstimated total backing field patterns: {total_backing}")
    print(f"\n✅ Modernization targets identified")
    print(f"   - Total auto-properties: {total}")
    print(f"   - Total backing fields: {total_backing}")
    print(f"   - Phase 3A scope: 50-100 conversions")

if __name__ == '__main__':
    main()
