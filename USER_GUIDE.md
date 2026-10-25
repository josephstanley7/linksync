# LinkSync User Guide (Plain English Manual)

> **Welcome to LinkSync!**  
> This guide is written for everyone—whether you are a programmer, tester, or non-technical user. No coding knowledge is required to use this guide.

---

## What is LinkSync?

LinkSync is an invisible, instant wireless cable between your **Samsung Galaxy A17** phone and your **HP laptop**. It allows you to:
1. **Take a screenshot on your phone** $\rightarrow$ and immediately press `Ctrl + V` (Paste) on your laptop. No more sending photos to yourself on Telegram or WhatsApp!
2. **Record your phone screen with drawing tools** (arrows, boxes, step numbers) even though your Galaxy A17 doesn't have a built-in screen recorder.
3. **Share your clipboard:** Copy text on your phone and paste it on your laptop (or vice-versa).
4. **Never lag your laptop:** Unlike Microsoft Phone Link, LinkSync is featherlight and won't make your laptop fans scream or slow down your machine.

---

## Step 1: Installing & Opening LinkSync on Your Laptop

### A. Installing LinkSync (One-Time Setup):
1. On your laptop desktop, you will find:  
   👉 **`LinkSync_Setup_v1.0.exe`**
2. Double-click it. The standard Windows Setup Wizard will open.
3. Click **Next** $\rightarrow$ **Install**.
4. That's it! LinkSync is now installed as a real Windows application:
   * A clean **LinkSync** shortcut is placed on your Desktop.
   * A **LinkSync** shortcut is added to your Windows Start Menu.
   * It appears in **Windows Settings $\rightarrow$ Apps** ("Add or Remove Programs") with an **Uninstall** option so you can delete it anytime.
   * You can copy this `LinkSync_Setup_v1.0.exe` file onto a USB stick or cloud drive and share it with anyone to install on their PC!

### B. Opening LinkSync:
1. Double-click the **LinkSync** icon on your Desktop or Start Menu.
2. The sleek dark window titled **"LinkSync - Native Phone-to-PC Bridge"** appears on your screen with zero terminal windows.

---

## Step 2: Connecting Your Phone to Your Laptop (Zero Configuration)

LinkSync automatically searches both your **Wi-Fi network** and your **USB cable** simultaneously. You never need to type any IP addresses or port numbers!

### Method A: Over Local Wi-Fi or Mobile Hotspot (Completely Wireless)
1. **Home/Office Wi-Fi:** Make sure your phone and laptop are connected to the same Wi-Fi router.
2. **Mobile Hotspot (Outside the House):** Turn on your Samsung phone's Mobile Hotspot and connect your laptop to it. LinkSync automatically detects your phone as the network gateway and connects in milliseconds—even though Android normally blocks general broadcasts!
3. Open the **LinkSync app** on your Samsung phone.
4. LinkSync automatically finds your laptop. Within 1 to 2 seconds, your phone and laptop will connect!
5. The top card will turn green: **"🟢 Connected: HP EliteBook Folio 9470M"** and **"Connected via Wi-Fi"**.

### What If Multiple Laptops Are Nearby?
If several people or family members have LinkSync installed on their laptops nearby:
* **First Time / Single PC:** If only your laptop is on, LinkSync connects to it automatically and remembers its unique ID (`PC-XXXXXXXX`).
* **Switching Computers:** Tap the button titled **"💻 Available Computers"** right under the status card on your phone. A menu pops up showing every active laptop on the network. Tap the computer you want to connect to, and LinkSync instantly switches to it!

### Method B: Over a USB Cable (Guaranteed Anywhere)
1. Plug your Samsung Galaxy A17 into your laptop using your standard USB charging cable.
2. LinkSync tests the direct USB cable link and connects in under 10 milliseconds.
3. **If a prompt appears on your phone screen saying "Allow USB debugging?":** Tap **"Allow"** (or check "Always allow from this computer").
4. If your phone was only charging or in file-locked mode, LinkSync will display:
   `"🔌 USB Cable detected. Please allow USB debugging or file transfer on phone."`
   Once unlocked and allowed, LinkSync connects immediately!

---

## Step 3: Bidirectional Screenshot Sync & ShareX Snipping Tools

LinkSync works in **both directions** without any manual button clicking:

### A. Phone to Laptop Screenshot Sync (Instant `Ctrl + V`):
1. **Take a screenshot on your phone** normally (press **Power + Volume Down** together).
2. **Auto-Transfer in Under 500ms:** You will see a Windows notification, and the screenshot is safely saved in `Pictures\LinkSync\Screenshots\`.
3. **Paste anywhere on your laptop:**  
   Open your browser, email, Slack, Discord, Word document, or code editor and simply press:  
   ⌨️ **`Ctrl + V`**
4. The screenshot pastes immediately without ever opening the LinkSync app!

### B. Laptop to Phone Screenshot Sync (ShareX-Grade Desktop Snipping):
LinkSync brings the full screenshot power of **ShareX** to your Windows system tray and desktop:
1. **Trigger Snipping:**
   * **From System Tray:** Right-click the LinkSync tray icon next to the clock and choose:
     * 📸 **Take Screenshot (Region):** Freeze the screen, drag a selection box with smooth crosshairs and dimension badges (`W × H px`).
     * 🖥️ **Take Screenshot (Full Screen):** Instantly captures all monitors.
     * 🪟 **Take Screenshot (Active Window):** Captures only the current window without drop shadows.
   * **From Desktop App:** Click the prominent **"📸 Snip Screen (ShareX)"** button at the bottom of the left navigation rail.
2. **Interactive Selection & Action Bar:**
   * When snipping a region, the screen dims with an un-dimmed cutout over your selection.
   * Floating quick action buttons appear:
     * **`✓ Copy & Sync`** (or double-click): Copies directly to your Windows clipboard AND automatically syncs it to your Samsung Galaxy phone!
     * **`✏️ Annotate`**: Opens the image directly in the ShareX Annotator for drawing arrows and blurring tokens.
     * **`💾 Save`**: Saves to your `Pictures\LinkSync\Screenshots\` folder.
     * **`✕ Cancel`** (or press `Esc`): Closes the snip tool.
3. **Automatic Mobile Sync:**
   * When you snip on your laptop, the image is streamed directly to your phone.
   * The phone saves it into your **Gallery** (`Pictures/Screenshots`), and places the image on your mobile clipboard ready to paste into WhatsApp, Telegram, or Notes!

---

## Step 4: Redesigned Mobile & Desktop Interfaces

### Mobile Navigation (Samsung One UI Style):
* When you open LinkSync on your Samsung Galaxy A17, you are greeted with a clean, focused screen displaying **only the connection card** in the center:
  * Green indicator dot and connected laptop name: `🟢 HP EliteBook Folio 9470M`.
  * Minimalist `Disconnect` or `⚡ Connect to Laptop` action button.
* At the bottom, a native **Bottom Navigation Bar** lets you switch between:
  1. 🔗 **Connect:** The clean centered connection screen.
  2. 📋 **Clipboard:** A Samsung DeX-style chronological history of all copied snippets with source badges (`📱 FROM PHONE` in cyan, `💻 FROM LAPTOP` in emerald), timestamps, and 1-tap `📋 Copy` buttons.
  3. 📸 **Screenshots:** Browse all recent screenshots and screen recordings with 1-tap `▶ Open` and `⚡ Send to PC`.
     * **Auto-Delete on PC Sync:** Optional switch to automatically remove screenshots from your phone once they are confirmed saved on your laptop. Keeps phone gallery storage clean.
     * **2-on-a-Row Grid vs List:** Switch between `🔲 Grid` (arranged 2 per row with image previews and size badges) and `☰ List` view anytime.
     * **Modern Tactile Buttons:** Redesigned high-contrast buttons (`▶ Open` and `⚡ Sync`) for instant interaction.

### Desktop Navigation (Microsoft Phone Link Style):
* **Left Navigation Rail (240px):**
  * Top device header with phone name (`Samsung Galaxy A17`), live connection status, and battery/network badges.
  * Nav items: **Overview**, **Photos & Screenshots**, **Clipboard Stream**, **Video Suite**, and **Annotator**.
  * Quick-snip button: **`📸 Snip Screen (ShareX)`** pinned at the bottom of the rail.
* **Right View Area:** Smoothly displays whichever tab you select without bulky headers or window lag.

---

## Step 4: Screen Recording on Mobile (XRecorder-Style) & Laptop (ShareX-Style)

### A. Recording Your Phone Screen (XRecorder Patterns):
Because your Galaxy A17 does not come with a built-in screen recorder, LinkSync puts one right into your notification menu:

1. **Start Recording Anytime:**
   * **From Quick Settings:** Swipe down from the very top of your phone screen twice and tap **"LinkSync Record"**.
   * **From LinkSync App:** Open LinkSync and tap the red **"⏺ Start Screen Recording"** button inside the Connection screen.
2. **Audio Choice (Microphone vs. Silent):**
   * Before recording begins, an audio preferences popup appears:
     * 🎙️ **Record with Audio:** Captures your microphone and voice narration in crisp 44.1 kHz AAC stereo.
     * 🔇 **Record without Sound:** Produces a 100% silent, lightweight video.
     * ☑️ **Remember my choice:** Check this to remember your setting so future recordings start in 1 tap without asking. (You can toggle "Ask Audio Choice Every Time" in the LinkSync app settings).
3. **Choose "A Single App" vs. "Entire Screen":**
   * LinkSync fully supports Android 14+ App Screen Sharing:
     * **A Single App:** Choose any specific app (Chrome, WhatsApp, Settings). Android captures *only* that application window—all status bars, notifications, and floating overlays are automatically excluded from the video!
     * **Entire Screen:** Records your entire phone display.
4. **Clean Videos (Floating Bubble Controller Hidden):**
   * In XRecorder fashion, the floating bubble controller is **automatically hidden** during active recording so your video remains 100% clean with zero UI obstructions!
   * **How to control while hidden:**
     * 📳 **Shake Phone to Stop:** Simply shake your phone! LinkSync vibrates with haptic feedback and immediately finalizes the recording.
     * 📲 **Notification Bar Controls:** Swipe down your notification shade for instant `[ ⏹ Stop ]`, `[ ⏸ Pause / ▶ Resume ]`, and `[ 🎨 Brush ]` actions.
     * 👁️ **Manual Hide/Show:** If you choose to keep the bubble visible in settings, tap `👁️` on the capsule to hide it anytime.
5. **Live On-Screen Drawing (While Recording):**
   * Tap **`🎨 Brush`** in the notification shade (or on the bubble) to open the drawing palette:
     * 🏹 **Arrow:** Drag across the screen to draw a sharp vector arrow pointing directly at a bug.
     * 🔲 **Rectangle:** Drag to frame an error banner.
     * 🔢 **Step Badges (①, ②, ③):** Tap anywhere on the screen to drop auto-incrementing numbered circles.
     * ✏️ **Pen:** Freehand smooth sketch.
   * **Switch back to touching apps:** Tap **`👆 Touch`** to resume navigating apps normally while your drawings stay visible on screen!
6. **Post-Recording Preview & Trimming Gate:**
   * When you finish recording, LinkSync opens the preview player immediately. Review playback, trim start/end points with zero re-encoding loss, and tap **`⚡ Save & Sync to Laptop`**!

---

### B. Recording Your Laptop Desktop (ShareX-Style):
LinkSync brings the complete screen recording pipeline of **ShareX** to your Windows desktop using hardware-accelerated H.264 video:

1. **Start Desktop Recording:**
   * **From Left Sidebar:** Click **"🎥 Record Screen (ShareX)"** at the bottom of the navigation rail.
   * **From Header:** Click the red **"🎥 Record"** button at the top right of the window.
   * **From System Tray:** Right-click the LinkSync tray icon $\rightarrow$ **"🎥 Record Screen (Region)"** or **"🖥️ Record Screen (Full Screen)"**.
   * **From Region Snip:** In the snipping overlay, drag any box on your screen and click **"🎥 Record Region"** (or press Enter).
2. **ShareX Floating Recording Toolbar:**
   * An unbordered, floating control toolbar appears on your screen with:
     * 🔴 **Blinking REC Dot** and bold `REC` indicator.
     * ⏱️ **Live Duration:** `00:00:00` updating every second.
     * 📐 **Resolution Tag:** Displays exact recorded width and height (e.g. `1280 × 720`).
     * ⏸️ **Pause / Resume:** Freezes recording at the Windows kernel level (0% CPU).
     * ⏹️ **Stop:** Cleanly flushes the MP4 container and saves the recording.
     * ✕ **Abort:** Instantly discards and deletes the recording if you made a mistake.
   * While recording a region, an animated red dashed border frames the recorded area.
3. **ShareX & XRecorder Post-Recording Preview Gate:**
   * When you click **`⏹ Stop`** on either phone or laptop, LinkSync does **not** blindly send the video before you have a chance to review it.
   * **On Phone (`VideoPreviewActivity`):** An inline player opens with Play/Pause, timeline scrubber, and Start/End trimming handles.
     * Tap **`⚡ Save & Sync to Laptop`** to losslessly trim (if points are set), save to your phone gallery under `Movies/LinkSync`, and stream to your laptop.
     * Tap **`💾 Save to Phone Gallery Only`** to save locally without network sync.
     * Tap **`🗑️ Discard Recording`** to permanently delete the video.
   * **On Laptop (`VideoSuiteWindow` Preview Mode):** A top preview banner appears over the Video Suite:
     * Click **`⚡ Save & Sync to Phone`** to trim (if markers are set), copy to Windows clipboard, and stream to your Galaxy A17!
     * Click **`💾 Save Locally`** to save to `Pictures\LinkSync\Screenshots` and copy to clipboard.
     * Click **`🗑️ Discard`** to delete the recording.
4. **Crystal-Clear Audio Recording:**
   * Mobile screen recordings now capture synchronized stereo AAC audio (44.1 kHz, 128 kbps) along with your voiceover explanations using Android 14+ dual foreground service permissions (`mediaProjection|microphone`).
   * When prompted for microphone permission upon starting recording, tap **"While using the app"** or **"Only this time"**.
5. **Zero Broken Images in Phone Gallery:**
   * All screen recordings transferred from laptop to phone are safely saved in `Movies/LinkSync` and registered under Android's video index (`MediaStore.Video.Media`). Samsung Gallery displays real video thumbnails with play icons—never broken pictures!

---

## Step 5: Editing & Annotating Screenshots (ShareX-Grade Desktop Annotator)

When you take a screenshot, you often need to highlight a bug, hide sensitive private details (like passwords or API tokens), or number the steps:

1. **Opening the Annotator:**  
   * **From Desktop App:** Double-click any screenshot in the LinkSync media gallery (or click its pink **"✏️ Annotate"** button).
   * **From System Tray:** Right-click the tray icon and choose **"✏️ Open Image Annotator"**.
   * **Drag & Drop:** Drag and drop any image file directly into the annotator window.
   * **Paste from Clipboard:** Press **`Ctrl + V`** inside the annotator to immediately edit whatever screenshot is currently in your Windows clipboard!
   * **Zoom & Pan:** Hold **`Ctrl` and scroll your mouse wheel** to zoom in and out smoothly (or use the bottom right zoom buttons: `Fit`, `1:1`, `➕`, `➖`).
2. **Professional Bug-Reporting Tools:**
   * 🏹 **Vector Arrows:** Drag to draw an arrow pointing directly at a button or error message. Choose your favorite color and line thickness.
   * ① **Step Number Badges:** Tap anywhere on the image to drop a numbered circle (`①`, `②`, `③`...). Click `[🔄 #1]` anytime to restart your step count back to 1.
   * 🔲 **Box / Rectangle:** Drag to outline an input field or modal dialog.
   * 🔦 **Spotlight / Dimmer:** Drag a box around a feature. The rest of the screen will automatically dim with a dark mask, spotlighting the exact button you want viewers to focus on.
   * ⬛ **Mosaic Pixelation:** Drag a box over passwords, API keys, or credit card numbers to permanently scramble them with pixelation.
   * 🌫️ **Gaussian Blur:** Drag a box over names, email addresses, or phone numbers to give them a smooth frosted-glass blur.
   * 🖍️ **Highlighter:** Draw over text to highlight it in vibrant translucent yellow.
   * 🔤 **Text Callout:** Click anywhere to type a clean explanation card.
   * 📱 **Samsung Galaxy Bezel Mockup:** Check the box at the top to wrap your phone screenshot inside a realistic Samsung Galaxy smartphone chassis with a camera punch-hole and drop shadow!
3. **Instant Exporting:**
   * Press **`Ctrl + C`** (or click **"📋 Copy"**): The fully annotated image at 100% full quality is copied directly into your Windows clipboard. You can paste it into Telegram, GitHub issues, Slack, or email immediately!
   * Press **`Ctrl + S`** (or click **"💾 Save"**): Saves changes back to disk.

---

## Step 6: Cutting Videos & Creating High-Framerate GIFs (Video Suite)

When you record a bug or feature demo on your phone, you usually want to trim out the beginning or convert it into a short animated GIF for GitHub, Jira, or Telegram:

1. **Opening the Video Suite:**  
   Click the purple **"🎬 Video Tools"** button next to any `.mp4` video in LinkSync (or click the top **"🎬 Video Suite"** button).
2. **Lossless Slicing (< 300ms, 0% CPU):**
   * Use the timeline slider to find where the action begins and click **"[ Set Start"**.
   * Drag to where the action ends and click **"Set End ]"**.
   * Click **"⚡ Lossless Cut MP4"**.
   * *Because LinkSync uses direct keyframe stream copying, cutting happens in less than half a second without re-encoding the video or heating up your laptop!*
3. **Exporting Animated GIFs:**
   * Choose your resolution: **720p (Recommended for GitHub/Slack)**, **480p (Lightweight)**, or **1080p**.
   * Choose your frame rate: **15 FPS (Smooth & compact)** or **24 FPS**.
   * Click **"🖼️ Export Animated GIF"**.
   * LinkSync runs a 2-pass high-clarity palette filter that makes the GIF look crisp and sharp without fuzzy color banding.
   * **Instant Copy:** As soon as the GIF finishes, it is automatically copied into your Windows clipboard so you can paste it into Telegram or Slack right away!

---

## Step 7: Using the Two-Way Universal Clipboard & Background Mode (Samsung DeX Style)

Copying links, passwords, and verification codes back and forth is completely automatic:

* **Sending from Phone $\rightarrow$ Laptop:**
  * **Automatic Sync:** Copy text anywhere on your phone; LinkSync detects it and immediately syncs it to your laptop clipboard (`Ctrl + V`). You can paste it into Notepad, browser, or Slack without ever touching LinkSync!
  * **Text Selection ("⚡ Send to PC"):** When highlighting any text or link on your phone (in Chrome, WhatsApp, Notes), tap **"⚡ Send to PC"** directly in the floating selection menu.
  * **1-Tap Notification Bar:** Swipe down from the top of your phone screen anytime and tap **`[📋 Send Copied Text to PC]`** in your LinkSync notification.
  * **In-App Resumption:** Switching back to or opening the LinkSync phone app automatically checks and pushes copied text.
* **Sending from Laptop $\rightarrow$ Phone:**
  * Highlight any text or link on your laptop and press `Ctrl + C`.
  * Open any text field or app on your phone, press and hold, and tap **Paste**.

### Samsung DeX Style Clipboard Stream with Timestamps:
* Look at the right-hand panel of the LinkSync desktop window: **"📋 Universal Clipboard Stream"**.
* Every item displays a modern card with:
  * **Source badge:** `📱 FROM PHONE (GALAXY A17)` (Cyan) or `💻 FROM PC (FOLIO 9470M)` (Emerald).
  * **Exact arrival timestamp** (e.g. `14:25:08`).
  * **Full text preview** with character count.
  * A **`📋 Re-copy`** button to re-load any older text snippet into your Windows clipboard with a single click.

### Running LinkSync in the Background on Laptop (System Tray):
* When you click the `X` button on the LinkSync laptop window, **the app does not close or stop working**.
* It neatly minimizes into your **Windows System Tray** (in the bottom-right corner next to the clock) using the official LinkSync icon.
* **Working Set Memory Trimming:** While in the background, LinkSync trims its memory down to **~3.0 MB of RAM** and **0.0% CPU**, keeping your laptop cool and completely lag-free!
* **Auto-Start with Windows:** Right-click the LinkSync tray icon and check **"Start with Windows"**. LinkSync will automatically launch silently in the background on every computer boot (`--minimized`).
* **Hover:** Hovering over the icon shows the live status (e.g., `LinkSync — Connected: Samsung SM_A175F`).
* **Reopen:** Double-click or single-click the tray icon anytime to bring the window back to the screen.
* **Exit:** Right-click the tray icon and select **"Exit LinkSync"** if you want to completely shut it down.

---

## Step 8: Real-Time Phone Mirroring & Remote Control (Samsung Galaxy A17)

Samsung deliberately restricts "Samsung DeX" to expensive flagship phones and completely blocks it on Galaxy A-series devices. **LinkSync breaks this barrier**, bringing high-performance 60 FPS real-time phone mirroring and full laptop control to your Samsung Galaxy A17!

### A. Starting the Mirror Window:
1. Ensure your phone is connected to your laptop (via Wi-Fi or USB cable).
2. On your laptop, you can start mirroring in any of these 4 convenient ways:
   * **From Overview:** Click the large **"📱 Start Mirroring"** button inside the "Real-Time Phone Mirroring" card.
   * **From Sidebar:** Click **"📱 Phone Mirror (60 FPS)"** on the left navigation rail.
   * **From Header:** Click the quick **"📱 Mirror"** button in the top right header strip.
   * **From System Tray:** Right-click the LinkSync icon by the clock and select **"📱 Mirror Phone Screen (60 FPS)"**.
3. A crisp, high-definition window titled **`LinkSync Phone Mirror (Samsung Galaxy A17)`** pops up immediately!
4. **Hardware-Accelerated Smoothness:** The stream runs at a fluid **60 FPS** using native **Intel HD Graphics QuickSync H.264 decoding**, so your laptop fans stay quiet and your CPU usage stays under 3%.

### B. Controlling Your Phone with Laptop Mouse & Trackpad:
You can interact with your phone just like touching the screen:
* **Left Click:** Tap buttons, open apps, activate toggles.
* **Click & Drag:** Swipe between home screens, pull down the notification shade, or swipe away apps.
* **Mouse Scroll Wheel:** Scroll smoothly up and down web pages, Instagram feeds, and long lists.
* **Right-Click:** Instantly triggers the Android **Back** button.
* **Middle-Click:** Instantly triggers the Android **Home** button.
* **Right-Click & Drag:** Pulls down the Android notification shade from anywhere on the screen!

### C. Typing with Your Laptop Keyboard:
* Click inside any search bar, password input, or chat message box on the mirrored screen.
* Type directly using your physical laptop keyboard!
* **Instant Text Injection:** Text appears on your phone screen in real-time, making filling out long registration forms, typing code, or messaging contacts 10x faster.
* Special keys like `Enter`, `Backspace`, arrow navigation, and `Ctrl + A` work seamlessly.

### D. "Screen-Off" Power Saving Mode (Keep Phone Cool & Battery Full):
During long phone testing or mirroring sessions, keeping the phone's physical screen turned on generates heat and drains battery:
1. In LinkSync on your laptop, click **"🌙 Screen-Off Mirror"** (or in the system tray, select **"🌙 Mirror Screen-Off (Battery Saver)"**).
2. Alternatively, inside an active mirror window, press:  
   ⌨️ **`Alt + O`** (or `Ctrl + O`)
3. Your physical Samsung phone screen will immediately turn completely black!
4. You can continue interacting, clicking, typing, and testing apps on your laptop screen while your phone stays cold and uses almost no battery.
5. Press **`Alt + Shift + O`** (or press the phone's physical power button) whenever you want to turn the physical screen back on.

---

## Step 9: Navigating the Redesigned User Interface (Big-Tech Standard)

LinkSync features a modern, bespoke user interface designed according to the highest standards of Apple Continuity, Samsung One UI 6, and Linear:

### Laptop (Windows 10 / .NET 9 WPF):
* **Left Navigation Rail (250px):** Clean vertical navigation using native vector glyphs (Home, Photos, Clipboard, Video, Annotator, Phone Mirror, Settings).
* **Hero Continuity Deck:** At the top of the Overview screen, an elevated hardware card displays your connected Samsung Galaxy A17 silhouette, live battery status, Wi-Fi/USB transport pills, and instant 1-click 60 FPS mirror buttons.
* **2×2 Balanced Action Matrix:**
  * **Screen Capture & Snip:** Region snip, active window snip, full-screen snip.
  * **Image Annotator:** Step badges (①, ②, ③), vector arrows, blur/pixelate redactions, and 1-tap phone mockups.
  * **Video Suite & Screen Recording:** Region and fullscreen desktop recording, lossless keyframe cutting, and high-FPS animated GIF generation.
  * **Universal Clipboard Stream:** Two-way clipboard synchronization with auto-paste controls.
* **Dedicated Settings & Audio Tab:** Change storage folders, toggle auto-copying to clipboard, configure Windows startup, and inspect Intel QuickSync GPU acceleration.

### Mobile Phone (Samsung One UI 6):
* **One UI 6 Viewing Zone (Top 25%):** Clean typography and high-contrast status summary.
* **One UI 6 Interaction Zone (Bottom 75%):** Ergonomic 20dp squircle focus cards placed within easy reach of your thumb.
* **4-Tab Bottom Navigation Bar:**
  1. **Dashboard:** Hardware connection card, active PC picker, and quick record launcher.
  2. **Clipboard:** Live stream of copied items with timestamped cards and 1-tap re-copy.
  3. **Gallery:** Synced screenshots and video recordings with Grid and List views and auto-delete settings.
  4. **Settings:** Dedicated settings page with microphone audio toggle, always-ask popup preference, ghost overlay isolation, shake-to-stop sensitivity, and device hardware profile.

---

## Step 10: Phone Calls, SMS Texting & 1-Click Verification Codes (Phone Link Style)

LinkSync brings full communication hub capabilities directly to your laptop dashboard, designed with the clean layout of Microsoft Phone Link but with 0% cloud requirements and zero bloat:

### A. Reading and Sending SMS Messages:
1. On the left navigation rail of LinkSync, click **💬 Messages**.
2. **Left Conversations Pane:** See all your recent text threads arranged chronologically, with contact names, dates, and preview snippets.
3. **Right Chat Stream:** Click any conversation to view the full chat history with distinct chat bubbles (blue for your sent messages, dark gray for received messages).
4. **Sending a Reply:** Type your message in the bottom input bar and press **Enter** (or click the Send button). Your phone immediately sends the SMS through your carrier network without touching your phone.

### B. Calling Contacts and the Digital Keypad:
1. Click **📞 Calls** on the left rail.
2. **Tactile Dial Pad:** Use your mouse or laptop number keys to enter a phone number on the 12-button keypad (`0-9`, `*`, `#`).
3. Click the green **`[ 📞 Call ]`** button. Your phone immediately initiates the cellular call.
4. **Live Call Duration & Hangup:** An active in-call card displays the dial status, live elapsed call timer (`00:01:23`), and a red **`[ 🛑 End Call ]`** button to remotely terminate the call.

### C. Browsing and Searching Contacts:
1. Click **👥 Contacts** on the left rail.
2. Type any name or number in the instant search box.
3. Every contact card shows the contact's name, phone number, and two 1-click action buttons:
   * **`[ 📞 Call ]`**: Instantly switches to the dialer and places a call.
   * **`[ 💬 SMS ]`**: Instantly opens a new SMS conversation thread with that person.

### D. 1-Click Copy Verification Codes (2FA OTP Toasts):
1. Whenever your bank, Google, Microsoft, or any service sends you a 2-factor login SMS (e.g. *"Your security code is 482910"*):
2. A sleek notification card slides up from the bottom-right corner of your laptop screen with an audible chime:
   * Displays the sender name (e.g., `Google` or `Chase`).
   * Displays the message preview.
   * Automatically copies the code directly into your Windows clipboard!
   * Shows a prominent 1-click button: **`[ 📋 Copy Code: 482910 ]`**.
3. Simply press **`Ctrl + V`** on your laptop browser or login page to paste the code immediately!

---

## Frequently Asked Questions & Troubleshooting (FAQ)

#### Q: The laptop says "Searching for Phone" and won't turn green. What should I do?
1. Check that both devices are on the exact same Wi-Fi name.
2. If you are on office or public Wi-Fi, plug in the USB cable and click **"⚡ Connect via USB (ADB Tunnel)"** on the laptop. This works 100% of the time.

#### Q: Will LinkSync make my laptop slow or loud?
No. Microsoft Phone Link lags your laptop because it uses heavy browser background engines (`PhoneExperienceHost.exe`) that eat 500MB of RAM. LinkSync is written as a tiny native Windows program that uses less than **35 MB of RAM** (less than a single browser tab) and uses **0% of your CPU** when waiting for files.

#### Q: Where are my files saved on my laptop?
All synced photos and videos land in your personal Windows Pictures folder:  
`C:\Users\<YourUsername>\Pictures\LinkSync\Screenshots\`  
You can open it instantly by clicking the **"📁 Open Screenshots"** button in LinkSync.

#### Q: Do I need an internet connection or an account?
**No.** LinkSync has no cloud servers, no account sign-ups, and requires zero internet. It talks directly between your phone and laptop over your local network. Your photos, text, and videos never leave your room.
