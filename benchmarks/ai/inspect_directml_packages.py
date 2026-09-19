#!/usr/bin/env python3
import hashlib
import io
import urllib.request
import zipfile

PACKAGES = [
    (
        "Microsoft.ML.OnnxRuntime.DirectML",
        "1.29.0",
        "https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime.directml/1.29.0/microsoft.ml.onnxruntime.directml.1.29.0.nupkg",
    ),
    (
        "Microsoft.AI.DirectML",
        "1.15.4",
        "https://api.nuget.org/v3-flatcontainer/microsoft.ai.directml/1.15.4/microsoft.ai.directml.1.15.4.nupkg",
    ),
]

for name, version, url in PACKAGES:
    with urllib.request.urlopen(url, timeout=120) as response:
        payload = response.read()
    print(f"PACKAGE {name} {version} size={len(payload)} sha256={hashlib.sha256(payload).hexdigest()}")
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        for info in sorted(archive.infolist(), key=lambda item: item.filename.lower()):
            lower = info.filename.lower().replace("\\", "/")
            interesting = (
                lower.startswith("runtimes/win-x64/native/")
                or lower.endswith("/directml.dll")
                or lower == "directml.dll"
            )
            if not interesting or info.is_dir():
                continue
            data = archive.read(info)
            print(
                f"FILE {info.filename} size={len(data)} "
                f"sha256={hashlib.sha256(data).hexdigest()}"
            )
