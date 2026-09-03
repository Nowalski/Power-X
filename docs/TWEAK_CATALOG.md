# Tweak catalog

> Generated from `TweakCatalog` by `powerx tweak docs`. Do not edit by hand.

## File Explorer

### `explorer.classic-context-menu`: Restore the classic right-click menu

- **What it does:** Brings back the full Windows 10 style right-click menu, so there is no "Show more options" step.
- **Why you might want it:** Every shell extension shows up straight away, with no extra click to reach common items.
- **Downside:** You lose the compact Windows 11 menu and its icon row. A few apps only design for the new one.
- **Risk:** Moderate
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** 22000 ≤ build
- **Source:** Empty InprocServer32 under CLSID {86ca1aa0-34aa-4e8b-a509-50c905bae2a2}, the shim used widely and reversibly

### `explorer.compact-mode`: Use compact spacing in File Explorer

- **What it does:** Tightens the row spacing in Explorer lists (the "Use compact mode" folder option).
- **Why you might want it:** More items on screen at once, closer to the Windows 10 density.
- **Downside:** Smaller targets for touch.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** 22000 ≤ build
- **Source:** Explorer Advanced\UseCompactMode

### `explorer.disable-sync-provider-ads`: Hide OneDrive and sync provider ads in Explorer

- **What it does:** Turns off the "sync provider notifications" that show OneDrive promotions and prompts inside the Explorer window.
- **Why you might want it:** Takes the advertising out of the file manager. OneDrive sync itself is not affected.
- **Downside:** You will not see OneDrive storage prompts in Explorer.
- **Risk:** Low · **Recommended**
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Explorer Advanced\ShowSyncProviderNotifications

### `explorer.launch-to-this-pc`: Open File Explorer to This PC

- **What it does:** Makes Explorer open on "This PC" instead of "Home".
- **Why you might want it:** Quicker access to your drives, and it skips the Home page's recent and recommended files.
- **Downside:** You lose the quick recent-files view on the Home page.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Explorer Advanced\LaunchTo: 1 = This PC, 2 = Home, 3 = Downloads

### `explorer.show-file-extensions`: Show file name extensions

- **What it does:** Shows the file type (.exe, .pdf, .txt) on every file in Explorer.
- **Why you might want it:** With extensions hidden it is easy to mistake invoice.pdf.exe for a PDF. Showing them is a basic safety win.
- **Downside:** File names look a little busier. Nothing stops working.
- **Risk:** Low · **Recommended**
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Windows shell HideFileExt value (<https://learn.microsoft.com/windows/win32/shell/how-to-customize-the-file-icon-overlay>)

### `explorer.show-hidden-files`: Show hidden files and folders

- **What it does:** Shows files and folders that have the Hidden attribute. Protected operating-system files stay hidden.
- **Why you might want it:** Handy when you are troubleshooting or clearing out per-user app data.
- **Downside:** More clutter in day-to-day browsing, and it is easier to move or delete something by accident.
- **Risk:** Moderate
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Explorer Advanced\Hidden: 1 = show, 2 = don't show

## Gaming

### `gaming.disable-game-dvr`: Disable background game recording (Game DVR)

- **What it does:** Turns off the background capture path that Xbox Game Bar uses to keep recording your gameplay.
- **Why you might want it:** That background recording costs a bit of performance in some games. If you never grab clips, this stops the work.
- **Downside:** "Record the last 30 seconds" and background clips stop working. Manual capture in Game Bar may also stop.
- **Risk:** Moderate
- **Restart:** SignOut
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** GameConfigStore\GameDVR_Enabled and CurrentVersion\GameDVR\AppCaptureEnabled
- **Source:** Independent benchmarks put the effect at roughly 1 to 3 percent average FPS, and it varies by game. Not the big gains often claimed.

## Input

### `privacy.disable-pointer-suggestions`: Disable "Enhance pointer precision" (mouse acceleration)

- **What it does:** Turns off the Windows mouse acceleration curve, so pointer movement tracks your hand 1:1.
- **Why you might want it:** Consistent aim in games and a cursor that always moves the same distance for the same flick. Matches the "Enhance pointer precision" checkbox in Settings > Mouse.
- **Downside:** If you are used to acceleration, slow precise movements feel different at first.
- **Risk:** Low
- **Restart:** SignOut
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Control Panel\Mouse MouseSpeed / MouseThreshold1 / MouseThreshold2, the "Enhance pointer precision" checkbox

## Multitasking

### `multitasking.disable-snap-assist`: Disable Snap Assist suggestions

- **What it does:** After you snap a window, Windows stops offering a grid of your other windows to fill the rest of the screen.
- **Why you might want it:** Snapping feels quicker with no follow-up prompt.
- **Downside:** You place the second window yourself.
- **Risk:** Low
- **Restart:** none
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Explorer Advanced\SnapAssist

## Performance

### `performance.best-appearance-for-speed`: Adjust visual effects for best performance

- **What it does:** Turns off window and menu animations, fade and slide effects, drag-full-window, listview shadows and Aero Peek. This is the same set as "Adjust for best performance" in System Properties > Performance, but it leaves font smoothing on.
- **Why you might want it:** On a slow CPU or GPU, or an old laptop, the desktop feels a lot snappier. Font smoothing stays on so text still looks right.
- **Downside:** The UI is flat and abrupt, with no smooth transitions.
- **Risk:** Low
- **Restart:** SignOut
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Explorer\VisualEffects\VisualFXSetting = 2 plus the documented per-effect values

### `performance.disable-search-indexing`: Turn off Windows Search indexing

- **What it does:** Disables the Windows Search service, so it stops indexing your files in the background.
- **Why you might want it:** On a hard drive or a low-RAM machine the indexer is a constant, real load. On a spinning disk this is the biggest Potato mode win there is.
- **Downside:** Search inside File Explorer and Outlook gets much slower, because it falls back to scanning. Start-menu app search is not affected.
- **Risk:** Advanced
- **Restart:** Reboot
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Services\WSearch\Start = 4 (disabled)

### `performance.disable-transparency`: Turn off transparency effects

- **What it does:** Turns off the acrylic blur on the Start menu, taskbar and other surfaces.
- **Why you might want it:** The GPU recomputes that blur constantly, so turning it off helps weak integrated graphics. Matches "Transparency effects" in Settings, under Personalisation > Colours.
- **Downside:** Those surfaces go solid. Purely a look.
- **Risk:** Low
- **Restart:** none
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Themes\Personalize\EnableTransparency

### `performance.instant-menus`: Remove the menu-open delay

- **What it does:** Sets the menu-show delay to 0 ms. The default is 400 ms.
- **Why you might want it:** Menus and Start feel instant instead of laggy.
- **Downside:** Menus can feel twitchy if you sweep the mouse across them fast.
- **Risk:** Low
- **Restart:** SignOut
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Control Panel\Desktop\MenuShowDelay

### `performance.no-startup-delay`: Remove the startup-app delay

- **What it does:** Removes the roughly 10-second wait Windows adds before it launches your startup apps after you sign in.
- **Why you might want it:** Your startup apps are ready sooner. Windows adds that wait to make the desktop paint faster first, which you often do not need on a fast disk.
- **Downside:** The desktop may feel a bit busier in the first few seconds after login.
- **Risk:** Low
- **Restart:** none
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Explorer\Serialize\StartupDelayInMSec

## Privacy

### `privacy.advertising-id`: Disable the advertising ID

- **What it does:** Turns off the per-user advertising identifier that apps use to build an ad profile of you.
- **Why you might want it:** Cuts down cross-app ad tracking. Same as the "Let apps show me personalised ads" switch in Settings, under Privacy & security > General.
- **Downside:** The ads you see get less targeted. Nothing else changes.
- **Risk:** Low · **Recommended**
- **Restart:** none
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** AdvertisingInfo\Enabled (<https://learn.microsoft.com/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#7-advertising-id>)

### `privacy.disable-lockscreen-facts`: Hide lock screen tips and fun facts

- **What it does:** Stops Windows Spotlight overlaying tips, ads and trivia on the lock screen.
- **Why you might want it:** A clean lock screen image with no text or promotions on top of it.
- **Downside:** You lose the like and dislike buttons for the Spotlight picture, and the daily fact.
- **Risk:** Low
- **Restart:** none
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** ContentDeliveryManager RotatingLockScreenOverlayEnabled / SubscribedContent-338387Enabled

### `privacy.disable-suggested-content`: Stop suggested content in Settings and Start

- **What it does:** Turns off the Content Delivery Manager feeds that push app promotions, suggested content and tips into Settings, Start and notifications.
- **Why you might want it:** A cleaner OS with less advertising baked in. None of this content does anything useful.
- **Downside:** You no longer see Microsoft's suggested apps or feature tips.
- **Risk:** Low · **Recommended**
- **Restart:** none
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** ContentDeliveryManager SubscribedContent-* and SystemPaneSuggestionsEnabled, the values Settings flips

### `privacy.reduce-telemetry`: Set diagnostic data to the minimum for this edition

- **What it does:** Drops the system diagnostic-data level to the lowest Windows allows on this edition. That is Security on Enterprise and Education, and "Required only" everywhere else.
- **Why you might want it:** Sends less data to Microsoft. Matches "Required diagnostic data" in Settings, under Privacy > Diagnostics & feedback.
- **Downside:** Home and Pro cannot go below Required. Some Insider and feedback features expect more.
- **Risk:** Low · **Recommended**
- **Restart:** none
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Policy DataCollection\AllowTelemetry (0 Security, 1 Required) (<https://learn.microsoft.com/windows/privacy/configure-windows-diagnostic-data-in-your-organization>)

### `privacy.tailored-experiences`: Turn off tailored experiences

- **What it does:** Stops Windows using your diagnostic data to personalise tips, ads and recommendations.
- **Why you might want it:** Less profiling of how you use your PC. Matches the "Tailored experiences" switch in Settings, under Privacy & security > Diagnostics & feedback.
- **Downside:** Suggestions in Settings and on the lock screen become generic.
- **Risk:** Low · **Recommended**
- **Restart:** none
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Privacy\TailoredExperiencesWithDiagnosticDataEnabled (<https://learn.microsoft.com/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services>)

## Search

### `search.disable-web-results`: Remove web results from Start search

- **What it does:** Stops the Start menu search box sending your queries to Bing and showing web results.
- **Why you might want it:** Faster search that stays local, and your keystrokes in Start no longer go to a search engine.
- **Downside:** No inline web answers from the Start search box. Open a browser instead.
- **Risk:** Low · **Recommended**
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Search\BingSearchEnabled = 0 (per-user)

## Security (advanced)

### `security.disable-defender-realtime`: Turn off Microsoft Defender real-time protection (policy)

- **What it does:** Writes the Group Policy values that disable Microsoft Defender Antivirus and its real-time monitoring. Meant for machines running a different, active antivirus.
- **Why you might want it:** You installed a third-party AV and want Defender fully out of the way, or you are doing malware analysis in an isolated VM.
- **Downside:** If nothing else is protecting the machine, it is now open to file-based malware. On Windows 10 and 11 with Tamper Protection on (the default) these values are ignored until you turn Tamper Protection off by hand in Windows Security. PowerX will not touch that setting for you.
- **Risk:** SecurityTradeoff
- **Restart:** Reboot
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Policy Windows Defender\DisableAntiSpyware; Real-Time Protection\DisableRealtimeMonitoring (<https://learn.microsoft.com/microsoft-365/security/defender-endpoint/microsoft-defender-antivirus-windows>)

### `security.disable-firewall`: Turn off the Windows Firewall (all profiles)

- **What it does:** Sets the Domain, Private and Public firewall profiles to off in the Windows Firewall service config.
- **Why you might want it:** Chasing down a blocked app or game server, or you run a separate hardware or third-party firewall. Turning it back on is one click.
- **Downside:** Every listening service on this PC is now reachable from the local network. Do not do this on public Wi-Fi or any network you do not trust.
- **Risk:** SecurityTradeoff
- **Restart:** none
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Services\SharedAccess\...\FirewallPolicy\{Standard,Public,Domain}Profile\EnableFirewall (<https://learn.microsoft.com/windows/security/operating-system-security/network-security/windows-firewall/>)

### `security.disable-smartscreen`: Turn off SmartScreen (apps and files, Edge, Store)

- **What it does:** Uses policy to turn off the Microsoft Defender SmartScreen reputation check for downloaded programs, for Edge and for Store apps.
- **Why you might want it:** Stops the "Windows protected your PC" prompt and the reputation lookup on every new executable. Some developers and privacy-focused users would rather rely on their own judgement plus Defender's on-access scan.
- **Downside:** You lose an early warning against malware and phishing sites that are new. Only sensible if you are careful about what you run and keep another layer, such as Defender real-time or a DNS filter.
- **Risk:** SecurityTradeoff
- **Restart:** SignOut
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Policy System\EnableSmartScreen = 0; Explorer\SmartScreenEnabled = Off (<https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/>)

### `security.disable-uac`: Turn off User Account Control (UAC)

- **What it does:** Sets EnableLUA to 0, which turns UAC off completely. No consent or credential prompts, and every process for an admin account runs with full admin rights.
- **Why you might want it:** A single-user machine where the prompts get in the way, or a legacy line-of-business app that misbehaves under UAC virtualization.
- **Downside:** A big drop in security. Malware that reaches your account runs elevated with no prompt, and file and registry virtualization stop. It also breaks most Store and packaged apps, including parts of Windows, because they need UAC on. Needs a reboot.
- **Risk:** SecurityTradeoff
- **Restart:** Reboot
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Policies\System\EnableLUA (1 = on, 0 = off) (<https://learn.microsoft.com/windows/security/application-security/application-control/user-account-control/settings-and-configuration>)

## Start

### `start.disable-recommendations`: Reduce Start menu recommendations

- **What it does:** Stops the Start menu showing tips, app promotions and recommended website shortcuts.
- **Why you might want it:** A quieter Start menu with less advertising in it.
- **Downside:** The recommended area no longer shows suggested content. Your recent files still appear there.
- **Risk:** Low · **Recommended**
- **Restart:** none
- **Privilege:** User
- **Compatibility:** 22621 ≤ build
- **Source:** Start_IrisRecommendations under Explorer\Advanced

## Taskbar

### `desktop.show-seconds-in-clock`: Show seconds in the taskbar clock

- **What it does:** Adds a seconds field to the taskbar clock.
- **Why you might want it:** You want the time to the second at a glance.
- **Downside:** A tiny bit of extra work redrawing the clock every second. You will not notice it on modern hardware.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** 22621 ≤ build
- **Source:** Explorer Advanced\ShowSecondsInSystemClock

### `taskbar.align-left`: Align the taskbar to the left

- **What it does:** Moves the taskbar icons and the Start button to the left, like Windows 10.
- **Why you might want it:** Start stays in the same corner every time, which suits muscle memory from older Windows.
- **Downside:** None.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** 22000 ≤ build
- **Source:** Explorer Advanced\TaskbarAl: 0 = left, 1 = centre

### `taskbar.collapse-search`: Shrink taskbar search to an icon

- **What it does:** Swaps the wide taskbar search box for a small icon. (You can also hide search entirely; this sets it to the icon.)
- **Why you might want it:** Gets back a big chunk of taskbar width.
- **Downside:** One extra click before you can start typing a search.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Search\SearchboxTaskbarMode: 0 hidden, 1 icon, 2 box

### `taskbar.hide-chat`: Hide the Chat / Teams button

- **What it does:** Removes the Microsoft Teams (Chat) button from the taskbar.
- **Why you might want it:** You use a different chat app, or none, and want the space back.
- **Downside:** No one-click consumer Teams chat.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** 22000 ≤ build
- **Source:** Explorer Advanced\TaskbarMn

### `taskbar.hide-task-view`: Hide the Task View button

- **What it does:** Removes the Task View button from the taskbar. Win+Tab still works.
- **Why you might want it:** Taskbar space, if you use the keyboard shortcut or do not use virtual desktops.
- **Downside:** No one-click access to Task View or virtual desktops.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** all supported builds
- **Source:** Explorer Advanced\ShowTaskViewButton

### `taskbar.hide-widgets`: Hide the Widgets button

- **What it does:** Removes the Widgets (weather and news) button from the taskbar.
- **Why you might want it:** Frees up taskbar space and stops the panel opening when you brush past it. A little less background activity if you never use it.
- **Downside:** The Widgets board is no longer one click away. Win+W still opens it unless you also remove the package.
- **Risk:** Low
- **Restart:** Explorer
- **Privilege:** User
- **Compatibility:** 22000 ≤ build
- **Source:** Explorer Advanced\TaskbarDa: 0 = hidden, 1 = shown

## Windows Update

### `update.defer-quality-updates`: Delay monthly quality updates by 30 days

- **What it does:** Holds each monthly cumulative (security) update for 30 days after release before offering it to you.
- **Why you might want it:** Gives a bad patch time to be pulled or fixed before it reaches your machine.
- **Downside:** You run up to 30 days behind on security fixes. Not a good idea on a machine that faces the internet with nothing else protecting it.
- **Risk:** SecurityTradeoff
- **Restart:** none
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Group Policy "Select when Quality Updates are received": DeferQualityUpdates / DeferQualityUpdatesPeriodInDays

### `update.exclude-drivers`: Don't get drivers from Windows Update

- **What it does:** Stops Windows Update handing you driver updates alongside quality updates. You still install drivers from the vendor or Device Manager.
- **Why you might want it:** Keeps a Windows-pushed driver from overwriting a working vendor driver. This bites people most often with GPUs and audio.
- **Downside:** Keeping drivers current is now on you.
- **Risk:** Moderate
- **Restart:** none
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Group Policy "Do not include drivers with Windows Updates": ExcludeWUDriversInQualityUpdate

### `update.no-auto-restart`: Never auto-restart while I'm signed in

- **What it does:** Windows will not reboot on its own to finish an update while someone is logged on. It waits for you to restart.
- **Why you might want it:** No more losing work to a surprise reboot in the middle of the night or mid-session.
- **Downside:** Updates that need a restart sit pending until you reboot, so you have to remember to do it.
- **Risk:** Low · **Recommended**
- **Restart:** none
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Group Policy "No auto-restart with logged on users": AU\NoAutoRebootWithLoggedOnUsers

### `update.pin-feature-version`: Pause feature updates (pin to the current version)

- **What it does:** Pins Windows to the feature-update version you are on now. New feature updates (say 24H2 to 25H2) are held back. Monthly security and quality updates keep installing.
- **Why you might want it:** Stay patched but move to a big version on your own schedule. Turning this off lets the next feature update through.
- **Downside:** No new Windows features until you remove the pin. Microsoft eventually forces the update when your version nears end of servicing.
- **Risk:** Advanced
- **Restart:** none
- **Privilege:** Administrator
- **Compatibility:** all supported builds
- **Source:** Group Policy "Select the target Feature Update version": TargetReleaseVersion / TargetReleaseVersionInfo (<https://learn.microsoft.com/windows/deployment/update/waas-configure-wufb>)

