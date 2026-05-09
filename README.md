# Splitter

Splitter is a command line tool for cutting video files into equal length or fixed length segments using FFmpeg.  
It supports multi threaded splitting, ETA and speed reporting, flexible duration formats, and a simple terminal user interface.

This tool is intended for creators, archivists, and automation workflows that need fast and predictable video segmentation.

---

## Features

- Multi threaded splitting for high performance
- Equal length segments (default)
- Fixed length segments with the `--force` option
- Flexible duration formats (`Ns`, `NmMs`, or plain seconds)
- Estimate only mode (`--estimate`)
- Terminal progress display with ETA and speed
- Custom output filename masks
- FFmpeg passthrough for advanced users
- Cross platform (.NET 10)

---

## Requirements

- FFmpeg and FFprobe installed and available in the system PATH
- .NET 10 SDK or newer

>Note: You must download YuNet ONNX model:
https://github.com/opencv/opencv_zoo/tree/master/models/face_detection_yunet (github.com in Bing)

---

## Usage

```
splitter <input.mp4> <output_folder> [options] [--] <ffmpeg passthrough>
```

---

## Options

### --duration=<value>

Overrides the default 60 second target chunk size.

Accepted formats:

| Format | Meaning |
|--------|---------|
| Ns | N seconds |
| NmMs | N minutes and M seconds |
| N | N seconds (plain number) |

Examples:

```
--duration=90s
--duration=2m30s
--duration=45
```

Without `--force`, the program adjusts the segment length so that all segments are equal.

---

### --force

Uses the duration exactly as provided.  
The last segment may be shorter.

Example:

```
--duration=45 --force
```

---

### --estimate

Prints calculated segment information and exits.  
No splitting is performed.

Example:

```
splitter video.mp4 out/ --duration=2m30s --estimate
```

---

### --mask=<pattern>

Custom output filename pattern.

Default:

```
<OriginalName>_Seg%03d.mp4
```

Supports:

- `%03d` for zero padded index
- `%d` for plain index

Example:

```
--mask="Part_%03d.mp4"
```

---

### FFmpeg passthrough

Anything after `--` is passed directly to FFmpeg.

Example:

```
splitter video.mp4 out/ -- -an -sn
```

---

## How It Works

1. Reads total duration using ffprobe  
2. Parses the target duration (default 60 seconds or from `--duration=`)  
3. Computes the number of segments  
4. If not forced, adjusts segment length so all segments are equal  
5. Runs multiple FFmpeg processes in parallel  
6. Displays progress, ETA, and speed in the terminal  

---

## Examples

Split into equal 60 second segments:

```
splitter video.mp4 out/
```

Split into equal 90 second segments:

```
splitter video.mp4 out/ --duration=90s
```

Split into fixed 45 second segments:

```
splitter video.mp4 out/ --duration=45 --force
```

Estimate only:

```
splitter video.mp4 out/ --duration=2m30s --estimate
```

Custom output names:

```
splitter video.mp4 out/ --mask="Clip_%03d.mp4"
```

Pass extra flags to FFmpeg:

```
splitter video.mp4 out/ -- -an -sn
```

---

## Building

```
dotnet build
```

Or run directly:

```
dotnet run -- video.mp4 out/
```

