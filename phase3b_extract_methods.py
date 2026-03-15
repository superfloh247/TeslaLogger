#!/usr/bin/env python3
"""
Phase 3B: Extract Admin Methods to WebServer.Admin.cs
Moves all 25 Admin_* methods from WebServer.cs to WebServer.Admin.cs partial class.
"""

import re
from pathlib import Path

def extract_admin_methods(execute=False):
    """Extract admin methods to partial class."""
    
    webserver_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger/WebServer.cs')
    admin_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger/WebServer.Admin.cs')
    
    # Read WebServer.cs
    with open(webserver_path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    print("=== PHASE 3B: WEBSERVER.ADMIN EXTRACTION ===\n")
    
    # Find all admin methods with line numbers
    admin_methods = []
    method_lines_to_remove = set()
    
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
            
            # Extract method lines
            method_lines = lines[method_start:method_end+1]
            admin_methods.append({
                'name': method_name,
                'start': method_start,
                'end': method_end,
                'lines': method_lines
            })
            
            # Mark these lines for removal
            for line_num in range(method_start, method_end + 1):
                method_lines_to_remove.add(line_num)
            
            i = method_end + 1
        else:
            i += 1
    
    print(f"Found {len(admin_methods)} admin methods")
    print(f"Total lines to extract: {sum(len(m['lines']) for m in admin_methods)}\n")
    
    # Read existing WebServer.Admin.cs to find where to insert
    if admin_path.exists():
        with open(admin_path, 'r', encoding='utf-8') as f:
            admin_content = f.read()
        # Find insertion point (before closing brace of last method)
        insert_point = admin_content.rfind('\n    }')
    else:
        admin_content = None
        insert_point = -1
    
    # Build new WebServer.Admin.cs content
    header = '''namespace TeslaLogger
{
    public partial class WebServer
    {
        // Admin panel endpoint handlers (extracted from WebServer.cs)
        // These methods handle administrative operations like backups, updates, configuration
        
'''
    
    footer = '''    }
}
'''
    
    # Collect all method implementations
    methods_content = ''
    for method in admin_methods:
        methods_content += ''.join(method['lines'])
        if method != admin_methods[-1]:
            methods_content += '\n'
    
    # Generate new admin file
    if admin_path.exists() and '// PLANNED METHODS' in admin_content:
        # Replace placeholder section
        admin_content_new = header + methods_content + footer
    else:
        admin_content_new = header + methods_content + footer
    
    if execute:
        # Write WebServer.Admin.cs
        with open(admin_path, 'w', encoding='utf-8') as f:
            f.write(admin_content_new)
        
        # Write WebServer.cs (remove admin methods)
        remaining_lines = [lines[i] for i in range(len(lines)) if i not in method_lines_to_remove]
        with open(webserver_path, 'w', encoding='utf-8') as f:
            f.writelines(remaining_lines)
        
        print(f"✅ Extraction complete!")
        print(f"   - WebServer.Admin.cs: Written ({len(admin_content_new)} bytes)")
        print(f"   - WebServer.cs: Updated (removed {len(method_lines_to_remove)} lines)")
        print(f"\nNext: Run 'dotnet build TeslaLoggerNET8.sln' to verify")
    else:
        print(f"Preview mode - no files changed")
        print(f"   Run with --execute to apply changes\n")
        print(f"WebServer.Admin.cs would contain {len(methods_content)} bytes of code")
        print(f"WebServer.cs would be reduced to {sum(len(lines[i]) for i in range(len(lines)) if i not in method_lines_to_remove)} bytes")

if __name__ == '__main__':
    import sys
    execute = '--execute' in sys.argv
    extract_admin_methods(execute=execute)
