#!/usr/bin/env python3
"""
Phase 2: Modernize null pattern matching
Convert: == null → is null
Convert: != null → is not null
Preserve line endings (CRLF/LF)
"""

import re
from pathlib import Path

def convert_null_patterns(filepath):
    """
    Convert old-style null checks to modern pattern matching.
    Preserves line endings.
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
    
    # Keep the final line ending if original had it
    has_trailing_newline = text_content.endswith(line_ending)
    
    conversions = 0
    modified_lines = []
    
    for line in lines:
        original_line = line
        
        # Pattern 1: == null → is null
        # Careful: don't match "== nullable" or other false positives
        # Match: <identifier/expression> == null
        # Use negative lookahead to avoid matching "==" in comments
        if ' == null' in line and not line.strip().startswith('//'):
            # Replace: word == null with word is null
            # But be careful with:
            #   - dr[col] == null
            #   - obj.field == null
            #   - func() == null
            #   - (expression) == null
            line = re.sub(r'(\w+|\]|\))\s*==\s*null(?!\w)', r'\1 is null', line)
            
            if line != original_line:
                conversions += 1
                modified_lines.append(line)
                continue
        
        # Pattern 2: != null → is not null
        if ' != null' in line and not line.strip().startswith('//'):
            # Replace: word != null with word is not null
            line = re.sub(r'(\w+|\]|\))\s*!=\s*null(?!\w)', r'\1 is not null', line)
            
            if line != original_line:
                conversions += 1
                modified_lines.append(line)
                continue
        
        modified_lines.append(line)
    
    # Reconstruct with original line endings
    modified_content = line_ending.join(modified_lines)
    if has_trailing_newline:
        modified_content += line_ending
    
    # Write back
    with open(filepath, 'wb') as f:
        f.write(modified_content.encode('utf-8'))
    
    return conversions

# Process all .cs files
base_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
total_conversions = 0
files_changed = []
errors = []

for cs_file in sorted(base_path.glob('*.cs')):
    try:
        count = convert_null_patterns(str(cs_file))
        if count > 0:
            files_changed.append((cs_file.name, count))
            total_conversions += count
    except Exception as e:
        errors.append((cs_file.name, str(e)))

# Also process subdirectories
for cs_file in sorted(base_path.glob('*/*.cs')):
    try:
        count = convert_null_patterns(str(cs_file))
        if count > 0:
            rel_path = cs_file.relative_to(base_path)
            files_changed.append((str(rel_path), count))
            total_conversions += count
    except Exception as e:
        rel_path = cs_file.relative_to(base_path)
        errors.append((str(rel_path), str(e)))

# Display results
print(f"\n{'File':<50} {'Conversions':<12}")
print("-" * 62)
for filename, count in sorted(files_changed, key=lambda x: -x[1]):
    print(f"{filename:<50} {count:<12}")

print("-" * 62)
print(f"{'TOTAL':<50} {total_conversions:<12} conversions")
print(f"\n✅ {len(files_changed)} files modified")

if errors:
    print(f"\n⚠️ {len(errors)} errors encountered:")
    for filename, error in errors:
        print(f"  - {filename}: {error}")
