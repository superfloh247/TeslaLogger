#!/usr/bin/env python3
"""
Fix Dictionary<> instantiation patterns in C# code.
Uses bracket matching to properly handle nested generics.

Converts:
  new Dictionary<...>() { }   →    new Dictionary<...> { }
  new Dictionary<...>()       →    new Dictionary<...>()
"""

import re
import sys
from pathlib import Path


def find_matching_bracket(text, start_pos, open_char, close_char):
    """Find the matching closing bracket, handling nesting."""
    if start_pos >= len(text) or text[start_pos] != open_char:
        return -1
    
    count = 1
    pos = start_pos + 1
    
    while pos < len(text) and count > 0:
        if text[pos] == open_char:
            count += 1
        elif text[pos] == close_char:
            count -= 1
        pos += 1
    
    return pos - 1 if count == 0 else -1


def fix_dictionary_instantiation(content):
    """Fix Dictionary<>() { } → Dictionary<> { } patterns."""
    
    # Pattern: new Dictionary< ... >() { or ( or .
    pattern = r'new\s+Dictionary\s*<'
    matches = list(re.finditer(pattern, content))
    
    if not matches:
        return content, 0
    
    result = []
    last_end = 0
    changes = 0
    
    for match in matches:
        result.append(content[last_end:match.end()])
        
        # We're at the opening <, find the matching >
        angle_close = find_matching_bracket(content, match.end() - 1, '<', '>')
        if angle_close == -1:
            # Malformed, skip
            result.append(content[match.end():match.end() + 10])
            last_end = match.end() + 10
            continue
        
        # Now check if there's a () immediately after >
        after_angle = angle_close + 1
        
        # Skip whitespace
        ws_match = re.match(r'\s*', content[after_angle:])
        if ws_match:
            ws_len = len(ws_match.group())
            check_pos = after_angle + ws_len
        else:
            check_pos = after_angle
        
        # Check for () pattern
        if check_pos + 1 < len(content) and content[check_pos:check_pos+2] == '()':
            # Check what comes after ()
            after_paren = check_pos + 2
            if after_paren < len(content):
                next_char = content[after_paren]
                if next_char in '{.':
                    # This is Dictionary<>() { or Dictionary<>().
                    # Remove the () before the { or .
                    result.append(content[match.end():check_pos])
                    result.append(content[after_paren:])
                    last_end = after_paren
                    changes += 1
                    continue
        
        # No fix needed, include content from start to after >
        result.append(content[match.end():angle_close + 1])
        last_end = angle_close + 1
    
    result.append(content[last_end:])
    return ''.join(result), changes


def process_file(file_path):
    """Process a single C# file."""
    try:
        content = file_path.read_text(encoding='utf-8')
    except Exception as e:
        print(f"❌ {file_path}: Cannot read - {e}")
        return False
    
    fixed_content, change_count = fix_dictionary_instantiation(content)
    
    if change_count > 0:
        file_path.write_text(fixed_content, encoding='utf-8')
        print(f"✅ {file_path.name}: Fixed {change_count} Dictionary<>() patterns")
        return True
    else:
        return False


def main():
    """Find and process all C# files."""
    if len(sys.argv) > 1:
        search_dir = Path(sys.argv[1])
    else:
        search_dir = Path('/Users/lindner/VSCode/TeslaLogger/TeslaLogger')
    
    cs_files = list(search_dir.glob('*.cs'))
    print(f"Found {len(cs_files)} C# files")
    
    files_changed = 0
    for cs_file in sorted(cs_files):
        if process_file(cs_file):
            files_changed += 1
    
    print(f"\n📊 Total files changed: {files_changed}/{len(cs_files)}")


if __name__ == '__main__':
    main()
