# Master Architectural & Reverse-Engineering Plan: LinkSync
### A ShareX-Grade Native Companion for Windows & Android

> **Document Version:** 3.0.0  
> **Target Devices:** HP EliteBook Folio 9470M (Windows 10/11) & Samsung Galaxy A17 (Android)  
> **Core Tenets:** Zero Cloud Overhead, Pure Native C# & Kotlin, Hardware-Accelerated Video, Two-Way Developer Bridge, 100% Encrypted Local P2P.

*(This is a synchronized local workspace copy. The primary artifact is maintained in system memory: [phone_sync_master_plan.md](file:///C:/Users/thenp/.gemini/antigravity/brain/19884181-3d51-495c-a0c5-f3e77ddc231a/phone_sync_master_plan.md))*

---

## High-Level Summary of Built-in Developer Superpowers

1. **Two-Way Universal Clipboard:** Copy links or tokens on PC $\rightarrow$ paste on phone. Copy OTPs or crash logs on phone $\rightarrow$ paste on PC.
2. **Instant Screenshot-to-Clipboard Bridge:** Phone hardware screenshot $\rightarrow$ 200ms transfer $\rightarrow$ ready to `Ctrl + V` on Windows.
3. **Galaxy A17 Screen Recorder & Live Drawing (XRecorder):**
   * Quick Settings Tile for 1-tap recording.
   * Floating overlay toolbar for live drawing (Arrows, Step Badges, Boxes, Text).
   * Native internal audio capture (Android 10+).
   * Auto-sync to laptop + optional auto-delete on phone to preserve internal storage.
4. **ShareX-Grade Image Annotator & Video Suite:**
   * Step badges (①, ②, ③...), pixelate / blur redaction, spotlight dimmer, Galaxy A mockup frame.
   * Lossless video trimming (< 300ms) and animated GIF converter for GitHub / Jira / Telegram.
5. **Ultra-Low Latency Mirroring with Remote Input:**
   * 60 FPS H.264 stream decoded via Intel HD 4000 DXVA2 hardware decode (< 3% CPU on HP Folio 9470M).
   * Control phone using laptop's physical keyboard and mouse.
   * "Screen-Off" power save mode to prevent phone display burn-in and heating.
6. **Communication Hub & 1-Click OTP:**
   * SMS thread viewer and sender.
   * Windows Toast notification with a **[Copy Code: 123456]** button for instant 2FA logins.
   * Digital phone dialer.
7. **Reverse Push & APK Drag-and-Drop:**
   * Drop test files or freshly compiled `.apk` files onto the laptop window to push and auto-install on phone.
8. **Restricted Wi-Fi Fallback:**
   * Automatically falls back to USB cable via `adb reverse tcp:54884 tcp:54884` if Wi-Fi has AP isolation or is offline.
