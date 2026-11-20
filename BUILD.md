# Building from source

**Requirements:** Windows + [.NET SDK 9](https://dotnet.microsoft.com/download) (x64).

The app bundles two external tools as embedded resources. Before building, download the Windows x64 binaries and place them here:

- `src/embed/N_m3u8DL-RE` — from [N_m3u8DL-RE releases](https://github.com/nilaoda/N_m3u8DL-RE/releases) (the `win-x64` build)
- `src/embed/ffmpeg` — from any static FFmpeg win64 build, e.g. [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases) (`ffmpeg-master-latest-win64-lgpl.zip` → `bin/ffmpeg`)

Then publish one portable, self-contained binary:

```bat
cd src
dotnet publish M3u8Downloader.csproj -c Release -o ..\build
```

Result: a single file in `..\build\` that runs on any Windows 10/11 x64 PC with no .NET installed. The two tools are extracted to `%LOCALAPPDATA%\M3u8Downloader\bin` on first run.

### Fast UI-only build (no embedded tools)

Handy for working on the interface — skips embedding the ~150 MB of tools:

```bat
dotnet build M3u8Downloader.csproj -c Release -p:NoEmbed=true
```

### Developer flags

| Flag | What it does |
|------|--------------|
| `--selftest` | offline wiring check (embedded tools, paths) |
| `--e2e <m3u8 url>` | real headless download test through the engine |
| `--render out.png [demo]` | render the window to a PNG (`demo` = a filled "finished" state) |
