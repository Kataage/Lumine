# Lumine v2

Lumine v2 is a greenfield rebuild focused on a fast, low-overhead image-library core before AI features are added.

## Foundation stack

- .NET 10 LTS
- Avalonia 12
- SQLite via Microsoft.Data.Sqlite
- libvips via NetVips
- Windows x64 as the first production target
- NativeAOT as a release requirement

The legacy Go/Wails implementation remains available on historical branches. v2 does not import or reference the legacy production source tree.

## Module boundaries

- **Lumine.Core**: dependency-free product/domain contracts.
- **Lumine.Library**: local-library persistence and indexing; depends only on Core.
- **Lumine.Image**: image decoding, thumbnails and cache infrastructure; depends only on Core.
- **Lumine.Viewer**: large-collection viewer contracts/rendering; depends only on Core.
- **Lumine.App**: composition root and Avalonia desktop shell.
- **Lumine.Foundation.Smoke**: runtime verification for foundation dependencies.

See [docs/architecture.md](docs/architecture.md).

## Build

```powershell
dotnet restore Lumine.sln
dotnet build Lumine.sln -c Release
dotnet run --project tests/Lumine.Foundation.Smoke/Lumine.Foundation.Smoke.csproj -c Release
dotnet publish src/Lumine.App/Lumine.App.csproj -c Release -r win-x64 --self-contained true
```

The repository pins SDK 10.0.401 in `global.json`.
