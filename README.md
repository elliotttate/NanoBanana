# NanoBanana (WinUI 3)

A dark-theme WinUI 3 desktop app that reproduces the core functionality of `nano-banana-pro-image-editor` in a native Windows experience.

## Feature parity

- Single image editing with Gemini 3 Pro Image.
- Batch folder processing (`.jpg`, `.jpeg`, `.png`, `.webp`) with recursive subfolder support.
- Incremental batch runs that process only new/changed files.
- Persistent processing index so folder history is remembered across app restarts/folder switching.
- Process mode for reviewing generated `variation_1..4` outputs, comparing against originals, and choosing the best image.
- Per-selection notes + transparency flag saved into output file metadata.
- Background redo queue in Process mode to regenerate and continue reviewing the next item.
- 4 generated variations per source image.
- Aspect ratio lock: generated outputs are normalized to the source image aspect ratio in single, batch, and process redo flows.
- Resolution options: `1K`, `2K`, `4K`.
- Before/after comparison slider in single-image mode.
- Save selected single variation to disk.
- Batch outputs written to a sibling `*_processed` folder while preserving source subfolder structure.
- Process selections written to a sibling `*_selected` folder while preserving source subfolder structure.
- Export current batch run results as a ZIP archive.
- Timestamped log console with severity coloring and clear action.
- Rotating loading status messages during generation.

## Requirements

- Windows 10/11
- .NET 9 SDK (or newer installed SDK that can target `net9.0-windows10.0.19041.0`)
- Gemini API key

## API key

The app resolves API key in this order:

1. Value entered in **Settings** (Connection API key field).
2. `GEMINI_API_KEY` environment variable.
3. `API_KEY` environment variable.

## Build

```powershell
dotnet build .\NanoBananaProWinUI.sln -c Release
```

## Run

```powershell
dotnet run --project .\NanoBananaProWinUI.csproj
```

## Notes

- Batch processing intentionally runs files sequentially with a short delay to reduce rate-limit pressure.
- If quota/rate-limit signals are detected, the app applies a cooldown pause before continuing.
