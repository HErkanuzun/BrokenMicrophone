# Security Policy · Güvenlik Politikası

## Supported versions · Desteklenen sürümler

Only the [latest release](https://github.com/HErkanuzun/BrokenMicrophone/releases/latest) receives fixes.
Düzeltmeler yalnızca [son sürüme](https://github.com/HErkanuzun/BrokenMicrophone/releases/latest) gelir.

## Reporting a vulnerability · Güvenlik açığı bildirme

Please **do not** open a public issue for security problems. Report them privately on GitHub:
**Security → Report a vulnerability** ([link](https://github.com/HErkanuzun/BrokenMicrophone/security/advisories/new)).

Güvenlik sorunları için lütfen herkese açık bir konu (issue) **açma**. GitHub'da gizli olarak bildir:
**Security → Report a vulnerability** ([bağlantı](https://github.com/HErkanuzun/BrokenMicrophone/security/advisories/new)).

## What the app does on your computer · Program bilgisayarında ne yapar

| | |
|---|---|
| Administrator rights · Yönetici izni | Never for the app itself. The setup asks once, to install into Program Files and add the firewall rule below. · Programın kendisi hiç istemez. Kurulum bir kez ister: Program Files'a kurmak ve aşağıdaki güvenlik duvarı izni için. |
| Network · Ağ | Listens on one TCP port (47820 by default) on your local network. Serves the phone page over HTTPS and receives audio over a WebSocket on the same port; plain HTTP is only redirected to HTTPS. Connections need the random key from the QR code. Nothing is sent to the internet; the only outgoing request is "Check for updates" in About, and only when you press it (GitHub API). · Yerel ağda tek bir TCP portunu (varsayılan 47820) dinler: telefon sayfasını HTTPS ile sunar, sesi aynı porttan WebSocket ile alır. Bağlanmak için QR koddaki rastgele anahtar gerekir. İnternete hiçbir şey gitmez; tek dış istek Hakkında'daki "Güncellemeleri denetle"dir, o da sen basınca. |
| Certificate · Sertifika | A self-signed certificate made on first start, stored in `%AppData%\BrokenMicrophone\cert.pfx` and used only by this app's local server. It is **not** added to Windows' trusted certificates. · İlk açılışta üretilen kendinden imzalı sertifika; sadece bu programın yerel sunucusu kullanır, Windows'un güvenilen sertifikalarına **eklenmez**. |
| Firewall · Güvenlik duvarı | The setup adds an inbound rule named "Broken Microphone" for this exe only, on private and domain networks only; uninstalling removes it. · Kurulum sadece bu exe için, sadece özel ve etki alanı ağlarında gelen bağlantı izni ekler; kaldırınca silinir. |
| Audio · Ses | Received audio is played into the output you pick (normally a virtual microphone's input). It is never recorded or saved. · Gelen ses seçtiğin çıkışa çalınır; asla kaydedilmez. |
| Files · Dosyalar | `%AppData%\BrokenMicrophone\settings.txt` (language, window position, port, connection key, output, network) and `cert.pfx`. · Ayarlar ve sertifika. |
| Android app · Android uygulaması | Optional. Permissions: microphone, notifications, network, Bluetooth (to use a headset microphone), keep Wi-Fi awake. Talks only to the computer you add, over TLS, and pins that computer's certificate on first connection (a changed certificate is refused until you confirm). Home-network addresses only unless you confirm; links opened by websites always ask first; the key is stored encrypted (Android Keystore) and excluded from backups; touches are ignored while another window covers the app. No analytics, no ads, no internet requests. Downloaded from GitHub Releases; signed with the project's own key (certificate SHA-256 B7:4F:9A:B8:C1:88:71:79:DF:E2:0B:B0:CF:3B:9E:A4:AA:20:D8:F5:BB:90:39:44:12:E6:BD:DE:87:19:B3:27); the APK's SHA-256 is in SHA256SUMS.txt. · İsteğe bağlı. İzinler: mikrofon, bildirim, ağ, Bluetooth (kulaklık mikrofonu için), Wi-Fi'ı uyanık tutma. Sadece eklediğin bilgisayarla TLS üzerinden konuşur; ilk bağlantıda sertifikasını hatırlar, değişirse sen onaylamadan bağlanmaz. Onay vermedikçe sadece ev ağındaki adreslere bağlanır; web sitelerinden gelen bağlantılar her zaman sorar; anahtar Android Keystore ile şifreli saklanır ve yedeklere girmez; üstte başka pencere varken dokunuşlar yok sayılır. GitHub Releases'tan iner. Analiz, reklam ya da internet isteği yok. |
| Registry · Kayıt defteri | Writes nothing except the installer's own uninstall entry. · Kurulum paketinin kaldırma kaydı dışında hiçbir şey yazmaz. |

The connection key in the QR code is like a password for your microphone: don't share screenshots of the window. To change it, delete `settings.txt` while the app is closed.
QR koddaki anahtar mikrofonunun şifresi gibidir: pencerenin ekran görüntüsünü paylaşma. Değiştirmek için program kapalıyken `settings.txt`'yi sil.

Uninstalling from Windows "Apps" removes the program and the firewall rule, and asks whether to delete `%AppData%\BrokenMicrophone`.
Windows "Uygulamalar"dan kaldırınca program ve güvenlik duvarı izni silinir; ayar klasörünün de silinip silinmeyeceği sorulur.

## Verifying releases · Sürümleri doğrulama

Release files are built from this repository by GitHub Actions. Each release includes `SHA256SUMS.txt`:

```powershell
Get-FileHash .\BrokenMicrophone-1.0-Setup.exe -Algorithm SHA256
```

<a id="code-signing-policy"></a>
## Code signing policy · Kod imzalama politikası

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Ücretsiz kod imzalama hizmetini [SignPath.io](https://about.signpath.io) sağlar, sertifika [SignPath Foundation](https://signpath.org)'a aittir.

| Role · Görev | Members · Kişiler |
|---|---|
| Committers and reviewers · Geliştirenler ve inceleyenler | [HErkanuzun](https://github.com/HErkanuzun) |
| Approvers · İmzayı onaylayanlar | [HErkanuzun](https://github.com/HErkanuzun) |

Only files built by GitHub Actions from this repository are signed: the program and its setup in each release. Every signing request is approved by hand. Changes from people outside the team are reviewed before they are merged.

Sadece bu depodan GitHub Actions ile derlenen dosyalar imzalanır: her sürümdeki program ve kurulum paketi. Her imza isteği elle onaylanır. Ekip dışından gelen değişiklikler birleştirilmeden önce incelenir.

### Privacy · Gizlilik

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. Audio only travels from the phone you connect to this computer, on your local network.

Bu program, kullanıcı ya da kuran kişi özellikle istemedikçe hiçbir bilgiyi başka bir ağ sistemine göndermez. Ses sadece bağladığın telefondan bu bilgisayara, yerel ağ içinde gider.
