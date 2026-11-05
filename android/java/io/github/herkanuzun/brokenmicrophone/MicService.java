package io.github.herkanuzun.brokenmicrophone;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.graphics.drawable.Icon;
import android.media.AudioDeviceInfo;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.media.audiofx.AcousticEchoCanceler;
import android.media.audiofx.AudioEffect;
import android.media.audiofx.AutomaticGainControl;
import android.media.audiofx.NoiseSuppressor;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.os.SystemClock;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

/**
 * Ekran kapalıyken de çalışan mikrofon servisi: sesi alır, bilgisayara gönderir.
 * İki iş parçacığı: "audio" mikrofonu okuyup gönderir, "net" bağlantıyı kurar ve koparsa yeniden kurar.
 */
public class MicService extends Service {
    static final String ACT_START = "io.github.herkanuzun.brokenmicrophone.START";
    static final String ACT_STOP = "io.github.herkanuzun.brokenmicrophone.STOP";
    static final String ACT_MUTE = "io.github.herkanuzun.brokenmicrophone.MUTE";
    static final String ACT_REOPEN = "io.github.herkanuzun.brokenmicrophone.REOPEN";

    private AudioManager am;
    private PowerManager.WakeLock wake;
    private WifiManager.WifiLock wifi;

    private volatile int gen;               // her başlatmada artar; eski iş parçacıkları kendiliğinden biter
    private volatile WsClient ws;
    private volatile boolean stateDirty;    // bilgisayara durum (hız, mikrofon, sustur) gönderilmeli
    private volatile boolean reopen;        // mikrofon ayarı değişti, kaydedici yeniden açılmalı
    private String host = "", key = "";
    private int port;
    private int rate = 48000;
    private boolean bluetoothOn;
    private final List<AudioEffect> effects = new ArrayList<>();

    static void start(Context c, String host, int port, String key) {
        Intent i = new Intent(c, MicService.class).setAction(ACT_START)
                .putExtra("host", host).putExtra("port", port).putExtra("key", key);
        if (Build.VERSION.SDK_INT >= 26) c.startForegroundService(i);
        else c.startService(i);
    }

    static void send(Context c, String action) {
        if (!Hub.running) return;
        c.startService(new Intent(c, MicService.class).setAction(action));
    }

    @Override
    public void onCreate() {
        super.onCreate();
        L.init(this);
        am = (AudioManager) getSystemService(AUDIO_SERVICE);
    }

    @Override
    public IBinder onBind(Intent intent) { return null; }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String a = intent == null ? null : intent.getAction();
        if (ACT_START.equals(a)) {
            goForeground();
            String h = intent.getStringExtra("host"), k = intent.getStringExtra("key");
            int p = intent.getIntExtra("port", 0);
            if (h == null || k == null || p <= 0) { stopEverything(); return START_NOT_STICKY; }
            if (!Hub.running || Hub.state == Hub.ERROR || !h.equals(host) || p != port || !k.equals(key)) {
                halt();
                host = h; port = p; key = k;
                begin();
            }
        } else if (ACT_MUTE.equals(a)) {
            Hub.muted = !Hub.muted;
            stateDirty = true;
            Hub.fire();
            updateNotification();
        } else if (ACT_REOPEN.equals(a)) {
            reopen = true;
        } else if (ACT_STOP.equals(a) || !Hub.running) {
            stopEverything();
        }
        return START_NOT_STICKY;
    }

    @Override
    public void onDestroy() {
        halt();
        super.onDestroy();
    }

    private void stopEverything() {
        halt();
        if (Build.VERSION.SDK_INT >= 24) stopForeground(STOP_FOREGROUND_REMOVE);
        else stopForeground(true);
        stopSelf();
    }

    // ------------------------------------------------------------------ başlat / durdur
    private void begin() {
        final int g = ++gen;
        Hub.running = true;
        Hub.target = host + ":" + port;
        Hub.set(Hub.CONNECTING, "");
        lock(true);
        Thread net = new Thread(() -> netLoop(g), "net");
        Thread audio = new Thread(() -> audioLoop(g), "audio");
        audio.setPriority(Thread.MAX_PRIORITY);
        net.start();
        audio.start();
        updateNotification();
    }

    private void halt() {
        gen++;
        WsClient c = ws;
        ws = null;
        if (c != null) c.close();
        if (Hub.running) {
            Hub.running = false;
            Hub.peak = 0;
            Hub.set(Hub.IDLE, "");
        }
        lock(false);
    }

    private void lock(boolean on) {
        try {
            if (on) {
                if (wake == null) {
                    wake = ((PowerManager) getSystemService(POWER_SERVICE)).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "BrokenMicrophone:mic");
                    wake.setReferenceCounted(false);
                }
                wake.acquire();
                if (wifi == null) {
                    // Wi-Fi güç tasarrufu sesi takıntılı yapar
                    int mode = Build.VERSION.SDK_INT >= 29 ? WifiManager.WIFI_MODE_FULL_LOW_LATENCY : WifiManager.WIFI_MODE_FULL_HIGH_PERF;
                    wifi = ((WifiManager) getApplicationContext().getSystemService(WIFI_SERVICE)).createWifiLock(mode, "BrokenMicrophone:wifi");
                    wifi.setReferenceCounted(false);
                }
                wifi.acquire();
            } else {
                if (wake != null && wake.isHeld()) wake.release();
                if (wifi != null && wifi.isHeld()) wifi.release();
            }
        } catch (RuntimeException ignored) { }
    }

    // ------------------------------------------------------------------ ağ
    private void netLoop(int g) {
        Pins pins = new Pins(this, host + ":" + port);
        int wait = 500, fails = 0;
        while (gen == g) {
            WsClient c = null;
            try {
                c = WsClient.open(host, port, key, userAgent(), pins);
                if (gen != g) { c.close(); return; }
                ws = c;
                stateDirty = true;
                fails = 0;
                wait = 500;
                Hub.set(Hub.LIVE, "");
                updateNotification();
                c.pump();
            } catch (WsClient.BadKey e) {
                if (gen == g) Hub.set(Hub.ERROR, "badKey");
                wait = 5000;
            } catch (IOException e) {
                if (gen != g) return;
                if (pins.mismatch) {
                    // bilgisayarın sertifikası değişti: kullanıcı onaylamadan yeniden denenmez
                    Hub.set(Hub.ERROR, "cert");
                    updateNotification();
                    return;
                }
                fails++;
                Hub.set(Hub.CONNECTING, fails > 1 ? "unreachable" : "");
            } finally {
                if (c != null) {
                    if (ws == c) ws = null;
                    c.close();
                }
            }
            if (gen != g) return;
            updateNotification();
            SystemClock.sleep(wait);
            wait = Math.min(wait * 2, 4000);
        }
    }

    private static String userAgent() {
        return "BrokenMicrophone-Android/" + BuildInfo.VERSION + " (Android " + Build.VERSION.RELEASE + "; " + Build.MODEL + ")";
    }

    static String deviceName() {
        String m = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.trim();
        String model = Build.MODEL == null ? "Android" : Build.MODEL.trim();
        if (m.length() > 0 && !model.toLowerCase().startsWith(m.toLowerCase()))
            model = Character.toUpperCase(m.charAt(0)) + m.substring(1) + " " + model;
        return model;
    }

    private String stateJson() {
        return "{\"t\":\"hello\",\"rate\":" + rate + ",\"mic\":" + quote(Hub.mic) + ",\"muted\":" + Hub.muted
                + ",\"name\":" + quote(deviceName()) + "}";
    }

    static String quote(String s) {
        StringBuilder b = new StringBuilder("\"");
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '"' || c == '\\') b.append('\\').append(c);
            else if (c < 0x20) b.append(String.format("\\u%04x", (int) c));
            else b.append(c);
        }
        return b.append('"').toString();
    }

    // ------------------------------------------------------------------ ses
    private void audioLoop(int g) {
        android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_URGENT_AUDIO);
        while (gen == g) {
            reopen = false;
            AudioRecord rec = openRecorder();
            if (rec == null) {
                if (gen == g) Hub.set(Hub.ERROR, "mic");
                SystemClock.sleep(2000);
                continue;
            }
            if (Hub.state == Hub.ERROR && "mic".equals(Hub.reason))
                Hub.set(ws != null ? Hub.LIVE : Hub.CONNECTING, "");
            stateDirty = true;
            byte[] b = new byte[rate / 50 * 2];     // 20 ms
            long lastPing = 0;
            try {
                rec.startRecording();
                while (gen == g && !reopen) {
                    int n = rec.read(b, 0, b.length);
                    if (n < 0) break;               // mikrofon kayboldu (ör. kulaklık ayrıldı): yeniden aç
                    if (n == 0) continue;
                    int peak = 0;
                    for (int i = 0; i + 1 < n; i += 2) {
                        int v = (short) ((b[i] & 0xff) | (b[i + 1] << 8));
                        if (v < 0) v = -v;
                        if (v > peak) peak = v;
                    }
                    Hub.peak = Hub.muted ? 0 : peak / 32768f;
                    WsClient c = ws;
                    if (c == null) continue;
                    try {
                        if (stateDirty) { stateDirty = false; c.text(stateJson()); }
                        long now = SystemClock.elapsedRealtime();
                        if (now - lastPing > 2000) { lastPing = now; c.ping(); }
                        if (!Hub.muted) c.send(2, b, 0, n & ~1);
                    } catch (IOException e) {
                        c.close();                  // "net" fark edip yeniden bağlanır
                    }
                }
            } catch (RuntimeException ignored) {
            } finally {
                try { rec.stop(); } catch (RuntimeException ignored) { }
                rec.release();
                releaseEffects();
            }
        }
        Hub.peak = 0;
        if (!Hub.running) bluetooth(null);     // yeniden başlatıldıysa yeni iş parçacığı kullanıyor
    }

    private AudioRecord openRecorder() {
        SharedPreferences p = getSharedPreferences("settings", MODE_PRIVATE);
        boolean ns = p.getBoolean("ns", true), aec = p.getBoolean("aec", false), agc = p.getBoolean("agc", false);
        AudioDeviceInfo dev = Mics.find(am, p.getString("device", ""));
        boolean bt = dev != null && Mics.isBluetooth(dev);
        bluetooth(bt ? dev : null);
        // telefonun kendi işlemesi (gürültü/yankı) "iletişim" kaynağında çalışır; Bluetooth kulaklık da onu ister
        int source = bt || ns || aec ? MediaRecorder.AudioSource.VOICE_COMMUNICATION : MediaRecorder.AudioSource.MIC;
        int[] rates = { 48000, 44100, 16000, 8000 };
        for (int r : rates) {
            int min = AudioRecord.getMinBufferSize(r, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
            if (min <= 0) continue;
            AudioRecord rec;
            try {
                rec = new AudioRecord(source, r, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, Math.max(min, r / 10 * 2));
            } catch (RuntimeException e) {
                continue;
            }
            if (rec.getState() != AudioRecord.STATE_INITIALIZED) { rec.release(); continue; }
            if (dev != null) rec.setPreferredDevice(dev);
            effects(rec.getAudioSessionId(), ns, aec, agc);
            rate = r;
            Hub.mic = dev == null ? L.t("Telefonun mikrofonu", "Phone microphone") : Mics.label(dev);
            return rec;
        }
        return null;
    }

    private void effects(int session, boolean ns, boolean aec, boolean agc) {
        releaseEffects();
        try {
            if (NoiseSuppressor.isAvailable()) add(NoiseSuppressor.create(session), ns);
            if (AcousticEchoCanceler.isAvailable()) add(AcousticEchoCanceler.create(session), aec);
            if (AutomaticGainControl.isAvailable()) add(AutomaticGainControl.create(session), agc);
        } catch (RuntimeException ignored) { }
    }

    private void add(AudioEffect e, boolean on) {
        if (e == null) return;
        try { e.setEnabled(on); } catch (RuntimeException ignored) { }
        effects.add(e);
    }

    private void releaseEffects() {
        for (AudioEffect e : effects) try { e.release(); } catch (RuntimeException ignored) { }
        effects.clear();
    }

    /** Bluetooth kulaklığın mikrofonu "arama" bağlantısıyla (SCO) açılır. null = kapat. */
    private void bluetooth(AudioDeviceInfo dev) {
        try {
            if (dev != null) {
                am.setMode(AudioManager.MODE_IN_COMMUNICATION);
                if (Build.VERSION.SDK_INT >= 31) {
                    for (AudioDeviceInfo d : am.getAvailableCommunicationDevices())
                        if (d.getType() == dev.getType()) { am.setCommunicationDevice(d); break; }
                } else {
                    am.startBluetoothSco();
                    am.setBluetoothScoOn(true);
                }
                bluetoothOn = true;
                SystemClock.sleep(800);             // kulaklığın mikrofonu açılana kadar
            } else if (bluetoothOn) {
                if (Build.VERSION.SDK_INT >= 31) am.clearCommunicationDevice();
                else {
                    am.setBluetoothScoOn(false);
                    am.stopBluetoothSco();
                }
                am.setMode(AudioManager.MODE_NORMAL);
                bluetoothOn = false;
            }
        } catch (RuntimeException ignored) { }
    }

    // ------------------------------------------------------------------ bildirim
    private void goForeground() {
        NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26 && nm.getNotificationChannel("mic") == null) {
            NotificationChannel ch = new NotificationChannel("mic", L.t("Mikrofon", "Microphone"), NotificationManager.IMPORTANCE_LOW);
            ch.setShowBadge(false);
            nm.createNotificationChannel(ch);
        }
        Notification n = buildNotification();
        if (Build.VERSION.SDK_INT >= 29) startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE);
        else startForeground(1, n);
    }

    private void updateNotification() {
        if (!Hub.running) return;
        try {
            ((NotificationManager) getSystemService(NOTIFICATION_SERVICE)).notify(1, buildNotification());
        } catch (RuntimeException ignored) { }
    }

    private Notification buildNotification() {
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, "mic") : new Notification.Builder(this);
        int fl = PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE;
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), fl);
        PendingIntent mute = PendingIntent.getService(this, 1, new Intent(this, MicService.class).setAction(ACT_MUTE), fl);
        PendingIntent stop = PendingIntent.getService(this, 2, new Intent(this, MicService.class).setAction(ACT_STOP), fl);
        String title;
        switch (Hub.state) {
            case Hub.LIVE: title = Hub.muted ? L.t("Susturuldu", "Muted") : L.t("Mikrofon bilgisayarda", "Microphone is on the computer"); break;
            case Hub.ERROR: title = L.t("Bağlanamadı", "Couldn't connect"); break;
            default: title = L.t("Bilgisayara bağlanıyor…", "Connecting to the computer…");
        }
        b.setSmallIcon(R.drawable.ic_stat)
                .setContentTitle(title)
                .setContentText(Hub.target)
                .setContentIntent(open)
                .setOngoing(true)
                .setShowWhen(false)
                .setColor(0xFF2EC4B0)
                .addAction(new Notification.Action.Builder((Icon) null, Hub.muted ? L.t("Sesi aç", "Unmute") : L.t("Sustur", "Mute"), mute).build())
                .addAction(new Notification.Action.Builder((Icon) null, L.t("Durdur", "Stop"), stop).build());
        return b.build();
    }
}
