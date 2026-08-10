# ScreenSpell — التدقيق الإملائي العربي على الشاشة

ScreenSpell watches your Windows desktop, reads the Arabic text on screen with the built-in
Windows OCR engine, spell checks every word and draws a red squiggle under the suspicious
ones through a transparent, click-through overlay. Found words are also listed in the main
window where they can be ignored or added to your personal dictionary.

## Requirements

- Windows 10 version 2004 (build 19041) or newer / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (SDK to build, runtime to run)
- The **Arabic** OCR language pack:
  `Settings > Time & language > Language & region > Add a language > العربية`,
  and make sure *Optical character recognition* is ticked in the optional features.
  Add **English** the same way if you also want English words checked.
  Without it the app starts but the status bar reports that OCR is unavailable.

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
| إظهار الطبقة فوق الشاشة | toggles the on-screen squiggles |
| النافذة النشطة فقط | scans only the window in front, which is much faster |
| إطارات التثبيت | consecutive scans before a word is underlined (1 disables the smoothing) |
| أقل طول للكلمة | shorter words are never checked |
| عدد الاقتراحات | how many corrections are listed per word |
| تكبير الصورة قبل القراءة | upscale applied before OCR, 1 to 4 (needs a restart) |
| لغة القراءة / لغات إضافية | OCR language tags, comma separated (needs a restart) |
| بدء التدقيق عند فتح التطبيق | starts the loop automatically |
| الإخفاء إلى شريط المهام عند الإغلاق | keeps the app running in the tray |
| استعادة الافتراضي | puts every setting above back to its default |
| تجاهل | ignores the word for this session |
| إضافة إلى القاموس | adds the word to your dictionary, permanently |

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
| `Overlay` | net8.0-windows | transparent click-through squiggle overlay |
| `Tray` | net8.0-windows | notification area icon and menu |
| `Main` | net8.0-windows10.0.19041.0 | WPF shell, DI host, scan loop |
| `Tests` | net8.0 | xUnit tests for the non-UI layers |
