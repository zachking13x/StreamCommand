# Stream Command 1.0.31 — Partner Center submission

> **SUPERSEDED 2026-10-16 by `../Listing-1.0.31/STORE_LISTING_1.0.31.md`.** This copy still contains pricing
> claims ("one-time purchase", "no monthly fee") that fail 10.1.4.3. Do not paste from here.

**Package to upload:** `StorePackageBuild\StreamCommand_1.0.31.0.msix` (Release, signed).
Never upload `StreamCommand_1.0.31.0_DEVPRO.msix` — it contains the dev Pro unlock.

> 1.0.28 was submitted on 2026-10-12. Partner Center won't take a new package until that
> submission finishes or you cancel it — 1.0.30 includes everything in 1.0.28 and 1.0.29.

> **PRICING CHANGE IN THIS SUBMISSION.** 1.0.30 switches from "free app + three subscription
> add-ons" to a **paid app, $7.99 one-time, with a 7-day Store free trial and no add-ons.**
> Do the add-on stop-sell and the price change in the same release, so the app is never both
> paid and selling subscriptions.

---

## 1. Pricing and availability

| Field | Value |
|---|---|
| Base price | **$7.99 USD** (Store maps equivalent tiers per market) |
| Free trial | **7 days** |
| Markets | All markets |
| Release | As soon as it passes certification |
| Visibility | Public |
| Audience | Everyone |
| Add-ons | **None** — set `pro_monthly`, `pro_annual`, `pro_lifetime` to *Stop acquisition* |

CONFIRMED 2026-10-13: Free trial is set to **Time-limited / 7 days** and base price is $7.99 across
the Default and 240-market groups. The trial is real — it just must not be advertised in the listing text.

Licences already bought on the three retired add-ons are still honoured in code
(`EntitlementService.LegacyProductIds`), so no existing buyer loses access.

---

## 2. App name

```
Stream Command
```

## 3. Short description (keep under 100 characters)

```
Run your whole stream without leaving your game — scenes, audio and Go Live in one window.
```

## 4. Description (max 10,000 characters)

> **CERT REJECTION 2026-10-13 — policy 10.1.4.3 (App Quality, Description):** *"Claims made about
> pricing in the description must be true and clearly understandable. The description claims that
> your paid product is free."*
>
> The trial is genuinely configured (Pricing and availability → Free trial → **Time-limited, 7 days**,
> base price $7.99), so the old line **"Try everything free for 7 days."** was TRUE — it was rejected
> for being *unclear*, not false. Read cold next to "$7.99", a reviewer sees a paid product described
> as free.
>
> Fix: say nothing about price or trial in the description. The Store already shows the price and a
> **Free trial** badge on the listing automatically, so buyers still see the trial — removing the
> sentence costs nothing and removes the only thing cert objected to. **Don't re-add "free", "trial"
> or a price figure to this description**; arguing the wording risks another multi-day rejection
> cycle for zero gain.
>
> **Not even about something else:** 1.0.30 bounced again because the description called OBS "the free
> streaming software". Cert reads "free" anywhere in a paid listing as a pricing claim.

> **CERT REJECTION 2026-10-14 — "description is not clear and does not tell you what the app does."**
> The features were all listed, but the opening assumed the reader streams ("your stream",
> "scenes", "OBS"). Reviewers aren't streamers and read the top lines. Fix: the first paragraph
> now says in plain words WHAT the app is and WHO it's for, before any streamer jargon.
> Keep that plain-English opening first in any future rewrite.

```
Stream Command is a control panel app for people who live stream games on Twitch using OBS
Studio. It connects to OBS on your PC and to your Twitch account,
and lets you start and stop your broadcast, switch what viewers see, mute your microphone,
and read and reply to your Twitch chat, all from one window or with keyboard shortcuts while
you keep playing your game.

No more alt-tabbing out of your game and breaking the moment: the controls you actually
reach for mid-stream are in one place.


CONTROL EVERYTHING WITHOUT ALT-TABBING

Global shortcuts work while your game has focus: switch OBS scenes, mute your mic, and go
live or end your stream straight from the keyboard. Going live or ending a stream takes two
presses, so a stray key can never start a broadcast. You can change the keys, or turn
shortcuts off entirely, in Settings.


WHAT YOU CAN DO

Go live without leaving your game
Start and stop your stream, switch scenes, and see at a glance whether OBS is connected.

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
Run your own setup list before going live, so you stop discovering a muted mic twenty
minutes in.

Plan your schedule
Keep upcoming streams in one place and see what is next.

Stream summary when you are done
Session length and how much your automation handled, every time you end a stream.

Make it yours
Pick an accent colour and the whole app restyles around it.


ONE PURCHASE, NOT A SUBSCRIPTION

Stream Command is a one-time purchase. There is no monthly fee, no tiers, and nothing is
held back for a higher plan — every feature listed above is included.


WHAT YOU NEED

- Windows 10 version 2004 or newer, or Windows 11
- OBS Studio 28 or newer with its WebSocket server enabled
  (in OBS: Tools, then WebSocket Server Settings — it is built into OBS)
- A Twitch account to connect chat and alerts

Live preview also needs OBS Virtual Camera started, which is a separate output inside OBS
(Tools, then Start Virtual Camera).


YOUR DATA STAYS ON YOUR PC

Stream Command talks to Twitch and to your local copy of OBS, and nothing else. There is no
account to create and no server of ours in the middle. Your Twitch token and OBS password
are encrypted and stored only on your own machine.
```

## 5. What's new in this version (max 1,500 characters)

> Same 10.1.4.3 rule applies here — cert scans the WHOLE English listing, not just the
> description. The old text repeated "Try everything free for 7 days, then a single $7.99
> unlock", so fixing only the description would have been rejected again.

```
A new look, keyboard control, and Stream Command now opens OBS for you.

- Opens OBS for you: if OBS is not running, Stream Command starts it minimised to the system
  tray and connects automatically. One app to open, and nothing covering your game.

- One-time purchase replaces subscriptions. No monthly fee, no tiers, and every feature is
  included.
- Global shortcuts: switch OBS scenes (Ctrl+Shift+F1-F8), mute your mic (Ctrl+Shift+F10) and
  go live or end your stream (Ctrl+Shift+F11, press twice) while your game keeps focus.
  Change the modifier keys in Settings if another app already uses them.
- A brand-new look: a sleeker, easier-to-read design built to glance at mid-game. Every
  accent colour now restyles the whole app.
- Stays in sync with OBS: the current scene and your mic mute state always match OBS.
- OBS now connects as soon as the app opens, instead of waiting until you opened Live
  Control, and the connect button reports what happened when it cannot reach OBS.
- Opening the app again brings the existing window to the front instead of starting a
  second copy.
- Chat and connection reliability: send messages from the app and see your own messages
  appear, OBS and Twitch reconnect automatically if a connection drops, and live preview
  recovers instead of freezing if the OBS Virtual Camera stops.
- Fixes: checklist progress ring, scene button highlights, and the app now fully exits when
  you close it.
```

## 6. Screenshots (1920x1080 PNG, upload in this order — Store allows 10)

| # | File | Caption (optional, max 200 chars) |
|---|------|-----------------------------------|
| 1 | `mockup-1-hero.png` | Control your whole stream without leaving your game. |
| 2 | `01-dashboard.png` | Your whole stream at a glance: session time, viewers, followers, scenes, events and live chat. |
| 3 | `mockup-2-in-game.png` | Switch scenes mid-match with a global shortcut. Your game never loses focus. |
| 4 | `02-live-control.png` | Go live, switch scenes and manage audio from one screen. |
| 5 | `05-automation.png` | Thank every follower and sub automatically while you play. |
| 6 | `04-pre-stream-checklist.png` | Never forget a mic check. Run your pre-stream checklist every time. |
| 7 | `06-chat-monitor.png` | Read and reply to your Twitch chat without switching windows. |
| 8 | `08-planner.png` | Plan your upcoming streams. |
| 9 | `10-quick-launch.png` | Your streaming sites and tools, one click away. |

> **Review 10/16 rejected 10.1.4.3 on a SCREENSHOT, not the text:** Partner Center still held
> `03-pro-upgrade.png` from an older submission ("Free includes 1 rule" + $6.99/$49.99/$89.99
> plans that no longer exist). Cert reads every image too. **DELETE it from the listing's
> screenshot list in Partner Center** — uploading the new set does not remove old images.
>
> OCR scan (Windows OCR) of every PNG here, 2026-10-16: also dropped `12-settings.png`
> ("Free & built into OBS 28+") and `11-streamer-tools.png` ("Royalty-free music library") —
> both contain the word "free" in the app UI and would bounce the same way. 9 screenshots is fine.

Never upload: `03-pro-upgrade.png`, `11-streamer-tools.png`, `12-settings.png`.
Spare captures (OCR-clean): `07-analytics.png` (mostly an empty "coming soon" page — weak), `09-growth.png`.
All screenshots use a demo profile ("novaplays") with sample data; the game scene is original
artwork.

## 7. Search terms (up to 7, max 40 chars each)

```
obs controller
obs hotkeys
twitch chat
stream deck
streaming dashboard
scene switcher
twitch automation
```

---

## 8. Notes for certification

```
RESUBMISSION (1.0.31, listing update) - fix for the 10/16 report
10.1.4.3: the flagged image was an outdated screenshot of a retired upgrade screen ("Free
includes 1 rule" and three subscription plans that no longer exist). It has been removed from
the listing. The remaining screenshots show the current app and contain no pricing claims.

RESUBMISSION (1.0.31) - fixes for the 1.0.30 report
10.1.2.10: at 200% on 2560x1600 the main window (1280x780) was taller than the ~1280x752 work
area. Every window now shrinks to fit the work area when it opens, the sidebar shows a scrollbar
when its items do not fit, and the Setup Wizard and upgrade window scroll. Every page was checked
at 1280x752.
10.1.4.3: the description called OBS Studio "the free streaming software". That phrase is removed;
the description no longer contains the word "free" anywhere.

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
1.0.31 adds optional global keyboard shortcuts using the standard Win32 RegisterHotKey API
(Ctrl+Shift+F1-F8, F10 and F11 by default). The app does not install keyboard hooks and does
not record keystrokes. Shortcuts can be turned off in Settings, Global shortcuts.

PURCHASING
This version is a paid app with a 7-day Store free trial. There are no in-app purchases and
no subscription add-ons. Previously published add-ons are set to stop-selling, and licences
already owned are still honoured in code.

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
