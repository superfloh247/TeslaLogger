#!/usr/bin/env python3
"""
Warning Analysis Tool - Analyzes C# compiler warnings from build output
"""

import subprocess
import re
from collections import defaultdict

def analyze_warnings():
    # Run build
    result = subprocess.run(['dotnet', 'build', 'TeslaLoggerNET8.sln'], 
                           capture_output=True, text=True, cwd='/Users/lindner/VSCode/TeslaLogger')
    build_output = result.stdout + result.stderr
    
    # Parse warnings
    warnings_by_code = defaultdict(list)
    warnings_by_file = defaultdict(list)
    warning_texts = defaultdict(int)
    
    for line in build_output.split('\n'):
        if 'warning ' in line:
            # Extract code (CS####)
            code_match = re.search(r'(CS\d+)', line)
            if code_match:
                code = code_match.group(1)
                
                # Extract file
                file_match = re.search(r'([^/\\]+\.cs)\(', line)
                file = file_match.group(1) if file_match else 'Unknown'
                
                # Extract message
                msg_match = re.search(r':\s+"?([^"\[\]]+)"?(?:\s+\[|$)', line)
                msg = msg_match.group(1) if msg_match else 'Unknown message'
                
                warnings_by_code[code].append({'file': file, 'msg': msg, 'line': line})
                warnings_by_file[file].append(code)
                warning_texts[msg] += 1
    
    return warnings_by_code, warnings_by_file, warning_texts

def main():
    print("Building project and analyzing warnings...\n")
    warnings_by_code, warnings_by_file, warning_texts = analyze_warnings()
    
    total_warnings = sum(len(v) for v in warnings_by_code.values())
    
    print("=" * 80)
    print("COMPILER WARNINGS ANALYSIS")
    print("=" * 80)
    
    print(f"\nTotal Warnings: {total_warnings}\n")
    
    print("TOP WARNING CODES (By Frequency):\n")
    print(f"{'Code':<10} {'Count':<8} {'Description':<50}")
    print("-" * 70)
    
    sorted_codes = sorted(warnings_by_code.items(), key=lambda x: len(x[1]), reverse=True)
    
    for code, items in sorted_codes[:15]:
        count = len(items)
        # Map code to description
        descriptions = {
            'CS8625': 'NULL-Literal to Non-Nullable Type',
            'CS8618': 'Non-Nullable Field Missing NULL',
            'CS8600': 'NULL to Non-Nullable Conversion',
            'CS8767': 'Parameter NULL-ability Mismatch',
            'CS8604': 'Possible NULL Reference Argument',
            'CS0162': 'Unreachable Code',
            'CS0169': 'Field Never Used',
            'CS0618': 'Obsolete Member',
            'CS0103': 'Name Not Found',
            'CS1061': 'No Definition / Method Not Found',
            'SYSLIB0014': 'Obsolete API (Use Alternative)',
            'CS0246': 'Type/Namespace Not Found',
        }
        desc = descriptions.get(code, 'See description below')
        print(f"{code:<10} {count:<8} {desc:<50}")
    
    print(f"\n\nTOP FILES WITH WARNINGS:\n")
    print(f"{'Filename':<45} {'Count':<8}")
    print("-" * 55)
    
    sorted_files = sorted(warnings_by_file.items(), key=lambda x: len(x[1]), reverse=True)
    for filename, codes in sorted_files[:20]:
        count = len(codes)
        print(f"{filename:<45} {count:<8}")
    
    print(f"\n\nWARNING DETAILS BY CODE:\n")
    
    for code, items in sorted_codes:
        if len(items) > 0:
            print(f"\n{code}: ({len(items)} warnings)")
            print(f"  Sample: {items[0]['msg'][:70]}")
            print(f"  Files affected: {len(set(it['file'] for it in items))}")
            files_with_code = defaultdict(int)
            for item in items:
                files_with_code[item['file']] += 1
            top_files = sorted(files_with_code.items(), key=lambda x: x[1], reverse=True)[:3]
            for f, c in top_files:
                print(f"    - {f}: {c}")

if __name__ == '__main__':
    main()
