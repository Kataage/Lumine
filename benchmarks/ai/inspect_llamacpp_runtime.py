#!/usr/bin/env python3
import hashlib
import io
import urllib.request
import zipfile

ASSETS = [
    (
        "cpu",
        "https://github.com/ggml-org/llama.cpp/releases/download/v0.4.1/llama-v0.4.1-bin-win-cpu-x64.zip",
    ),
    (
        "vulkan",
        "https://github.com/ggml-org/llama.cpp/releases/download/v0.4.1/llama-v0.4.1-bin-win-vulkan-x64.zip",
    ),
]

for backend, url in ASSETS:
    with urllib.request.urlopen(url, timeout=180) as response:
        payload = response.read()
    print(
        f"PACKAGE backend={backend} size={len(payload)} "
        f"sha256={hashlib.sha256(payload).hexdigest()} url={url}"
    )
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        for info in sorted(archive.infolist(), key=lambda item: item.filename.lower()):
            if info.is_dir():
                continue
            name = info.filename.replace("\\", "/")
            lower = name.lower()
            if (
                lower.endswith("llama-server.exe")
                or lower.endswith(".dll")
            ):
                data = archive.read(info)
                print(
                    f"FILE backend={backend} path={name} size={len(data)} "
                    f"sha256={hashlib.sha256(data).hexdigest()}"
                )
