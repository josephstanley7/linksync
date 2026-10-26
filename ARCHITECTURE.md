# ARCHITECTURE.md — LinkSync Technical Architecture & Protocol

> **System Name:** LinkSync  
> **Topology:** Peer-to-Peer Local Bridge (Dual Node: Windows Receiver + Android Node)  
> **Security Model:** Zero-Trust Local LAN, Ephemeral Key Exchange, Mutual TLS (mTLS)

---

## 1. High-Level Architecture (C4 Model)

```mermaid
flowchart TB
    subgraph Mobile ["Samsung Galaxy A17 (Native Kotlin)"]
        FS[Foreground Sync Service\nFOREGROUND_SERVICE_DATA_SYNC]
        CO[MediaStore ContentObserver\nScreenshots & Recordings]
        QS[Quick Settings Tile\n'LinkSync Record']
        WM[WindowManager Controller\nFloating Capsule & Live Drawing Canvas]
        MP[MediaProjection & MediaRecorder Engine\nHardware H.264 + AAC Stereo Audio\nFOREGROUND_SERVICE_MEDIA_PROJECTION | MICROPHONE]
        VP[VideoPreviewActivity\nLossless MediaMuxer Trimmer & Save/Sync Gate]
        TEL[Telephony & SMS Hub\n2FA OTP Extractor & SMS Dispatcher]
        CLIP_M[Android Clipboard Manager]
        QDB[(Encrypted Room DB Queue)]
        WS_C[mTLS Client / Socket Engine]

        QS --> MP
        MP --> WM
        MP --> VP
        VP -->|Approved Save & Sync| FS
        CO -->|Auto-Detect Media| FS
        TEL -->|Incoming SMS / Calls| FS
        CLIP_M <-->|Two-Way Text Sync| FS
        FS <--> QDB
        FS <--> WS_C
    end

    subgraph Desktop ["HP Folio 9470M (Native C# .NET 9 Win32 / Skia)"]
        TRAY[System Tray Resident\nNotifyIcon + Global Hotkey Hooks]
        SOCK_S[mTLS Server / Socket Engine\nPort 54884]
        
        subgraph Native Windows
            DASH[Main Dashboard\nSMS, Calls, Contacts & Battery Status]
            ANNOT[ShareX-Grade Image Annotator\nDirect2D/WPF Hardware Canvas]
            VIDEO_SUITE[Video Suite & Lossless Slicer\nFFmpeg Keyframe & 2-Pass GIF]
            MIRROR[Hardware-Accelerated Mirror\nDXVA2 Intel QuickSync Player]
        end

        subgraph Background Subsystems
            CLIP_D[Win32 Clipboard Manager]
            FS_SYNC[File Writer & Storage Manager]
            DND[Drag-and-Drop File / APK Handler]
            TOAST[Windows Action Center Toast Provider\n1-Click OTP Copy Action]
            INPUT[Keyboard & Mouse Event Serializer]
        end

        SOCK_S <--> TRAY
        SOCK_S --> FS_SYNC
        SOCK_S <--> CLIP_D
        SOCK_S --> TOAST
        SOCK_S --> MIRROR
        SOCK_S <--> DASH
        DND --> SOCK_S
        MIRROR --> INPUT --> SOCK_S
    end

    subgraph Transport ["Dual Transport Layer"]
        WIFI[Encrypted Local Wi-Fi\nUDP Auto-Discovery: 54885\nTCP Bridge: 54884]
        USB[USB Cable Fallback\nADB Reverse Tunnel: tcp:54884]
    end

    WS_C <===> WIFI <===> SOCK_S
    WS_C <===> USB <===> SOCK_S
```

---

## 2. Dynamic Discovery & Multi-PC Protocol (UDP Port 54885)

To eliminate hardcoded IP addresses and guarantee zero-configuration connectivity across home Wi-Fi, public Wi-Fi, and **Android Mobile Hotspot tethering**, LinkSync implements a resilient multi-tier UDP discovery engine:

1. **Persistent Device Identity (`DeviceId`):**
   * Desktop receiver generates a unique, persistent hardware fingerprint (e.g., `PC-F804F480`) stored in `%LocalAppData%\LinkSync\device_id.txt`.
   * The beacon packet format is: `LINKSYNC_DISCOVERY|<DeviceName>|<TcpPort>|<DeviceId>`.
2. **Mobile Hotspot SoftAP Traversal:**
   * Because Android Linux tethering firewall blocks `255.255.255.255` broadcasts between hotspot clients and the access point, desktop queries its active default gateway (e.g. `10.253.25.103`) and transmits directed unicast discovery datagrams directly to the gateway on port `54885` alongside subnet directed broadcasts.
3. **Passive Multicast-Locked Android Listener:**
   * Android holds `WifiManager.MulticastLock` with `CHANGE_WIFI_MULTICAST_STATE` to prevent chipset power-save filters from dropping discovery datagrams, continuously listening on `0.0.0.0:54885` with `SO_REUSEADDR`.
4. **Multi-PC Disambiguation & Pairing Management:**
   * When multiple PCs run LinkSync on the same network, Android exposes a reactive `discoveredDesktops` StateFlow.
   * **Auto-Pair:** If only 1 PC is discovered, Android pairs and connects immediately.
   * **Remembered Pair:** If a previously paired `DeviceId` is present in the discovered list, Android prioritizes it automatically.
   * **Switching:** Users can tap the "Available Computers" button anytime to pick from all detected PCs or forget the pairing.
5. **Directed Pong:** Desktop also responds to `LINKSYNC_PING` with `LINKSYNC_PONG|<DeviceName>|<TcpPort>|<DeviceId>`.

---

## 3. Binary Wire Protocol & Packet Framing

All frames exchanged between phone and desktop over TCP use an 8-byte big-endian header followed by the payload:

```
+-------------------------------------------------------------------------+
|                        LINKSYNC BINARY FRAME FORMAT                     |
+-------------------+-------------------+---------------------------------+
| Magic (2 bytes)   | OpCode (2 bytes)  | Payload Length (4 bytes, Big-E) |
| 0x4C 0x53 ('LS')  | uint16            | uint32                          |
+-------------------+-------------------+---------------------------------+
| Payload: Encrypted JSON or Raw Byte Stream (Image / H.264 NAL / File)    |
+-------------------------------------------------------------------------+
```

### Protocol OpCodes Reference

| OpCode | Hex | Name | Payload Description |
| :--- | :--- | :--- | :--- |
| `1` | `0x0001` | `OP_HANDSHAKE` | JSON with `deviceName`, `manufacturer`, `androidVersion`. |
| `16` | `0x0010` | `OP_CLIPBOARD_TEXT` | UTF-8 encoded text string for universal clipboard sync. |
| `32` | `0x0020` | `OP_FILE_SYNC` | `[4-byte metaLen][meta JSON][raw image/video bytes]`. |
| `33` | `0x0021` | `OP_FILE_REVERSE_PUSH` | Pushed file from laptop to phone `Downloads/` or `.apk` install. |
| `34` | `0x0022` | `OP_FILE_SYNC_ACK` | JSON with `{"fileName": name, "status": "saved"}` handshake confirmation. |
| `48` | `0x0030` | `OP_MIRROR_START` | Signals phone to start `MediaProjection` virtual display. |
| `49` | `0x0031` | `OP_MIRROR_STOP` | Signals phone to stop virtual display. |
| `50` | `0x0032` | `OP_VIDEO_STREAM_DATA` | Raw H.264 Annex-B NAL units for hardware GPU decode. |
| `51` | `0x0033` | `OP_INPUT_EVENT` | Remote mouse click / touch coordinate or physical keyboard event. |
| `64` | `0x0040` | `OP_SMS_INCOMING` | JSON with sender, body, and parsed 2FA verification code. |
| `65` | `0x0041` | `OP_SMS_SEND` | JSON with recipient and text body to send via cellular radio. |
| `66` | `0x0042` | `OP_CALL_ACTION` | JSON with dial number, answer, or hangup command. |
| `67` | `0x0043` | `OP_CONTACTS_REQ` | Laptop request to sync phone contacts directory. |
| `68` | `0x0044` | `OP_CONTACTS_RESP` | JSON array of `ContactItem` with display name and phone numbers. |
| `69` | `0x0045` | `OP_SMS_LIST_REQ` | Laptop request to sync recent SMS message threads. |
| `70` | `0x0046` | `OP_SMS_LIST_RESP` | JSON array of `SmsMessageItem` with address, body, date, and type. |
| `71` | `0x0047` | `OP_CALL_STATE` | JSON with phone state (`IDLE`, `RINGING`, `OFFHOOK`) and active number. |
| `80` | `0x0050` | `OP_DEVICE_STATUS` | JSON with battery percentage, charging state, and Wi-Fi dBm. |
| `81` | `0x0051` | `OP_LOCK_PC` | Empty payload. Triggers remote Windows workstation lock (`LockWorkStation`). |
| `96` | `0x0060` | `OP_NOTIFICATION` | JSON with `key`, `packageName`, `appName`, `title`, `text`, and `timestamp`. |
| `97` | `0x0061` | `OP_NOTIFICATION_REQ` | Empty payload. Requests immediate dump of all active notifications on phone. |

---

## 3. Reverse-Engineered Engineering Paradigms

### A. ShareX (Desktop Annotator & Media Engine)
* **Zero Electron:** Written in pure C# .NET 9 using Windows APIs (`AddClipboardFormatListener`, `Shell_NotifyIcon`).
* **Hardware Rasterization:** Image annotation vector layers (arrows with trigonometric heads, auto-incrementing step badges, blur/pixelate redaction, spotlight dimmer, and Samsung Galaxy chassis bezel mockup) drawn via DirectX/Direct2D hardware acceleration.
* **Desktop Screen Recording Engine (`DesktopScreenRecorder.cs`):** FFmpeg `gdigrab` capture pipeline (`-f gdigrab -rtbufsize 150M -c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p`) with even dimension alignment. Renders a click-through animated dashed boundary overlay (`RecordingBorderWindow.xaml`, `WS_EX_TRANSPARENT`) and a floating recording toolbar (`ScreenRecordingBarWindow.xaml`) with live duration `00:00:00`, resolution tag, `⏹ Stop`, `⏸ Pause/Resume` (via NT kernel process suspension `NtSuspendProcess`), and `✕ Abort`.
* **Automated Post-Recording Pipeline:** Writes `'q'` to FFmpeg standard input to cleanly flush the MP4 `moov` container atom, automatically copies the resulting MP4 to the Windows Clipboard (`FileDropList`), streams the video to the connected Galaxy A17 via `SyncServer`, and immediately opens `VideoSuiteWindow` for trimming or GIF export.
* **Lossless Video Trimming & Frame Extraction:** Sub-second keyframe stream copy (`ffmpeg -c copy`) executing in < 300ms with 0% CPU re-encode on Ivy Bridge, with micro-stepping (`◀ -0.1s`, `+0.1s ▶`) and still frame extraction (`ffmpeg -vframes 1`) to clipboard and Image Annotator.
* **Two-Pass Animated GIF:** Single-pipeline `split[s0][s1]` with Lanczos downscaling and Bayer dithering palette generation for high-clarity GitHub and Slack bug repros.
* **Footprint:** Consumes **~38 MB RAM** and **0.0% idle CPU**.

### B. XRecorder (Mobile Engine)
* **Overlay Layering:** Uses Android `WindowManager` with `TYPE_APPLICATION_OVERLAY`.
* **Touch Filtering:** Dynamically toggles `FLAG_NOT_TOUCHABLE` so users can interact with apps under test normally, and switches to touch-intercept when tapping the floating "Brush" tool.
* **In-Video Baking:** Because Android's `MediaProjection` records the hardware display compositor (`SurfaceFlinger`), all drawn shapes are automatically recorded into the video stream in real-time with **0 ms post-processing**.
* **Clean 1-Tap Start:** `ScreenCapturePromptActivity` runs in an isolated task (`com.linksync.app.capture`, `launchMode="singleTop"`) with a transparent no-animation theme so the Quick Settings tile never foregrounds `MainActivity`. State guards ensure single-shot intent dispatch without Android 14+ permission loops. `LinkSyncTileService` debounces rapid taps (1000ms) and stays inactive until `ScreenRecorderService` publishes `ACTION_RECORDING_STATE`.

### C. Multi-Path Universal Clipboard Engine & DeX-Style Timeline
* **Android 10+ Background Sandbox Bypass:** Android 10+ (`API 29–35`) denies background clipboard reads to non-IME apps. LinkSync bypasses this via 4 defense-in-depth channels:
  1. **Automated Logcat Interceptor (`ClipboardLogcatWatcher.kt`):** Subscribed to `ClipboardService:E` with `android.permission.READ_LOGS`. Upon detecting access denial, immediately spawns a transparent, zero-animation trampoline (`ClipSyncActivity.kt`) for 20ms to gain momentary window focus, read `primaryClip`, and forward over socket.
  2. **Native Text Selection Action (`SendTextActivity.kt`):** Registers `android.intent.action.PROCESS_TEXT` ("⚡ Send to PC") in Android's native text context menu.
  3. **Notification Quick Action:** `[📋 Send Copied Text to PC]` action button embedded directly in the persistent foreground notification.
  4. **Foreground Lifecycle Capture:** On `MainActivity.onResume()`, reads and pushes pending clipboard data.
* **Decoupled Two-Way Echo Suppression Algorithm:**
  * To prevent infinite ping-pong loops without dropping subsequent user copies of identical text, both nodes maintain separate state pairs:
    * `lastReceivedFromPeerText` + timestamp (`SystemClock.elapsedRealtime()` / `DateTime.UtcNow`).
    * `lastSentToPeerText` + timestamp.
  * When device B receives text from device A, it sets its clipboard and marks `lastReceivedFromPeerText = text`.
  * When device B's local clipboard hook fires, if `text == lastReceivedFromPeerText` within a 3500ms window, it is recognized as a peer-initiated sync and suppressed from echoing back.
  * Any user copy outside that window—or any new text—is immediately forwarded over the socket.
* **Samsung DeX Timeline & Desktop UI:**
  * Displays a live stream of synced clipboard entries with formatted arrival timestamps (`HH:mm:ss`), source badges (`📱 FROM PHONE (GALAXY A17)` in cyan / `💻 FROM PC (FOLIO 9470M)` in emerald), character count metadata, and a one-click `📋 Re-copy` action.
* **Windows Desktop Tray Residency & Memory Trimming:**
  * **Zero Shutdown on Close:** Windows `OnClosing` event cancels destruction (`e.Cancel = true; Hide()`) and runs resident in the notification area via `SystemTrayManager` (`Shell_NotifyIconW`), preserving server sockets and Win32 clipboard hooks.
  * **Aggressive Working Set Trimming:** Upon minimizing to tray, invokes `kernel32.dll!SetProcessWorkingSetSize(hProcess, -1, -1)` with garbage collection, reducing idle background memory from ~38 MB down to **3.0 MB RAM** on Ivy Bridge with 0.0% CPU.
  * **Autostart Integration:** Configurable `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry with `--minimized` flag for silent background boot.
  * **Single-Instance Wakeup IPC (`Local\LinkSync_App_Wakeup_Signal_9470M`):** 
    * Primary instance creates a named auto-reset event and registers a non-blocking wait on the CLR thread pool (`ThreadPool.RegisterWaitForSingleObject`).
    * When secondary instance launches (from Desktop shortcut or Start Menu), it sets the named event in < 1ms and exits.
    * Primary instance wakes up, invokes WPF Dispatcher, and restores the window to foreground (`Show()`, `WindowState = Normal`, `Activate()`, `Topmost = true`, `Topmost = false`, `Focus()`).
    * Eliminates silent exit bugs when launching from shortcuts while running hidden in tray.
  * **Inno Setup Clean Overwrite & Auto-Termination:** `installer.iss` specifies `CloseApplications=yes` and Pascal `InitializeSetup` / `InitializeUninstall` hooks that execute `taskkill /F /IM LinkSync.Desktop.exe` to prevent locked DLLs or orphaned background processes during re-installs.
  * **Robust OleSetClipboard Retries:** `Clipboard.SetDataObject()` wrapped in a 10-attempt loop with backoff to bypass transient Win32 clipboard locks.

### D. ShareX Interactive Desktop Snipping & Bidirectional Media Protocol
* **Pre-Capture Desktop Freeze:** `ScreenCaptureService.CaptureRegion()` queries `SystemParameters.VirtualScreenLeft/Top/Width/Height` across all connected displays and snapshots the virtual desktop bitmap into memory *before* instantiating `ScreenSnippingWindow`. This prevents the snipping tool or cursor artifacts from being captured.
* **Interactive Cutout & Shader Dimmer:** The full desktop is displayed in an unbordered, topmost window with a 60% opacity dark overlay (`#99000000`). When the user drags a selection rectangle, a `CombinedGeometry` (`GeometryCombineMode.Exclude`) cuts an un-dimmed hole through the dark overlay, rendering the selected region at 100% brightness.
* **ShareX-Style Guides & Floating Action Bar:**
  * Real-time crosshair lines track cursor coordinates across screen bounds.
  * A coordinate and dimension pill (`{W} × {H} px`) displays dynamically above or below the selection.
  * Upon mouse release, a floating frosted action bar appears with instant options: `✓ Copy & Sync`, `✏️ Annotate`, `💾 Save`, and `✕ Cancel`. Double-clicking the selection rectangle triggers immediate copy and sync.
* **Active Window Snip via DWM Frame Bounds:** `CaptureActiveWindow()` calls Win32 `GetForegroundWindow()` followed by `DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS)`. This eliminates the transparent window drop shadows and padding introduced by standard `GetWindowRect()`.
* **Bidirectional `OP_FILE_SYNC` Pipeline:**
  * Desktop $\rightarrow$ Phone: Snipped image bytes are transmitted over the TCP socket as an `OP_FILE_SYNC` frame with metadata `{fileName, mimeType: "image/png"}`.
  * On the Android side, `ForegroundSyncService` receives the frame, saves the PNG into `Environment.DIRECTORY_PICTURES/Screenshots`, triggers Android's `MediaScannerConnection` for gallery discovery, and sets the image URI on Android's `ClipboardManager`.
  * Phone $\rightarrow$ Desktop: Monotonic MediaStore ID tracking and inotify `FileObserver` trigger `OP_FILE_SYNC` transmission within 500ms of hardware screenshot capture.

### E. Microsoft Phone Link & Samsung One UI Navigation Architecture
* **Desktop UI Overhaul (Microsoft Phone Link Model):**
  * **240px Left Navigation Rail:** Hosts the connected device header (`Samsung Galaxy A17`, live status badge, and battery/network pills), navigation buttons (`Overview`, `Photos & Screenshots`, `Clipboard Stream`, `Video Suite`, `Annotator`), and a dedicated bottom action button: `📸 Snip Screen (ShareX)`.
  * **Dynamic Content Views:** Right-hand pane switches cleanly between views without tab bar clutter.
* **Mobile UI Overhaul (Samsung One UI Model):**
  * **Default Centered View:** Upon launch, only the primary connection status card is visible in the middle of the screen (`layoutConnect`).
  * **Native Bottom Navigation:** A pinned `BottomNavigationView` provides 3 dedicated views:
    1. `Connect` (centered connection status and reconnect controls).
    2. `Clipboard` (DeX-style chronological history cards with cyan phone and emerald laptop badges, timestamps, and `📋 Copy` buttons).
    3. `Screenshots` (recent captures with `▶ Open` and `⚡ Send to PC` options).

### F. Two-Way Media Handshake & Safe Auto-Delete Architecture (Feature 13)
* **Wire Confirmation Handshake (`OP_FILE_SYNC_ACK` `0x0022`):**
  * When Desktop writes an incoming screenshot to `Pictures\LinkSync\Screenshots`, `SyncServer` immediately writes an `OpFileSyncAck` frame containing `{"fileName": fileName, "status": "saved", "timestamp": ...}` back onto the client stream.
  * Mobile `ForegroundSyncService` receives `OP_FILE_SYNC_ACK`. If user preference `auto_delete_synced_screenshots` is enabled, the service executes safe local deletion.
* **Storage Deletion & MediaStore Clean-up:**
  * Checks tracked URI in `sentFileUris[fileName]`.
  * Deletes file across screenshot folders (`Pictures/Screenshots`, `DCIM/Screenshots`, `Movies/LinkSync`).
  * Issues `contentResolver.delete(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, ...)` to remove indexed gallery entries.
  * Emits `fileSyncEvent` flow to update `MainActivity` gallery asynchronously without user reload.
* **Sub-Millisecond Downsampled Thumbnail Engine:**
  * To guarantee 60 FPS scrolling and prevent Out-Of-Memory (OOM) on Samsung Galaxy A17 and HP Folio 9470M, thumbnails are decoded asynchronously via `BitmapFactory.Options.inSampleSize` (scaled to 260x220px for grid, 140x140px for list, and `DecodePixelWidth=140` for WPF).
  * In-memory 12MB LRU cache prevents repeated disk IO and garbage collection pressure.
* **Layout Switching (2-on-a-Row Grid vs 1-Column List):**
  * Persisted `screenshot_layout_mode` preference ("grid" vs "list").
  * 2-on-a-row grid arranges cards into paired horizontal weights (`weightSum = 2f`) with preview images, top-right file size badges (`1.4 MB`), video play icons (`▶`), ellipsized titles, and high-tactile pill action buttons (`▶ Open` and `⚡ Sync`).

### G. Communication Hub Architecture (Phone Link Model & 1-Click OTP)
* **Contacts Synchronization (`ContactsSyncManager.kt` & `OP_CONTACTS_RESP`):**
  * Queries `ContactsContract.CommonDataKinds.Phone` using projection `[CONTACT_ID, DISPLAY_NAME, NUMBER, PHOTO_THUMBNAIL_URI]`.
  * Deduplicates multiple phone numbers under single contacts and normalizes non-numeric dialing tokens.
  * Dispatched over wire as JSON array; desktop maintains in-memory `List<ContactViewModel>` with real-time substring filtering (`ContactSearch_TextChanged`).
* **SMS Messages Threading Engine (`SmsManagerService.kt` & `OP_SMS_LIST_RESP`):**
  * Queries `Telephony.Sms.CONTENT_URI` retrieving conversation streams with `THREAD_ID`, `ADDRESS`, `BODY`, `DATE`, `TYPE` (`MESSAGE_TYPE_INBOX` vs `MESSAGE_TYPE_SENT`).
  * Desktop groups messages by thread ID and contact address in `MainWindow.xaml.cs`.
  * Left pane (320px) displays conversation previews with contact display names, timestamps, and snippet previews. Right pane renders full chronological bubble history with responsive alignment (inbox messages left-aligned `#1A1D27`, sent messages right-aligned `#0284C7`).
  * Outgoing SMS dispatch via `OP_SMS_SEND` calls Android `SmsManager.sendTextMessage()` with multipart support.
* **Dialer & Call Control Subsystem (`CallControlManager.kt` & `OP_CALL_ACTION`):**
  * Desktop digital dialer features 12 tactile keys (`0-9`, `*`, `#`), editable number box, quick backspace, and 1-click dial.
  * Phone executes remote call via `Intent.ACTION_CALL` (or fallback to `ACTION_DIAL`).
  * Real-time telephony callback via Android 12+ `TelephonyCallback.CallStateListener` and legacy `PhoneStateListener` pushes `OP_CALL_STATE` frames (`IDLE`, `RINGING`, `OFFHOOK`).
  * Active call card on desktop tracks call duration with an embedded `DispatcherTimer` and provides a remote `[ End Call ]` button invoking Android `TelecomManager.endCall()`.
* **1-Click 2FA OTP Toast Pipeline (`SmsBroadcastReceiver.kt` & `OtpToastWindow.xaml`):**
  * `SmsBroadcastReceiver` intercepts raw SMS PDUs with highest priority (`priority = 999`).
  * `OtpExtractor.kt` matches incoming verification tokens using high-precision regex (`\d{4,8}` and `G-\d{6}`) while explicitly filtering out false positives such as calendar years (`2023-2028`).
  * Phone dispatches `OP_SMS_INCOMING` containing `{sender, body, otpCode}`.
  * Desktop `SyncServer` catches event, instantly writes `otpCode` to the Windows Clipboard (`System.Windows.Clipboard.SetText`), and instantiates `OtpToastWindow`:
    - Screen-bottom-right floating fluent card with 120ms slide-up animation.
    - System sound chime (`SystemSounds.Asterisk.Play()`).
    - Prominent tactile button: **`[ 📋 Copy Code: XXXXXX ]`**.
    - Auto-dismiss countdown timer (8 seconds) with progress bar and manual `✕` close.

---

## 4. Security & Cryptographic Boundaries

```
+----------------------------------------------------------------------------------------------------+
|                                      THREAT MODEL & MITIGATIONS                                    |
+--------------------------+-------------------------------------+-----------------------------------+
| Threat Vector            | Risk Scenario                       | LinkSync Architectural Defense    |
+--------------------------+-------------------------------------+-----------------------------------+
| Untrusted Local Wi-Fi    | Packet sniffing in a shared office  | Mandatory TLS 1.3 / ChaCha20-     |
| (Eavesdropping)          | or coffee shop captures SMS/photos. | Poly1305 end-to-end encryption.   |
+--------------------------+-------------------------------------+-----------------------------------+
| Rogue Device Injection   | Unauthorized machine on LAN sends   | Cryptographic Pairing: 6-digit    |
| (Spoofing)               | commands to make phone place calls. | PIN or QR Code (mTLS certificate) |
+--------------------------+-------------------------------------+-----------------------------------+
| Replay Attacks           | Attacker intercepts and resends     | Per-packet incrementing nonce     |
|                          | "SEND_SMS" or "CALL" packets.       | and Unix timestamp validation.    |
+--------------------------+-------------------------------------+-----------------------------------+
| Android OS Background    | Samsung One UI kills background     | Foreground Service with persistent|
| Eviction                 | sync service to save battery.       | notification & system lock.       |
+--------------------------+-------------------------------------+-----------------------------------+
| Local Storage Theft      | Malware on PC reads synced contacts | OS-level credential vault         |
|                          | and unencrypted private keys.       | (DPAPI on Windows, Keystore on OS)|
+--------------------------+-------------------------------------+-----------------------------------+
```

---

## 5. Screen Recording Engine, Audio Control & Ghost Overlay Isolation

```mermaid
flowchart TD
    subgraph Trigger ["Recording Initiation"]
        QS[Quick Settings Tile\n'LinkSync Record']
        ACT[MainActivity\n'Start Screen Recording']
        PROMPT[ScreenCapturePromptActivity\nAudio Modal Gate & Pure ScreenCaptureIntent]
        QS --> PROMPT
        ACT --> PROMPT
    end

    subgraph ScopeSelection ["Android 14+ Scope Prompt"]
        PROMPT -->|projectionManager.createScreenCaptureIntent| SYS_UI[System Sharing Dialog]
        SYS_UI -->|Choice A: A Single App| SINGLE_APP[Target Application Surface Only\nAll Overlays & Bars Excluded]
        SYS_UI -->|Choice B: Entire Screen| FULL_SCREEN[Full Display VirtualDisplay]
    end

    subgraph ServicePipeline ["ScreenRecorderService"]
        SINGLE_APP --> REC_ENG[MediaRecorder + Hardware MediaCodec H.264]
        FULL_SCREEN --> REC_ENG
        
        AUDIO_CHOICE{Audio Preference?}
        AUDIO_CHOICE -->|With Audio| MIC_AAC[MIC AudioSource + AAC 44.1kHz Encoder\nFOREGROUND_SERVICE_MICROPHONE]
        AUDIO_CHOICE -->|Muted / Silent| MUTE[No AudioSource / No AAC\nSilent Video Stream]
        MIC_AAC --> REC_ENG
        MUTE --> REC_ENG

        GHOST{Ghost Controller?}
        GHOST -->|Auto-Hide Enabled| HIDE[Floating Bubble Set to GONE\n0px Visible in Captured Video]
        GHOST -->|Always Visible| BUBBLE[Floating Pill on Edge with 👁️ Hide Toggle]

        SHAKE[Accelerometer Sensor Listener\nDebounced 100ms Delta] -->|Shake Phone Detected| STOP[Haptic Vibration + Stop Recording]
        NOTIF[Persistent Notification Shade\n⏹ Stop | ⏸ Pause | 🎨 Brush] --> STOP
    end

    subgraph Finalization ["Post-Recording Pipeline"]
        STOP --> PREVIEW[VideoPreviewActivity\nLossless MediaMuxer Trimmer]
        PREVIEW -->|Save & Sync| SYNC[ForegroundSyncService\nMediaStore.Video.Media Registration + TCP Stream]
    end
```

---

## 6. Real-Time Phone Mirroring & Remote Input Injection (Group 8)

```mermaid
flowchart LR
    subgraph Host ["HP EliteBook Folio 9470M (Windows 10)"]
        UI[WPF UI & System Tray\nPhoneMirrorManager.cs]
        SDL[scrcpy v5.0.1 Engine\nSDL3 Window & Input Interceptor]
        DXVA[Intel HD Graphics 4000 GPU\nDXVA2 QuickSync Hardware H.264 Decoder]
        
        UI -->|Spawn Process| SDL
        SDL -->|DirectX / DXVA2 Pipeline| DXVA
        SDL -->|Trackpad / Mouse & Keystrokes| ADB_CLI[Android Debug Bridge CLI\nADB Tunnel / Socket]
    end

    subgraph Phone ["Samsung Galaxy A17 (One UI Core)"]
        SRV[scrcpy-server JAR\nAndroid Shell UID 2000]
        INJECT[InputManager / Service\nandroid.permission.INJECT_EVENTS]
        DISP[SurfaceFlinger / Display Compositor]
        CODEC[Hardware MediaCodec\n8 Mbps AVC/H.264 Encoder @ 60 FPS]
        POW[PowerManager / DisplayController\ncmd display power-off]

        ADB_CLI -->|Forward Port / Push JAR| SRV
        SRV -->|Inject Clicks, Swipes & Keys| INJECT
        DISP -->|Raw Buffer Feed| CODEC
        CODEC -->|H.264 NAL Stream (< 50ms)| SDL
        SRV -->|Screen-Off Mode| POW
    end
```

### Architectural Highlights:
1. **Zero-Overhead Intel QuickSync Video Pipeline:**
   * Strictly enforces `--video-codec=h264` to utilize the Folio 9470M's Ivy Bridge Intel HD Graphics 4000 dedicated QuickSync hardware decoder.
   * Eliminates the thermal runaway and 100% CPU lockups caused by software-only HEVC, VP9, or AV1 decoding on dual-core hardware.
   * Maintains steady 60 FPS streaming with latency under 50 milliseconds and laptop CPU utilization under 3%.
2. **Dynamic Phone IP Resolution (any network topology):**
   * `PhoneMirrorManager.GetCandidateDeviceIps(hintIp)` builds a prioritized candidate list:
     1. `SyncServer.ConnectedClientIp` — extracted from the live TCP socket's `RemoteEndPoint`. Correct on all topologies without any additional discovery.
     2. All default gateway IPs enumerated from `NetworkInterface.GetAllNetworkInterfaces()` — catches mobile hotspot topology where the phone *is* the default gateway (e.g. `10.253.25.103`).
   * `StartMirrorAsync` iterates candidates calling `adb connect <ip>:5555` with 2.5s timeout per attempt until one succeeds.
   * No hardcoded IP addresses. Works on home Wi-Fi, mobile hotspot, office LAN, and any subnet without user configuration.
3. **Permission-Free Input Injection (UID 2000 Shell Level):**
   * Operates with Android `shell` UID (UID 2000), which possesses the system-level `android.permission.INJECT_EVENTS` capability.
   * Maps physical mouse clicks, trackpad multi-touch gestures, right-click (Android Back), and middle-click (Android Home) directly into native Android `MotionEvent` and `KeyEvent` streams.
   * Physical laptop keyboard keystrokes are translated into Android key codes or directly injected via UTF-8 text strings without requiring a custom on-screen keyboard on the phone.
4. **Screen-Off Battery & Thermal Preservation:**
   * Operates with Android `shell` UID (UID 2000), which possesses the system-level `android.permission.INJECT_EVENTS` capability.
   * Maps physical mouse clicks, trackpad multi-touch gestures, right-click (Android Back), and middle-click (Android Home) directly into native Android `MotionEvent` and `KeyEvent` streams.
   * Physical laptop keyboard keystrokes are translated into Android key codes or directly injected via UTF-8 text strings without requiring a custom on-screen keyboard on the phone.
3. **Screen-Off Battery & Thermal Preservation:**
   * Implements `--turn-screen-off` and `--stay-awake` using Android's low-level display power state controls (`cmd display power-off`).
   * The phone's AMOLED screen is completely shut down, eliminating heat generation and battery drain while the video encoder continues capturing the virtual display compositor buffer seamlessly.

---

## 7. UI/UX Design System & Architectural Hierarchy

### A. Desktop (C# .NET 9 WPF — Raycast / Linear Standard):
* **Layered Obsidian Palette:** Canvas (`#0A0C10`), Surface (`#12141A`), Card (`#181B22`), Card Hover (`#222834`), Borders (`#222632`).
* **Vector Iconography:** Zero emoji dependency. Windows 10 native `Segoe MDL2 Assets` vector glyphs mapped to standard Unicode code points (`\uE80F`, `\uEB9F`, `\uE8C8`, `\uE714`, `\uE70F`, `\uE8EA`, `\uE713`, `\uE7C5`).
* **Spatial Ergonomics:**
  * 250px Left Navigation Rail with connected device hardware badge and quick snip/record triggers.
  * Hero Continuity Deck displaying vector device silhouette, connection state, battery level, transport pills, and 1-click mirror launch.
  * 2×2 Balanced Action Matrix (Screen Capture, Image Annotator, Video Suite, Universal Clipboard).
  * Dedicated 4-panel Settings & Hardware Decode view (`ViewSettings`).
* **Tactile Motion Physics:** 120ms cubic-bezier transition curves with subtle `scale(0.98)` downscale upon click.

### B. Mobile (Kotlin Android — Samsung One UI 6 Standard):
* **Viewing Zone vs. Interaction Zone Split:**
  * Upper 25%: Non-interactive viewing zone with large branding, connection state, and telemetry.
  * Lower 75%: Thumb-reachable interaction zone housing 20dp squircle focus cards.
* **Bottom Navigation Architecture:** 4-tab pinned `BottomNavigationView` (Dashboard, Clipboard, Gallery, Settings) using Material XML vector drawables.
* **Dedicated Settings View (`layoutSettings`):** Isolates recording audio toggles, ghost overlay visibility, shake-to-stop gesture sensitivity, and device profiles from the primary connection screen.

