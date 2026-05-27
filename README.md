# Splitter

Splitter is a high-performance command line tool for cutting one or more video files into equal or 
fixed‑length segments using multi‑threaded FFmpeg execution. It supports batch input, flexible 
duration formats, rotation, smart face/body‑aware cropping, ETA and speed reporting, with nice GUI 
or both rich and plain‑text terminal output.

## Features

- Human face or body detection with smart cropping
- Multi-threaded FFmpeg splitting for maximum throughput
- Equal or fixed‑length segmentation
- Batch input via file masks or list files
- Smart cropping with face/body tracking
- Rotation correction
- ETA, speed, and progress display
- FFmpeg passthrough for advanced control
- [Potentially] Cross-platform (.NET 10)

## Screenshots

### Command line interface
![Splitter](splitter-cli/splitter.png)
### Graphical user interface
![Splitter UI](splitter-ui/screenshot.png)

## Requirements

- FFmpeg and FFprobe available in system PATH
- .NET 10 Runtime or newer

## More info

[Command line tool](splitter-cli/README.md)
[GUI tool](splitter-ui/README.md)
