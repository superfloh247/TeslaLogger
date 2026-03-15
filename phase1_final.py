import re
from pathlib import Path

def convert_file_collections(filepath):
    """
    Convert old-style collection initialization to new().
    Uses line-by-line processing with proper bracket matching.
    """
    with open(filepath, 'r', encoding='utf-8', errors='ignore') as f:
        lines = f.readlines()
    
    conversions = 0
    modified_lines = []
    
    for line in lines:
        # Skip if no collection pattern
        if not any(x in line for x in ['new Dictionary', 'new List', 'new HashSet', 'new Queue', 'new Stack']):
            modified_lines.append(line)
            continue
        
        # Pattern: new Collection<...>(...)
        # Replace with: new()
        # But only if the line has simple assignment pattern
        
        original_line = line
        
        # Match: new Dictionary<...>() or new List<...>() etc
        # This pattern works line-by-line without multiline issues
        line = re.sub(r'new\s+Dictionary\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+List\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+HashSet\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+Queue\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        line = re.sub(r'new\s+Stack\s*<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*\(\s*\)', 'new()', line)
        
        if line != original_line:
            conversions += 1
        
        modified_lines.append(line)
    
    # Write back
    with open(filepath, 'w', encoding='utf-8') as f:
        f.writelines(modified_lines)
    
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
print(f"\n✅ {len(files_changed)} files modified")
