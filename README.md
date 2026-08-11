# ScreenSpell — التدقيق الإملائي العربي على الشاشة

ScreenSpell watches your Windows desktop, reads the Arabic text on screen, spell checks every
word and draws a red squiggle under the suspicious ones through a transparent, click-through
overlay. Found words are also listed in the main window where they can be ignored or added to
your personal dictionary.

## Requirements

- Windows 10 version 2004 (build 19041) or newer / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (SDK to build, runtime to run)
- The **Arabic** OCR language pack, for the default Windows engine only:
  `Settings > Time & language > Language & region > Add a language > العربية`,
  and make sure *Optical character recognition* is ticked in the optional features.
  Add **English** the same way if you also want English words checked.
  Without it the app starts but the status bar reports that OCR is unavailable.
  The Tesseract and PaddleOCR engines below need no language pack at all.

## How the screen is read

OCR is the slow part of the loop, so it is avoided whenever the application can be asked
directly, and shrunk when it cannot:

1. **قراءة لحظية من التطبيقات** (`ReadTextDirectly`, on by default) — the text of the window in
   front is taken from UI Automation instead of from a picture of it. It is exact (no misread
   letters at all), takes microseconds, and works with browsers, Office, editors and most
   Windows controls. Password fields are skipped. Windows that expose nothing (games, images,
   canvas-drawn pages, remote desktops) return no text and fall through to OCR.
2. **قراءة الجزء المتغيّر فقط** (`IncrementalScan`, on by default) — when OCR is needed, only
   the part of the window that was repainted since the last pass is recognised, and the fresh
   words are merged with the ones already known outside that area. Typing a word therefore
   costs a couple of lines instead of a whole window. The first pass, a window move or resize,
   and a repaint covering most of the frame all fall back to reading everything.

Both are checkboxes in the settings panel and take effect on the next scan, without a restart.

Watching the screen is separated from reading it. Every tick grabs a downscaled probe (384 px
on the long edge) and hashes it; a tick that finds the picture unchanged - which is nearly
every tick while you read a page - never copies a full frame, never walks the automation tree
and never redraws. The window is captured at full resolution only once there is something new
to read, a pass that is still recognising is never joined by the next tick, and a screen that
has been still for a while is probed a few times a second until it moves again.

## Correcting from the overlay

The squiggles are not just a report: right-click (or left-click) one and a small menu opens
over the word with its corrections, *تجاهل الكلمة* and *إضافة إلى القاموس*, so nothing sends
you back to the main window.

Only the underline strip itself catches the mouse - a few pixels tall, under the word. The
window region is rebuilt from the issues on every redraw, so every other click on the screen
reaches the application below untouched, without the overlay forwarding anything.

Picking a correction copies it to the clipboard, ready to paste over the word: the overlay
cannot type into another application's window. *تجاهل* and *إضافة إلى القاموس* apply
immediately, remove the underline, and are remembered in `settings.json`.

## OCR engines

Pick one under **محرك القراءة** in the settings panel; the choice is applied when the app is
restarted, and the status bar names the engine actually in use. Everything is offline, and
the models ship inside the repository (`ScreenSpell/Engines/Models`, ~49 MB in total), so
there is nothing to download.

| Engine | Setting value | Models | Notes |
| --- | --- | --- | --- |
| Windows OCR (default) | `Windows` | none | fastest, needs the language packs, no real per-word confidence and it misreads small text |
| Tesseract 5 | `Tesseract` | `tessdata/ara.traineddata` (12.6 MB), `tessdata/eng.traineddata` (15.4 MB) | no language pack needed, real confidence, the most accurate reading and the slowest |
| PaddleOCR | `Paddle` | `paddle_det.onnx` (4.7 MB), `paddle_rec_arabic.onnx` (8 MB), `paddle_rec_english.onnx` (7.8 MB) and their dictionaries | most accurate on Arabic, a Latin line is read again by the English model and the surer reading wins, heaviest on the CPU |

The two offline engines read the enlarged frame like the Windows one does, and they only run
once the picture has settled, so the cost is paid per screen change and not per refresh. When
the selected engine cannot load its models the app logs a warning and falls back to Windows OCR.

The bundled files are the upstream releases: `tessdata_best` for Tesseract (Apache 2.0) and the
PP-OCRv4 detector plus the PP-OCRv5 Arabic and English mobile recognisers exported to ONNX
(Apache 2.0). The English recogniser is optional: without it the Arabic model reads Latin too.

## Build and run

```powershell
git clone <this-repo>
cd ScreenSpell\ScreenSpell
dotnet restore
dotnet build -c Release
dotnet run --project Main\ScreenSpell.Main.csproj -c Release
```

A self-contained folder you can copy anywhere:

```powershell
dotnet publish Main\ScreenSpell.Main.csproj -c Release -r win-x64 --self-contained false -o publish
.\publish\ScreenSpell.exe
```

### Tests

```powershell
dotnet test Tests\ScreenSpell.Tests.csproj
```

The test project and every non-UI project target plain `net8.0`, so they also build and run
on Linux/macOS. The `Capture`, `OCR`, `Overlay`, `Tray` and `Main` projects are Windows only;
building them from a non-Windows machine requires `-p:EnableWindowsTargeting=true` and they
cannot be executed there.

## Using the app

| Control | Effect |
| --- | --- |
| بدء التدقيق / إيقاف | starts and stops the periodic scan loop |
| فحص الآن | forces a single scan, ignoring the unchanged-screen cache |
| الفاصل الزمني | milliseconds between two probes, used only when the refresh sync is off |
| أقل ارتفاع للنص | words drawn smaller than this many pixels are skipped (0 = no limit) |
| المزامنة مع معدل تحديث الشاشة | probes the screen every refresh and recognises as soon as it settles |
| تحسين الصورة قبل القراءة | grey scale and contrast stretch, fewer misread letters |
| قراءة لحظية من التطبيقات | takes the text from the application through UI Automation, OCR only as a fallback |
| قراءة الجزء المتغيّر فقط | re-reads just the repainted area and keeps the rest of the words |
| إظهار الطبقة فوق الشاشة | toggles the on-screen squiggles |
| النافذة النشطة فقط | scans only the window in front, which is much faster |
| إطارات التثبيت | consecutive scans before a word is underlined (1 disables the smoothing) |
| أقل طول للكلمة | shorter words are never checked |
| عدد الاقتراحات | how many corrections are listed per word |
| تكبير الصورة قبل القراءة | upscale applied before OCR, 1 to 4 (needs a restart) |
| لغة القراءة / لغات إضافية | OCR language tags, comma separated (needs a restart) |
| محرك القراءة | Windows OCR, Tesseract 5 or PaddleOCR (needs a restart) |
| بدء التدقيق عند فتح التطبيق | starts the loop automatically |
| الإخفاء إلى شريط المهام عند الإغلاق | keeps the app running in the tray |
| استعادة الافتراضي | puts every setting above back to its default |
| تجاهل | ignores the word for this session |
| إضافة إلى القاموس | adds the word to your dictionary, permanently |
| نقرة يمين على الخط الأحمر | opens the corrections menu over the word, on the overlay itself |

Every setting in `settings.json` that is worth changing is editable from the *الإعدادات* panel;
the two toolbar checkboxes are saved as soon as you click them, the rest on *حفظ الإعدادات*.

Closing the window keeps the app in the notification area; use *خروج* in the tray menu to
quit (set `MinimizeToTray` to `false` to close on window close instead).

## Configuration

Settings live in `%LOCALAPPDATA%\ScreenSpell\settings.json` and are written whenever you press
*حفظ الإعدادات* or add a word to the dictionary. Logs are written next to it under `Logs\`.

```jsonc
{
  "SyncToRefreshRate": true,     // watch the screen at its refresh rate, OCR once it settles
  "ScanIntervalMs": 800,         // fallback delay, used only when the sync above is off
  "ScanActiveWindowOnly": true,  // scan the foreground window instead of the whole screen
  "MinTextHeight": 9,            // words drawn smaller than this many pixels are ignored
  "EnhanceContrast": true,       // grey scale + contrast stretch before recognition
  "ReadTextDirectly": true,      // ask the application for its text (UI Automation) before any OCR
  "IncrementalScan": true,       // when OCR is needed, recognise only the repainted area
  "OcrEngine": "Windows",        // Windows | Tesseract | Paddle (applied at startup)
  "OcrScale": 2.0,               // frame is enlarged this much before OCR (1 = off, applied at startup)
  "Language": "ar",              // primary OCR language tag
  "AdditionalLanguages": ["en"], // extra OCR languages, if their packs are installed
  "StabilityFrames": 2,          // scans a word must stay wrong before it is underlined (1 = off)
  "MaxSuggestions": 5,
  "MinWordLength": 3,            // shorter tokens are treated as noise
  "ShowOverlay": true,
  "StartScanningOnLaunch": false,
  "MinimizeToTray": true,
  "DictionaryDirectory": "Dictionaries",
  "OnnxModelPath": "Models/ArabicSpellModel.onnx",
  "UserDictionary": [],
  "IgnoredWords": []
}
```

## Dictionaries

The spell checker is word-list based and ships with the LibreOffice/ayaspell Arabic word list
(`SpellCheck/Dictionaries/ar.dic`, ~274k entries) and an English word list
(`SpellCheck/Dictionaries/en.txt`, ~370k entries) plus a small seed list of app-specific
terms. All of them are copied next to the executable at build time, so no extra download is
needed.

Both Arabic and English words are checked. Capitalised Latin words (`Google`, `Faouzia`),
links, mail addresses and paths (`https://example.com`, `name@host`, `C:\Users`), tokens
containing digits and mixed-script OCR artefacts are skipped, since on a desktop they are
names rather than misspellings. English words are only read from the screen
when the English OCR pack is installed; otherwise a warning is logged at startup and only
Arabic is recognised.

To add more word lists (another language, domain vocabulary), drop them into either

- `Dictionaries\` next to `ScreenSpell.exe`, or
- `%LOCALAPPDATA%\ScreenSpell\Dictionaries\`

Both `*.txt` (one word per line, `#` comments) and Hunspell `*.dic` files (`word/FLAGS`) are
accepted. Common clitics (`ال`, `و`, `ب`, `ل`, `ها`, `هم` …) are stripped before lookup,
so a stem list already covers most inflected forms.

## Optional ONNX model

If you have a character-level Arabic misspelling classifier exported to ONNX, put it at the
`OnnxModelPath` above together with a `vocab.txt` (one character per line, the line index is
the token id). The model must take a single `int64[1, sequence]` input and return a
`float[1, 2]` output where index 1 is the "misspelled" logit. When the model is absent or
fails to load, the word list checker is used instead — this is the default and fully
supported path.

## Project layout

| Project | Target | Role |
| --- | --- | --- |
| `Core` | net8.0 | models (`ScreenFrame`, `OcrWord`, `SpellIssue`, `AppSettings`) and service interfaces |
| `Text` | net8.0 | Arabic normalisation (diacritics, tatweel, alif/yaa variants, digits) |
| `SpellCheck` | net8.0 | word list, edit distance, suggestion ranking, ONNX wrapper |
| `Cache` | net8.0 | frame hash cache + per-word result cache |
| `Settings` | net8.0 | JSON settings persistence |
| `Capture` | net8.0-windows | GDI screen capture into a `ScreenFrame` |
| `OCR` | net8.0-windows10.0.19041.0 | `Windows.Media.Ocr` provider |
| `Engines` | net8.0 | Tesseract 5 and PaddleOCR providers with their bundled models |
| `Automation` | net8.0-windows | UI Automation text source, the OCR-free reading path |
| `Overlay` | net8.0-windows | transparent click-through squiggle overlay |
| `Tray` | net8.0-windows | notification area icon and menu |
| `Main` | net8.0-windows10.0.19041.0 | WPF shell, DI host, scan loop |
| `Tests` | net8.0 | xUnit tests for the non-UI layers |
