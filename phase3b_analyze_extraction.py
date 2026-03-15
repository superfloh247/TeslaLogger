#!/usr/bin/env python3
"""
Phase 3B: WebServer.cs Admin Methods Extraction
Safely extracts all 25 Admin_* methods from WebServer.cs
to WebServer.Admin.cs partial class file.
"""

import re
from pathlib import Path

def extract_admin_methods():
    """Extract admin methods to partial class."""
    
    webserver_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger/WebServer.cs')
    admin_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger/WebServer.Admin.cs')
    
    # Read WebServer.cs
    with open(webserver_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    print("=== PHASE 3B: WEBSERVER.ADMIN EXTRACTION ===\n")
    print(f"Source file: {webserver_path.name}")
    print(f"Target file: {admin_path.name}\n")
    
    # Find all admin methods with line numbers
    admin_methods = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if re.search(r'(private|public)\s+(static\s+)?void\s+Admin_', line):
            method_start = i
            method_name = re.search(r'Admin_\w+', line).group(0)
            
            # Find matching closing brace
            brace_count = 0
            method_end = i
            found_opening = False
            
            for j in range(i, len(lines)):
                brace_count += lines[j].count('{')
                brace_count -= lines[j].count('}')
                
                if lines[j].count('{') > 0:
                    found_opening = True
                
                if found_opening and brace_count == 0:
                    method_end = j
                    break
            
            admin_methods.append({
                'name': method_name,
                'start': method_start,
                'end': method_end,
                'lines': method_end - method_start + 1
            })
        
        i += 1
    
    print(f"Found {len(admin_methods)} admin methods:\n")
    
    total_lines = 0
    for idx, method in enumerate(admin_methods, 1):
        print(f"  {idx:2}. {method['name']:<35} Lines {method['start']+1:4d}-{method['end']+1:4d} ({method['lines']:3d} lines)")
        total_lines += method['lines']
    
    print(f"\nTotal lines to extract: {total_lines}")
    print(f"\nNext steps:")
    print(f"  1. Review extraction boundaries")
    print(f"  2. Execute: python3 phase3b_extract_methods.py --execute")
    print(f"  3. Verify build: dotnet build TeslaLoggerNET8.sln")
    print(f"  4. Commit changes")

if __name__ == '__main__':
    extract_admin_methods()
