"""Build step: copies the entire backend tree (including nodes/, models/, training/)
into bin/ so the LambdaFlow CLI can package it via compileDirectory: bin."""

import os
import shutil
import glob

BIN = "bin"
SDK_SOURCE = os.path.normpath(os.path.join("..", "lambdaflow", "Sdk", "Python", "lambdaflow.py"))

os.makedirs(BIN, exist_ok=True)


_SKIP_DIRS = {"bin", "__pycache__", ".git", "node_modules"}


def copy_tree(src_dir: str, dst_dir: str):
    os.makedirs(dst_dir, exist_ok=True)
    for item in os.listdir(src_dir):
        if item in _SKIP_DIRS:
            continue
        src = os.path.join(src_dir, item)
        dst = os.path.join(dst_dir, item)
        if os.path.isdir(src):
            copy_tree(src, dst)
        elif item.endswith(".py") and item != "build.py":
            shutil.copy2(src, dst)


# Copy all Python sources preserving directory structure
copy_tree(".", BIN)

# Copy the LambdaFlow SDK
if not os.path.isfile(SDK_SOURCE):
    raise FileNotFoundError(f"LambdaFlow Python SDK not found at {SDK_SOURCE}")
shutil.copy(SDK_SOURCE, os.path.join(BIN, "lambdaflow.py"))

count = sum(len(files) for _, _, files in os.walk(BIN) if files)
print(f"Backend built — {count} files in {BIN}/")
