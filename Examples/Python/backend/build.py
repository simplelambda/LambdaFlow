"""Build the Python backend directory consumed by the LambdaFlow CLI."""

import os
import shutil
from pathlib import Path

BACKEND_DIR = Path(__file__).resolve().parent
BIN = BACKEND_DIR / "bin"
SDK_CANDIDATES = (
    BACKEND_DIR.parent / "lambdaflow" / "Sdk" / "Python" / "lambdaflow.py",
    BACKEND_DIR.parents[2] / "lambdaflow" / "Sdk" / "Python" / "lambdaflow.py",
)
SDK_SOURCE = next((path for path in SDK_CANDIDATES if path.is_file()), None)

BIN.mkdir(exist_ok=True)


_SKIP_DIRS = {"bin", "__pycache__", ".git", "node_modules"}


def copy_tree(src_dir: Path, dst_dir: Path) -> None:
    dst_dir.mkdir(exist_ok=True)
    for item in os.listdir(src_dir):
        if item in _SKIP_DIRS:
            continue
        src = src_dir / item
        dst = dst_dir / item
        if src.is_dir():
            copy_tree(src, dst)
        elif item.endswith(".py") and item != "build.py":
            shutil.copy2(src, dst)


# Copy all Python sources preserving directory structure
copy_tree(BACKEND_DIR, BIN)

# Copy the LambdaFlow SDK
if SDK_SOURCE is None:
    searched = ", ".join(str(path) for path in SDK_CANDIDATES)
    raise FileNotFoundError(f"LambdaFlow Python SDK not found. Searched: {searched}")
shutil.copy2(SDK_SOURCE, BIN / "lambdaflow.py")

count = sum(len(files) for _, _, files in os.walk(BIN))
print(f"Backend built — {count} files in {BIN}/")
