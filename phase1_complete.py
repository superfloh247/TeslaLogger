import re
from pathlib import Path

def convert_collections(content):
    """Convert old-style collection initialization to target-typed new()."""
    
    # Pattern: new Dictionary<...> ( ... )
    # Replacement: new()
    conversions = [
        (r'new\s+Dictionary<([^>]+)>\s*\(\s*\)', r'new()'),
        (r'new\s+List<([^>]+)>\s*\(\s*\)', r'new()'),
        (r'new\s+HashSet<([^>]+)>\s*\(\s*\)', r'new()'),
        (r'new\s+Queue<([^>]+)>\s*\(\s*\)', r'new()'),
        (r'new\s+Stack<([^>]+)>\s*\(\s*\)', r'new()'),
    ]
    
    count = 0
    for pattern, replacement in conversions:
        new_content, replacements = re.subn(pattern, replacement, content)
        count += replacements
        content = new_content
    
    return content, count

# Process all .cs files
base_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
total_conversions = 0
files_changed = 0

for cs_file in sorted(base_path.glob('*.cs')):
    try:
        original = cs_file.read_text(encoding='utf-8')
        converted, count = convert_collections(original)
        
        if count > 0:
            cs_file.write_text(converted, encoding='utf-8')
            files_changed += 1
            total_conversions += count
            print(f"✅ {cs_file.name:<40} ({count:3} conversions)")
    except Exception as e:
        print(f"❌ {cs_file.name}: {e}")

print(f"\n{'='*60}")
print(f"Total files changed: {files_changed}")
print(f"Total conversions: {total_conversions}")
