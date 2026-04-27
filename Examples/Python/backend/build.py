"""Tiny build step: copies all Python sources next to a bin/ folder so the
LambdaFlow CLI can pick them up via compileDirectory: bin."""

import glob
import os
import shutil

os.makedirs("bin", exist_ok=True)
for src in glob.glob("*.py"):
    if src == "build.py":
        continue
    shutil.copy(src, os.path.join("bin", src))
print(f"Copied {len(glob.glob('bin/*.py'))} python files into bin/")
