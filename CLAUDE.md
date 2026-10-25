# CLAUDE.md — Agent Guidelines & Instructions for LinkSync

## Mandatory Pre-Flight Check (MUST READ FIRST)
Before proposing changes, executing commands, or editing files:
1. Read `AGENTS.md` (Hardware constraints, verified tool paths, strict architectural rules).
2. Read `PROGRESS.md` (What is done, active task, what is next).
3. Read `ARCHITECTURE.md` (C4 diagrams, binary wire protocol, packet frames).

## Mandatory Post-Flight Check (MUST UPDATE AFTER TASK)
Before finishing your response:
1. Update `PROGRESS.md` to check off completed items (`[x]`) and record current status.
2. Update `ARCHITECTURE.md` if any wire protocol, OpCode, or subsystem was modified.
3. Update `USER_GUIDE.md` if any user-facing feature or procedure changed.
4. Log any new technical design decisions in `DECISIONS.md`.

## Strict Architectural Guardrails
- **Target Hardware:** HP EliteBook Folio 9470M (Intel HD 4000 GPU, dual-core Ivy Bridge) & Samsung Galaxy A17.
- **NO ELECTRON / NO CHROMIUM / NO HEAVY WEB RUNTIMES:** Desktop MUST remain native C# .NET 9 Win32 / WPF (< 40MB RAM).
- **H.264 ONLY:** Hardware QuickSync acceleration only supports H.264 (AVC). Never use H.265, VP9, or AV1.
- **100% LOCAL PEER-TO-PEER:** Zero cloud dependencies, zero external servers.
- **JDK 17 LTS ONLY FOR GRADLE:** Always use `C:\Users\thenp\AppData\Local\Programs\jdk-17\jdk-17.0.20.1+1` (specified in `mobile/gradle.properties`). Never change to global Java 25.
