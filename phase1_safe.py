import re
from pathlib import Path

def safe_convert_collections(content):
    """
    Convert old-style collection initialization to target-typed new().
    Handles nested generics properly by matching angle brackets.
    """
    
    # Patterns: new Dictionary<...>() or new List<...>() etc.
    # Must preserve nested generics like Dictionary<string, List<int>>
    
    patterns = [
        (r'new\s+Dictionary\s*<([^<>]*(?:<[^<>]*>[^<>]*)*)>\s*\(\s*\)', 'Dictionary'),
        (r'new\s+List\s*<([^<>]*(?:<[^<>]*>[^<>]*)*)>\s*\(\s*\)', 'List'),
        (r'new\s+HashSet\s*<([^<>]*(?:<[^<>]*>[^<>]*)*)>\s*\(\s*\)', 'HashSet'),
        (r'new\s+Queue\s*<([^<>]*(?:<[^<>]*>[^<>]*)*)>\s*\(\s*\)', 'Queue'),
        (r'new\s+Stack\s*<([^<>]*(?:<[^<>]*>[^<>]*)*)>\s*\(\s*\)', 'Stack'),
    ]
    
    total_replacements = 0
    
    for pattern, ctype in patterns:
        replacements = len(re.findall(pattern, content))
        content = re.sub(pattern, 'new()', content)
        total_replacements += replacements
        
    return content, total_replacements

# Process all .cs files
base_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
total_conversions = 0
files_changed = []

for cs_file in sorted(base_path.glob('*.cs')):
    try:
        original = cs_file.read_text(encoding='utf-8', errors='ignore')
        converted, count = safe_convert_collections(original)
        
        if count > 0:
            cs_file.write_text(converted, encoding='utf-8')
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
