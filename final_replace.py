import re
import os

os.chdir('TeslaLogger')
total_count = 0

for filename in os.listdir('.'):
    if filename.endswith('.cs'):
        try:
            with open(filename, 'r') as f:
                content = f.read()
            
            # Replace both patterns: literals and variables
            pattern = r'Task\.Delay\(([^)]+)\)\.GetAwaiter\(\)\.GetResult\(\)'
            count = len(re.findall(pattern, content))
            
            if count > 0:
                replacement = r'System.Threading.Thread.Sleep(\1)'
                new_content = re.sub(pattern, replacement, content)
                with open(filename, 'w') as f:
                    f.write(new_content)
                if count > 0:
                    print(f"{filename}: {count}")
                total_count += count
        except Exception as e:
            print(f"Error in {filename}: {e}")

print(f"\nTotal: {total_count}")
