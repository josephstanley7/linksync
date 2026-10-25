# AGENTS.md — System Context & Mandatory Workflow for AI Coding Agents

> 🛑 **MANDATORY PROTOCOL FOR ALL AI AGENTS (NO EXCEPTIONS):**  
> 
> 1. **PRE-FLIGHT REQUIREMENT (READ BEFORE CODING):**  
>    Before generating, modifying, or executing ANY code in this repository, you **MUST** read the following documents in order:  
>    * [`AGENTS.md`](file:///c:/Users/thenp/Desktop/linksync/AGENTS.md) (Hardware limits, tool paths, inviolable rules).  
>    * [`FEATURES.md`](file:///c:/Users/thenp/Desktop/linksync/FEATURES.md) (Plain-English feature checklist; take ONE feature at a time, verify with user, and tick `[x]`).  
>    * [`PROGRESS.md`](file:///c:/Users/thenp/Desktop/linksync/PROGRESS.md) (Current phase, completed tasks, what is next).  
>    * [`ARCHITECTURE.md`](file:///c:/Users/thenp/Desktop/linksync/ARCHITECTURE.md) (C4 diagrams, binary protocol, packet specs).  
> 
> 2. **POST-FLIGHT REQUIREMENT (UPDATE BEFORE FINISHING):**  
>    Before completing your turn or ending your task, you **MUST** update the documentation:  
>    * Check off finished items (`[x]`) in [`FEATURES.md`](file:///c:/Users/thenp/Desktop/linksync/FEATURES.md) and [`PROGRESS.md`](file:///c:/Users/thenp/Desktop/linksync/PROGRESS.md) and set the active milestone.  
>    * Update [`ARCHITECTURE.md`](file:///c:/Users/thenp/Desktop/linksync/ARCHITECTURE.md) if any OpCode, wire frame, or data structure was added or modified.  
>    * Update [`USER_GUIDE.md`](file:///c:/Users/thenp/Desktop/linksync/USER_GUIDE.md) if user-facing instructions or features changed.  
>    * Log new architectural choices in [`DECISIONS.md`](file:///c:/Users/thenp/Desktop/linksync/DECISIONS.md).

---

## 1. The Hardware Reality & Target Environment

1. **The Laptop (Desktop Receiver Node):**
   * **Model:** HP EliteBook Folio 9470M (Ultrabook from 2012/2013).
   * **Processor:** 3rd Gen Intel Core i5-3427U / i7-3667U (Dual-core, 4 threads, 17W TDP Ivy Bridge).
   * **Graphics:** Intel HD Graphics 4000 (Supports **H.264 / AVC** hardware decoding via QuickSync; **NO** HEVC/H.265, VP9, or AV1 hardware decode).
   * **Operating System:** Windows 10 Pro (Build 19045).
   * **Resource Ceiling:** Idle RAM must stay **under 40 MB**; idle CPU must stay **under 0.5%**.

2. **The Mobile Device (Android Node):**
   * **Model:** Samsung Galaxy A17 (A-Series running Samsung One UI Core).
   * **Key Missing Feature:** Samsung removes the native "Screen Recorder" Quick Settings tile from this phone model.
   * **Operating System:** Android 13+ (Target SDK: 35).
   * **OS Quirks:** Aggressive background process killing by One UI. Requires a Foreground Service with an active notification channel and battery optimization whitelisting.

---

## 2. Inviolable Architectural Rules (DO NOT BREAK)

1. **STRICTLY NO ELECTRON, CHROMIUM, OR HEAVY WEB RUNTIMES ON DESKTOP:**
   * Never introduce Node.js desktop wrappers, Electron, heavy WebView2 polling loops, or Python GUI runtimes.
   * The desktop receiver **MUST** remain a compiled, native **C# .NET 9 Win32 / WPF** application.
2. **ZERO EXTERNAL SERVERS / ZERO CLOUD DEPENDENCIES:**
   * LinkSync is 100% local peer-to-peer. Never add Firebase, AWS, Supabase, or external relay servers.
   * All communication occurs over local Wi-Fi TCP sockets or a local USB ADB tunnel (`localhost:54884`).
3. **HARDWARE-ACCELERATED H.264 ONLY:**
   * For screen recording and real-time mirroring, always use **H.264 (AVC)**. Never use H.265, VP9, or AV1, as the Intel HD 4000 lacks hardware decoders for them and will pin the dual-core CPU at 100%.
4. **MAINTAIN TWO-WAY CLIPBOARD ISOLATION:**
   * Avoid infinite clipboard feedback loops by tracking `_lastObservedClipboard` on both ends.

---

## 3. Verified Toolchain Paths & Environment Variables

When running builds or commands, always use these exact verified paths:

| Tool | Path / Location | Environment Setting |
| :--- | :--- | :--- |
| **.NET 9 SDK** | `C:\Users\thenp\AppData\Local\Microsoft\dotnet\dotnet.exe` | In User `PATH` |
| **Android SDK** | `C:\Users\thenp\AppData\Local\Android\Sdk` | `sdk.dir` in `mobile/local.properties` |
| **ADB CLI** | `C:\Users\thenp\AppData\Local\Android\Sdk\platform-tools\adb.exe` | In User `PATH` |
| **JDK 17 LTS** | `C:\Users\thenp\AppData\Local\Programs\jdk-17\jdk-17.0.20.1+1` | `org.gradle.java.home` in `mobile/gradle.properties` |
| **FFmpeg** | `C:\Users\thenp\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg...\bin\ffmpeg.exe` | In User `PATH` |

> **IMPORTANT ON JAVA:** The laptop has Java 25 installed globally, but Android Gradle Plugin (AGP) **requires JDK 17 or 21**. Never change `mobile/gradle.properties` away from the JDK 17 path!

---

## 4. How to Build & Run Without Guessing

### Desktop Application:
```powershell
# Build Desktop Solution
dotnet build "c:\Users\thenp\Desktop\linksync\desktop\LinkSync.sln"

# Run Desktop Application
dotnet run --project "c:\Users\thenp\Desktop\linksync\desktop\src\LinkSync.Desktop\LinkSync.Desktop.csproj"
```

### Mobile Application:
```powershell
# Navigate to mobile directory
Set-Location "c:\Users\thenp\Desktop\linksync\mobile"

# Build debug APK using local Gradle wrapper with JDK 17
.\gradlew.bat assembleDebug

# Install APK to connected phone via ADB
adb install -r app\build\outputs\apk\debug\app-debug.apk
```

---

## 5. Repository Layout Map

```
linksync/
├── AGENTS.md               <- YOU ARE HERE (Mandatory agent rules & paths)
├── PROGRESS.md             <- Real-time state: what is DONE, IN-PROGRESS, NEXT
├── README.md               <- Public overview & problem statement
├── USER_GUIDE.md           <- Plain-English guide for non-programmers
├── ARCHITECTURE.md         <- Deep technical spec, C4 diagrams, wire protocol
├── DECISIONS.md            <- Architecture Decision Records (ADRs)
├── desktop/                <- C# .NET 9 Native Windows Receiver (~156 KB binary)
│   ├── src/
│   │   ├── LinkSync.Core/      <- Protocol frames, OpCodes, binary serializers
│   │   └── LinkSync.Desktop/   <- WPF Tray UI, SyncServer, Win32 Clipboard Watcher
│   └── LinkSync.sln
└── mobile/                 <- Kotlin Android Native App
    ├── app/
    │   └── src/main/
    │       ├── AndroidManifest.xml
    │       ├── java/com/linksync/app/
    │       │   ├── MainActivity.kt
    │       │   ├── net/LinkSyncProtocol.kt
    │       │   ├── observer/ScreenshotObserver.kt
    │       │   ├── service/ForegroundSyncService.kt
    │       │   └── tile/LinkSyncTileService.kt
    │       └── res/
    ├── gradle.properties   <- Configured to JDK 17 LTS
    └── gradlew.bat         <- Gradle 8.11.1 Wrapper
```

---

## 6. Cursor Cloud specific instructions

Cloud agents run on Linux. Build and exercise the portable pieces here. The WPF receiver (`desktop/src/LinkSync.Desktop`, `net9.0-windows`) stays a Windows build for the EliteBook.

Installed toolchain (login shells pick this up from `/etc/profile.d/linksync-cloud.sh`):

| Tool | Linux location |
| :--- | :--- |
| JDK 17 | `/usr/lib/jvm/java-17-openjdk-amd64` (also `/usr/local/bin/java`) |
| .NET 9 SDK | `/usr/local/share/dotnet` (`dotnet` on `/usr/local/bin/dotnet`) |
| Android SDK | `/opt/android-sdk` (platform 35, build-tools 35.0.0, platform-tools) |
| ADB | `/usr/local/bin/adb` |

`mobile/gradle.properties` keeps `org.gradle.java.home` on the Windows JDK 17 path used by the EliteBook. Linux Gradle reads `~/.gradle/gradle.properties`, which sets `org.gradle.java.home` to the JDK above. There is no POSIX `gradlew` in the repo (only `gradlew.bat`). From `mobile/`, build the debug APK with the wrapper jar:

```bash
java -classpath gradle/wrapper/gradle-wrapper.jar org.gradle.wrapper.GradleWrapperMain \
  -Dorg.gradle.java.home="${JAVA_HOME:-/usr/lib/jvm/java-17-openjdk-amd64}" \
  :app:assembleDebug --no-daemon
```

`mobile/local.properties` is gitignored. It should contain `sdk.dir=/opt/android-sdk`. The debug APK is `mobile/app/build/outputs/apk/debug/app-debug.apk`.

Build the portable protocol library with:

```bash
dotnet build desktop/src/LinkSync.Core/LinkSync.Core.csproj
```

Nothing needs to be running on boot. A hello-world is a TCP loopback on `127.0.0.1:54884`: `LinkSyncProtocol.kt` (phone framing) exchanges `OP_HANDSHAKE` (`0x0001`), `OP_CLIPBOARD_TEXT` (`0x0010`), and `OP_FILE_SYNC_ACK` (`0x0022`) with `LinkSync.Core`.
