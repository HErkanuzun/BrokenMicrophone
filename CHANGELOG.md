# Changelog

All notable changes to Broken Microphone. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
