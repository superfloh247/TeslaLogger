#!/usr/bin/env python3
"""
Phase 1: Convert old-style collection initialization to new().
IMPORTANT: Preserves original line endings (CRLF or LF)
"""

import re
from pathlib import Path

def convert_file_collections(filepath):
    """
    Convert old-style collection initialization to new().
    Preserves original line ending format.
    """
    with open(filepath, 'rb') as f:
        content_bytes = f.read()
    
    # Detect line ending type
    if b'\r\n' in content_bytes:
        line_ending = '\r\n'
        text_content = content_bytes.decode('utf-8', errors='ignore')
    elif b'\n' in content_bytes:
        line_ending = '\n'
        text_content = content_bytes.decode('utf-8', errors='ignore')
    else:
        line_ending = '\n'
        text_content = content_bytes.decode('utf-8', errors='ignore')
    
    lines = text_content.split(line_ending)
    
    # Keep the final empty line only if original had it
    if text_content.endswith(line_ending):
        has_trailing_newline = True
    else:
        has_trailing_newline = False
    
    conversions = 0
    modified_lines = []
    
    for line in lines:
        original_line = line
        
        # Match: new Dictionary<...>() or new List<...>() etc
        line = re.sub(r'new\s+Dictionary\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+List\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+HashSet\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+Queue\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+Stack\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        
        if line != original_line:
            conversions += 1
        
        modified_lines.append(line)
    
    # Reconstruct with original line endings
    modified_content = line_ending.join(modified_lines)
    if has_trailing_newline:
        modified_content += line_ending
    
    # Write back as binary to preserve exact encoding
    with open(filepath, 'wb') as f:
        f.write(modified_content.encode('utf-8'))
    
    return conversions

# Process all .cs files
base_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
total_conversions = 0
files_changed = []

for cs_file in sorted(base_path.glob('*.cs')):
    try:
        count = convert_file_collections(str(cs_file))
        if count > 0:
            files_changed.append((cs_file.name, count))
            total_conversions += count
    except Exception as e:
        print(f"❌ {cs_file.name}: {e}")

# Display results
print(f"\n{'File':<45} {'Conversions':<12}")
print("-" * 57)
for filename, count in sorted(files_changed, key=lambda x: -x[1]):
    print(f"{filename:<45} {count:<12}")

print("-" * 57)
print(f"{'TOTAL':<45} {total_conversions:<12} conversions")
print(f"\n✅ {len(files_changed)} files modified (line endings preserved)")
