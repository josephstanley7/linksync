# GitHub Copilot Instructions for LinkSync

## Mandatory Workflow
1. **Pre-flight:** Read `AGENTS.md`, `PROGRESS.md`, and `ARCHITECTURE.md` before writing or modifying code.
2. **Post-flight:** Always update `PROGRESS.md`, `ARCHITECTURE.md`, `USER_GUIDE.md`, and `DECISIONS.md` whenever features or milestones are completed.

## Core Rules
- Desktop receiver MUST remain pure native C# .NET 9 Win32 / WPF (< 40MB RAM). Strictly NO Electron or heavy webviews.
- Mobile client is native Kotlin (Android 13–15).
- All media and video streams MUST use hardware H.264 (AVC) to ensure compatibility with Intel HD 4000 QuickSync.
- Android Gradle builds MUST use JDK 17 LTS (`C:\Users\thenp\AppData\Local\Programs\jdk-17\jdk-17.0.20.1+1`).
