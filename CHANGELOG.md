# Changelog

All notable changes to Broken Microphone. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed
- New Windows window in the same style as the Android app: a bottom tab bar with four pages (Microphone, Connect, Audio, Settings), large titles and glass cards over a soft colour background. No more single long page.
- The window frame matches the design: no white Windows title bar; the background reaches the top and the minimize / maximize / close buttons are drawn in the same glass style. Snapping, resizing from the edges, shadow and rounded corners still work. Dialogs use the same frame.
- The firewall permission now covers every network type (also "Public") but only for devices on your local network (`remoteip=localsubnet`). Before, it only covered private networks, so phones couldn't connect when Windows had marked the network as Public.

### Fixed
- A saved window position taller than the screen is moved back inside it, so the tab bar is never hidden behind the taskbar.
- The last line of text in dialogs is no longer cut off.

### Added
- The window checks Windows Firewall by itself (no admin rights needed to check). If the phone would be blocked, the Microphone page says so and **Allow** fixes it in one click (Windows asks for administrator rights). It also removes block rules Windows may have added for the program.

## [1.2.1] - 2026-11-06

### Changed
- "Install a virtual microphone" no longer sends you to a website: Broken Microphone downloads VB-CABLE from VB-Audio's official server itself and only runs the setup if it carries VB-Audio's valid digital signature.

### Fixed
- The audio only plays on the speakers if you turn on "Hear myself on the speakers" (off by default), so you no longer hear yourself.
- If a virtual microphone is installed but a speaker is selected, the window says the audio isn't reaching Discord and offers "Send to the virtual microphone". Virtual microphones are labelled in the list.

## [1.2] - 2026-11-06

### Added
- Android app: scans the computer's QR code itself (camera, decoded on the phone with ZXing; nothing leaves the phone).
- Android app: language choice on first launch, and a Settings screen (language, computer, microphone, updates, about).
- Android app: update check. Reads the latest version number from GitHub at most twice a day (can be turned off) and offers the new APK from GitHub Releases.
- Phone page on Android: asks once whether to open the app, download the APK or continue in the browser; "don't ask again" is remembered on that phone.

### Changed
- Android app: new layout with an iOS-style tab bar and four pages: Microphone, Scan, Audio, Settings. No more long scrolling page. Manual address entry is in Settings.

## [1.1] - 2026-11-06

### Added
- Android app (APK, ~90 KB): keeps sending audio with the screen off, with a notification to mute or stop. Bluetooth headset microphones, noise suppression, echo cancellation and automatic gain. It remembers the computer's certificate on first connection and refuses to connect if it changes.
- The APK is attached to each release as `BrokenMicrophone.apk` and has a download page: https://herkanuzun.github.io/BrokenMicrophone/. The new **Android app** button in the window shows that page's QR code.
- On Android the phone page shows **Get it on GitHub** and **Open in app** (passes the address and key to the app; the app asks before connecting).
- App security: connects only to home-network addresses unless you confirm, ignores links you didn't approve, stores the key encrypted with Android Keystore, excludes it from backups, ignores touches while another window covers it, hides its preview in recent apps.
- The phone page now sends a strict Content-Security-Policy and related security headers.

## [1.0] - 2026-11-06

First release.

### Added
- Use a phone, tablet or the wireless headset connected to them as the computer's microphone over Wi-Fi. No app on the phone: scan the QR code and the page opens in the browser (iPhone, iPad, Android).
- Encrypted local connection (TLS) on one port; a random key in the QR code keeps other devices on the network out.
- If the browser refuses the WebSocket (some iOS versions do with a self-signed certificate), the page sends audio with HTTPS requests instead and remembers that for the device.
- Phone page: microphone picker (wireless headsets included), noise suppression, echo cancellation, automatic gain, mute, level meter, keeps the screen on, reconnects by itself.
- Computer window: QR code, connected devices, live level, output picker that selects a virtual microphone (VB-CABLE) automatically, optional listening on the speakers.
- Several phones can connect; the one that talks or that you click is used, audio is never mixed.
- 60 ms jitter buffer; if audio piles up, the oldest part is dropped so delay stays low.
- Setup adds a Windows Firewall rule for this app on private networks only; the portable version can add it from "Can't connect?".
- Turkish and English.

[1.1]: https://github.com/HErkanuzun/BrokenMicrophone/releases/tag/v1.1
[1.0]: https://github.com/HErkanuzun/BrokenMicrophone/releases/tag/v1.0
