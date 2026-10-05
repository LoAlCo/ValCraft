# Notices

## SkyCraft

`fabric/` (the Minecraft side) and `protocol/valcraft_protocol.h` are derived from SkyCraft by
chasmlol, <https://github.com/chasmlol/SkyCraft>, under the MIT License. They were renamed from
SkyCraft/Skyrim to ValCraft/Valheim with `tools/rename_fork.py`, then changed for Valheim.
The Valheim plugin (`valheim/`) ports ideas from SkyCraft's SKSE plugin (collision voxelization,
tick interpolation, the input bridge) to C#.

```
MIT License

Copyright (c) 2026 chasmlol

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Minecraft side dependencies

Fabric API (Apache-2.0) and e4mc (MIT, https://github.com/vgskye/e4mc-minecraft-architectury) are fetched by Gradle and bundled unmodified with the Minecraft side of releases.
