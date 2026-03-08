# TubeBurn Authoring Stack

TubeBurn starts with **C#/.NET + Avalonia** across the application and the native DVD authoring port.

## Why C# first

- The heaviest media processing is delegated to `ffmpeg`, not the authoring orchestration layer.
- Delivery risk is dominated by binary correctness and debugging speed, not raw CPU throughput.
- Native .NET support for binary I/O, testing, and desktop UI keeps the first implementation path compact.

## Why keep the external bridge

- It preserves a practical path to autoplay DVD output before the native port reaches parity.
- It gives the project a reference execution path while native modules grow incrementally.
- It makes A/B validation against known-good outputs possible.

## When to revisit Rust

Revisit Rust only if profiling shows the authoring core itself is a bottleneck, if low-level buffer work becomes awkward to maintain safely in C#, or if a reusable standalone core library becomes a stronger priority than iteration speed.
