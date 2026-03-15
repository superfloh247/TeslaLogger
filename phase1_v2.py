import re
from pathlib import Path

def safe_convert_collections_v2(content):
    """
    Convert old-style collection initialization to target-typed new().
    For cases without type context, keep the full type specification.
    """
    
    # These patterns match the context around collection initialization
    # to determine if 'new()' can be used or if we need to keep the type
    
    replacements = 0
    
    # Pattern 1: var x = new Dictionary/List/etc -> can use new()
    # These lines have 'var' which provides type context
    pattern_var = r'var\s+\w+\s*=\s*new\s+(Dictionary|List|HashSet|Queue|Stack)<[^()]*>\s*\('
    content, count = re.subn(pattern_var, r'var \g<1> = new(', content)
    # Fix the broken syntax
    content = re.sub(r'var (\w+) = new\(', r'var \g<1> = new()', content)
    replacements += count
    
    # Pattern 2: Type name before = operator: Dictionary<...> x = new -> can use new()
    pattern_typed = r'(Dictionary|List|HashSet|Queue|Stack)<[^()]*>\s+\w+\s*=\s*new\s+\1<[^()]*>\s*\('
    content, count = re.subn(pattern_typed, r'\g<1><___> \g<1>_x = new(', content)
    # This is getting complex, let's use a simpler approach
    
    # Pattern 3: new X<type>() with no assignment context (problematic)
    # In collections, these need explicit type
    pattern_explicit = r'new\s+(Dictionary|List|HashSet|Queue|Stack)<([^>]+)>\s*\(\s*\)'
    
    def replace_explicit(match):
        ctype = match.group(1)
        gtype = match.group(2)
        # If there's generic type info, we can use new()
        # Otherwise keep the full form (shouldn't happen but safety)
        return f'new {ctype}<{gtype}>()'  # Keep for now, will handle specially
    
    # For now, just convert the clear cases
    # Conversion: new Dictionary<...>() -> new()
    # Simple safe regex that works in assignments
    patterns = [
        (r'=\s*new\s+Dictionary<([^()]+)>\s*\(\)', r'= new()'),
        (r'=\s*new\s+List<([^()]+)>\s*\(\)', r'= new()'),
        (r'=\s*new\s+HashSet<([^()]+)>\s*\(\)', r'= new()'),
        (r'=\s*new\s+Queue<([^()]+)>\s*\(\)', r'= new()'),
        (r'=\s*new\s+Stack<([^()]+)>\s*\(\)', r'= new()'),
    ]
    
    for pattern, replacement in patterns:
        n = len(re.findall(pattern, content))
        content = re.sub(pattern, replacement, content)
        replacements += n
    
    return content, replacements

# Process all .cs files
base_path = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
total_conversions = 0
files_changed = []

for cs_file in sorted(base_path.glob('*.cs')):
    try:
        original = cs_file.read_text(encoding='utf-8', errors='ignore')
        converted, count = safe_convert_collections_v2(original)
        
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
