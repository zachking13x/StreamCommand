> **SUPERSEDED by `../Listing-1.0.32/STORE_LISTING_1.0.32.md`** — kept for the 1.0.31 history and its image rules.

# Stream Command 1.0.31 — listing-only resubmit (after the 10/16 10.1.4.3 report)

**Package:** keep the `StreamCommand_1.0.31.0.msix` already in the submission (10.1.2.10 passed;
only the listing failed). No new package needed.

> **RULE FOR THIS LISTING: zero pricing language, anywhere, ever.** Policy 10.1.4.3 has now failed
> three times: "Try everything free for 7 days" (1.0.29), "the free streaming software" about OBS
> (1.0.30), and on 10/16 the report "the description claims that your paid product is free".
> Cert reads EVERY customer-visible field and image, not just the description, so this file rewrites
> every field to describe features only. The Store shows the price and the trial badge itself.
>
> Removed from the 1.0.31 copy: the "ONE PURCHASE, NOT A SUBSCRIPTION" section ("one-time purchase",
> "no monthly fee", "no tiers", "nothing is held back for a higher plan") and the What's-new bullet
> "One-time purchase replaces subscriptions". Images removed: see section 7 — the **2:3 poster art
> (`StoreLogo_720x1080.png`) literally says "FREE + PRO UPGRADE AVAILABLE"** and was never replaced
> after the pricing switch. That is the most likely source of "claims your paid product is free".
>
> Never re-add: free, trial, price, $, cost, paid, purchase, subscription (as a pricing word),
> upgrade, unlock, premium, Pro, "one-time", "no monthly fee", "1 rule".

Every block below in a ```` ``` ```` fence is the exact text to paste.

---

## 1. Pricing and availability — do NOT change

Leave as configured: base price $7.99, Free trial Time-limited / 7 days, add-ons on Stop acquisition.
The Store renders the price and trial badge from these settings; that is allowed. Only the listing text
and images may not talk about it.

## 2. Product name

```
Stream Command
```

## 3. Short description (Store limit 1,000; kept short)

```
Run your whole stream without leaving your game: scenes, audio, chat and Go Live in one window.
```

## 4. Description

```
Stream Command is a control panel app for people who live stream games on Twitch using OBS
Studio. It connects to OBS on your PC and to your Twitch account, and lets you start and stop
your broadcast, switch what viewers see, mute your microphone, and read and reply to your
Twitch chat, all from one window or with keyboard shortcuts while you keep playing your game.

No more alt-tabbing out of your game and breaking the moment: the controls you actually reach
for mid-stream are in one place.


CONTROL EVERYTHING WITHOUT ALT-TABBING

Global shortcuts work while your game has focus: switch OBS scenes, mute your mic, and go live
or end your stream straight from the keyboard. Going live or ending a stream takes two presses,
so a stray key can never start a broadcast. You can change the keys, or turn shortcuts off
entirely, in Settings.


WHAT YOU CAN DO

Go live without leaving your game
Start and stop your stream, switch scenes, and see at a glance whether OBS is connected.

Opens OBS for you
If OBS is not running, Stream Command starts it minimised to the system tray and connects
automatically, so there is one app to open and nothing covering your game.

See your stream inside the app
A live preview of your own broadcast, right next to your controls, so you never alt-tab just
to check whether the right scene is actually on air.

Read and reply to chat
Your Twitch chat in a side panel with alerts for follows, subs and raids. Type a reply and
send it without switching windows.

Thank every follower automatically
Automation rules that respond to follows, subs, gift subs and raids, so your community gets
acknowledged even when you are mid-fight.

On-stream alerts
Show follower and subscriber alerts on your broadcast as they happen.

A pre-stream checklist that actually gets used
Run your own setup list before going live, so you stop discovering a muted mic twenty minutes
in.

Plan your schedule
Keep upcoming streams in one place and see what is next.

Stream summary when you are done
Session length and how much your automation handled, every time you end a stream.

Make it yours
Pick an accent colour and the whole app restyles around it.


WHAT YOU NEED

- Windows 10 version 2004 or newer, or Windows 11
- OBS Studio 28 or newer with its WebSocket server enabled
  (in OBS: Tools, then WebSocket Server Settings; it is built into OBS)
- A Twitch account to connect chat and alerts

Live preview also needs OBS Virtual Camera started, which is a separate output inside OBS
(Tools, then Start Virtual Camera).


YOUR DATA STAYS ON YOUR PC

Stream Command talks to Twitch and to your local copy of OBS, and nothing else. There is no
account to create and no server of ours in the middle. Your Twitch token and OBS password are
encrypted and stored only on your own machine.
```

## 5. What's new in this version

```
A new look, keyboard control, and Stream Command now opens OBS for you.

- Opens OBS for you: if OBS is not running, Stream Command starts it minimised to the system
  tray and connects automatically. One app to open, and nothing covering your game.
- Global shortcuts: switch OBS scenes (Ctrl+Shift+F1-F8), mute your mic (Ctrl+Shift+F10) and
  go live or end your stream (Ctrl+Shift+F11, press twice) while your game keeps focus.
  Change the modifier keys in Settings if another app already uses them.
- A brand-new look: a sleeker, easier-to-read design built to glance at mid-game. Every accent
  colour now restyles the whole app.
- Fits high-scaling displays: every window now sizes itself to your screen, and the sidebar
  scrolls when it needs to.
- Stays in sync with OBS: the current scene and your mic mute state always match OBS.
- OBS now connects as soon as the app opens, and the connect button reports what happened when
  it cannot reach OBS.
- Opening the app again brings the existing window to the front instead of starting a second
  copy.
- Chat and connection reliability: send messages from the app and see your own messages appear,
  OBS and Twitch reconnect automatically if a connection drops, and live preview recovers
  instead of locking up if the OBS Virtual Camera stops.
- Fixes: checklist progress ring, scene button highlights, and the app now fully exits when you
  close it.
```

## 6. Product features (Partner Center "Product features", one per box, max 200 chars each)

Replace whatever is in these boxes today — older submissions may still hold tier/plan bullets.

```
Start and stop your stream and switch OBS scenes from one window
Global keyboard shortcuts for scenes, mic mute and Go Live that work while your game has focus
Opens OBS for you, minimised to the system tray, and connects automatically
Live preview of your own broadcast next to your controls
Read and reply to your Twitch chat without switching windows
Automation rules that thank followers, subs, gift subs and raids
On-stream follower and subscriber alerts
Pre-stream checklist so you never go live with a muted mic
Stream planner for your upcoming broadcasts
End-of-stream summary with session length and automation activity
Accent colours that restyle the whole app
Your Twitch token and OBS password stay encrypted on your own PC
```

## 7. Images

### Screenshots (upload in this order)

Folder: `C:\Users\Zachk\OneDrive\Desktop\StreamCommand_Store_Screenshots_UPLOAD` (already numbered).

| # | File | Caption |
|---|------|---------|
| 1 | `01_mockup-1-hero.png` | Control your whole stream without leaving your game. |
| 2 | `02_01-dashboard.png` | Your whole stream at a glance: session time, viewers, followers, scenes, events and live chat. |
| 3 | `03_mockup-2-in-game.png` | Switch scenes mid-match with a global shortcut. Your game never loses focus. |
| 4 | `04_02-live-control.png` | Go live, switch scenes and manage audio from one screen. |
| 5 | `05_05-automation.png` | Thank every follower and sub automatically while you play. |
| 6 | `06_04-pre-stream-checklist.png` | Never forget a mic check. Run your pre-stream checklist every time. |
| 7 | `07_06-chat-monitor.png` | Read and reply to your Twitch chat without switching windows. |
| 8 | `08_08-planner.png` | Plan your upcoming streams. |
| 9 | `09_10-quick-launch.png` | Your streaming sites and tools, one click away. |

OCR re-run 2026-10-16 (Windows OCR) over all 9: no free / trial / price / $ / Pro / upgrade / unlock.
The only matches are Twitch viewer terms on the app's own event feed ("Subscribed", "Subscribers",
"Re-subscription (any tier)") — those describe Twitch subs the streamer receives, not this app's
pricing, and they appeared in images that already passed earlier reviews. Kept.

**Must NOT be in Partner Center** (delete if present — uploading new images never removes old ones):
`03-pro-upgrade.png` (old 3-plan window with $ prices), `11-streamer-tools.png` ("Royalty-free"),
`12-settings.png` ("Free & built into OBS"), and the old `Screenshot1-6_*.png` set.

### Store logos / poster art

- **2:3 poster art (720x1080): replace `StoreLogo_720x1080.png` with `StoreAssets\StoreLogo_720x1080_clean.png`.**
  The original says "FREE + PRO UPGRADE AVAILABLE" and "Automation rules (Pro)". The clean copy has
  the badge and "(Pro)" painted out; OCR confirms no pricing words remain. (It is still the old purple
  artwork, and some feature-row icons render as empty boxes — cosmetic, not a cert issue; a navy/teal
  redraw is a separate job. Deleting the poster art entirely is also fine: it is optional for a desktop app.)
- 1:1 box art / app tile (`AppTile_300x300.png`): OCR clean.

### Trailers

If any trailer or trailer thumbnail is uploaded from the old listing, delete it unless you have
checked it frame by frame — cert will read it like a screenshot.

## 8. Search terms (7 max, 40 chars each)

```
obs controller
obs hotkeys
twitch chat
stream controller
streaming dashboard
scene switcher
twitch automation
```

("stream deck" swapped for "stream controller": Stream Deck is Elgato's trademark and keyword
use of another company's product name is its own cert risk.)

## 9. Other text fields — check each one in Partner Center

These fields are not in this repo, so their current contents are unknown. Open each and remove any
pricing wording; leave blank if unsure:
- **Additional system requirements** — suggested text below.
- **Copyright and trademark info** — owner name + year only.
- **Additional license terms** — blank unless you have real terms.
- **Developed by** — your name only.
- **Short title / Sort title / Voice title** — "Stream Command" or blank.
- **Website / Support contact / Privacy policy** — URLs only; make sure the linked pages don't
  advertise "free" either if cert follows them.

```
OBS Studio 28 or newer with the WebSocket server enabled. A Twitch account for chat and alerts.
```

## 10. Notes for certification (private to the reviewer, not shown in the Store)

```
RESUBMISSION (1.0.31, listing-only update) - fix for the 10/16 report (10.1.4.3)
Every customer-visible field has been rewritten to describe features only. The description,
short description, What's new, product features, search terms and screenshot captions no
longer contain any statement about plans or editions. The 2:3 poster art, which
still showed an outdated plans badge from an earlier version of the app, has
been replaced, and outdated screenshots of a retired plans screen have been deleted. The
package is unchanged from the previous submission; 10.1.2.10 passed in that review.

Stream Command is a native WPF (.NET 8) desktop application packaged as MSIX.

CAPABILITIES
runFullTrust is required for any packaged desktop (Desktop Bridge) WPF app. It is not used
for elevated or privileged system access.

internetClient is used only to reach:
  - Twitch IRC over WebSocket (wss://irc-ws.chat.twitch.tv:443), to show the user's own
    Twitch chat and send messages they type.
  - The Twitch Helix API, for the signed-in user's own channel stats.
  - OBS WebSocket on loopback only (ws://127.0.0.1:4455) to control the user's local OBS
    Studio install. Non-loopback hosts are rejected in code.

No data is sent to any server operated by the developer. There is no developer account
system. Twitch tokens and the OBS password are encrypted with DPAPI and stored only in
%LocalAppData%\StreamCommand\settings.json.

GLOBAL SHORTCUTS
Optional global keyboard shortcuts use the standard Win32 RegisterHotKey API
(Ctrl+Shift+F1-F8, F10 and F11 by default). The app does not install keyboard hooks and does
not record keystrokes. Shortcuts can be turned off in Settings, Global shortcuts.

LICENSING
The app has no in-app offers and no add-ons. Previously published add-ons are set to stop
acquisition; licences already owned are still honoured in code.

TESTING NOTES
The app needs the tester's own Twitch account and a local OBS install to exercise the live
features. Both connection steps can be skipped:
  - The Setup Wizard has a "Set up later" link on every step.
  - With neither OBS nor Twitch connected, the app opens normally and shows designed empty
    states ("Connect OBS to control your scenes", "Connect Twitch to see live chat") with a
    link to the in-app Setup Guide.
The app is therefore fully launchable and navigable with no external accounts or software
configured.
```
