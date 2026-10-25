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

## Step 4: Screen Recording with Live On-Screen Drawing

Because your Galaxy A17 does not come with a built-in screen recorder, LinkSync puts one right into your notification menu:

1. **Swipe down from the very top of your phone screen** twice to open the full Samsung Quick Settings menu.
2. Look for the button titled **"LinkSync Record"**. (If you don't see it, tap the three dots or pencil icon in the top right, tap "Edit Buttons", and drag LinkSync Record into your active list).
3. **Tap "LinkSync Record":**
   * Stay in whatever app you were already using. LinkSync itself will not open.
   * A small system permission message will ask if you want to record your screen. Tap **"Start now"**.
   * A tiny floating circle (bubble) will appear on the edge of your screen (if overlay permission is already granted).
4. **Drawing on Your Screen while Recording:**
   * Tap the **Brush icon** on the floating circle.
   * A mini drawing toolbar will slide out:
     * 🏹 **Arrow:** Tap and drag to draw a clean arrow pointing at a button or bug.
     * 🔲 **Rectangle:** Drag to draw a box around an error message.
     * 🔢 **Step Badges (①, ②, ③):** Tap anywhere on the screen to drop numbered circles showing the steps of how a problem happened.
     * ✏️ **Pen:** Freehand drawing or circling.
   * Tap the **Hand/Cursor icon** whenever you want to stop drawing and tap on apps normally again.
5. **Stopping and Syncing:**
   * When you are done, tap the red **Stop button** on the floating circle (or swipe down your notification menu and tap the tile again).
   * The video is saved and **automatically uploaded straight to your laptop**!

---

## Step 5: Editing & Annotating Screenshots (ShareX-Grade Desktop Annotator)

When you take a screenshot, you often need to highlight a bug, hide sensitive private details (like passwords or API tokens), or number the steps:

1. **Opening the Annotator:**  
   In the LinkSync window on your laptop, find the screenshot in your list on the left and click **"✏️ Annotate"** (or double-click the item). You can also click the top button **"✏️ Annotator"** to open any picture from your PC.
2. **Professional Bug-Reporting Tools:**
   * 🏹 **Vector Arrows:** Drag to draw an arrow pointing directly at a button or error message. Choose your favorite color and line thickness.
   * ① **Step Number Badges:** Tap anywhere on the image to drop a numbered circle (`①`, `②`, `③`...). The number increments automatically for step-by-step bug reproduction guides.
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
