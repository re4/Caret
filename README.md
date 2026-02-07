# NotepadPlusPlusSharp #

A fast, dark-mode text editor for Windows built from scratch in C# / WPF.

## Why does this exist?

In early 2026, [Kaspersky reported](https://securelist.com/notepad-supply-chain-attack/118708/) that the Notepad++ update infrastructure had been compromised for months — from roughly June through December 2025. Attackers pushed malicious updates to real users, targeting government orgs, financial institutions, and individuals across multiple countries. The infection chains rotated constantly, making the whole thing hard to detect.

After reading that, I didn't feel great about running Notepad++ anymore. So I built my own replacement. NotepadPlusPlusSharp # does everything I actually used Notepad++ for, without depending on someone else's update pipeline.

## Features

**Editor**
- Syntax highlighting for 20+ languages (C#, C/C++, Python, JavaScript/TypeScript, Java, HTML, XML, CSS, PHP, SQL, JSON, Markdown, PowerShell, Batch, F#, VB.NET, TeX, and more)
- Auto-detects the language from file content when there's no file extension to go on
- Code folding for brace-based and XML/HTML languages
- Find & Replace with regex support, match case, whole word, wrap around
- Go to line
- Line operations — duplicate, move up/down, toggle comment
- Multi-tab editing with drag-and-drop file opening
- Ctrl+scroll zoom per tab
- Word wrap, whitespace/EOL visualization, indent guides, line numbers

**Session persistence**
- Remembers everything on exit, just like Notepad++ does — open tabs, unsaved content, cursor positions, scroll offsets, zoom levels, window size/position, and all editor settings
- No "do you want to save?" on close. Just reopen and pick up where you left off.

**File handling**
- Auto-detect encoding (UTF-8, UTF-8 BOM, UTF-16 LE/BE, ANSI)
- Switch encoding on the fly
- Line ending detection (CRLF, LF, CR)
- Recent files list
- Print support

**Editing tools**
- Upper/lowercase conversion
- Trim trailing whitespace
- Tab ↔ space conversion
- Select all, cut, copy, paste, undo, redo

**UI**
- Dark theme across the entire app — menus, tabs, dialogs, scrollbars, status bar, everything
- Dark title bar on Windows 10/11
- Status bar showing position, selection info, language, encoding, line endings, zoom, and file stats
- Right-click tab context menu (close, close others, close to left/right, copy path, open folder)
- Always on top option

**Keyboard shortcuts**
- `Ctrl+N/O/S/W` — new, open, save, close
- `Ctrl+Shift+S` — save as
- `Ctrl+F/H/G` — find, replace, go to line
- `Ctrl+D` — duplicate line
- `Alt+Up/Down` — move line
- `Ctrl+/` — toggle comment
- `Ctrl+Shift+U / Ctrl+U` — uppercase / lowercase
- `Ctrl+Tab / Ctrl+Shift+Tab` — switch tabs
- `Ctrl+Mousewheel` — zoom
- `Ctrl+0` — reset zoom
- `F3 / Shift+F3` — find next / previous

**Installer**
- Windows installer (Inno Setup)
- Desktop shortcut
- Start menu entry
- Right-click context menu: "Edit with Notepad++ #" on any file or folder

## Build

Requires .NET 10 SDK.

```
dotnet build
dotnet run
```

To publish a self-contained exe:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

To build the installer, install [Inno Setup 6](https://jrsoftware.org/isdl.php) and run:

```
.\build-installer.ps1
```

The installer will be in the `Output/` folder.

## License

[GPL-3.0](LICENSE)
